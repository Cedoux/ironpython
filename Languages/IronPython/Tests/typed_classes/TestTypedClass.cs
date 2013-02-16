using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using IronPython.Hosting;
using IronPython.Runtime.Types;
using IronPython.Runtime;
using IronPython.Tests;
using System.Diagnostics;

namespace IronPython.Tests {
    class TestTypedClass {
        static bool success = true;

        static void Test(string name, Func<bool> f) {
            try {
                Console.Write(name);
                Console.Write("... ");
                var result = f();
                Console.WriteLine(result ? "OK" : "FAILED");
                success = success && result;
            } catch (Exception e) {
                Console.WriteLine("ERROR ({0})", e.Message);
            }
        }

        static int Main(string[] args) {
            Test("StaticMethodWithArgs", () => TypedClass.StaticMethodWithArgs(1, 2.0) == "3.0");
            Test("StaticReadOnlyProperty", () => TypedClass.StaticReadOnlyProperty == 42);
            
            var t = new TypedClass();
            Test("ReadOnlyProperty", () => t.ReadOnlyProperty == 42);
            Test("ReadOnlyProperty:Virtual", () => t.GetType().GetProperty("ReadOnlyProperty").GetGetMethod().IsVirtual);

            Test("ReadWriteProperty:Get", () => t.ReadWriteProperty == -1);
            Test("ReadWriteProperty:Get:Virtual", () => t.GetType().GetProperty("ReadWriteProperty").GetGetMethod().IsVirtual);
            t.ReadWriteProperty = 69;
            Test("ReadWriteProperty:Set", () => t.ReadWriteProperty == 69);
            Test("ReadWriteProperty:Set:Virtual", () => t.GetType().GetProperty("ReadWriteProperty").GetSetMethod().IsVirtual);

            Test("NonVirtualReadOnlyProperty", () => t.NonVirtualReadOnlyProperty == 42);
            Test("NonVirtualReadOnlyProperty:NonVirtual", () => !t.GetType().GetProperty("NonVirtualReadOnlyProperty").GetGetMethod().IsVirtual);

            Test("ReturnOnlyMethod", () => t.ReturnOnlyMethod() == 42);
            Test("ReturnOnlyMethod:Virtual", () => t.GetType().GetMethod("ReturnOnlyMethod").IsVirtual);

            Test("MethodWithArgs", () => t.MethodWithArgs(1, 2.0) == "3.0");
            Test("MethodWithArgs:Virtual", () => t.GetType().GetMethod("MethodWithArgs").IsVirtual);

            Test("NonVirtualMethodWithArgs", () => t.NonVirtualMethodWithArgs(1, 2.0) == "3.0");
            Test("NonVirtualMethodWithArgs:NonVirtual", () => !t.GetType().GetMethod("NonVirtualMethodWithArgs").IsVirtual);

            return success ? 0 : 1;
        }
    }
}
