import clr

import System
from System import IDisposable

class Foo(object, IDisposable):
    __metaclass__ = clr.ClrClass

    bar = 1
    
    def __init__(self):
        pass
    
    @clr.method(object, (object, str, str))
    def frob(self, a, b, c):
        return 42
    
    @clr.method()
    def Dispose():
        pass

class MyList(System.Collections.ArrayList):
    __metaclass__ = clr.ClrClass

clr.AddReference("System.Web")
from System.Web import IHttpHandler
class MyHandler(IHttpHandler):
    __metaclass__ = clr.ClrClass

    IsReusable = True
    
    def ProcessRequest(self, context):
        context.Response.Write('Hello, World!')
