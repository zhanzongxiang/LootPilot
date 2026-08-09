param([string]$Runtime = "win-x64")
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root "artifacts\LootPilot-$Runtime"
dotnet publish "$root\src\TarkovPriceOverlay\TarkovPriceOverlay.csproj" `
  -c Release -r $Runtime --self-contained true -o $out
Copy-Item "$root\README.md" "$out\README.md" -Force
Copy-Item "$root\LICENSE" "$out\LICENSE" -Force
Copy-Item "$root\THIRD-PARTY-NOTICES.md" "$out\THIRD-PARTY-NOTICES.md" -Force
if (-not (Test-Path "$out\ocr-models\PP-OCRv6_det_small.onnx")) {
  throw "Publish validation failed: bundled OCR models are missing."
}
Write-Host "Published: $out"
