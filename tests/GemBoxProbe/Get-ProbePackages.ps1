param([string]$Id, [string]$Version)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
Add-Type -AssemblyName System.IO.Compression.FileSystem
$packageId = $Id.ToLowerInvariant()
$packageDirectory = Join-Path $PSScriptRoot "../../offline-cache/packages/$packageId/$Version"
[IO.Directory]::CreateDirectory($packageDirectory) | Out-Null
$packagePath = Join-Path $packageDirectory "$packageId.$Version.nupkg"
if (!(Test-Path -LiteralPath ($packagePath + '.sha512'))) {
    & curl.exe --fail --location --continue-at - --retry 3 --retry-all-errors --connect-timeout 20 --max-time 240 --silent --show-error --output $packagePath "https://api.nuget.org/v3-flatcontainer/$packageId/$Version/$packageId.$Version.nupkg"
    if ($LASTEXITCODE -ne 0) { throw "Package download failed: $packageId $Version" }
    [IO.Compression.ZipFile]::ExtractToDirectory($packagePath, $packageDirectory)
    $hash = [Security.Cryptography.SHA512]::Create()
    try { [IO.File]::WriteAllText($packagePath + '.sha512', [Convert]::ToBase64String($hash.ComputeHash([IO.File]::ReadAllBytes($packagePath)))) }
    finally { $hash.Dispose() }
}
Write-Output "$packageId $Version cached"
