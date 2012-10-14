import clr

import System
from System import IDisposable

_a = clr.wrap_attribute

class Foo(object, IDisposable):
    __metaclass__ = clr.ClrClass
    __clr_namespace__ = 'MyFoo'
    __clr_attributes__ = [_a(System.ObsoleteAttribute), _a(System.CLSCompliantAttribute, True, IsCompliant=False)]
    
    @clr.method(None, (str,))
    def __new__(cls, name):
        return super(Foo, cls).__new__(cls)
    
    @clr.method(object, (object, str, str))
    @clr.attributes([_a(System.ObsoleteAttribute)])
    def frob(self, a, b, c):
        return 42
    
    def bar(self):
        return -1

#~ class MyException(System.Exception):
    #~ def __new__(cls, *args):
        #~ return super(MyException, cls).__new__(cls, *args)


#~ class MyList(System.Collections.ArrayList):
    #~ __metaclass__ = clr.ClrClass

#~ clr.AddReference("System.Web")
#~ from System.Web import IHttpHandler
#~ class MyHandler(IHttpHandler):
    #~ __metaclass__ = clr.ClrClass

    #~ IsReusable = True
    
    #~ def ProcessRequest(self, context):
        #~ context.Response.Write('Hello, World!')

