param()

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$publishDirectory = Join-Path $projectRoot 'artifacts/windows-installer-app-self-contained'
$installerScript = Join-Path $projectRoot 'installer/TbkReport.iss'
$localCompiler = Join-Path $projectRoot 'tools/Inno Setup 6/ISCC.exe'
$installedCompilers = @(
    $localCompiler,
    'C:\Program Files (x86)\Inno Setup 6\ISCC.exe',
    'C:\Program Files\Inno Setup 6\ISCC.exe'
)
$compiler = $installedCompilers | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $compiler) { throw 'Inno Setup compiler was not found.' }

if (Test-Path -LiteralPath $publishDirectory) {
    Remove-Item -LiteralPath $publishDirectory -Recurse -Force
}
[IO.Directory]::CreateDirectory($publishDirectory) | Out-Null

Push-Location $projectRoot
try {
    & dotnet restore 'src/TbkReport.Windows/TbkReport.csproj' -r win-x64 `
        -p:Configuration=Release `
        --configfile 'NuGet.Offline.Config' `
        --packages (Join-Path $projectRoot 'offline-cache/installed-packages') `
        -p:NuGetAudit=false -p:DisableTransitiveFrameworkReferenceDownloads=true
    if ($LASTEXITCODE -ne 0) { throw 'Windows self-contained restore failed.' }

    & dotnet publish 'src/TbkReport.Windows/TbkReport.csproj' -c Release -r win-x64 `
        --self-contained true --no-restore -o $publishDirectory
    if ($LASTEXITCODE -ne 0) { throw 'Windows self-contained publish failed.' }

    # IDE-only Avalonia files, the macOS renderer and .NET debugging helpers are
    # not used by the installed Windows application.
    $releaseOnlyUnusedFiles = @(
        'Avalonia.DesignerSupport.dll',
        'Avalonia.Metal.dll',
        'createdump.exe',
        'Microsoft.DiaSymReader.Native.amd64.dll',
        'mscordaccore.dll',
        'mscordaccore_amd64_amd64_10.0.1226.42308.dll',
        'mscordbi.dll'
    )
    foreach ($fileName in $releaseOnlyUnusedFiles) {
        $filePath = Join-Path $publishDirectory $fileName
        if (Test-Path -LiteralPath $filePath) { Remove-Item -LiteralPath $filePath -Force }
    }

    Get-ChildItem -LiteralPath $publishDirectory -Recurse -File -Filter '*.pdb' |
        Remove-Item -Force

    & $compiler $installerScript
    if ($LASTEXITCODE -ne 0) { throw 'Windows installer compilation failed.' }
} finally {
    Pop-Location
}

Get-Item (Join-Path $projectRoot 'artifacts/installer/TBK-Report-v1.3-Setup.exe') |
    Select-Object FullName, Length, LastWriteTime
