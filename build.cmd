@echo off
rem Builds LiteDPI with the C# compiler that ships with Windows (.NET Framework 4.x). No SDK needed.
setlocal
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist "%CSC%" (
  echo Could not find csc.exe. It ships with .NET Framework 4.x, which is part of Windows 10 and 11.
  exit /b 1
)
cd /d "%~dp0"

"%CSC%" /nologo /optimize+ /target:winexe /out:LiteDPI.exe ^
  /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Core.dll ^
  src\AssemblyInfo.cs src\Hidpp.cs src\Config.cs src\DpiWorker.cs src\App.cs src\SettingsForm.cs || exit /b 1

"%CSC%" /nologo /optimize+ /target:exe /out:litedpi-probe.exe ^
  src\AssemblyInfo.cs src\Hidpp.cs src\Probe.cs || exit /b 1

echo Built LiteDPI.exe and litedpi-probe.exe
