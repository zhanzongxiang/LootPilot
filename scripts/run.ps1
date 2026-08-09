$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
dotnet run --project "$root\src\TarkovPriceOverlay\TarkovPriceOverlay.csproj"
