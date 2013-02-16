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
using System.Collections;

namespace IronPython.Runtime {
    public static partial class ClrModule {
        internal class ClrMethodInfo {
            internal string Name { get; set; }
            internal Type ReturnType { get; set; }
            internal string[] ArgNames { get; set; }
            internal Type[] ArgTypes { get; set; }
            internal bool IsStatic { get; set; }
            internal bool IsVirtual { get; set; }
            internal ClrAttributeInfo[] CustomAttributes { get; set; }
        }

        internal class ClrPropertyInfo {
            internal bool HasGetter { get; set; }
            internal bool HasSetter { get; set; }
            internal string Name { get; set; }
            internal Type Type { get; set; }
            internal bool IsStatic { get; set; }
            internal bool IsVirtual { get; set; }
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

                    object clr_return_type;
                    if (func.__dict__.TryGetValue("__clr_return_type__", out clr_return_type)) {
                        Type returnType = ConvertPythonTypeToClr(clr_return_type ?? typeof(void));

                        // ignore the 'self' parameter to instance methods
                        string[] argNames = isStatic ? func.ArgNames : ArrayUtils.ShiftLeft(func.ArgNames, 1);

                        object clr_arg_types = null;
                        Type[] argTypes = null;
                        if (func.__dict__.TryGetValue("__clr_arg_types__", out clr_arg_types) && clr_arg_types != null) {
                            argTypes = ((List)clr_arg_types)
                                            .Select(t => ConvertPythonTypeToClr(t ?? typeof(object)))
                                            .ToArray();
                        } else {
                            argTypes = Enumerable.Repeat(typeof(object), argNames.Length).ToArray();
                        }

                        bool isVirtual = true;
                        object isVirtualObj;
                        if (func.__dict__.TryGetValue("__clr_virtual__", out isVirtualObj)) {
                            isVirtual = (bool)isVirtualObj;
                        }

                        string name = func.__name__;

                        ClrAttributeInfo[] customAttribs = null;
                        object clr_attributes;
                        if (func.__dict__.TryGetValue("__clr_attributes__", out clr_attributes)) {
                            customAttribs = ((List)clr_attributes).Cast<ClrAttributeInfo>().ToArray();
                        }

                        yield return new ClrMethodInfo {
                            Name = name,
                            ReturnType = returnType,
                            ArgTypes = argTypes,
                            ArgNames = argNames,
                            CustomAttributes = customAttribs,
                            IsVirtual = isVirtual,
                            IsStatic = isStatic
                        };
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
                        if (prop.fget is staticmethod) {
                            func = (PythonFunction)((staticmethod)prop.fget).__func__;
                            isStatic = true;
                        } else {
                            continue;
                        }
                    }

                    object clr_return_type;
                    if (func.__dict__.TryGetValue("__clr_return_type__", out clr_return_type)) {
                        Type returnType = (Type)clr_return_type;

                        bool isVirtual = true;
                        object isVirtualObj;
                        if (func.__dict__.TryGetValue("__clr_virtual__", out isVirtualObj)) {
                            isVirtual = (bool)isVirtualObj;
                        }

                        string name = func.__name__;

                        ClrAttributeInfo[] customAttribs = null;
                        object clr_attributes;
                        if (func.__dict__.TryGetValue("__clr_attributes__", out clr_attributes)) {
                            customAttribs = ((List)clr_attributes).Cast<ClrAttributeInfo>().ToArray();
                        }

                        yield return new ClrPropertyInfo {
                            HasGetter = prop.fget != null,
                            HasSetter = prop.fset != null,
                            Name = name,
                            Type = returnType,
                            CustomAttributes = customAttribs,
                            IsStatic = isStatic,
                            IsVirtual = isVirtual
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

                ConstructorInfo defCtor = DefineForwardingConstructors(baseType, tb, pythonTypeField, objectOpsField);

                foreach(var method in methods) {
                    if (method.Name == "__new__") {
                        DefineConstructor(tb, method, defCtor);
                    } else {
                        DefineMethod(tb, method, pythonTypeField, objectOpsField);
                    }
                }

                // TODO Fields

                foreach (var property in properties) {
                    DefineProperty(tb, property, pythonTypeField, objectOpsField);
                }

                return tb.CreateType();
            }

            private static ConstructorInfo DefineForwardingConstructors(Type baseType, TypeBuilder tb, FieldBuilder pythonTypeField, FieldBuilder objectOpsField) {
                var invokeMember = typeof(ObjectOperations).GetMethod("InvokeMember",
                    new[] { typeof(object), typeof(string), typeof(object[]) });

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
                    var ilGen = new ILGen(newCtor.GetILGenerator());

                    // call the base class constructor
                    ilGen.EmitLoadArg(0);
                    ilGen.EmitType(tb);
                    ilGen.EmitFieldAddress(pythonTypeField);
                    ilGen.EmitFieldAddress(objectOpsField);
                    ilGen.EmitCall(typeof(DefaultEngine), "LoadPythonType");
                    for (int i = 1; i <= ctorParams.Length; ++i) {
                        ilGen.Emit(OpCodes.Ldarg, i);
                    }
                    ilGen.Emit(OpCodes.Call, baseCtor);

                    // call __init__
                    // Load all of the arguments into a local array
                    var args = ilGen.EmitLocalArray(typeof(object), ctorParams.Length, (i) => {
                        ilGen.EmitLoadArg(i);
                        ilGen.EmitBoxing(ctorParams[i].ParameterType);
                    });

                    ilGen.EmitFieldGet(objectOpsField);
                    ilGen.EmitLoadArg(0); // load "this"
                    ilGen.EmitString("__init__");
                    ilGen.Emit(OpCodes.Ldloc, args);
                    ilGen.EmitCall(invokeMember);

                    ilGen.Emit(OpCodes.Pop);
                    ilGen.Emit(OpCodes.Ret);

                    if (ctorParams.Length == 0) {
                        defCtor = newCtor;
                }
                }
                return defCtor;
            }

