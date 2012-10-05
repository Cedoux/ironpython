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
    platform_references = []
    
    def __init__(self, user_reference_paths=[], user_references=[]):
        self._reference_map = {}
        self._type_map = {}
        
        self._all_reference_paths = self.reference_paths + user_reference_paths
        self._all_references = set(self.platform_references + user_references)
        
        from pprint import pprint
        # pprint(self._all_reference_paths)
        # pprint(self._type_map)
    
    def rewrite_il(self, mod):
        pass
    
    def update_references(self, mod):
        mod.Runtime = self.runtime
        existing_references = set()
        
        for ref in mod.AssemblyReferences:
            existing_references.add(ref.Name)
            ref.Version = self._find_reference_assembly(ref.Name).Name.Version
        
        for missing_ref in self._all_references - existing_references:
            assy = self._find_reference_assembly(missing_ref)
            mod.AssemblyReferences.Add(assy.Name)
        
        for t in mod.GetTypeReferences():
            t.Scope = self._find_type(t.FullName).Scope
    
    def _find_reference_assembly(self, assy_name):
        if assy_name not in self._reference_map:
            assy_file = assy_name + '.dll'
            for path in self._all_reference_paths:
                assy_path = os.path.join(path, assy_file)
                if os.path.exists(assy_path):
                    assy = AssemblyDefinition.ReadAssembly(assy_path)
                    self._reference_map[assy_name] = assy
                    break
        
        return self._reference_map[assy_name]
    
    def _find_type(self, type_name):
        if not self._type_map:
            for assy in self._reference_map.values():
                for mod in assy.Modules:
                    for t in mod.GetTypes():
                        self._type_map[t.FullName] = t
        
        return self._type_map[type_name]
    
class v4Platform(Platform):
    runtime = TargetRuntime.Net_4_0
    platform_references = ['System.Numeric']

class Net40(v4Platform):
    reference_paths = [r'C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.0']

class v2Platform(Platform):
    runtime = TargetRuntime.Net_2_0
    platform_references = ['Microsoft.Scripting', 'Microsoft.Scripting.Core']

class Net20(v2Platform):
    reference_paths = [r'C:\Windows\Microsoft.NET\Framework\v2.0.50727',
        r'C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\v3.0',
        r'C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\v3.5']
    
    def rewrite_il(self, mod):
        fix_strongbox_ctor(mod)
    
    def update_references(self, mod):
        for t in mod.GetTypeReferences():
            if t.FullName.startswith('System.Linq.Expressions.'):
                t.Namespace = 'Microsoft.Scripting.Ast'
            elif t.FullName.startswith('System.Func'):
                _, num = t.FullName.split('`')
                try:
                    if int(num) > 5:
                        t.Namespace = 'Microsoft.Scripting.Utils'
                except ValueError:
                    pass
                
        super(Net20, self).update_references(mod)
    
class Silverlight5(v4Platform):
    reference_paths = [r'C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\Silverlight\v5.0']

class WinRT(v4Platform):
    reference_paths = [r'']

class Android(v2Platform):
    platform_references = v2Platform.platform_references + ['System.Numeric']
    reference_paths = [r'C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\MonoAndroid\v1.0']

def fix_strongbox_ctor(mod):
    dcc = mod.GetType('DLRCachedCode')

    strongbox_ctor_v2 = clr.GetClrType(System.Runtime.CompilerServices.StrongBox).MakeGenericType(System.Array[object]).GetConstructor((System.Array[object],))
    strongbox_ctor_v4 = clr.GetClrType(System.Runtime.CompilerServices.StrongBox).MakeGenericType(System.Array[object]).GetConstructor(System.Type.EmptyTypes)

    #~ for method in dcc.Methods:
        #~ il = method.Body.GetILProcessor()
        #~ print method.Name
        #~ for instr in method.Body.Instructions:
            #~ if instr.OpCode == OpCodes.Newobj:
                #~ if instr.Operand.FullName == method.Module.Import(strongbox_ctor_v4).FullName:
                    #~ il.InsertBefore(instr, il.Create(OpCodes.Ldnull))
                    #~ il.Replace(instr, il.Create(OpCodes.Newobj, method.Module.Import(strongbox_ctor_v2)))

    # This is super-fragile, but the above loop is not working
    method = dcc.Methods[0]
    il = method.Body.GetILProcessor()
    instr = method.Body.Instructions[4]
    assert instr.Operand.FullName == method.Module.Import(strongbox_ctor_v4).FullName
    il.InsertBefore(instr, il.Create(OpCodes.Ldnull))
    il.Replace(instr, il.Create(OpCodes.Newobj, method.Module.Import(strongbox_ctor_v2)))

def main():
    assy_file = sys.argv[1]
    p = Net20([os.path.join(os.environ['DLR_ROOT'], r'bin\v2Debug')])
    
    assy = AssemblyDefinition.ReadAssembly(assy_file)
    p.rewrite_il(assy.Modules[0])
    p.update_references(assy.Modules[0])
    
    assy.Write('out.dll')
    
if __name__ == '__main__':
    main()