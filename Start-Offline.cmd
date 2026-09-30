@echo off
setlocal
cd /d "%~dp0"
set AVALONIA_TELEMETRY_OPTOUT=1
set DOTNET_CLI_TELEMETRY_OPTOUT=1
dotnet restore TbkReport.slnx --configfile NuGet.Offline.Config --packages offline-cache/installed-packages -p:NuGetAudit=false
if errorlevel 1 goto fail
dotnet build TbkReport.slnx -c Release --no-restore -m:1 -nodeReuse:false
if errorlevel 1 goto fail
start "" "%~dp0src\TbkReport\bin\Release\net10.0\TbkReport.exe"
exit /b 0
:fail
echo Offline build failed. See the error above.
pause
exit /b 1
