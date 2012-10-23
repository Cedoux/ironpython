/* ****************************************************************************
 *
 * Copyright (c) Microsoft Corporation. 
 *
 * This source code is subject to terms and conditions of the Apache License, Version 2.0. A 
 * copy of the license can be found in the License.html file at the root of this distribution. If 
 * you cannot locate the  Apache License, Version 2.0, please send an email to 
 * dlr@microsoft.com. By using this source code in any fashion, you are agreeing to be bound 
 * by the terms of the Apache License, Version 2.0.
 *
 * You must not remove this notice, or any other, from this software.
 *
 *
 * ***************************************************************************/

#if FEATURE_REFEMIT

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;

using Microsoft.Scripting;
using Microsoft.Scripting.Generation;
using Microsoft.Scripting.Runtime;
using Microsoft.Scripting.Utils;

using IronPython.Runtime.Operations;
using IronPython.Runtime.Types;
using Microsoft.Scripting.Hosting;

namespace IronPython.Runtime {
    public static partial class ClrModule {
        internal class ClrMethodInfo {
            internal string Name { get; set; }
            internal Type ReturnType { get; set; }
            internal string[] ArgNames { get; set; }
            internal Type[] ArgTypes { get; set; }
            internal bool IsStatic { get; set; }
            internal ClrAttributeInfo[] CustomAttributes { get; set; }
        }

        internal class ClrPropertyInfo {
            internal bool HasGetter { get; set; }
            internal bool HasSetter { get; set; }
            internal string Name { get; set; }
            internal Type Type { get; set; }
            internal bool IsStatic { get; set; }
            internal ClrAttributeInfo[] CustomAttributes { get; set; }
        }

        public class ClrAttributeInfo {
            internal Type Type { get; set; }
            internal object[] Args { get; set; }
            internal IDictionary<string, object> KeywordArgs { get; set; }
        }

        public class ClrPInvokeInfo {

        }

        public static ClrAttributeInfo wrap_attribute(Type attrib, [ParamDictionary]IDictionary<string, object> kwargs, params object[] args) {
            return new ClrAttributeInfo {
                Type = attrib,
                Args = args,
                KeywordArgs = kwargs
            };
        }

        /// <summary>
        /// Create a CLS-compliant version of a Python class, for use directly from
        /// other .NET langauges.
        /// 
        /// To use, simply set the metaclass to clr.ClrClass, like so:
        ///     class Foo(object):
        ///         __metaclass__ clr.ClrClass
        /// </summary>
        public class ClrClass : PythonType {
            internal static Dictionary<string, Type> preLoadedClrTypes = new Dictionary<string, Type>();

            internal static void LoadExistingClrTypes(Assembly asm) {
                foreach (var t in asm.GetTypes()) {
                    var attrib = t.GetCustomAttributes(
                        typeof(PythonClrTypeAttribute), 
                        false).SingleOrDefault() as PythonClrTypeAttribute;
                    
                    if (attrib != null) {
                        var key = attrib.ModuleName + "." + attrib.ClassName;
                        lock (preLoadedClrTypes) {
                            preLoadedClrTypes[key] = t;
                        }
                    }
                }
            }

            public ClrClass(CodeContext/*!*/ context, string name, PythonTuple bases, PythonDictionary dict)
                : base(context, name, bases, dict) {
                // Can do nothing here b/c __clrtype__ is called from PythonType()
            }
            
            public override Type __clrtype__() {
                Type clrType = LoadClrClass();
                if (clrType == null) {

                var baseType = base.__clrtype__();
                var dict = this.GetMemberDictionary(DefaultContext.Default);

                object nso;
                string ns = dict.TryGetValue("__clr_namespace__", out nso) ? (string)nso : string.Empty;

                object attribs_obj;
                var attribs = dict.TryGetValue("__clr_attributes__", out attribs_obj) ?
                    (IList<object>)attribs_obj :
                    new List<object>();

                var funcs = GetTypedFunctions(dict).ToList();
                    var props = GetTypedProperties(dict).ToList();

                    clrType = CreateClrClass(baseType, ns, attribs.Cast<ClrAttributeInfo>(), funcs, props);
                }

                InitializeClrType(clrType);

                return clrType;
            }

