param()

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$offlineCache = Join-Path $projectRoot 'offline-cache'
$signingDirectory = Join-Path $projectRoot 'signing'
$keyStore = Join-Path $signingDirectory 'tbk-report-release.keystore'
$passwordFile = Join-Path $signingDirectory 'tbk-report-release.password'

if (-not (Test-Path -LiteralPath $keyStore) -or -not (Test-Path -LiteralPath $passwordFile)) {
    throw 'The permanent Android Release key or password is missing from the signing directory.'
}

$env:JAVA_HOME = Join-Path $offlineCache 'jdk'
$env:ANDROID_HOME = Join-Path $offlineCache 'android-sdk'
$env:ANDROID_USER_HOME = Join-Path $offlineCache 'android-user-home'
$env:ANDROID_SDK_HOME = $offlineCache
$env:DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE = 'true'
$env:TBK_ANDROID_SIGNING_PASSWORD = [IO.File]::ReadAllText($passwordFile).Trim()
$env:TEMP = Join-Path $offlineCache 'android-build-temp'
$env:TMP = $env:TEMP

[IO.Directory]::CreateDirectory($env:ANDROID_USER_HOME) | Out-Null
[IO.Directory]::CreateDirectory($env:TEMP) | Out-Null

$project = Join-Path $projectRoot 'src/TbkReport.Android/TbkReport.Android.csproj'
$arguments = @(
    '--no-restore',
    '-c', 'Release',
    '-r', 'android-arm64',
    '-t:SignAndroidPackage',
    '-p:RuntimeIdentifiers=android-arm64',
    "-p:AndroidSdkDirectory=$env:ANDROID_HOME",
    "-p:JavaSdkDirectory=$env:JAVA_HOME",
    '-p:AndroidCommandLineToolsVersion=latest',
    '-p:UsedAvaloniaProducts='
)

Push-Location $projectRoot
try {
    & dotnet build $project @arguments
    if ($LASTEXITCODE -ne 0) { throw 'Android Release build failed.' }
} finally {
    Pop-Location
}

Get-ChildItem (Join-Path $projectRoot 'src/TbkReport.Android/bin/Release') `
    -Filter '*-Signed.apk' -Recurse |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1 FullName, Length, LastWriteTime
