@echo off
setlocal EnableExtensions
chcp 65001 >nul
title WCP 日语词书一键安装

set "ROOT=%~dp0"
set "SUPPORT=%ROOT%support"
if not exist "%SUPPORT%\保持窗口-运行安装.ps1" set "SUPPORT=%ROOT%"

echo ========================================
echo   WCP 日语词书一键安装
echo   请只双击此文件，无需打开 support 文件夹
echo ========================================
echo.

set "PS_EXE=%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe"
if exist "%SystemRoot%\Sysnative\WindowsPowerShell\v1.0\powershell.exe" set "PS_EXE=%SystemRoot%\Sysnative\WindowsPowerShell\v1.0\powershell.exe"
call :TestPowerShell "%PS_EXE%"
if not errorlevel 1 goto RunWindowsPowerShell

where pwsh.exe >nul 2>&1
if errorlevel 1 goto CompatibilityHelp
set "PS_EXE=pwsh.exe"
call :TestPowerShell "%PS_EXE%"
if not errorlevel 1 goto RunPowerShell7

:CompatibilityHelp
call "%SUPPORT%\兼容模式-说明.cmd"
exit /b %ERRORLEVEL%

:RunWindowsPowerShell
echo 已选择：系统 Windows PowerShell（兼容模式）。
"%PS_EXE%" -NoLogo -NoProfile -ExecutionPolicy Bypass -NoExit -File "%SUPPORT%\保持窗口-运行安装.ps1" -HostLabel "Windows PowerShell"
exit /b %ERRORLEVEL%

:RunPowerShell7
echo 已选择：PowerShell 7（兼容模式）。
"%PS_EXE%" -NoLogo -NoProfile -ExecutionPolicy Bypass -NoExit -File "%SUPPORT%\保持窗口-运行安装.ps1" -HostLabel "PowerShell 7"
exit /b %ERRORLEVEL%

:TestPowerShell
if not exist "%SUPPORT%\检查系统兼容性.ps1" exit /b 1
"%~1" -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%SUPPORT%\检查系统兼容性.ps1" >nul 2>&1
exit /b %ERRORLEVEL%
