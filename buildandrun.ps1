param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",

    [string]$TargetFramework = "net10.0-windows",

    [string]$Scenario,

    [switch]$NoBuild,

    [switch]$CursorOnly,

    [switch]$TarsOnly,

    [string[]]$CursorArgs = @(),

    [string[]]$TarsArgs = @()
)

$ErrorActionPreference = "Stop"

$cursorProjectPath = Join-Path $PSScriptRoot "src\assistants\cursor\Cursor.csproj"
$tarsProjectPath = Join-Path $PSScriptRoot "src\assistants\tars\Tars.csproj"
$resolvedCursorProjectPath = [System.IO.Path]::GetFullPath($cursorProjectPath)
$resolvedTarsProjectPath = [System.IO.Path]::GetFullPath($tarsProjectPath)
$resolvedCognitionProjectPath = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "src\body\cognition\cognition.csproj"))
$resolvedExecutionProjectPath = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "src\body\execution\execution.csproj"))
$resolvedScenarioPath = $null

if ($CursorOnly -and $TarsOnly) {
    throw "Use either -CursorOnly or -TarsOnly, not both."
}

if ($CursorOnly -and -not [string]::IsNullOrWhiteSpace($Scenario)) {
    throw "-Scenario routes to tars. Use -TarsOnly with -Scenario, or omit -CursorOnly."
}

foreach ($path in @($cursorProjectPath, $tarsProjectPath, $resolvedCognitionProjectPath, $resolvedExecutionProjectPath)) {
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

$runTars = $TarsOnly -or -not [string]::IsNullOrWhiteSpace($Scenario)
$runCursor = $CursorOnly -or -not $runTars

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

function Build-LaunchProject {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [string]$ProjectPath
    )

    Invoke-DotNetStep `
        -Description "Building $Name ($Configuration | $TargetFramework)..." `
        -Arguments @(
            "build",
            $ProjectPath,
            "-c", $Configuration,
            "-f", $TargetFramework,
            "--no-restore",
            "-maxcpucount:1"
        )
}

function Invoke-Assistant {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [string]$ProjectPath,

        [string[]]$AssistantArgs = @()
    )

    $dotnetArgs = @(
        "run",
        "--project", $ProjectPath,
        "-c", $Configuration,
        "-f", $TargetFramework
    )

    if ($NoBuild) {
        $dotnetArgs += "--no-build"
    }

    if ($AssistantArgs.Count -gt 0) {
        $dotnetArgs += "--"
        $dotnetArgs += $AssistantArgs
    }

    Write-Host "Running $Name..." -ForegroundColor Cyan
    & dotnet @dotnetArgs
    if ($LASTEXITCODE -ne 0) {
        throw "$Name exited with code $LASTEXITCODE"
    }
}

if (-not $NoBuild) {
    Stop-RunningRepoRuntimeProcesses `
        -CommandLineNeedles @($resolvedCursorProjectPath, $resolvedTarsProjectPath, $resolvedCognitionProjectPath, $resolvedExecutionProjectPath, "src\\assistants\\cursor", "src\\assistants\\tars", "src\\body\\cognition", "src\\body\\execution") `
        -ProcessNames @("cognition.exe", "execution.exe")

    if ($runCursor) {
        Build-LaunchProject -Name "cursor" -ProjectPath $cursorProjectPath
    }

    if ($runTars) {
        Build-LaunchProject -Name "tars" -ProjectPath $tarsProjectPath
    }
}

if ($runCursor) {
    Invoke-Assistant -Name "cursor" -ProjectPath $cursorProjectPath -AssistantArgs $CursorArgs
}

if ($runTars) {
    $effectiveTarsArgs = @()
    if (-not [string]::IsNullOrWhiteSpace($resolvedScenarioPath)) {
        $effectiveTarsArgs += @("--scenario", $resolvedScenarioPath)
    }

    $effectiveTarsArgs += $TarsArgs
    Invoke-Assistant -Name "tars" -ProjectPath $tarsProjectPath -AssistantArgs $effectiveTarsArgs
}
