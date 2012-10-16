import clr
clr.AddReference("Microsoft.Build.Framework")
clr.AddReference("Microsoft.Build.Utilities.v4.0")

from System.Diagnostics import Debugger

from Microsoft.Build.Utilities import Task
from Microsoft.Build.Framework import MessageImportance

class Ipyc(Task):
    __metaclass__ = clr.ClrClass
    __clr_namespace__ = "IronPython.MSBuild"

    def Execute(self):
        self.Log.LogMessage(MessageImportance.High, "Hello, world");
        return True
