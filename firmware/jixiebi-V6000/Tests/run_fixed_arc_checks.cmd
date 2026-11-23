@echo off
setlocal
rem Run from an x64 Native Tools Command Prompt for Visual Studio.
if "%~1"=="" (
  echo Usage: run_fixed_arc_checks.cmd ^<output directory^>
  exit /b 1
)
set "ARC_MASTER=%~dp0.."
set "ARC_DRIVER=%~dp0..\..\user-firmware-keil-57"
if not exist "%~1" mkdir "%~1"
pushd "%~1"
if errorlevel 1 exit /b 1
cl /nologo /O2 /W3 /std:c11 /I"%ARC_MASTER%\Tests\stubs" /I"%ARC_MASTER%\USER\Inc" "%ARC_MASTER%\USER\Src\can_protocol.c" "%ARC_MASTER%\USER\Src\host_bridge.c" "%ARC_MASTER%\USER\Src\serial_cmd.c" "%ARC_MASTER%\USER\Src\gripper_servo.c" "%ARC_MASTER%\Tests\can42_checks.c" /Fe:can42-arc-checks.exe
if errorlevel 1 goto failed
can42-arc-checks.exe
if errorlevel 1 goto failed
cl /nologo /c /O2 /W3 /std:c11 /I"%ARC_MASTER%\Tests\stubs" /I"%ARC_MASTER%\USER\Inc" "%ARC_MASTER%\USER\Src\can_protocol.c" "%ARC_MASTER%\USER\Src\host_bridge.c" "%ARC_MASTER%\USER\Src\serial_cmd.c" "%ARC_MASTER%\USER\Src\gripper_servo.c"
if errorlevel 1 goto failed
cl /nologo /O2 /EHsc /std:c++17 /I"%ARC_MASTER%\Tests\stubs" /I"%ARC_MASTER%\USER\Inc" /I"%ARC_DRIVER%\Ctrl\Motor" "%ARC_MASTER%\Tests\fixed_arc_integration.cpp" can_protocol.obj host_bridge.obj serial_cmd.obj gripper_servo.obj /Fe:fixed-arc-integration.exe
if errorlevel 1 goto failed
fixed-arc-integration.exe actual-arc-samples.json
if errorlevel 1 goto failed
popd
exit /b 0
:failed
popd
exit /b 1
