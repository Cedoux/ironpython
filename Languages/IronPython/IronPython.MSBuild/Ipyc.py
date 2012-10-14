import clr
clr.AddReference("Microsoft.Build.Utilities.v4.0")

from Microsoft.Build.Utilities import Task

class Ipyc(Task):
    __metaclass__ = clr.ClrClass
    __clr_namespace__ = "IronPython.MSBuild"

    def Execute(self):
        self.Log.LogMessage(MessageImportance.High, "Hello, world");
        return True
