param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",

    [string]$TargetFramework = "net10.0-windows",

    [string]$Scenario,

    [switch]$NoBuild,

    [switch]$BrainOnly,

    [switch]$FaceOnly,

    [string[]]$BrainArgs = @()
)

$ErrorActionPreference = "Stop"

$solutionPath = Join-Path $PSScriptRoot "src\heronwin.sln"
$brainProjectPath = Join-Path $PSScriptRoot "src\herhead\brain\Brain.csproj"
$faceProjectPath = Join-Path $PSScriptRoot "src\herhead\face\Face.csproj"
$resolvedFaceProjectPath = [System.IO.Path]::GetFullPath($faceProjectPath)
$resolvedScenarioPath = $null

if ($BrainOnly -and $FaceOnly) {
    throw "Use either -BrainOnly or -FaceOnly, not both."
}

if (-not [string]::IsNullOrWhiteSpace($Scenario) -and $BrainArgs -contains "--scenario") {
    throw "Use either -Scenario or pass --scenario through -BrainArgs, not both."
}

foreach ($path in @($solutionPath, $brainProjectPath, $faceProjectPath)) {
    if (-not (Test-Path $path)) {
        throw "Required path not found: $path"
    }
}

if (-not [string]::IsNullOrWhiteSpace($Scenario)) {
    if ([System.IO.Path]::IsPathRooted($Scenario)) {
        $resolvedScenarioPath = [System.IO.Path]::GetFullPath($Scenario)
    }
    else {
        $resolvedScenarioPath = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot $Scenario))
    }

    if (-not (Test-Path $resolvedScenarioPath)) {
        throw "Scenario file not found: $resolvedScenarioPath"
    }
}

function Invoke-DotNetStep {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Description,

        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    Write-Host $Description -ForegroundColor Cyan
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet command failed: dotnet $($Arguments -join ' ')"
    }
}

function Stop-RunningFaceProcesses {
    param(
        [Parameter(Mandatory = $true)]
        [string]$FaceProjectPath
    )

    $matchingProcesses = Get-CimInstance Win32_Process |
        Where-Object {
            $_.Name -ieq "dotnet.exe" -and
            $_.CommandLine -and
            $_.CommandLine.IndexOf($FaceProjectPath, [System.StringComparison]::OrdinalIgnoreCase) -ge 0
        }

    if (-not $matchingProcesses) {
        return
    }

    Write-Host "Stopping existing face process(es)..." -ForegroundColor DarkCyan
    foreach ($process in $matchingProcesses) {
        Stop-Process -Id $process.ProcessId -Force -ErrorAction Stop
    }
}

if (-not $NoBuild) {
    Invoke-DotNetStep `
        -Description "Building heronwin solution ($Configuration | $TargetFramework)..." `
        -Arguments @(
            "build",
            $solutionPath,
            "-c", $Configuration,
            "-f", $TargetFramework
        )
}

$runFace = -not $BrainOnly
$runBrain = -not $FaceOnly

if ($runFace) {
    Stop-RunningFaceProcesses -FaceProjectPath $resolvedFaceProjectPath

    $faceArgs = @(
        "run",
        "--project", $faceProjectPath,
        "-c", $Configuration,
        "-f", $TargetFramework
    )

    if ($NoBuild) {
        $faceArgs += "--no-build"
    }

    Write-Host "Starting face in a separate process..." -ForegroundColor Cyan
    Start-Process `
        -FilePath "dotnet" `
        -ArgumentList $faceArgs `
        -WorkingDirectory $PSScriptRoot | Out-Null
}

if ($runBrain) {
    $brainArgsList = @(
        "run",
        "--project", $brainProjectPath,
        "-c", $Configuration,
        "-f", $TargetFramework
    )

    if ($NoBuild) {
        $brainArgsList += "--no-build"
    }

    if ($BrainArgs.Count -gt 0) {
        $brainArgsList += "--"
        if (-not [string]::IsNullOrWhiteSpace($Scenario)) {
            $brainArgsList += @("--scenario", $resolvedScenarioPath)
        }

        $brainArgsList += $BrainArgs
    }
    elseif (-not [string]::IsNullOrWhiteSpace($Scenario)) {
        $brainArgsList += "--"
        $brainArgsList += @("--scenario", $resolvedScenarioPath)
    }

    Write-Host "Running brain..." -ForegroundColor Cyan
    & dotnet @brainArgsList
    if ($LASTEXITCODE -ne 0) {
        throw "brain exited with code $LASTEXITCODE"
    }
}
else {
    Write-Host "Face was started. Brain launch was skipped because -FaceOnly was specified." -ForegroundColor DarkCyan
}
