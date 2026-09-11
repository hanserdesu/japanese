@echo off
rem Build WCP Sentence Audio plugin (BepInEx 5, C#5 via NETFX csc)
set GAME=E:\SteamLibrary\steamapps\common\WCP-WordGirlgriend
set MGD=%GAME%\wcp_Data\Managed
set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
set REFS=/r:"%GAME%\BepInEx\core\BepInEx.dll" ^
 /r:"%MGD%\netstandard.dll" ^
 /r:"%MGD%\mscorlib.dll" ^
 /r:"%MGD%\System.dll" ^
 /r:"%MGD%\System.Core.dll" ^
 /r:"%MGD%\UnityEngine.dll" ^
 /r:"%MGD%\UnityEngine.CoreModule.dll" ^
 /r:"%MGD%\UnityEngine.UIModule.dll" ^
 /r:"%MGD%\UnityEngine.AudioModule.dll" ^
 /r:"%MGD%\UnityEngine.UnityWebRequestModule.dll" ^
 /r:"%MGD%\UnityEngine.UnityWebRequestAudioModule.dll" ^
 /r:"%MGD%\UnityEngine.UI.dll" ^
 /r:"%MGD%\UnityEngine.TextRenderingModule.dll" ^
 /r:"%MGD%\Unity.TextMeshPro.dll"
"%CSC%" /nologo /noconfig /nostdlib+ /target:library /langversion:5 /optimize+ %REFS% ^
 /out:"%~dp0SentenceAudioMod.dll" "%~dp0SentenceAudioMod.cs"
if errorlevel 1 (echo BUILD FAILED & exit /b 1)
echo BUILD OK
copy /y "%~dp0SentenceAudioMod.dll" "%GAME%\BepInEx\plugins\SentenceAudioMod.dll" >nul
echo DEPLOYED to BepInEx\plugins
