param(
    [switch]$Engine,
    [string]$GodotPath = $env:GODOT
)
$ErrorActionPreference = 'Stop'
$env:DOTNET_NOLOGO = '1'
$researchRoot = Join-Path $PSScriptRoot '..\DizzyUltimate\extracted'
$coverage = Get-Content -LiteralPath (Join-Path $researchRoot 'object-analysis\route-coverage-audit.json') -Raw | ConvertFrom-Json
$artifacts = Join-Path $PSScriptRoot 'Artifacts'
New-Item -ItemType Directory -Path $artifacts -Force | Out-Null
$summary = [ordered]@{
    started_utc = [DateTime]::UtcNow.ToString('o')
    ordinary_fixtures = $coverage.ordinary_sources.Count
    ordinary_fixture_cycles = ($coverage.ordinary_sources | Measure-Object -Property loops -Sum).Sum
    kernel_passed = $false
    engine_requested = [bool]$Engine
    engine_passed = @()
    complete = $false
}
function Save-CheckSummary {
    $summary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $artifacts 'check-summary.json') -Encoding utf8
}
Save-CheckSummary
$fixturePaths = @($coverage.ordinary_sources | ForEach-Object { Join-Path $researchRoot ('object-analysis\' + $_.fixture) })
& dotnet run --project (Join-Path $PSScriptRoot 'Tests\KernelChecks.csproj') -- (Join-Path $researchRoot 'godot-port-data') @fixturePaths
if ($LASTEXITCODE -ne 0) { throw 'C# reference checks failed' }
$summary.kernel_passed = $true
Save-CheckSummary
if ($Engine) {
    $GodotPath = & (Join-Path $PSScriptRoot 'tools/get-godot.ps1') -GodotPath $GodotPath
    & dotnet build (Join-Path $PSScriptRoot 'Dizzy.csproj')
    if ($LASTEXITCODE -ne 0) { throw 'Godot C# build failed' }
    & $GodotPath --headless --path $PSScriptRoot --editor --import --quit --quiet
    if ($LASTEXITCODE -ne 0) { throw 'Godot import failed' }
    foreach ($fixturePath in $fixturePaths) {
        & $GodotPath --headless --path $PSScriptRoot -- "--verify-fixture=$fixturePath"
        if ($LASTEXITCODE -ne 0) { throw "Godot replay failed: $fixturePath" }
        $summary.engine_passed += [IO.Path]::GetFileName($fixturePath)
        Save-CheckSummary
    }
}
$summary.complete = $true
$summary.finished_utc = [DateTime]::UtcNow.ToString('o')
Save-CheckSummary
