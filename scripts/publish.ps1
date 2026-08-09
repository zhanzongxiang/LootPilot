param([string]$Runtime = "win-x64")
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root "artifacts\LootPilot-$Runtime"
dotnet publish "$root\src\TarkovPriceOverlay\TarkovPriceOverlay.csproj" `
  -c Release -r $Runtime --self-contained false -o $out
Write-Host "Published: $out"
