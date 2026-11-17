param([Parameter(Mandatory = $true)][string]$GameRoot)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
Push-Location $repo
try {
    foreach ($project in @('MikuGlider.csproj', 'tests/RefactorRuntime/RefactorRuntime.csproj',
        'tests/RefactorMetadata/RefactorMetadata.csproj', 'tests/MissionAudio/MissionAudio.csproj')) {
        & dotnet build $project -c Release "-p:GameRoot=$GameRoot"
        if ($LASTEXITCODE -ne 0) { throw "Build failed: $project" }
    }
    & tests/RefactorRuntime/bin/Release/net48/RefactorRuntime.exe
    if ($LASTEXITCODE -ne 0) { throw 'Runtime regression checks failed.' }
    & tests/RefactorMetadata/bin/Release/net10.0/RefactorMetadata.exe $repo (Join-Path $GameRoot 'Mycopunk_Data/Managed')
    if ($LASTEXITCODE -ne 0) { throw 'Native integration or resource checks failed.' }
    $configFolder = Join-Path $repo 'scratch/test-configs'
    New-Item -ItemType Directory -Force -Path $configFolder | Out-Null
    & tests/MissionAudio/bin/Release/net48/MissionAudio.exe (Join-Path $configFolder 'mission-audio.cfg')
    if ($LASTEXITCODE -ne 0) { throw 'Mission audio regression checks failed.' }
} finally {
    Pop-Location
}
