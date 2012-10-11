import clr

import System
from System import IDisposable

_a = clr.wrap_attribute

class Foo(object, IDisposable):
    __metaclass__ = clr.ClrClass
    __clr_namespace__ = 'MyFoo'
    __clr_attributes__ = [_a(System.ObsoleteAttribute), _a(System.CLSCompliantAttribute, True, IsCompliant=False)]
    
    @clr.method(object, (object, str, str))
    @clr.attributes([_a(System.ObsoleteAttribute)])
    def frob(self, a, b, c):
        return 42

class MyList(System.Collections.ArrayList):
    __metaclass__ = clr.ClrClass

clr.AddReference("System.Web")
from System.Web import IHttpHandler
class MyHandler(IHttpHandler):
    __metaclass__ = clr.ClrClass

    IsReusable = True
    
    def ProcessRequest(self, context):
        context.Response.Write('Hello, World!')
