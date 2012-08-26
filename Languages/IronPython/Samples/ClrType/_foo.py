import clrtype

class Foo(object):
    __metaclass__ = clrtype.ClrClass

    bar = 1
    
    def __init__(self):
        import clr
        import System
        System.Diagnostics.Debugger.Break()
    
    @clrtype.accepts()
    @clrtype.returns(object)
    def frob(self):
        pass
