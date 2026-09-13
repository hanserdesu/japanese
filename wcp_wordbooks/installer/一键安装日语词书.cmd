@echo off
chcp 65001 >nul
setlocal
PowerShell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install-WCP-Japanese.ps1"
set "rc=%ERRORLEVEL%"
echo.
if not "%rc%"=="0" echo 安装未完成，错误代码 %rc%。
if "%rc%"=="0" echo 安装完成。请重新启动万词破。
pause
exit /b %rc%
