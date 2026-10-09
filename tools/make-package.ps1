# Собирает архив, который достаточно распаковать в папку игры.
# Запуск: powershell -ExecutionPolicy Bypass -File tools\make-package.ps1

$ErrorActionPreference = "Stop"

$root     = Split-Path -Parent $PSScriptRoot
$vendor   = Join-Path $root "vendor\bepinex-extracted"
$modDll   = Join-Path $root "src\HidePlayerInput\bin\Release\HidePlayerInput.dll"
$stage    = Join-Path $root "build\stage"
$distDir  = Join-Path $root "dist"

if (-not (Test-Path $vendor)) { throw "Нет vendor\bepinex-extracted — сначала tools\get-bepinex.ps1" }
if (-not (Test-Path $modDll)) { throw "Нет собранного мода — сначала dotnet build -c Release" }

$version = ([xml](Get-Content (Join-Path $root "src\HidePlayerInput\HidePlayerInput.csproj"))).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1

if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force "$stage\BepInEx\plugins" | Out-Null

Copy-Item "$vendor\winhttp.dll"          $stage -Force
Copy-Item "$vendor\doorstop_config.ini"  $stage -Force
Copy-Item "$vendor\.doorstop_version"    $stage -Force
Copy-Item "$vendor\BepInEx\core"         "$stage\BepInEx" -Recurse -Force
Copy-Item $modDll                        "$stage\BepInEx\plugins" -Force
Copy-Item (Join-Path $root "docs\ЧИТАЙ_МЕНЯ.txt") $stage -Force

New-Item -ItemType Directory -Force $distDir | Out-Null
$zip = Join-Path $distDir "HidePlayerInput-$version.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path "$stage\*" -DestinationPath $zip -Force

Write-Host "Готово: $zip"
Write-Host ("Размер: {0:N0} КБ" -f ((Get-Item $zip).Length / 1KB))
