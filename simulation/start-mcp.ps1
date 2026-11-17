param([string]$Unity = 'C:\Program Files\Unity\Hub\Editor\6000.4.12f1\Editor\Unity.exe')
$ErrorActionPreference = 'Stop'
$labProject = Join-Path $PSScriptRoot 'Project'
$labReports = Join-Path $PSScriptRoot 'Reports'
New-Item -ItemType Directory -Force -Path $labReports | Out-Null
# An existing editor is detected by Unity's own project lock. Never stop another editor.
if (Test-Path -LiteralPath (Join-Path $labProject 'Temp/UnityLockfile')) {
    try {
        $labLock = [System.IO.File]::Open((Join-Path $labProject 'Temp/UnityLockfile'), [System.IO.FileMode]::Open, [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::None)
        $labLock.Dispose()
    } catch [System.IO.IOException] {
        Write-Output 'Project already open. MCP starts automatically after script reload.'
        exit 0
    }
}
$env:UNITY_MCP_ALLOW_BATCH = '1'
$labProcess = Start-Process -FilePath $Unity -ArgumentList @('-batchmode', '-projectPath', ('"'+$labProject+'"'), '-logFile', ('"'+(Join-Path $labReports 'mcp-editor.log')+'"')) -WindowStyle Hidden -PassThru
Write-Output ('Started Miku lab editor PID '+$labProcess.Id+'. Project startup enables MCP automatically.')
