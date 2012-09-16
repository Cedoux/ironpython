import sys
import os
import itertools

import clr
clr.AddReference('Mono.Cecil')
clr.AddReference('System.Core')

import System

from Mono.Cecil import *
from Mono.Cecil.Cil import OpCodes

class Platform(object):
    default_references = [
        'mscorlib', 'System', 'System.Core'
    ]
    
    platform_references = []
    
    def __init__(self, refpath, user_references):
        self._reference_map = {}
        self._type_map = {}
        for ref in itertools.chain(self.default_references, self.platform_references, user_references):
            dll = os.path.join(refpath, ref + '.dll')
            assy = AssemblyDefinition.ReadAssembly(dll)
            self._reference_map[assy.Name.Name] = assy
            
            for mod in assy.Modules:
                for t in mod.GetTypes():
                    self._type_map[t.FullName] = t
        
        from pprint import pprint
        pprint(self._reference_map)
        pprint(self._type_map)
    
    def rewrite_il(self, mod):
        pass
    
    def update_references(self, mod):
        for t in mod.GetTypeReferences():
            try:
                t.Scope = self._type_map[t.FullName].Scope
            except KeyError:
                print 'Skipping', t.FullName
                pass
        
        for ref in mod.AssemblyReferences:
            try:
                ref.Version = self._reference_map[ref.Name].Name.Version
            except KeyError:
                print 'Skipping', ref.Name
                pass
        
        mod.Runtime = self.runtime
    
    def clean_attributes(self, assy):
        pass

class Net40(Platform):
    runtime = TargetRuntime.Net_4_0
    extra_references = ['System.Numeric']

class Net20(Platform):
    runtime = TargetRuntime.Net_2_0
    
    def rewrite_il(self, mod):
        fix_strongbox_ctor(mod)
    
    def update_references(self, mod):
        super(Net20, self).update_references(mod)
        
        msc = AssemblyNameReference.Parse('Microsoft.Scripting.Core, Version=1.1.0.30, Culture=neutral, PublicKeyToken=7f709c5b713576e1')
        mod.AssemblyReferences.Add(msc)

        for t in mod.GetTypeReferences():
            n = t.FullName
            if n.startswith('System.Dynamic.'):
                t.Scope = msc
            elif n.startswith('System.Linq.Expressions.'):
                t.Scope = msc
                t.Namespace = 'Microsoft.Scripting.Ast'
            elif (n.startswith('System.Runtime.CompilerServices.CallSite') or n.startswith('System.Runtime.CompilerServices.Closure')):
                t.Scope = msc
            elif n.startswith('System.Func'):
                _, num = n.split('`')
                try:
                    if int(num) > 5:
                        t.Scope = msc
                        t.Namespace = 'Microsoft.Scripting.Utils'
                except ValueError:
                    pass
    
    def clean_attributes(self, assy):
        to_remove = []
        for attrib in assy.CustomAttributes:
            if attrib.AttributeType.FullName == 'System.Security.SecurityRulesAttribute':
                to_remove.append(attrib)
        
        for dead in to_remove:
            assy.CustomAttributes.Remove(dead)

class Silverlight5(Platform):
    runtime = TargetRuntime.Net_4_0
    extra_references = ['System.Numeric']

class WinRT(Platform):
    runtime = TargetRuntime.Net_4_0

class Android(Platform):
    runtime = TargetRuntime.Net_2_0
    extra_references = ['System.Numeric']

def fix_strongbox_ctor(mod):
    dcc = mod.GetType('DLRCachedCode')

    strongbox_ctor_v2 = clr.GetClrType(System.Runtime.CompilerServices.StrongBox).MakeGenericType(System.Array[object]).GetConstructor((System.Array[object],))
    strongbox_ctor_v4 = clr.GetClrType(System.Runtime.CompilerServices.StrongBox).MakeGenericType(System.Array[object]).GetConstructor(System.Type.EmptyTypes)

    #for method in dcc.Methods:
    #    for instr in method.Body.Instructions:
            #~ if instr.OpCode == OpCodes.Newobj:
                #~ if instr.Operand.FullName == method.Module.Import(strongbox_ctor_v4).FullName:
                    #~ il = method.Body.GetILProcessor()
                    #~ il.InsertBefore(instr, il.Create(OpCodes.Ldnull))
                    #~ il.Replace(instr, il.Create(OpCodes.Newobj, method.Module.Import(strongbox_ctor_v2)))

    method = dcc.Methods[0]
    il = method.Body.GetILProcessor()
    instr = method.Body.Instructions[4]
    assert instr.Operand.FullName == method.Module.Import(strongbox_ctor_v4).FullName
    il.InsertBefore(instr, il.Create(OpCodes.Ldnull))
    il.Replace(instr, il.Create(OpCodes.Newobj, method.Module.Import(strongbox_ctor_v2)))

def change_references(mod):
    mod.Runtime = TargetRuntime.Net_2_0
    refs = mod.AssemblyReferences
    refs[0].Version = System.Version(2,0,0,0)
    refs[3].Version = System.Version(2,0,0,0)
    refs[5].Version = System.Version(2,0,0,0)
    refs[2].Version = System.Version(3,5,0,0)

    sc = refs[2]
    msc = AssemblyNameReference.Parse('Microsoft.Scripting.Core, Version=1.1.0.30, Culture=neutral, PublicKeyToken=7f709c5b713576e1')
    refs.Add(msc)

    for t in ad.Modules[0].GetTypeReferences():
        n = t.FullName
        if n.startswith('System.Dynamic.'):
            t.Scope = msc
        elif n.startswith('System.Linq.Expressions.'):
            t.Scope = msc
            t.Namespace = 'Microsoft.Scripting.Ast'
        elif (n.startswith('System.Runtime.CompilerServices.CallSite') or n.startswith('System.Runtime.CompilerServices.Closure')):
            t.Scope = msc
        elif n.startswith('System.Func'):
            # moved from System.Core in 3.5 to mscorlib in 4
            t.Scope = sc
            if (n.startswith('System.Func`6') or n.startswith('System.Func`7') or n.startswith('System.Func`8')):
                t.Scope = msc
                t.Namespace = 'Microsoft.Scripting.Utils'

def main():
    assy_file = sys.argv[1]
    p = Net20(r'C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v3.5\Profile\Client', [])
    
    assy = AssemblyDefinition.ReadAssembly(assy_file)
    p.rewrite_il(assy.Modules[0])
    p.update_references(assy.Modules[0])
    p.clean_attributes(assy)
    
    assy.Write('out.dll')
    
if __name__ == '__main__':
    main()