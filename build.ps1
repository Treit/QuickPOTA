#requires -Version 7.0
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [ValidateSet('win-x64', 'win-arm64', 'linux-x64', 'linux-arm64', 'osx-x64', 'osx-arm64')]
    [string]$Runtime = $(if ($IsWindows -or $null -eq $IsWindows) { 'win-x64' } elseif ($IsLinux) { 'linux-x64' } else { 'osx-arm64' }),

    [switch]$NoPublish,
    [switch]$Clean,
    [switch]$Run
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSCommandPath
Set-Location $root

if ($IsWindows -or $null -eq $IsWindows) {
    $installer = 'C:\Program Files (x86)\Microsoft Visual Studio\Installer'
    if ((Test-Path $installer) -and ($env:PATH -notlike "*$installer*")) {
        $env:PATH = "$installer;$env:PATH"
    }
}

if ($Clean) {
    Write-Host "Cleaning bin/ and obj/ ..." -ForegroundColor Cyan
    Remove-Item -Recurse -Force bin, obj -ErrorAction SilentlyContinue
}

Write-Host "Building ($Configuration) ..." -ForegroundColor Cyan
dotnet build -c $Configuration -nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed with exit code $LASTEXITCODE." }

if (-not $NoPublish) {
    Write-Host "Publishing AOT ($Configuration, $Runtime) ..." -ForegroundColor Cyan
    dotnet publish -c $Configuration -r $Runtime -nologo
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE." }

    $publishDir = Join-Path $root "bin\$Configuration\net10.0\$Runtime\publish"
    $exe = Get-ChildItem $publishDir -File | Where-Object { $_.Name -like 'quickpota*' -and $_.Extension -in @('.exe', '') } | Select-Object -First 1
    if ($exe) {
        $sizeMb = [math]::Round($exe.Length / 1MB, 2)
        Write-Host ""
        Write-Host "Built: $($exe.FullName) ($sizeMb MB)" -ForegroundColor Green
    }

    if ($Run -and $exe) {
        Write-Host ""
        & $exe.FullName
    }
}
