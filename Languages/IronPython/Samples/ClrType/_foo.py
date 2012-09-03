import clr
import clrtype

import System
from System import IDisposable

class Foo(object, IDisposable):
    __metaclass__ = clr.ClrClass

    bar = 1
    
    def __init__(self):
        pass
    
    #@clrtype.accepts(System.Object, str, System.String)
    #@clrtype.returns(object)
    @clr.method(object, (object, str, str))
    def frob(self, a, b, c):
        return 42
    
    @clrtype.accepts()
    @clrtype.returns()
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
