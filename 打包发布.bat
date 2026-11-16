@echo off
setlocal
cd /d "%~dp0"
title Package WifiList v1.6.2
set "VER=1.6.2"
set "LOG=%CD%\build.log"
set "OUT=%CD%\dist"

echo.
echo   ================================================
echo    Packaging WifiList v%VER%
echo      build\WifiList.exe                  portable exe
echo      build\WifiList-Setup-%VER%.exe      installer
echo      dist\WifiList-%VER%-portable.zip    portable zip
echo   ================================================
echo.

rem ---- locate the .NET Framework 4.x C# compiler ----
set "CSC="
if exist "%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not defined CSC if exist "%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not defined CSC (
    echo   [ERROR] .NET Framework 4.x compiler was not found.
    echo           Install .NET Framework 4.8 and run this again.
    echo.
    pause
    exit /b 1
)

if not exist "assets\template.html" (
    echo   [ERROR] assets\template.html is missing.
    echo.
    pause
    exit /b 1
)

if not exist "installer\Setup.cs" (
    echo   [ERROR] installer\Setup.cs is missing.
    echo.
    pause
    exit /b 1
)

echo   compiler : %CSC%
echo.

if not exist "build" md "build"

rem ================= 1. the application =================
echo   [1/4] compiling WifiList.exe ...
"%CSC%" /nologo /target:winexe /platform:anycpu /optimize+ /codepage:65001 ^
 /out:"build\WifiList.exe" /win32icon:assets\app.ico ^
 /resource:"assets\template.html",WifiList.Template.html ^
 /resource:"assets\app.ico",WifiList.AppIcon.ico ^
 /r:System.dll /r:System.Core.dll /r:System.Drawing.dll ^
 /r:System.Windows.Forms.dll /r:System.Xml.dll ^
 "src\AssemblyInfo.cs" "src\Program.cs" "src\Lang.cs" "src\UiKit.cs" "src\Chrome.cs" ^
 "src\Panels.cs" "src\Dialogs.cs" "src\MainForm.cs" "src\WifiData.cs" "src\WlanApi.cs" ^
 "src\HtmlExporter.cs" "src\Update.cs" > "%LOG%" 2>&1
set "RC=%ERRORLEVEL%"
if not "%RC%"=="0" goto appfail
if not exist "build\WifiList.exe" goto appfail
echo         ok

rem ================= 2. the installer =================
echo   [2/4] compiling the installer ...
"%CSC%" /nologo /target:winexe /platform:anycpu /optimize+ /codepage:65001 ^
 /out:"build\WifiList-Setup-%VER%.exe" /win32icon:assets\app.ico ^
 /resource:"build\WifiList.exe",WifiList.Payload.exe ^
 /r:System.dll /r:System.Core.dll /r:System.Drawing.dll ^
 /r:System.Windows.Forms.dll ^
 "installer\Setup.cs" >> "%LOG%" 2>&1
set "RC=%ERRORLEVEL%"
if not "%RC%"=="0" goto setupfail
if not exist "build\WifiList-Setup-%VER%.exe" goto setupfail
echo         ok

rem ================= 3. portable folder =================
echo   [3/4] preparing the portable folder ...
if exist "build\portable" rd /s /q "build\portable"
md "build\portable"
copy /y "build\WifiList.exe" "build\portable\WifiList.exe" >nul
if exist "assets\portable-readme.txt" copy /y "assets\portable-readme.txt" "build\portable\README.txt" >nul
echo         ok

rem ================= 4. dist =================
echo   [4/4] writing dist ...
if exist "%OUT%" rd /s /q "%OUT%"
md "%OUT%"
copy /y "build\WifiList.exe" "%OUT%\WifiList.exe" >nul
copy /y "build\WifiList-Setup-%VER%.exe" "%OUT%\WifiList-Setup-%VER%.exe" >nul
powershell -NoProfile -ExecutionPolicy Bypass -Command "Compress-Archive -Path '%CD%\build\portable\*' -DestinationPath '%OUT%\WifiList-%VER%-portable.zip' -Force" >> "%LOG%" 2>&1
if not exist "%OUT%\WifiList-%VER%-portable.zip" goto zipfail
echo         ok

echo.
echo   ================================================
echo    [OK] All artefacts are ready:
echo   ================================================
for %%F in ("%OUT%\*") do echo      %%~nxF   (%%~zF bytes)
echo.
echo    Folder: %OUT%
echo.
echo    Upload these two files to the GitHub release:
echo      WifiList-Setup-%VER%.exe      (installer)
echo      WifiList-%VER%-portable.zip   (portable)
echo.
exit /b 0

:appfail
echo.
echo   [FAILED] the application did not compile.
call :showlog
exit /b 1

:setupfail
echo.
echo   [FAILED] the installer did not compile.
call :showlog
exit /b 1

:zipfail
echo.
echo   [FAILED] the portable zip was not created.
call :showlog
exit /b 1

:showlog
echo   Full log: %LOG%
echo.
type "%LOG%"
echo.
pause
exit /b 1