            private Type LoadClrClass() {
                string module = (string)PythonType.Get__module__(DefaultContext.Default, this);
                string className = this.Name;
                string key = module + "." + className;

                lock(preLoadedClrTypes) {
                    Type clrType;
                    return preLoadedClrTypes.TryGetValue(key, out clrType) ? clrType : null;
                }
            }

            private void InitializeClrType(Type clrType) {
                var pythonTypeField = clrType.GetField("__pythonType");
                pythonTypeField.SetValue(null, this);
            }

            private IEnumerable<ClrMethodInfo> GetTypedFunctions(PythonDictionary dict) {
                foreach (var value in dict.Values) {
                    bool isStatic = false;
                    PythonFunction func = value as PythonFunction;
                    if(func == null) {
                        if(value is staticmethod) {
                            func = (PythonFunction)((staticmethod)value).__func__;
                            isStatic = true;
                        } else {
                            continue;
                        }
                    }

                        object clr_method_info_obj;
                        if(func.__dict__.TryGetValue("_clr_method_info", out clr_method_info_obj)) {
                            var clr_method_info = clr_method_info_obj as ClrMethodInfo;
                            if(clr_method_info == null)
                                throw new Exception();

                        clr_method_info.Name = func.__name__;
                        clr_method_info.IsStatic = isStatic;
                            yield return clr_method_info;
                        }
                    }
                }

            private IEnumerable<ClrPropertyInfo> GetTypedProperties(PythonDictionary dict) {
                foreach (var value in dict.Values) {
                    bool isStatic = false;
                    PythonProperty prop = value as PythonProperty;
                    if(prop == null) {
                        continue;
                    }

                    PythonFunction func = prop.fget as PythonFunction;
                    if(func == null) {
                        if(value is staticmethod) {
                            func = (PythonFunction)((staticmethod)value).__func__;
                            isStatic = true;
                        } else {
                            continue;
                        }
                    }

                    object clr_method_info_obj;
                    if (func.__dict__.TryGetValue("_clr_method_info", out clr_method_info_obj)) {
                        var clr_method_info = clr_method_info_obj as ClrMethodInfo;
                        if(clr_method_info == null)
                            throw new Exception();

                        yield return new ClrPropertyInfo {
                            HasGetter = prop.fget != null,
                            HasSetter = prop.fset != null,
                            Name = clr_method_info.Name,
                            Type = clr_method_info.ReturnType,
                            CustomAttributes = clr_method_info.CustomAttributes,
                            IsStatic = isStatic
                        };
                    }
                }
            }

            private Type CreateClrClass(Type baseType,
                                        string ns,
                                        IEnumerable<ClrAttributeInfo> attributes,
                                        IEnumerable<ClrMethodInfo> methods,
                                        IEnumerable<ClrPropertyInfo> properties) {
                var fullName = !string.IsNullOrEmpty(ns) ? ns + "." + Name : Name;
                var tg = Snippets.Shared.DefineType(fullName, baseType, true, false);
                var tb = tg.TypeBuilder;

                string module = (string)PythonType.Get__module__(DefaultContext.Default, this);
                string className = this.Name;

                var markerAttrCtor = typeof(PythonClrTypeAttribute).GetConstructors().Single();
                tb.SetCustomAttribute(new CustomAttributeBuilder(markerAttrCtor, new[] { module, className }));

                foreach(var attrib in attributes) {
                    DefineAttribute(tb, attrib);
                }

                var pythonTypeField = tg.AddStaticField(typeof(PythonType), "__pythonType");
                var objectOpsField = tg.AddStaticField(typeof(ObjectOperations), "__operations");

                ConstructorInfo defCtor = null;
                foreach (var baseCtor in baseType.GetConstructors()) {
                    var ctorParams = baseCtor.GetParameters();

                    // if the first argument is PythonType, don't add it to this one
                    if (ctorParams[0].ParameterType == typeof(PythonType)) {
                        ctorParams = ctorParams.Skip(1).ToArray();
                    }

                    var newCtor = tb.DefineConstructor(
                            baseCtor.Attributes,
                            baseCtor.CallingConvention,
                            ctorParams.Select(p => p.ParameterType).ToArray());
                    var newCtorIL = new ILGen(newCtor.GetILGenerator());
                    newCtorIL.EmitLoadArg(0);
                    newCtorIL.EmitType(tb);
                    newCtorIL.EmitFieldAddress(pythonTypeField);
                    newCtorIL.EmitFieldAddress(objectOpsField);
                    newCtorIL.EmitCall(typeof(DefaultEngine), "LoadPythonType");
                    for (int i = 1; i <= ctorParams.Length; ++i) {
                        newCtorIL.Emit(OpCodes.Ldarg, i);

                    }
                    newCtorIL.Emit(OpCodes.Call, baseCtor);
                    newCtorIL.Emit(OpCodes.Ret);

                    if (ctorParams.Length == 0) {
                        defCtor = newCtor;
                }
                }

                foreach(var method in methods) {
                    if (method.Name == "__new__") {
                        DefineConstructor(tb, method, defCtor);
                    } else {
                        DefineMethod(tb, method, objectOpsField);
                }
                }

                // TODO Fields

                foreach (var property in properties) {
                    DefineProperty(tb, property, pythonTypeField, objectOpsField);
                }

                return tb.CreateType();
            }

