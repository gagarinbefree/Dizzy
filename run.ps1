param(
    [string]$GodotPath = $env:GODOT,
    [Parameter(ParameterSetName='Export')][switch]$ExportWindows,
    [Parameter(ParameterSetName='Check')][switch]$SmokeTest
)
$ErrorActionPreference = 'Stop'
Push-Location -LiteralPath $PSScriptRoot
try {
    $godot = & ./tools/get-godot.ps1 -GodotPath $GodotPath
    & dotnet build Dizzy.csproj
    if ($LASTEXITCODE -ne 0) { throw 'C# build failed' }
    & $godot --headless --path . --editor --import
    if ($LASTEXITCODE -ne 0) { throw 'Godot import failed' }
    if ($ExportWindows) {
        & python tools/get_windows_template.py .godot/windows_release_x86_64.exe
        if ($LASTEXITCODE -ne 0) { throw 'Windows export template setup failed' }
        New-Item -ItemType Directory -Force -Path build/windows | Out-Null
        & $godot --headless --path . --export-release 'Windows Desktop' 'build/windows/Dizzy.exe'
    } elseif ($SmokeTest) {
        & $godot --headless --path . -- --smoke-test
    } else {
        & $godot --path .
    }
    if ($LASTEXITCODE -ne 0) { throw "Godot exited with code $LASTEXITCODE" }
} finally {
    Pop-Location
}
