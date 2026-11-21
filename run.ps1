param([string]$GodotPath = (Join-Path $PSScriptRoot '..\.tools\godot-runtime\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe'))
$ErrorActionPreference = 'Stop'
if (!(Test-Path -LiteralPath $GodotPath)) {
    $foundGodot = Get-Command godot, godot-mono, Godot.exe -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($foundGodot) { $GodotPath = $foundGodot.Source }
    else { throw 'Godot .NET executable was not found. Pass its full path with -GodotPath.' }
}
$env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot '..\.tools\dotnet-home'
$env:DOTNET_NOLOGO = '1'
if (!(Test-Path -LiteralPath (Join-Path $PSScriptRoot 'Assets\start-state.json'))) { & (Join-Path $PSScriptRoot 'prepare-assets.ps1') }
& 'C:\Program Files\dotnet\dotnet.exe' build (Join-Path $PSScriptRoot 'Dizzy.csproj')
if ($LASTEXITCODE -ne 0) { throw 'C# build failed' }
& $GodotPath --headless --path $PSScriptRoot --editor --import --quit --quiet
if ($LASTEXITCODE -ne 0) { throw 'Godot import failed' }
& $GodotPath --path $PSScriptRoot
