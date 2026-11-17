$ErrorActionPreference = 'Stop'
# Keep stdout exclusively for the MCP protocol. Start the project if closed;
# its InitializeOnLoad bootstrap enables the editor-side stdio bridge.
& (Join-Path $PSScriptRoot 'start-mcp.ps1') | ForEach-Object { [Console]::Error.WriteLine($_) }
$env:UNITY_MCP_DISABLE_TELEMETRY = '1'
$mcpExecutable = Join-Path $PSScriptRoot '../tools/unity-mcp-env/Scripts/mcp-for-unity.exe'
& $mcpExecutable --transport stdio
exit $LASTEXITCODE
