param([string]$Runtime = "win-x64")
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root "artifacts\LootPilot-$Runtime"
dotnet publish "$root\src\TarkovPriceOverlay\TarkovPriceOverlay.csproj" `
  -c Release -r $Runtime --self-contained true -o $out
Copy-Item "$root\README.md" "$out\README.md" -Force
Copy-Item "$root\LICENSE" "$out\LICENSE" -Force
Copy-Item "$root\THIRD-PARTY-NOTICES.md" "$out\THIRD-PARTY-NOTICES.md" -Force
Write-Host "Published: $out"