            private void DefineProperty(TypeBuilder tb, ClrPropertyInfo property, FieldBuilder pythonTypeField, FieldBuilder objectOpsField) {
                // Type.GetMethod raises an AmbiguousMatchException if there is a generic 
                // and a non-generic method (like ObjectOperations.GetMember) with the 
                // same name and signature. So we have to do things the hard way.
                var getMember = typeof(ObjectOperations).GetMethods()
                    .Where(m => m.Name == "GetMember" && !m.IsGenericMethod && m.GetParameters().Length == 2)
                    .First();
                var setMember = typeof(ObjectOperations).GetMethods()
                    .Where(m => m.Name == "SetMember" && !m.IsGenericMethod && m.GetParameters().Length == 3)
                    .First();

                var pb = tb.DefineProperty(property.Name, PropertyAttributes.None, property.Type, null);

                var attribs = MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig;
                if (property.HasGetter) {
                    var getter = tb.DefineMethod("get_" + property.Name, attribs, property.Type, Type.EmptyTypes);
                    var ilGen = new ILGen(getter.GetILGenerator());

                    ilGen.EmitFieldGet(objectOpsField);
                    if (property.IsStatic) {
                        throw new Exception("static currently unsupported");
                    } else {
                        ilGen.EmitLoadArg(0); // load "this"
                    }

                    // Call ObjectOperations.GetMember
                    ilGen.EmitString(property.Name);
                    ilGen.EmitCall(getMember);

                    var retVal = ilGen.DeclareLocal(typeof(object));
                    ilGen.Emit(OpCodes.Stloc, retVal);  // store the result in retVal

                    ilGen.EmitFieldGet(objectOpsField);
                    ilGen.Emit(OpCodes.Ldloc, retVal);
                    ilGen.EmitType(property.Type);
                    ilGen.EmitCall(typeof(Type), "GetTypeFromHandle");
                    ilGen.EmitCall(typeof(ObjectOperations), "ConvertTo", new[] { typeof(object), typeof(Type) });
                    ilGen.EmitUnbox(property.Type);

                    ilGen.Emit(OpCodes.Ret);

                    pb.SetGetMethod(getter);
                }

                if (property.HasSetter) {
                    var setter = tb.DefineMethod("set_" + property.Name, attribs, null, new[] { property.Type });
                    var ilGen = new ILGen(setter.GetILGenerator());

                    ilGen.EmitFieldGet(objectOpsField);
                    if (property.IsStatic) {
                        throw new Exception("static currently unsupported");
                    } else {
                        ilGen.EmitLoadArg(0); // load "this"
                    }

                    // Call ObjectOperations.SetMember
                    ilGen.EmitString(property.Name);
                    ilGen.EmitLoadArg(property.IsStatic ? 0 : 1);
                    ilGen.EmitCall(setMember);

                    ilGen.Emit(OpCodes.Ret);

                    pb.SetSetMethod(setter);
                }
            }

