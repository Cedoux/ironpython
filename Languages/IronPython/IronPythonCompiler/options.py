import System

class Config(object):
    def __init__(self):
        self.output = None
        self.main = None
        self.main_name = None
        self.compile_types = False
        self.target = System.Reflection.Emit.PEFileKinds.Dll
        self.embed = False
        self.standalone = False
        self.mta = False
        self.platform = System.Reflection.PortableExecutableKinds.ILOnly
        self.machine = System.Reflection.ImageFileMachine.I386
        self.files = []

    def ParseArgs(self, args, respFiles=[]):
        for arg in args:
            arg = arg.strip()
            if arg.startswith("#"):
                continue

            if arg.startswith("/main:"):
                self.main_name = self.main = arg[6:]
                # only override the target kind if its current a DLL
                if self.target == System.Reflection.Emit.PEFileKinds.Dll:
                    self.target = System.Reflection.Emit.PEFileKinds.ConsoleApplication

            elif arg.startswith("/out:"):
                self.output = arg[5:]

            elif arg.startswith("/target:"):
                tgt = arg[8:]
                if tgt == "exe": self.target = System.Reflection.Emit.PEFileKinds.ConsoleApplication
                elif tgt == "winexe": self.target = System.Reflection.Emit.PEFileKinds.WindowApplication
                else: self.target = System.Reflection.Emit.PEFileKinds.Dll

            elif arg.startswith("/platform:"):
                pform = arg[10:]
                if pform == "x86":
                    self.platform = System.Reflection.PortableExecutableKinds.ILOnly | System.Reflection.PortableExecutableKinds.Required32Bit
                    self.machine  = System.Reflection.ImageFileMachine.I386
                elif pform == "x64":
                    self.platform = System.Reflection.PortableExecutableKinds.ILOnly | System.Reflection.PortableExecutableKinds.PE32Plus
                    self.machine  = System.Reflection.ImageFileMachine.AMD64
                else:
                    self.platform = System.Reflection.PortableExecutableKinds.ILOnly
                    self.machine  = System.Reflection.ImageFileMachine.I386

            elif arg.startswith("/embed"):
                self.embed = True

            elif arg.startswith("/standalone"):
                self.standalone = True

            elif arg.startswith("/mta"):
                self.mta = True

            elif arg.startswith("/compiletypes"):
                self.compile_types = True

            elif arg in ["/?", "-?", "/h", "-h"]:
                print __doc__
                sys.exit(0)

            else:
                if arg.startswith("@"):
                    respFile = System.IO.Path.GetFullPath(arg[1:])
                    if not respFile in respFiles:
                        respFiles.append(respFile)
                        with open(respFile, 'r') as f:
                           self.ParseArgs(f.readlines(), respFiles)
                    else:
                        print "WARNING: Already parsed response file '%s'\n" % arg[1:]
                else:
                    self.files.extend(arg.split(';'))

    def Validate(self):
        if not self.files and not self.main_name:
            print "No files or main defined"
            return False

        if self.target != System.Reflection.Emit.PEFileKinds.Dll and self.main_name == None:
            print "EXEs require /main:<filename> to be specified"
            return False

        if not self.output and self.main_name:
            self.output = System.IO.Path.GetFileNameWithoutExtension(self.main_name)
        elif not self.output and self.files:
            self.output = System.IO.Path.GetFileNameWithoutExtension(self.files[0])

        return True

    def __repr__(self):
        res = "Input Files:\n"
        for file in self.files:
            res += "\t%s\n" % file

        res += "Output:\n\t%s\n" % self.output
        res += "Target:\n\t%s\n" % self.target
        res += "Platform:\n\t%s\n" % self.platform
        res += "Machine:\n\t%s\n" % self.machine

        if self.target == System.Reflection.Emit.PEFileKinds.WindowApplication:
            res += "Threading:\n"
            if self.mta:
                res += "\tMTA\n"
            else:
                res += "\tSTA\n"        
        return res
