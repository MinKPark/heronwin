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

$brainProjectPath = Join-Path $PSScriptRoot "src\herhead\brain\Brain.csproj"
$faceProjectPath = Join-Path $PSScriptRoot "src\herhead\face\Face.csproj"
$resolvedFaceProjectPath = [System.IO.Path]::GetFullPath($faceProjectPath)
$resolvedBrainProjectPath = [System.IO.Path]::GetFullPath($brainProjectPath)
$resolvedEyesAndHandsProjectPath = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "src\herbody\eyesandhands\eyesandhands.csproj"))
$resolvedScenarioPath = $null

$runFace = -not $BrainOnly
$runBrain = -not $FaceOnly

if ($BrainOnly -and $FaceOnly) {
    throw "Use either -BrainOnly or -FaceOnly, not both."
}

if (-not [string]::IsNullOrWhiteSpace($Scenario) -and $BrainArgs -contains "--scenario") {
    throw "Use either -Scenario or pass --scenario through -BrainArgs, not both."
}

foreach ($path in @($brainProjectPath, $faceProjectPath, $resolvedEyesAndHandsProjectPath)) {
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

function Stop-RunningRepoRuntimeProcesses {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$CommandLineNeedles,

        [string[]]$ProcessNames = @()
    )

    $matchingProcesses = @(
        Get-CimInstance Win32_Process |
            Where-Object {
                $process = $_
                $commandLineMatches = $false
                if ($process.CommandLine) {
                    foreach ($needle in $CommandLineNeedles) {
                        if ($needle.Length -gt 0 -and $process.CommandLine.IndexOf($needle, [System.StringComparison]::OrdinalIgnoreCase) -ge 0) {
                            $commandLineMatches = $true
                            break
                        }
                    }
                }

                $nameMatches = $ProcessNames -contains $process.Name
                return $commandLineMatches -or $nameMatches
            }
    )

    if (-not $matchingProcesses) {
        return
    }

    Write-Host "Stopping running repo process(es) that would lock build outputs..." -ForegroundColor DarkCyan
    foreach ($process in $matchingProcesses) {
        Stop-Process -Id $process.ProcessId -Force -ErrorAction SilentlyContinue
    }
}

function Build-LaunchProjects {
    param(
        [Parameter(Mandatory = $true)]
        [bool]$ShouldBuildFace,

        [Parameter(Mandatory = $true)]
        [bool]$ShouldBuildBrain
    )

    if ($ShouldBuildFace) {
        Invoke-DotNetStep `
            -Description "Building face ($Configuration | $TargetFramework)..." `
            -Arguments @(
                "build",
                $faceProjectPath,
                "-c", $Configuration,
                "-f", $TargetFramework,
                "--no-restore",
                "-maxcpucount:1"
            )
    }

    if ($ShouldBuildBrain) {
        Invoke-DotNetStep `
            -Description "Building brain ($Configuration | $TargetFramework)..." `
            -Arguments @(
                "build",
                $brainProjectPath,
                "-c", $Configuration,
                "-f", $TargetFramework,
                "--no-restore",
                "-maxcpucount:1"
            )
    }
}

if (-not $NoBuild) {
    if ($runFace) {
        Stop-RunningFaceProcesses -FaceProjectPath $resolvedFaceProjectPath
    }

    if ($runBrain) {
        Stop-RunningRepoRuntimeProcesses `
            -CommandLineNeedles @($resolvedBrainProjectPath, $resolvedEyesAndHandsProjectPath, "src\\herhead\\brain", "src\\herbody\\eyesandhands") `
            -ProcessNames @("eyesandhands.exe")
    }

    Build-LaunchProjects -ShouldBuildFace:$runFace -ShouldBuildBrain:$runBrain
}

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
