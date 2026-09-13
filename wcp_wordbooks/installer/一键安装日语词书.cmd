@echo off
chcp 65001 >nul
setlocal
title WCP 日语词书一键安装
echo ========================================
echo   WCP 日语词书一键安装
echo   安装窗口会一直保持，完成后按 Enter 关闭
echo ========================================
echo.
PowerShell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0保持窗口-运行安装.ps1"
set "rc=%ERRORLEVEL%"
echo.
if not "%rc%"=="0" echo 安装未完成，错误代码 %rc%。
if "%rc%"=="0" echo 安装完成。请重新启动万词破。
exit /b %rc%
