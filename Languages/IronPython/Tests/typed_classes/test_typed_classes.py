
from iptest.assert_util import *
from iptest.process_util import *

def build_test_dll():
    result = launch_ironpython('-S',
        '-c',
        '"import clr;'
        "clr.CompileModules('typed_class.dll', 'typed_class.py', compileTypes=True)\""
    )
    
    if result != 0:
        raise Exception

def build_test_exe():
    result = run_csc("/t:exe /r:IronPython.dll "
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