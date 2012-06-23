@echo off

setlocal

if "%DLR_ROOT%" == "" set DLR_ROOT=%~dp0..
if "%DLR_BIN%" == "" set DLR_BIN=%DLR_ROOT%\bin\Debug

set _test_root=%DLR_ROOT%\Test
set _runner=%_test_root%\TestRunner\TestRunner\bin\Debug\TestRunner.exe
set _binpath=%DLR_BIN%

call :build_runner

"%_runner%" "%_test_root%\IronPython.tests" /verbose /threads:2 /binpath:"%_binpath%" %*

endlocal
goto:eof

:build_runner
%windir%\Microsoft.NET\Framework\v4.0.30319\MSBuild.exe /t:Rebuild %_test_root%\ClrAssembly\ClrAssembly.csproj /p:Configuration=Debug /v:quiet /nologo
%windir%\Microsoft.NET\Framework\v4.0.30319\MSBuild.exe /t:Rebuild %_test_root%\TestRunner\TestRunner.sln /p:Configuration=Debug /v:quiet /nologo
goto:eof
