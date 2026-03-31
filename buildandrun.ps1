$ErrorActionPreference = "Stop"

$projectPath = Join-Path $PSScriptRoot "herface\Herface.csproj"

if (-not (Test-Path $projectPath)) {
    throw "Project file not found: $projectPath"
}

Write-Host "Building herface..." -ForegroundColor Cyan
dotnet build $projectPath

Write-Host "Running herface..." -ForegroundColor Cyan
dotnet run --project $projectPath --no-build
