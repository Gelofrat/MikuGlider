param(
    [ValidateSet('Sync', 'Build', 'Open', 'Editor', 'Smoke', 'Full', 'Views', 'All', 'Arms', 'Weapons', 'Emotes', 'Seated', 'Parity', 'Gait', 'Replay', 'Combinations', 'ComboSmoke', 'Probe', 'Hair')]
    [string]$Action = 'Open',
    [string]$Unity = 'C:\Program Files\Unity\Hub\Editor\6000.4.12f1\Editor\Unity.exe',
    [ValidateSet('playable', 'sample', 'manual')]
    [string]$Driver = 'playable',
    [string]$Filter,
    [ValidateSet('v1', 'v2', 'v3')]
    [string]$Variant,
    [ValidateSet('player', 'batch')]
    [string]$RenderMode = 'batch',
    [switch]$Visible,
    [switch]$CodeOnly,
    [switch]$ReuseCatalog,
    [switch]$VestCloseup,
    [ValidateSet('L', 'R')]
    [string]$WristCloseup,
    [float]$WristYaw = 145,
    [switch]$GarmentDebug,
    [switch]$NativeGarment,
    [switch]$CullGarment,
    [switch]$LimbPoses,
    [switch]$FirstPerson,
    [ValidateRange(30, 140)]
    [float]$FirstPersonFov = 55,
    [switch]$BottomView,
    [switch]$LateBind
)

$ErrorActionPreference = 'Stop'
if ($Visible -and !$PSBoundParameters.ContainsKey('RenderMode')) { $RenderMode = 'player' }
$labRoot = $PSScriptRoot
$labProject = Join-Path $labRoot 'Project'
$labExecutable = Join-Path $labRoot 'Build\MikuPhysicsLab.exe'
$labReports = Join-Path $labRoot 'Reports'
$labState = Join-Path $labReports 'build-status.json'
New-Item -ItemType Directory -Force $labReports | Out-Null

function Invoke-LabProcess([string]$Executable, [string[]]$Arguments, [string]$Style, [string]$Tag) {
    $labProcess = Start-Process -FilePath $Executable -ArgumentList $Arguments -WindowStyle $Style -PassThru -RedirectStandardOutput (Join-Path $labReports ($Tag + '-stdout.log')) -RedirectStandardError (Join-Path $labReports ($Tag + '-stderr.log'))
    $labHandle = $labProcess.Handle
    $labProcess.WaitForExit()
    $labProcess.Refresh()
    return $labProcess.ExitCode
}

