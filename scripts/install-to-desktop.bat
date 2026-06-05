@echo off
setlocal

set "SRC=%~dp0..\dist"
set "TARGET=%USERPROFILE%\Desktop\CVBridge"

if not exist "%SRC%\CVBridge.exe" (
  echo CVBridge.exe not found. Run scripts\build.ps1 first.
  pause
  exit /b 1
)

if not exist "%TARGET%" mkdir "%TARGET%"

copy /y "%SRC%\CVBridge.exe" "%TARGET%\CVBridge.exe" >nul
copy /y "%SRC%\CVBridge.example.ini" "%TARGET%\CVBridge.example.ini" >nul

echo Installed to:
echo %TARGET%
echo.
pause

