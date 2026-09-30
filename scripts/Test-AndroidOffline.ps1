param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug',
    [ValidateSet('android-arm64', 'android-arm', 'android-x64', 'android-x86')]
    [string]$RuntimeIdentifier = 'android-arm64',
    [switch]$Rebuild
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$cache = Join-Path $root 'offline-cache'
$env:JAVA_HOME = Join-Path $cache 'jdk'
$env:ANDROID_HOME = Join-Path $cache 'android-sdk'
$env:ANDROID_USER_HOME = Join-Path $cache 'android-user-home'
$env:ANDROID_SDK_HOME = $cache
$env:DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE = 'true'
[IO.Directory]::CreateDirectory($env:ANDROID_USER_HOME) | Out-Null
# Mono's AOT tool cannot reliably read response files under a Cyrillic TEMP path.
$env:TEMP = Join-Path $cache 'android-build-temp'
$env:TMP = $env:TEMP
[IO.Directory]::CreateDirectory($env:TEMP) | Out-Null
$project = Join-Path $root 'tests/AndroidPackageCache/AndroidPackageCache.csproj'
$arguments = @(
    "-p:Configuration=$Configuration",
    "-p:RuntimeIdentifiers=$RuntimeIdentifier",
    "-p:AndroidSdkDirectory=$env:ANDROID_HOME",
    "-p:JavaSdkDirectory=$env:JAVA_HOME",
    '-p:AndroidCommandLineToolsVersion=latest',
    '-p:UsedAvaloniaProducts=',
    "-p:_ApkDebugKeyStore=$(Join-Path $cache 'android-debug.keystore')"
)
Push-Location $root
try {
    & dotnet restore $project --configfile (Join-Path $root 'NuGet.Offline.Config') `
        --packages (Join-Path $cache 'android-verified') -p:NuGetAudit=false `
        --force --no-http-cache @arguments
    if ($LASTEXITCODE -ne 0) { throw 'Offline NuGet restore failed.' }
    $target = if ($Rebuild) { 'Rebuild;SignAndroidPackage' } else { 'SignAndroidPackage' }
    & dotnet build $project --no-restore -c $Configuration -r $RuntimeIdentifier `
        "-t:$target" @arguments
    if ($LASTEXITCODE -ne 0) { throw 'Offline Android build failed.' }
    Get-ChildItem (Join-Path $root "tests/AndroidPackageCache/bin/$Configuration") `
        -Filter '*-Signed.apk' -Recurse | Select-Object FullName, Length
} finally { Pop-Location }