function Invoke-LabAction {
    if ($Action -in @('Sync', 'Build')) {
        if (Get-Process MikuPhysicsLab -ErrorAction SilentlyContinue) { throw 'Close Miku Physics Lab before updating resources or building.' }
        if ($Action -eq 'Build') {
            @{ state = 'building'; startedUtc = [DateTime]::UtcNow.ToString('O') } | ConvertTo-Json | Set-Content -LiteralPath $labState
        }
        $labSyncArguments = @((Join-Path $labRoot 'sync.py'))
        if ($CodeOnly) { $labSyncArguments += '--code-only' }
        if ($ReuseCatalog) { $labSyncArguments += '--reuse-catalog' }
        python @labSyncArguments
        if ($LASTEXITCODE -ne 0) { throw 'Resource synchronization failed.' }
    }
    if ($Action -eq 'Sync') { return }
    if ($Action -eq 'Build') {
        New-Item -ItemType Directory -Force (Join-Path $labProject 'Assets') | Out-Null
        $labTag = 'build-' + [DateTime]::Now.ToString('yyyyMMdd-HHmmss')
        $labLog = Join-Path $labReports ($labTag + '.log')
        $labArguments = @('-batchmode', '-projectPath', ('"' + $labProject + '"'), '-executeMethod', 'Horse.MikuPhysicsLab.Editor.LabBuild.Build', '-buildTarget', 'StandaloneWindows64', '-quit', '-logFile', ('"' + $labLog + '"'))
        $labExitCode = Invoke-LabProcess $Unity $labArguments 'Hidden' $labTag
        if ($labExitCode -ne 0 -or !(Select-String -LiteralPath $labLog -SimpleMatch 'LAB_BUILD_READY' -Quiet)) {
            @{ state = 'failed'; log = $labLog } | ConvertTo-Json | Set-Content -LiteralPath $labState
            throw "Unity build failed ($labExitCode). See $labLog"
        }
        @{ state = 'ready'; completedUtc = [DateTime]::UtcNow.ToString('O'); log = $labLog; engineSha256 = (Get-FileHash -LiteralPath (Join-Path $labRoot 'Build/UnityPlayer.dll') -Algorithm SHA256).Hash } | ConvertTo-Json | Set-Content -LiteralPath $labState
        Write-Output "LAB_BUILD_COMPLETED $labLog"
        return
    }
    if ($Action -eq 'Editor') {
        Start-Process -FilePath $Unity -ArgumentList @('-projectPath', ('"' + $labProject + '"'))
        return
    }
    if (!(Test-Path -LiteralPath $labExecutable) -or !(Test-Path -LiteralPath $labState) -or (Get-Content -LiteralPath $labState -Raw | ConvertFrom-Json).state -ne 'ready') {
        throw 'A complete build is required. Run simulation/lab.ps1 Build.'
    }
    if ($Action -eq 'Open') {
        Start-Process -FilePath $labExecutable -ArgumentList @('-logFile', ('"' + (Join-Path $labReports 'interactive.log') + '"'))
        return
    }
    if (Get-Process MikuPhysicsLab -ErrorAction SilentlyContinue) { throw 'A lab viewer or test is already running.' }
    $labMode = $Action.ToLowerInvariant()
    $labTag = $labMode + '-' + $Driver + '-' + $RenderMode + '-' + [DateTime]::Now.ToString('yyyyMMdd-HHmmss')
    $labArguments = @('-force-d3d11', '-labSuite', $labMode, '-labReports', ('"' + $labReports + '"'), '-logFile', ('"' + (Join-Path $labReports ($labTag + '.log')) + '"'), '-labDriver', $Driver)
    if ($RenderMode -eq 'batch') { $labArguments += '-batchmode' }
    if ($Filter) { $labArguments += @('-labFilter', ('"' + $Filter + '"')) }
    if ($Variant) { $labArguments += @('-labVariant', $Variant) }
    if ($VestCloseup) { $labArguments += @('-labVestCloseup', '1') }
    if ($WristCloseup) { $labArguments += @('-labWristCloseup', $WristCloseup, '-labWristYaw', $WristYaw.ToString([Globalization.CultureInfo]::InvariantCulture)) }
    if ($GarmentDebug) { $labArguments += @('-labGarmentDebug', '1') }
    if ($NativeGarment) { $labArguments += @('-labNativeGarment', '1') }
    if ($CullGarment) { $labArguments += @('-labCullGarment', '1') }
    if ($LimbPoses) { $labArguments += @('-labLimbPoses', '1') }
    if ($FirstPerson) { $labArguments += @('-labFirstPerson', '1', '-labFirstPersonFov', $FirstPersonFov.ToString([Globalization.CultureInfo]::InvariantCulture)) }
    if ($BottomView) { $labArguments += @('-labBottomView', '1') }
    if ($LateBind) { $labArguments += @('-labLateBind', '1') }
    $labWindowStyle = if ($Visible) { 'Normal' } else { 'Hidden' }
    $labStartedUtc = [DateTime]::UtcNow
    $labExitCode = Invoke-LabProcess $labExecutable $labArguments $labWindowStyle $labTag
    $labReportDirectory = Get-ChildItem -LiteralPath $labReports -Directory -Filter ('*-' + $labMode + '-' + $Driver) | Where-Object { $_.CreationTimeUtc -ge $labStartedUtc } | Sort-Object Name -Descending | Select-Object -First 1
    $labReportFile = if ($labReportDirectory) { Join-Path $labReportDirectory.FullName 'report.json' } else { $null }
    $labCompleted = $labReportFile -and (Test-Path -LiteralPath $labReportFile)
    $labPassed = if ($labCompleted) { (Get-Content -LiteralPath $labReportFile -Raw | ConvertFrom-Json).passed } else { $false }
    $labProcessResult = @{ action = $Action; driver = $Driver; renderMode = $RenderMode; visible = [bool]$Visible; vestCloseup = [bool]$VestCloseup; exitCode = $labExitCode; nativeFailure = $labExitCode -notin @(0, 2); testCompleted = [bool]$labCompleted; testPassed = $labPassed; report = $labReportFile; log = (Join-Path $labReports ($labTag + '.log')); completedUtc = [DateTime]::UtcNow.ToString('O'); engineSha256 = (Get-FileHash -LiteralPath (Join-Path $labRoot 'Build/UnityPlayer.dll') -Algorithm SHA256).Hash }
    $labProcessResult.garmentDebug = [bool]$GarmentDebug
    $labProcessResult.variant = $Variant
    $labProcessResult.limbPoses = [bool]$LimbPoses
    $labProcessResult.firstPerson = [bool]$FirstPerson
    $labProcessResult.lateBind = [bool]$LateBind
    $labProcessResult.nativeGarment = [bool]$NativeGarment
    $labProcessResult.cullGarment = [bool]$CullGarment
    $labProcessResult | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $labReports ($labTag + '-process.json'))
    if ($labReportDirectory) { $labProcessResult | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $labReportDirectory.FullName 'process.json') }
    if ($labProcessResult.nativeFailure) { throw "Unity Player failed ($labExitCode). Completed report: $labReportFile. Log: $($labProcessResult.log)" }
    if ($labExitCode -ne 0) { throw "Validation flagged issues. Report: $labReportFile" }
}

$labLock = $null
try {
    $labLock = [System.IO.File]::Open((Join-Path $labReports 'operation.lock'), [System.IO.FileMode]::OpenOrCreate, [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::None)
    Invoke-LabAction
}
finally {
    if ($labLock) { $labLock.Dispose() }
}