            private void DefineConstructor(TypeBuilder tb, ClrMethodInfo method, ConstructorInfo defCtor) {
                var attributes = MethodAttributes.Public;

                // TODO Overrides
                // TODO Static methods

                var cb = tb.DefineConstructor(
                    attributes,
                    CallingConventions.Standard,
                    method.ArgTypes
                );

                var argNames = method.ArgNames;
                for(int i = 0; i < method.ArgTypes.Length; ++i) {
                    cb.DefineParameter(i+1, ParameterAttributes.None, argNames[i+1]);
            }

                var ilGen = new ILGen(cb.GetILGenerator());
                ilGen.EmitLoadArg(0);
                ilGen.Emit(OpCodes.Call, defCtor);
                ilGen.Emit(OpCodes.Ret);
            }

            private void DefineMethod(TypeBuilder tb, ClrMethodInfo method, FieldBuilder objectOpsField) {
                var invokeMember = typeof(ObjectOperations).GetMethod("InvokeMember", 
                    new[] { typeof(object), typeof(string), typeof(object[]) });

                // Type.GetMethod raises an AmbiguousMatchException if there is a generic 
                // and a non-generic method (like ObjectOperations.GetMember) with the 
                // same name and signature. So we have to do things the hard way.
                var getMember = typeof(ObjectOperations).GetMethods()
                    .Where(m => m.Name == "GetMember" && !m.IsGenericMethod && m.GetParameters().Length == 2)
                    .First();
                var setMember = typeof(ObjectOperations).GetMethods()
                    .Where(m => m.Name == "SetMember" && !m.IsGenericMethod && m.GetParameters().Length == 3)
                    .First();

                var attributes = MethodAttributes.Public;

                // TODO Overrides
                // TODO Static methods

                var mb = tb.DefineMethod(
                    method.Name,
                    attributes,
                    method.ReturnType,
                    method.ArgTypes
                );

                foreach (var attrib in method.CustomAttributes) {
                    DefineAttribute(mb, attrib);
                }

                var argNames = method.ArgNames;
                for(int i = 0; i < method.ArgTypes.Length; ++i) {
                    mb.DefineParameter(i+1, ParameterAttributes.None, argNames[i+1]);
                }

                var ilGen = new ILGen(mb.GetILGenerator());

                // TODO Actual implementation to call Python method

                // Load all of the arguments into a local array
                var args = ilGen.DeclareLocal(typeof(object[]));
                ilGen.EmitArray(typeof(object), method.ArgTypes.Length, (i) => {
                    ilGen.EmitLoadArg(method.IsStatic ? i : i+1);
                });
                ilGen.Emit(OpCodes.Stloc, args);

                ilGen.EmitFieldGet(objectOpsField);
                if (method.IsStatic) {
                    throw new Exception("static currently unsupported");
                } else {
                    ilGen.EmitLoadArg(0); // load "this"
                }

                // Call ObjectOperations.InvokeMember
                ilGen.EmitString(method.Name);
                ilGen.Emit(OpCodes.Ldloc, args);
                ilGen.EmitCall(invokeMember);

                if (method.ReturnType != typeof(void)) {
                    var retVal = ilGen.DeclareLocal(typeof(object));
                    ilGen.Emit(OpCodes.Stloc, retVal);  // store the result in retVal

                    ilGen.EmitFieldGet(objectOpsField);
                    ilGen.Emit(OpCodes.Ldloc, retVal);
                    ilGen.EmitType(method.ReturnType);
                    ilGen.EmitCall(typeof(Type), "GetTypeFromHandle");
                    ilGen.EmitCall(typeof(ObjectOperations), "ConvertTo", new[] { typeof(object), typeof(Type) });
                    ilGen.EmitUnbox(method.ReturnType);
                } else {
                    ilGen.Emit(OpCodes.Pop);
                }
                ilGen.Emit(OpCodes.Ret);
            }

            private void DefineAttribute(TypeBuilder tb, ClrAttributeInfo attrib) {
                tb.SetCustomAttribute(MakeCab(attrib));
            }

            private void DefineAttribute(MethodBuilder mb, ClrAttributeInfo attrib) {
                mb.SetCustomAttribute(MakeCab(attrib));
            }

            private static CustomAttributeBuilder MakeCab(ClrAttributeInfo attrib)
            {
                ConstructorInfo ctor;
                Type[] ctor_arg_types = attrib.Args.Select(a => a.GetType()).ToArray();
                ctor = attrib.Type.GetConstructor(ctor_arg_types);

                // TODO Handle keyword args

                return new CustomAttributeBuilder(ctor, attrib.Args);
            }
        }

