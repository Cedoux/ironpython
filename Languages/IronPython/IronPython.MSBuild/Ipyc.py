import clr
clr.AddReference("Microsoft.Build.Framework")
clr.AddReference("Microsoft.Build.Utilities.v4.0")

from System.Diagnostics import Debugger

from Microsoft.Build.Utilities import Task
from Microsoft.Build.Framework import MessageImportance, RequiredAttribute

Required = clr.wrap_attribute(RequiredAttribute)

class Ipyc(Task):
    __metaclass__ = clr.ClrClass
    __clr_namespace__ = "IronPython.MSBuild"

    def __init__(self):
       self._target = "dll"

    @clr.property(str)
    @clr.attributes([Required])
    def Target(self):
        return self._target
    
    @Target.setter
    def Target(self, value):
        self._target = value

    def Execute(self):
        cmd = ["ipyc.exe"]
        cmd += ["/target:{}".format(self._target)]
        self.Log.LogMessage(MessageImportance.High, " ".join(cmd))
        return True
