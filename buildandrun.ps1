$ErrorActionPreference = "Stop"

$projectPath = Join-Path $PSScriptRoot "src\herface\Herface.csproj"
$configuration = "Debug"
$targetFramework = "net10.0-windows"
$binaryPath = Join-Path $PSScriptRoot "src\herface\bin\$configuration\$targetFramework\Herface.dll"

if (-not (Test-Path $projectPath)) {
    throw "Project file not found: $projectPath"
}

Write-Host "Building herface ($configuration | $targetFramework)..." -ForegroundColor Cyan
dotnet build $projectPath -c $configuration -f $targetFramework

if (-not (Test-Path $binaryPath)) {
    throw "Built binary not found: $binaryPath"
}

$binary = Get-Item $binaryPath
Write-Host "Using binary: $($binary.FullName)" -ForegroundColor DarkCyan
Write-Host "Binary timestamp: $($binary.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss"))" -ForegroundColor DarkCyan

Write-Host "Running herface..." -ForegroundColor Cyan
dotnet run --project $projectPath -c $configuration -f $targetFramework --no-build
