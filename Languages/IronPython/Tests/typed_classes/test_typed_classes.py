import os
import sys

from iptest.assert_util import *
from iptest.process_util import *

def build_test_dll():
    try:
        os.unlink('typed_class.dll')
    except OSError:
        pass
    
    result = run_tool(sys.executable,
        '-S -c "import clr; clr.CompileModules(\'typed_class.dll\', \'typed_class.py\', compileTypes=True)"'
    )
    
    #~ result = launch_ironpython('-S',
        #~ '-c',
        #~ '"import clr;'
        #~ "clr.CompileModules('typed_class.dll', 'typed_class.py', compileTypes=True)\""
    #~ )
    
    if result != 0:
        raise Exception

def build_test_exe():
    try:
        os.unlink('TestTypedClass.exe')
    except OSError:
        pass
    
    result = run_csc("/t:exe /debug /r:IronPython.dll "
        "/r:Microsoft.Dynamic.dll /r:Microsoft.Scripting.dll "
        "/r:typed_class.dll TestTypedClass.cs"
    )
    
    if result != 0:
        raise Exception

def test_consumable():
    result = launch('TestTypedClass.exe')
    AreEqual(result, 0)

build_test_dll()
build_test_exe()

run_test(__name__)