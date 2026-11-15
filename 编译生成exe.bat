@echo off
setlocal
cd /d "%~dp0"
title Build WifiList.exe v1.2.0

echo.
echo   ============================================
echo    Building WifiList.exe  v1.2.0
echo    native WinForms app, no installer needed
echo   ============================================
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
    echo           Keep the assets folder next to this file.
    echo.
    pause
    exit /b 1
)

rem /win32icon changes the icon Explorer shows for the .exe.
rem /resource embeds the same .ico so the taskbar can use it at runtime
rem (Explorer icon alone does not change the running window's taskbar icon).
set "ICONARG="
set "ICONRES="
if exist "assets\app.ico" (
    set "ICONARG=/win32icon:assets\app.ico"
    set "ICONRES=/resource:assets\app.ico,WifiList.AppIcon.ico"
)

echo   compiler : %CSC%
echo   output   : %CD%\WifiList.exe
echo.

:build
"%CSC%" /nologo /target:winexe /platform:anycpu /optimize+ /codepage:65001 ^
 /out:"WifiList.exe" %ICONARG% ^
 /resource:"assets\template.html",WifiList.Template.html %ICONRES% ^
 /r:System.dll /r:System.Core.dll /r:System.Drawing.dll ^
 /r:System.Windows.Forms.dll /r:System.Xml.dll ^
 "src\AssemblyInfo.cs" "src\Program.cs" "src\UiKit.cs" "src\Chrome.cs" ^
 "src\Panels.cs" "src\Dialogs.cs" "src\MainForm.cs" ^
 "src\WifiData.cs" "src\HtmlExporter.cs"

if errorlevel 1 goto onerror
if not exist "WifiList.exe" goto onerror
goto done

:onerror
if defined ICONARG (
    echo.
    echo   [retry] Retrying once without the custom icon...
    echo.
    set "ICONARG="
    set "ICONRES="
    goto build
)
goto failed

:done
echo.
echo   [OK] WifiList.exe v1.2.0 built successfully.
echo.
echo   From now on just double-click WifiList.exe - no rebuild needed.
echo.
start "" "WifiList.exe"
timeout /t 3 >nul
exit /b 0

:failed
echo.
echo   [FAILED] The compiler reported errors above.
echo   Copy the whole window and send it back for a fix.
echo.
pause
exit /b 1
