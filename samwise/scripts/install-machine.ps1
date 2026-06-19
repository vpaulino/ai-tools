param(
    [string]$PackageId = "Samwise",
    [string]$ToolPath = (Join-Path $env:ProgramFiles "Samwise\tools"),
    [string]$Source = (Join-Path $PSScriptRoot "..\artifacts"),
    [string]$Version = ""
)

$ErrorActionPreference = "Stop"

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "Machine-wide install requires an elevated PowerShell session."
}

$Source = (Resolve-Path $Source).Path
New-Item -ItemType Directory -Force -Path $ToolPath | Out-Null

$args = @("tool", "update", $PackageId, "--tool-path", $ToolPath, "--add-source", $Source)
if ($Version) { $args += @("--version", $Version) }

& dotnet @args
if ($LASTEXITCODE -ne 0) {
    $args = @("tool", "install", $PackageId, "--tool-path", $ToolPath, "--add-source", $Source)
    if ($Version) { $args += @("--version", $Version) }
    & dotnet @args
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

$machinePath = [Environment]::GetEnvironmentVariable("Path", "Machine")
$parts = $machinePath -split ";" | Where-Object { $_ }
if ($parts -notcontains $ToolPath) {
    [Environment]::SetEnvironmentVariable("Path", ($parts + $ToolPath -join ";"), "Machine")
    Write-Host "Added $ToolPath to the machine PATH. Open a new terminal to pick it up."
}

Write-Host "Installed machine-wide tool at $ToolPath."
