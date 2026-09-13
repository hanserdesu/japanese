@echo off
chcp 65001 >nul
setlocal
title WCP 日语词书一键安装
echo ========================================
echo   WCP 日语词书一键安装
echo   安装窗口会一直保持，请点击右上角 X 关闭
echo   安装完成后按 Enter 不会关闭窗口
echo ========================================
echo.
PowerShell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -NoExit -File "%~dp0保持窗口-运行安装.ps1"
set "rc=%ERRORLEVEL%"
exit /b %rc%
