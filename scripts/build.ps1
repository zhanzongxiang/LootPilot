$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
dotnet restore "$root\TarkovPriceOverlay.sln"
dotnet build "$root\TarkovPriceOverlay.sln" -c Release --no-restore
Write-Host "Build complete: $root\src\TarkovPriceOverlay\bin\Release\net8.0-windows\"
