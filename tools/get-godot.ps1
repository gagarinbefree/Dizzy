param([string]$GodotPath = $env:GODOT)
$ErrorActionPreference = 'Stop'
if ($GodotPath) { return (Get-Command $GodotPath -CommandType Application -ErrorAction Stop).Source }

$editor = 'Godot_v4.7.2-stable_mono_win64'
$cache = Join-Path $PSScriptRoot '../.tools'
$executable = Join-Path $cache "$editor/${editor}_console.exe"
if (Test-Path -LiteralPath $executable) { return (Resolve-Path -LiteralPath $executable).Path }
$installed = Get-Command godot-mono, godot -CommandType Application -ErrorAction SilentlyContinue |
    Where-Object { (& $_.Source --version) -like '4.7.2.stable.mono*' } | Select-Object -First 1
if ($installed) { return $installed.Source }
if ($env:OS -ne 'Windows_NT') { throw 'Install Godot 4.7.2 .NET or pass its executable with -GodotPath.' }

New-Item -Path $cache -ItemType Directory -Force | Out-Null
$archive = Join-Path $cache "$editor.zip"
$sha = 'a2a48473a7414c5f19fab690518caebb738c09ef9601f6bd2388676a7f53b3c0'
if (!(Test-Path -LiteralPath $archive) -or (Get-FileHash -LiteralPath $archive).Hash -ne $sha) {
    $download = "$archive.$PID.tmp"
    try {
        Invoke-WebRequest "https://github.com/godotengine/godot-builds/releases/download/4.7.2-stable/$editor.zip" -OutFile $download
        if ((Get-FileHash -LiteralPath $download).Hash -ne $sha) { throw 'Godot archive checksum mismatch.' }
        Move-Item -LiteralPath $download -Destination $archive -Force
    } finally {
        if (Test-Path -LiteralPath $download) { Remove-Item -LiteralPath $download }
    }
}
Expand-Archive -LiteralPath $archive -DestinationPath $cache -Force
return (Resolve-Path -LiteralPath $executable -ErrorAction Stop).Path
