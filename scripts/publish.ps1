param(
  [string]$Runtime = "win-x64",
  [switch]$SkipTests
)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

[xml]$versionProps = Get-Content (Join-Path $root "Directory.Build.props")
$version = [string]$versionProps.Project.PropertyGroup.VersionPrefix
if ([string]::IsNullOrWhiteSpace($version)) {
  throw "VersionPrefix is missing from Directory.Build.props."
}

if (-not $SkipTests) {
  dotnet test (Join-Path $root "TarkovPriceOverlay.sln") -c Release
  if ($LASTEXITCODE -ne 0) {
    throw "Tests failed. Publish was stopped."
  }
}

$artifactsRoot = [IO.Path]::GetFullPath((Join-Path $root "artifacts"))
$out = [IO.Path]::GetFullPath((Join-Path $artifactsRoot "LootPilot-$version-$Runtime"))
$expectedPrefix = $artifactsRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if (-not $out.StartsWith($expectedPrefix, [StringComparison]::OrdinalIgnoreCase)) {
  throw "Refusing to clean a publish path outside the artifacts directory: $out"
}
if (Test-Path -LiteralPath $out) {
  Remove-Item -LiteralPath $out -Recurse -Force
}

dotnet publish "$root\src\TarkovPriceOverlay\TarkovPriceOverlay.csproj" `
  -c Release -r $Runtime --self-contained true -o $out
if ($LASTEXITCODE -ne 0) {
  throw "dotnet publish failed."
}

Copy-Item "$root\README.md" "$out\README.md" -Force
Copy-Item "$root\LICENSE" "$out\LICENSE" -Force
Copy-Item "$root\THIRD-PARTY-NOTICES.md" "$out\THIRD-PARTY-NOTICES.md" -Force

$ocrFiles = @(
  "PP-OCRv6_det_small.onnx",
  "PP-OCRv6_rec_small.onnx",
  "ch_ppocr_mobile_v2.0_cls_mobile.onnx",
  "ppocrv6_small_dict.txt"
)
$missingOcrFiles = $ocrFiles | Where-Object {
  -not (Test-Path -LiteralPath (Join-Path $out "ocr-models\$_") -PathType Leaf)
}
if ($missingOcrFiles.Count -gt 0) {
  throw "Publish validation failed. Missing OCR files: $($missingOcrFiles -join ', ')"
}
Write-Host "Published LootPilot $version`: $out"
