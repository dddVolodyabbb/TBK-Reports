@echo off
setlocal
cd /d "%~dp0"
set AVALONIA_TELEMETRY_OPTOUT=1
set DOTNET_CLI_TELEMETRY_OPTOUT=1
rem Builds the Windows app using only the local cache offline-cache\packages.
rem The Android app is built separately: scripts\Build-AndroidRelease.ps1.
set PROJECT=src\TbkReport.Windows\TbkReport.csproj
dotnet restore "%PROJECT%" --configfile NuGet.Offline.Config --packages offline-cache\installed-packages -p:NuGetAudit=false
if errorlevel 1 goto fail
dotnet build "%PROJECT%" -c Release --no-restore -m:1 -nodeReuse:false
if errorlevel 1 goto fail
start "" "%~dp0src\TbkReport.Windows\bin\Release\net10.0\win-x64\TbkReport.exe"
exit /b 0
:fail
echo Offline build failed. See the error above.
pause
exit /b 1
