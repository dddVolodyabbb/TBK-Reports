param([switch]$CoreOnly)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$cache = Join-Path $root 'offline-cache'
Add-Type -AssemblyName System.IO.Compression.FileSystem

function Expand-VerifiedTool($archiveName, $relativeDestination, $algorithm, $expectedHash) {
    $archive = Join-Path $cache "android-downloads/$archiveName"
    if ((Get-FileHash -LiteralPath $archive -Algorithm $algorithm).Hash -ne $expectedHash) {
        throw "Incomplete or damaged archive: $archiveName"
    }
    $destination = [IO.Path]::GetFullPath((Join-Path $cache $relativeDestination)).TrimEnd('\') + '\'
    [IO.Directory]::CreateDirectory($destination) | Out-Null
    $zip = [IO.Compression.ZipFile]::OpenRead($archive)
    try {
        foreach ($entry in $zip.Entries) {
            # Vendor archives contain a single top-level directory.
            $separator = $entry.FullName.IndexOf('/')
            if ($separator -lt 0) { continue }
            $relative = $entry.FullName.Substring($separator + 1)
            if (!$relative) { continue }
            $target = [IO.Path]::GetFullPath((Join-Path $destination $relative))
            if (!$target.StartsWith($destination, [StringComparison]::OrdinalIgnoreCase)) {
                throw "Unsafe ZIP entry: $($entry.FullName)"
            }
            if ($entry.FullName.EndsWith('/')) {
                [IO.Directory]::CreateDirectory($target) | Out-Null
            } else {
                [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target)) | Out-Null
                [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $target, $true)
            }
        }
    } finally { $zip.Dispose() }
    Write-Host "Verified and extracted: $archiveName"
}

Expand-VerifiedTool 'microsoft-jdk-21-windows-x64.zip' 'jdk' 'SHA256' '192441a9d27da813bada974bb88b4cf64d37a9589ed37f204374d411ca5ce07f'
Expand-VerifiedTool 'platform-36_r02.zip' 'android-sdk/platforms/android-36' 'SHA1' '2c1a80dd4d9f7d0e6dd336ec603d9b5c55a6f576'
Expand-VerifiedTool 'build-tools_r36_windows.zip' 'android-sdk/build-tools/36.0.0' 'SHA1' 'f16ccffd34de8790dede813a6c7d8e2c11a27b50'
Expand-VerifiedTool 'platform-tools_r37.0.1-win.zip' 'android-sdk/platform-tools' 'SHA1' 'e03e78b1d80b396f1c3358e31251cb31740e1110'
if (!$CoreOnly) {
    Expand-VerifiedTool 'commandlinetools-win-16111833_latest.zip' 'android-sdk/cmdline-tools/latest' 'SHA1' '57d04f2d75eb8e8fffc5000a987e5de4b5a63e9d'
}
