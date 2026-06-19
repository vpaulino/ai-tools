param(
    [string]$PackageId = "Samwise",
    [string]$ProjectDir = (Get-Location).Path,
    [string]$Source = (Join-Path $PSScriptRoot "..\artifacts"),
    [string]$Version = ""
)

$ErrorActionPreference = "Stop"

$ProjectDir = (Resolve-Path $ProjectDir).Path
$Source = (Resolve-Path $Source).Path

Push-Location $ProjectDir
try {
    if (-not (Test-Path ".config\dotnet-tools.json")) {
        dotnet new tool-manifest
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    }

    $args = @("tool", "update", $PackageId, "--add-source", $Source)
    if ($Version) { $args += @("--version", $Version) }

    & dotnet @args
    if ($LASTEXITCODE -ne 0) {
        $args = @("tool", "install", $PackageId, "--add-source", $Source)
        if ($Version) { $args += @("--version", $Version) }
        & dotnet @args
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    }

    Write-Host "Installed project-local tool. Run it with: dotnet tool run samwise"
}
finally {
    Pop-Location
}
