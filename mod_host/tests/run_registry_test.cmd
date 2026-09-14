@echo off
rem Build + run the host identity-layer regression test OUTSIDE the game.
rem Only Core/*.cs + tests/RegistryTest.cs are compiled - no Unity, no game refs.
rem Real inputs: packs/*/manifest.json and the live MyBook.es3 word lists.
rem Build output goes to %TEMP% (a build artifact, not source). Override with REGTEST_OUT.
setlocal
set HERE=%~dp0
if "%REGTEST_OUT%"=="" set REGTEST_OUT=%TEMP%\wcphost_regtest
if not exist "%REGTEST_OUT%" mkdir "%REGTEST_OUT%"
set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
"%CSC%" /nologo /target:exe /optimize+ /codepage:65001 ^
 /out:"%REGTEST_OUT%\RegistryTest.exe" ^
 "%HERE%..\Core\Json.cs" ^
 "%HERE%..\Core\Manifest.cs" ^
 "%HERE%..\Core\ResourceRouter.cs" ^
 "%HERE%..\Core\ILanguageStrategy.cs" ^
 "%HERE%..\Core\StrategyContext.cs" ^
 "%HERE%..\StrategyLoader.cs" ^
 "%HERE%RegistryTest.cs"
if errorlevel 1 (echo BUILD FAILED & exit /b 1)
echo BUILD OK
"%REGTEST_OUT%\RegistryTest.exe" %*
exit /b %errorlevel%
