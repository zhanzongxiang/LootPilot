[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$buildScript = Join-Path $PSScriptRoot "scripts\build.ps1"

if (-not (Test-Path -LiteralPath $buildScript -PathType Leaf)) {
  throw "Build implementation was not found: $buildScript"
}

& $buildScript
if ($LASTEXITCODE -ne 0) {
  exit $LASTEXITCODE
}
