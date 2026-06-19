param(
    [string]$PackageId = "Samwise",
    [string]$Source = (Join-Path $PSScriptRoot "..\artifacts"),
    [string]$Version = ""
)

$ErrorActionPreference = "Stop"

$args = @("tool", "update", "--global", $PackageId, "--add-source", (Resolve-Path $Source))
if ($Version) { $args += @("--version", $Version) }

& dotnet @args
if ($LASTEXITCODE -ne 0) {
    $args = @("tool", "install", "--global", $PackageId, "--add-source", (Resolve-Path $Source))
    if ($Version) { $args += @("--version", $Version) }
    & dotnet @args
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

Write-Host "Installed user-global tool. If the command is not found, open a new terminal so PATH refreshes."
