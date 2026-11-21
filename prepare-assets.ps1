$ErrorActionPreference = 'Stop'
$sourceRoot = Join-Path $PSScriptRoot '..\DizzyUltimate\extracted'
$assetRoot = Join-Path $PSScriptRoot 'Assets'
New-Item -ItemType Directory -Force -Path $assetRoot | Out-Null
foreach ($name in @('world.json', 'start-state.json')) {
    Copy-Item -LiteralPath (Join-Path $sourceRoot "godot-port-data\$name") -Destination (Join-Path $assetRoot $name) -Force
}
$audioRoot = Join-Path $assetRoot 'Audio'
New-Item -ItemType Directory -Force -Path $audioRoot | Out-Null
Get-ChildItem -LiteralPath (Join-Path $sourceRoot 'resources\audio') -Filter '*.wav' | Copy-Item -Destination $audioRoot -Force
Write-Output "Prepared local game data in $assetRoot"