        /// <summary>
        /// Decorate a Python method with CLR type information.
        /// 
        /// Use this in conjuction with __metaclass__ = clr.ClrClass to create
        /// real .NET types from Python types.
        /// </summary>
        /// <param name="return_type">The return type.</param>
        /// <param name="arg_types">The argument types, in order.</param>
        /// <returns></returns>
        public static Func<PythonFunction, PythonFunction> method(
                [DefaultParameterValue(null)]object return_type, 
                [DefaultParameterValue(null)]IEnumerable<object> arg_types) {
            return func => {
                Type clr_return_type = ConvertPythonTypeToClr(return_type ?? typeof(void));
                Type[] clr_arg_types;
                if (arg_types != null) {
                    clr_arg_types = arg_types
                                        .Select(type => ConvertPythonTypeToClr(type))
                                        .ToArray();
                } else {
                    clr_arg_types = Enumerable.Repeat(typeof(object), func.NormalArgumentCount).ToArray();
                }

                object attribs_obj;
                var customAttribs = func.__dict__.TryGetValue("__clr_attributes__", out attribs_obj) ?
                    (IEnumerable<ClrAttributeInfo>)attribs_obj :
                    Enumerable.Empty<ClrAttributeInfo>();

                func.__dict__["_clr_method_info"] = new ClrMethodInfo {
                    Name = func.__name__,
                    ReturnType = clr_return_type,
                    ArgTypes = clr_arg_types,
                    ArgNames = func.ArgNames,
                    CustomAttributes = customAttribs.ToArray(),
                };

                return func;
            };
        }

        public static Func<PythonFunction, PythonProperty> property(Type propertyType) {
            return func => {
                var prop = new PythonProperty();
                prop.__init__(method(propertyType, null)(func), null, null, null);

                return prop;
            };
        }

        public static Func<PythonFunction, PythonFunction> attributes(
                IEnumerable<ClrAttributeInfo> attribs) {
            return func => {
                func.__dict__["__clr_attributes__"] = attribs;

                return func;
            };
        }

        public static Func<PythonFunction, PythonFunction> pinvoke(
                string dllName) {
            return func => {
                func.__dict__["__clr_pinvoke__"] = new ClrPInvokeInfo { };

                return func;
            };
        }

