import clr
clr.AddReference("Microsoft.Build.Framework")
clr.AddReference("Microsoft.Build.Utilities.v4.0")

from System import Array
from System.Diagnostics import Debugger
from System.IO import Path

from Microsoft.Build.Utilities import ToolTask, CommandLineBuilder
from Microsoft.Build.Framework import MessageImportance, OutputAttribute, ITaskItem

Output = clr.wrap_attribute(OutputAttribute)

class Ipyc(ToolTask):
    __metaclass__ = clr.ClrClass
    __clr_namespace__ = "IronPython.MSBuild"

    ToolName = "ipyc.exe"

    def __init__(self):
        self.switches = {}

    @clr.property(bool)
    def CompileTypes(self):
        return self.switches.get("CompileTypes", False)
    
    @CompileTypes.setter
    def CompileTypes(self, value):
        self.switches["CompileTypes"] = value
    
    @clr.property(bool)
    def Embed(self):
        return self.switches.get("Embed", False)
    
    @Embed.setter
    def Embed(self, value):
        self.switches["Embed"] = value
    
    @clr.property(str)
    def MainFile(self):
        return self.switches.get("MainFile", False)
    
    @MainFile.setter
    def MainFile(self, value):
        self.switches["MainFile"] = value

    @clr.property(str)
    def MultiThreadedApartment(self):
        return self.switches.get("MultiThreadedApartment", False)
    
    @MultiThreadedApartment.setter
    def MultiThreadedApartment(self, value):
        self.switches["MultiThreadedApartment"] = value

    @clr.property(ITaskItem)
    @clr.attributes([Output])
    def OutputAssembly(self):
        return self.switches.get("OutputAssembly", None)
    
    @OutputAssembly.setter
    def OutputAssembly(self, value):
        self.switches["OutputAssembly"] = value

    @clr.property(str)
    def Platform(self):
        return self.switches.get("Platform", None)
    
    @Platform.setter
    def Platform(self, value):
        self.switches["Platform"] = value

    @clr.property(Array[ITaskItem])
    def Sources(self):
        return self.switches.get("Sources", None)
    
    @Sources.setter
    def Sources(self, value):
        self.switches["Sources"] = value
    
    @clr.property(bool)
    def Standalone(self):
        return self.switches.get("Standalone", False)
    
    @Standalone.setter
    def Standalone(self, value):
        self.switches["Standalone"] = value

    @clr.property(str)
    def TargetType(self):
        return self.switches.get("TargetType", "dll")
    
    @TargetType.setter
    def TargetType(self, value):
        self.switches["TargetType"] = value.lower()

    def GenerateCommandLineCommands(self):
        clb = CommandLineBuilder()

        clb.AppendSwitchIfNotNull("/out:", self.OutputAssembly)

        clb.AppendSwitchIfNotNull("/target:", self.TargetType)

        if self.CompileTypes:
            clb.AppendSwitch("/compiletypes")

        if self.TargetType in ('exe', 'winexe'):
            clb.AppendSwitchIfNotNull("/main:", self.MainFile)

            if self.Standalone:
                clb.AppendSwitch("/standalone")

            if self.Embed:
                clb.AppendSwitch("/embed")

            if self.TargetType == 'winexe':
                if self.MultiThreadedApartment:
                    clb.AppendSwitch('/mta')

        if self.Platform:
            clb.AppendSwitchIfNotNull("/platform:", self.Platform)

        clb.AppendFileNamesIfNotNull(self.Sources, " ")

        args = clb.ToString()

        self.Log.LogMessage(MessageImportance.High, "Args: %s" % args)

        return args

    def GenerateFullPathToTool(self):
        dir = r'C:\Users\Jeff\Documents\Repositories\jdhardy-ironpython\bin\Debug'
        return Path.Combine(dir, self.ToolName)
