from iptest.assert_util import *
from iptest.process_util import *

def build_test_exe():
    run_csc("/t:exe /r:IronPython.dll "
            "/r:Microsoft.Dynamic.dll /r:Microsoft.Scripting.dll "
            "/r:typed_class.dll TestTypedClass.cs"
    )

def test_consumable():
    build_test_exe()
    result = launch('TestTypedClass.exe')
    AssertEqual(0, result)

run_test(__name__)