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
    # Restore only from the local offline cache so the build never needs the internet
    # and still works after bin/obj folders have been cleaned. No RID options here: global -r or
    # -p:RuntimeIdentifiers would also apply to TbkReport.Core and require an unneeded Android
    # runtime pack for net10.0. The Android project declares android-arm64 itself.
    & dotnet restore $project `
        -p:Configuration=Release `
        --configfile (Join-Path $projectRoot 'NuGet.Offline.Config') `
        --packages (Join-Path $offlineCache 'installed-packages') `
        -p:NuGetAudit=false
    if ($LASTEXITCODE -ne 0) { throw 'Android offline restore failed.' }

    & dotnet build $project @arguments
    if ($LASTEXITCODE -ne 0) { throw 'Android Release build failed.' }
} finally {
    Pop-Location
}

# The signed APK is copied next to the Windows installer under a name with the version from Version.props,
# like TBK-Report-v2.0-Android.apk («-test» at the end for a labelled build), the same way as the Setup.exe name.
[xml]$versionProps = Get-Content -LiteralPath (Join-Path $projectRoot 'Version.props') -Encoding UTF8
$versionGroup = $versionProps.Project.PropertyGroup
$appVersion = ([string]$versionGroup.TbkVersion).Trim()
if ($appVersion -notmatch '^\d+\.\d+\.\d+$') { throw "Version.props: TbkVersion must look like 2.0.0, not '$appVersion'." }
$parts = $appVersion.Split('.')
$appVersionShort = if ($parts[2] -eq '0') { "$($parts[0]).$($parts[1])" } else { $appVersion }
$apkSuffix = if (([string]$versionGroup.TbkVersionLabel).Trim()) { '-test' } else { '' }

$signedApk = Get-ChildItem (Join-Path $projectRoot 'src/TbkReport.Android/bin/Release/net10.0-android36.0/android-arm64') `
    -Filter '*-Signed.apk' |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1
if (-not $signedApk) { throw 'The signed APK was not found after the build.' }

$apkName = "TBK-Report-v$appVersionShort-Android$apkSuffix.apk"
$outputDirectory = Join-Path $projectRoot 'artifacts/installer'
[IO.Directory]::CreateDirectory($outputDirectory) | Out-Null
# One copy right in the build folder next to the *-Signed.apk, one next to the Windows installer.
foreach ($directory in @($signedApk.DirectoryName, $outputDirectory)) {
    $namedApk = Join-Path $directory $apkName
    Copy-Item -LiteralPath $signedApk.FullName -Destination $namedApk -Force
    Get-Item -LiteralPath $namedApk | Select-Object FullName, Length, LastWriteTime
}
