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

using Microsoft.Scripting;
using Microsoft.Scripting.Utils;

using IronPython.Runtime.Operations;
using IronPython.Runtime.Types;
using Microsoft.Scripting.Generation;
using System.Reflection.Emit;
using Microsoft.Scripting.Hosting;
using System.Reflection;
using System.Collections;
using System.Runtime.InteropServices;

namespace IronPython.Runtime {
    public static partial class ClrModule {
        internal class ClrMethodInfo {
            internal string Name { get; set; }
            internal Type ReturnType { get; set; }
            internal Type[] ArgTypes { get; set; }
        }

        public class ClrClass : PythonType {
            private CodeContext context;

            public ClrClass(CodeContext/*!*/ context, string name, PythonTuple bases, PythonDictionary dict)
                : base(context, name, bases, dict) {
                this.context = context;
            }
            
            public override Type __clrtype__() {
                var baseType = base.__clrtype__();

                var funcs = GetTypedFunctions().ToList();

                return baseType;
            }

            private IEnumerable<ClrMethodInfo> GetTypedFunctions() {
                var dict = this.GetMemberDictionary(context);
                foreach (var key in dict.Keys) {
                    var func = dict[key] as PythonFunction;
                    if (func != null) {
                        object clr_method_info_obj;
                        if(func.__dict__.TryGetValue("_clr_method_info", out clr_method_info_obj)) {
                            var clr_method_info = clr_method_info_obj as ClrMethodInfo;
                            if(clr_method_info == null)
                                throw new Exception();

                            yield return clr_method_info;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Decorate a Python method with CLR type information.
        /// 
        /// Use this in conjuction with __metaclass__ = Clr.ClrClass to create
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

                func.__dict__["_clr_method_info"] = new ClrMethodInfo {
                    Name = func.__name__,
                    ReturnType = clr_return_type,
                    ArgTypes = clr_arg_types
                };

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

