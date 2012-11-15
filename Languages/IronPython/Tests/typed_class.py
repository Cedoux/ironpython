import clr

from System import ObsoleteAttribute

Obsolete = clr.wrap_attribute(ObsoleteAttribute)

class TypedClass(object):
    __metaclass__ = clr.ClrClass
    __clr_namespace__ = "IronPython.Tests"
    
    # TODO Constructors
    def __init__(self):
        self._value = -1
    
    # Properties
    @clr.property(int)
    def ReadOnlyProperty(self):
        return 42
    
    #~ # --
    @clr.property(int)
    def ReadWriteProperty(self):
        return self._value
    
    @ReadWriteProperty.setter
    def ReadWriteProperty(self, value):
        self.value = value
    
    # --
    @clr.property(int, virtual=False)
    def NonVirtualReadOnlyProperty(self):
        return 42
    
    @clr.staticproperty(int)
    def StaticReadOnlyProperty():
        return 42
    
    # --
    @clr.property(int)
    @clr.attributes([Obsolete])
    def PropertyWithAttribute1(self):
        return 42
    
    #~ @clr.attributes([Obsolete])
    #~ @clr.property(int)
    #~ def PropertyWithAttribute2(self):
        #~ return 42
    
    # Methods
    def UntypedMethod(self):
        pass
    
    @clr.method(int)
    def ReturnOnlyMethod(self):
        pass
    
    @clr.method(int, [str, float])
    def MethodWithArgs(self, a, b):
        pass
    
    @clr.method(int, [str, float], virtual=False)
    def NonVirtualMethodWithArgs(self, a, b):
        pass

    @clr.method(int, [str, float])
    @clr.attributes([Obsolete])
    def MethodWithAttribute1(self, a, b):
        pass
    
    @clr.attributes([Obsolete])
    @clr.method(int, [str, float])
    def MethodWithAttribute2(self, a, b):
        pass
    
    #~ # static methods
    @clr.staticmethod(int, [str, float])
    def StaticMethodWithArgs(a, b):
        pass
    
    #~ @clr.staticmethod(int, [str, float])
    #~ @clr.attributes([Obsolete])
    #~ def StaticMethodWithAttribute1(self, a, b):
        #~ pass
    
    #~ @clr.attributes([Obsolete])
    #~ @clr.staticmethod(int, [str, float])
    #~ def StaticMethodWithAttribute2(self, a, b):
        #~ pass
