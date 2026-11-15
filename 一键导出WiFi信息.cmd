@echo off
setlocal
title WiFi Inventory
set "PS1=%~dp0WifiList.ps1"
if not exist "%PS1%" (
    echo.
    echo   [ERROR] WifiList.ps1 not found next to this launcher.
    echo.
    pause
    exit /b 1
)
powershell -NoProfile -ExecutionPolicy Bypass -File "%PS1%" %*
set "RC=%ERRORLEVEL%"
if not "%RC%"=="0" (
    echo.
    pause
)
exit /b %RC%