            private void DefineProperty(TypeBuilder tb, ClrPropertyInfo property, FieldBuilder pythonTypeField, FieldBuilder objectOpsField) {
                ContractUtils.Requires(!(property.IsVirtual && property.IsStatic));

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

                if (property.CustomAttributes != null) {
                foreach (var attrib in property.CustomAttributes) {
                    DefineAttribute(pb, attrib);
                }
                }

                var attribs = MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig;
                if (property.IsVirtual) {
                    attribs |= MethodAttributes.Virtual;
                }

                if (property.IsStatic) {
                    attribs |= MethodAttributes.Static;
                }

                if (property.HasGetter) {
                    var getter = tb.DefineMethod("get_" + property.Name, attribs, property.Type, Type.EmptyTypes);
                    var ilGen = new ILGen(getter.GetILGenerator());

                    if (property.IsStatic) {
                        ilGen.EmitType(tb);
                        ilGen.EmitFieldAddress(pythonTypeField);
                        ilGen.EmitFieldAddress(objectOpsField);
                        ilGen.EmitCall(typeof(DefaultEngine), "LoadPythonType");
                        ilGen.Emit(OpCodes.Pop);
                    }

                    ilGen.EmitFieldGet(objectOpsField);
                    if (property.IsStatic) {
                        ilGen.EmitFieldGet(pythonTypeField); // load the python type
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
                    ilGen.EmitCall(typeof(ObjectOperations), "ConvertTo", new[] { typeof(object), typeof(Type) });
                    ilGen.EmitUnbox(property.Type);

                    ilGen.Emit(OpCodes.Ret);

                    pb.SetGetMethod(getter);
                }

                if (property.HasSetter) {
                    var setter = tb.DefineMethod("set_" + property.Name, attribs, null, new[] { property.Type });
                    var ilGen = new ILGen(setter.GetILGenerator());

                    if (property.IsStatic) {
                        ilGen.EmitType(tb);
                        ilGen.EmitFieldAddress(pythonTypeField);
                        ilGen.EmitFieldAddress(objectOpsField);
                        ilGen.EmitCall(typeof(DefaultEngine), "LoadPythonType");
                        ilGen.Emit(OpCodes.Pop);
                    }

                    ilGen.EmitFieldGet(objectOpsField);
                    if (property.IsStatic) {
                        ilGen.EmitFieldGet(pythonTypeField); // load the python type
                    } else {
                        ilGen.EmitLoadArg(0); // load "this"
                    }

                    // Call ObjectOperations.SetMember
                    ilGen.EmitString(property.Name);
                    ilGen.EmitLoadArg(property.IsStatic ? 0 : 1);
                    ilGen.Emit(OpCodes.Box, property.Type);
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

            private void DefineMethod(TypeBuilder tb, ClrMethodInfo method, FieldBuilder pythonTypeField, FieldBuilder objectOpsField) {
                ContractUtils.Requires(method.ArgNames.Length == method.ArgTypes.Length);
                ContractUtils.Requires(!(method.IsVirtual && method.IsStatic));

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

                if (method.IsVirtual) {
                    attributes |= MethodAttributes.Virtual;
                }

                if (method.IsStatic) {
                    attributes |= MethodAttributes.Static;
                }

                // Overrides are handled by the class generated by NewTypeMaker

                var mb = tb.DefineMethod(
                    method.Name,
                    attributes,
                    method.ReturnType,
                    method.ArgTypes
                );

                if (method.CustomAttributes != null) {
                foreach (var attrib in method.CustomAttributes) {
                    DefineAttribute(mb, attrib);
                }
                }

                for(int p = 1, n = 0; n < method.ArgTypes.Length; ++p, ++n) {
                    mb.DefineParameter(p, ParameterAttributes.None, method.ArgNames[n]);
                }

                var ilGen = new ILGen(mb.GetILGenerator());

                if (method.IsStatic) {
                    ilGen.EmitType(tb);
                    ilGen.EmitFieldAddress(pythonTypeField);
                    ilGen.EmitFieldAddress(objectOpsField);
                    ilGen.EmitCall(typeof(DefaultEngine), "LoadPythonType");
                    ilGen.Emit(OpCodes.Pop);    // remove the result of LoadPythonType from the stack
                }

                // Load all of the arguments into a local array
                var args = ilGen.EmitLocalArray(typeof(object), method.ArgTypes.Length, (i) => {
                    ilGen.EmitLoadArg(method.IsStatic ? i : i + 1);
                    ilGen.EmitBoxing(method.ArgTypes[i]);
                });

                ilGen.EmitFieldGet(objectOpsField);
                if (method.IsStatic) {
                    ilGen.EmitFieldGet(pythonTypeField); // load the python type
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

            private void DefineAttribute(PropertyBuilder pb, ClrAttributeInfo attrib) {
                pb.SetCustomAttribute(MakeCab(attrib));
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
                [DefaultParameterValue(null)]IEnumerable arg_types,
                [DefaultParameterValue(true)]bool @virtual) {
            return func => {
                func.__dict__["__clr_return_type__"] = return_type;

                List clr_arg_types = null;
                if (arg_types != null) {
                    clr_arg_types = arg_types as List;
                    if (clr_arg_types == null) {
                        clr_arg_types = new List(arg_types.Cast<object>().ToList());
                }
                }

                func.__dict__["__clr_arg_types__"] = clr_arg_types;
                func.__dict__["__clr_virtual__"] = @virtual;

                return func;
            };
        }

        public static Func<PythonFunction, staticmethod> staticmethod(
                [DefaultParameterValue(null)]object return_type,
                [DefaultParameterValue(null)]IEnumerable<object> arg_types) {
            return func => {
                var result = new staticmethod(method(return_type, arg_types, false)(func));
                return result;
            };
        }

        public static Func<PythonFunction, PythonProperty> property(
                Type propertyType,
                [DefaultParameterValue(true)]bool @virtual) {
            return func => {
                var prop = new PythonProperty();
                prop.__init__(method(propertyType, null, @virtual)(func), null, null, null);

                return prop;
            };
        }

        public static Func<PythonFunction, PythonFunction> attribute(
                ClrAttributeInfo attrib) {
            return func => {
                List attributes;
                if (func.__dict__.ContainsKey("__clr_attributes__")) {
                    attributes = (List)func.__dict__["__clr_attributes__"];
                } else {
                    attributes = new List();
                    func.__dict__["clr_attributes__"] = attributes;
                }
                attributes.Add(attrib);

                return func;
            };
        }

        public static Func<PythonFunction, PythonFunction> attributes(
                IEnumerable<ClrAttributeInfo> attribs) {
            return func => {
                List attributes;
                if (func.__dict__.ContainsKey("__clr_attributes__")) {
                    attributes = (List)func.__dict__["__clr_attributes__"];
                } else {
                    attributes = new List();
                    func.__dict__["clr_attributes__"] = attributes;
                }
                attributes.AddRange(attribs);

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
            } else if (type is PythonType) {
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

            bool shouldCompileTypes = false;
            object compileTypes;
            if (kwArgs != null && kwArgs.TryGetValue("compileTypes", out compileTypes)) {
                bool? boolCompileTypes = compileTypes as bool?;
                shouldCompileTypes = boolCompileTypes != null && boolCompileTypes.GetValueOrDefault(false);
            }

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

                sc = pc.GetScriptCode(su, modName, ModuleOptions.Initialize, Compiler.CompilationMode.ToDisk);
                if (shouldCompileTypes) {
                    // Compile the module to trigger type generation and static compilation
                    // The Static generation code uses clr.compiledAssembly, which has been redirected to
                    // the same assembly generator as the rest of the code.
                    // This is equivalent to importing the module, so it is optional. CLR types can still
                    // be generated at runtime.
                    pc.CompileModule(filename, modName, su, ModuleOptions.Initialize | ModuleOptions.Optimized);
                }

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