        private static Type ConvertPythonTypeToClr(object type) {
            Type clr_type;
            if (type is Type) {
                clr_type = (Type)type;
            }
            if (type is PythonType) {
                clr_type = ((PythonType)type).FinalSystemType;
            } else {
                clr_type = typeof(void);
            }

            return clr_type;
        }

#if FEATURE_FILESYSTEM
        /// <summary>
        /// Provides a helper for compiling a group of modules into a single assembly.  The assembly can later be
        /// reloaded using the clr.AddReference API.
        /// </summary>
        public static void CompileModules(CodeContext/*!*/ context, string/*!*/ assemblyName, [ParamDictionary]IDictionary<string, object> kwArgs, params string/*!*/[]/*!*/ filenames) {
            ContractUtils.RequiresNotNull(assemblyName, "assemblyName");
            ContractUtils.RequiresNotNullItems(filenames, "filenames");

            // break the assemblyName into it's dir/name/extension
            string dir = Path.GetDirectoryName(assemblyName);
            if (String.IsNullOrEmpty(dir)) {
                dir = Environment.CurrentDirectory;
            }

            string name = Path.GetFileNameWithoutExtension(assemblyName);
            string ext = Path.GetExtension(assemblyName);

            var targetAssembly = new AssemblyGen(new AssemblyName(name), dir, ext, /*emitSymbols*/false);
            AssemblyGen oldSnippetsAssembly = Snippets.Shared.GetAssembly(false);
            Snippets.Shared.SetAssembly(false, targetAssembly);

            PythonContext pc = PythonContext.GetContext(context);

            for (int i = 0; i < filenames.Length; i++) {
                filenames[i] = pc.DomainManager.Platform.GetFullPath(filenames[i]);
            }

            Dictionary<string, string> packageMap = BuildPackageMap(filenames);

            List<SavableScriptCode> code = new List<SavableScriptCode>();
            foreach (string filename in filenames) {
                if (!pc.DomainManager.Platform.FileExists(filename)) {
                    throw PythonOps.IOError("Couldn't find file for compilation: {0}", filename);
                }

                ScriptCode sc;

                string modName;
                string dname = Path.GetDirectoryName(filename);
                string outFilename = "";
                if (Path.GetFileName(filename) == "__init__.py") {
                    // remove __init__.py to get package name
                    dname = Path.GetDirectoryName(dname);
                    if (String.IsNullOrEmpty(dname)) {
                        modName = Path.GetDirectoryName(filename);
                    } else {
                        modName = Path.GetFileNameWithoutExtension(Path.GetDirectoryName(filename));
                    }
                    outFilename = Path.DirectorySeparatorChar + "__init__.py";
                } else {
                    modName = Path.GetFileNameWithoutExtension(filename);
                }
                
                // see if we have a parent package, if so incorporate it into
                // our name
                string parentPackage;
                if (packageMap.TryGetValue(dname, out parentPackage)) {
                    modName = parentPackage + "." + modName;
                }

                outFilename = modName.Replace('.', Path.DirectorySeparatorChar) + outFilename;

                SourceUnit su = pc.CreateSourceUnit(
                    new FileStreamContentProvider(
                        context.LanguageContext.DomainManager.Platform,
                        filename
                    ),
                    outFilename,
                    pc.DefaultEncoding,
                    SourceCodeKind.File
                );

                sc = PythonContext.GetContext(context).GetScriptCode(su, modName, ModuleOptions.Initialize, Compiler.CompilationMode.ToDisk);

                code.Add((SavableScriptCode)sc);
            }

            object mainModule;
            if (kwArgs != null && kwArgs.TryGetValue("mainModule", out mainModule)) {
                string strModule = mainModule as string;
                if (strModule != null) {
                    if (!pc.DomainManager.Platform.FileExists(strModule)) {
                        throw PythonOps.IOError("Couldn't find main file for compilation: {0}", strModule);
                    }
                    
                    SourceUnit su = pc.CreateFileUnit(strModule, pc.DefaultEncoding, SourceCodeKind.File);
                    code.Add((SavableScriptCode)PythonContext.GetContext(context).GetScriptCode(su, "__main__", ModuleOptions.Initialize, Compiler.CompilationMode.ToDisk));
                }
            }

            SavableScriptCode.GenerateAssemblyCode(targetAssembly, code.ToArray());
            targetAssembly.SaveAssembly();

            Snippets.Shared.SetAssembly(false, oldSnippetsAssembly);
        }
#endif

        /// <summary>
        /// clr.CompileSubclassTypes(assemblyName, *typeDescription)
        /// 
        /// Provides a helper for creating an assembly which contains pre-generated .NET 
        /// base types for new-style types.
        /// 
        /// This assembly can then be AddReferenced or put sys.prefix\DLLs and the cached 
        /// types will be used instead of generating the types at runtime.
        /// 
        /// This function takes the name of the assembly to save to and then an arbitrary 
        /// number of parameters describing the types to be created.  Each of those
        /// parameter can either be a plain type or a sequence of base types.
        /// 
        /// clr.CompileSubclassTypes(object) -> create a base type for object
        /// clr.CompileSubclassTypes(object, str, System.Collections.ArrayList) -> create 
        ///     base  types for both object and ArrayList.
        ///     
        /// clr.CompileSubclassTypes(object, (object, IComparable)) -> create base types for 
        ///     object and an object which implements IComparable.
        /// 
        /// </summary>
        public static void CompileSubclassTypes(string/*!*/ assemblyName, params object[] newTypes) {
            if (assemblyName == null) {
                throw PythonOps.TypeError("CompileTypes expected str for assemblyName, got NoneType");
            }

            var typesToCreate = new List<PythonTuple>();
            foreach (object o in newTypes) {
                if (o is PythonType) {
                    typesToCreate.Add(PythonTuple.MakeTuple(o));
                } else {
                    typesToCreate.Add(PythonTuple.Make(o));
                }
            }

            NewTypeMaker.SaveNewTypes(assemblyName, typesToCreate);
        }

    }
}
#endif

