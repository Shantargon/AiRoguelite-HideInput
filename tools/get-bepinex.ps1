# Скачивает BepInEx и распаковывает в vendor\ — то, что нужно для сборки мода.
# Запуск: powershell -ExecutionPolicy Bypass -File tools\get-bepinex.ps1

$ErrorActionPreference = "Stop"

$version = "5.4.23.5"
$sha256  = "82f9878551030f54657792c0740d9d51a09500eeae1fba21106b0c441e6732c4"
$url     = "https://github.com/BepInEx/BepInEx/releases/download/v$version/BepInEx_win_x64_$version.zip"

$root    = Split-Path -Parent $PSScriptRoot
$dlDir   = Join-Path $root "vendor\bepinex-dl"
$outDir  = Join-Path $root "vendor\bepinex-extracted"
$zip     = Join-Path $dlDir "BepInEx_win_x64_$version.zip"

New-Item -ItemType Directory -Force $dlDir | Out-Null

if (-not (Test-Path $zip)) {
    Write-Host "Качаю BepInEx $version..."
    Invoke-WebRequest -Uri $url -OutFile $zip
}

$actual = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLower()
if ($actual -ne $sha256) {
    throw "Контрольная сумма не сошлась. Ожидали $sha256, получили $actual"
}

Write-Host "Распаковываю..."
New-Item -ItemType Directory -Force $outDir | Out-Null
Expand-Archive -Path $zip -DestinationPath $outDir -Force

Write-Host "Готово: $outDir"
