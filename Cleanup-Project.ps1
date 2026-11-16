# Очистка проекта ТБК Отчёт от тестовых и промежуточных сборок.
# Запуск из корня проекта:
#   powershell -NoProfile -ExecutionPolicy Bypass -File Cleanup-Project.ps1
# Скрипт сначала показывает план и ждёт подтверждения. Ничего не удаляется без ответа «да».

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
Set-Location -LiteralPath $root

# Релиз v1.3 на GitVerse: SHA-256 каждого файла (сверено 28.10.2026).
$release = 'artifacts\release-v1.3'
$expected = [ordered]@{
    'TBK-Report-v1.3-Android.apk'             = 'C36527DECBC13C4E50A61A458DC914E6EFD0ED14A70A4B718EBB0E5AA2EA29BA'
    'TBK-Report-v1.3-Windows.zip'             = 'FD5DB51FA997FC64C61845F8EABAD305F18E0BA38079751AFE8667E0F32DBFB4'
    'TBK-Report-v1.3-Features.docx'           = 'BBA9EBA461EDAD739CCC5711824128DA924FBEEE4667B6B1533BDC4AD78D3991'
    'TBK-Report-v1.3-Installation-Guide.docx' = 'B59BC135F3B9EDC174C659B0013F37DAAF7D4391547E01E664354A157FD8C008'
    'TBK-Report-v1.3-User-Guide.docx'         = 'BA3F54B021FB6E37EA84149D5F47AA701924349880C5CA60A6BC77E27C8E1F98'
}

# Что переносится, а не удаляется.
$moves = @(
    @{ From = $release;                         To = 'releases\v1.3' },
    @{ From = 'artifacts\windows-data-backups'; To = 'backups\windows-data-backups' }
)

# Что удаляется целиком (всё это либо тестовые сборки, либо пересобирается заново).
$remove = @(
    'builds',
    'artifacts',
    'На публикацию',
    'tools\android-native-diagnostics',
    'offline-cache\android-build-temp',
    '.dotnet-home',
    'android-build.log',
    'TODO.md'
)
$remove += Get-ChildItem -LiteralPath 'src', 'tests' -Directory |
    ForEach-Object { Join-Path $_.FullName 'bin'; Join-Path $_.FullName 'obj' } |
    Where-Object { Test-Path -LiteralPath $_ } |
    ForEach-Object { Resolve-Path -LiteralPath $_ -Relative }

# 1. Проверка релиза до любых изменений.
$sourceRelease = if (Test-Path -LiteralPath $release) { $release } else { 'releases\v1.3' }
foreach ($name in $expected.Keys) {
    $path = Join-Path $sourceRelease $name
    if (-not (Test-Path -LiteralPath $path)) { throw "Нет файла релиза: $path. Очистка отменена." }
    $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
    if ($hash -ne $expected[$name]) { throw "Файл $path не совпадает с релизом на GitVerse. Очистка отменена." }
}
Write-Host "Релиз v1.3 совпадает с GitVerse: $($expected.Count) файлов." -ForegroundColor Green

# 2. План.
Write-Host "`nБудет перенесено:" -ForegroundColor Cyan
foreach ($m in $moves) { if (Test-Path -LiteralPath $m.From) { Write-Host "  $($m.From)  ->  $($m.To)" } }
Write-Host "  (из releases\v1.3 убирается TBK-Report-v1.3-Setup.exe: он уже лежит внутри Windows.zip)"
Write-Host "`nБудет удалено:" -ForegroundColor Yellow
$total = 0L
foreach ($item in $remove) {
    if (-not (Test-Path -LiteralPath $item)) { continue }
    $size = (Get-ChildItem -LiteralPath $item -Recurse -File -Force -ErrorAction SilentlyContinue |
        Measure-Object Length -Sum).Sum
    if ($null -eq $size) { $size = (Get-Item -LiteralPath $item -Force).Length }
    $total += $size
    Write-Host ("  {0,-60} {1,10:N1} МБ" -f $item, ($size / 1MB))
}
Write-Host ("Итого освободится примерно {0:N0} МБ." -f ($total / 1MB))

$answer = (Read-Host "`nВыполнить? Введите «да» (или y)").Trim().ToLowerInvariant()
if ($answer -notin @('да', 'д', 'y', 'yes')) { Write-Host 'Отменено, ничего не изменено.'; exit 0 }

# 3. Перенос.
foreach ($m in $moves) {
    if (-not (Test-Path -LiteralPath $m.From)) { continue }
    if (Test-Path -LiteralPath $m.To) { throw "Папка $($m.To) уже существует. Перенос остановлен." }
    New-Item -ItemType Directory -Force -Path (Split-Path $m.To -Parent) | Out-Null
    Move-Item -LiteralPath $m.From -Destination $m.To
    Write-Host "Перенесено: $($m.From) -> $($m.To)"
}
$setup = 'releases\v1.3\TBK-Report-v1.3-Setup.exe'
if (Test-Path -LiteralPath $setup) { Remove-Item -LiteralPath $setup -Force }

# 4. Удаление.
foreach ($item in $remove) {
    if (-not (Test-Path -LiteralPath $item)) { continue }
    Remove-Item -LiteralPath $item -Recurse -Force
    Write-Host "Удалено: $item"
}

Write-Host "`nГотово. Сборка: Start-Offline.cmd" -ForegroundColor Green
