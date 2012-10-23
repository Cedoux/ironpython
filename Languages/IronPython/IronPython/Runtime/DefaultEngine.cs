using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Scripting.Hosting;
using System.Threading;
using IronPython.Hosting;
using IronPython.Runtime.Types;

namespace IronPython.Runtime {
    public static class DefaultEngine {
        private static ScriptEngine defaultEngine;

        public static ScriptEngine GetDefaultEngine() {
            if (defaultEngine == null) {
                var engine = Python.CreateEngine();
                Interlocked.CompareExchange(ref defaultEngine, engine, null);
            }

            return defaultEngine;
        }

        static bool TryGetPythonTypeName(Type t, out string moduleName, out string className) {
            var attrib = t.GetCustomAttributes(
                typeof(PythonClrTypeAttribute), 
                false).SingleOrDefault() as PythonClrTypeAttribute;

            if (attrib != null) {
                moduleName = attrib.ModuleName;
                className = attrib.ClassName;
                return true;
            } else {
                moduleName = className = null;
                return false;
            }
        }

        public static PythonType LoadPythonType(Type t, ref PythonType pt, ref ObjectOperations ops) {
            // If it's already set, just return it 
            if(pt != null)
                return pt;
            
            var engine = GetDefaultEngine();
            ops = engine.Operations;

            // Add the type's assembly to make sure any pre-generated classes are picked up
            engine.Runtime.LoadAssembly(t.Assembly);

            string moduleName, className;
            if (TryGetPythonTypeName(t, out moduleName, out className)) {
                var moduleScope = engine.ImportModule(moduleName);
                var classType = moduleScope.GetVariable(className);

                return pt = (PythonType)classType;
            } else {
                return null;
            }
        }
    }
}
