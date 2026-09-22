<#
.SYNOPSIS
    Builds CTS Revit Plugin for Revit 2023 / 2024 / 2025 / 2026 and optionally
    registers it in Revit (dev loop) or packages it as an Autodesk bundle.

.EXAMPLE
    .\build.ps1
    Release build of all four versions -> artifacts\bin\<version>\Release

.EXAMPLE
    .\build.ps1 -Versions 2024 -Configuration Debug -Install
    Dev loop: build only Revit 2024 and register the .addin pointing to the build output.
    (Close Revit first, otherwise the DLL is locked.)

.EXAMPLE
    .\build.ps1 -Bundle
    Builds all versions and creates dist\CTSRevitPlugin.bundle (+ .bundle.zip), ready to copy to
    %AppData%\Autodesk\ApplicationPlugins\  (or use -InstallBundle to do that copy for you).
#>
[CmdletBinding()]
param(
    [ValidateSet('2023', '2024', '2025', '2026')]
    [string[]]$Versions = @('2023', '2024', '2025', '2026'),

    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [switch]$Install,        # write CTSRevitPlugin.addin to %ProgramData%\Autodesk\Revit\Addins\<version>
    [switch]$Bundle,         # create dist\CTSRevitPlugin.bundle and dist\CTSRevitPlugin.bundle.zip
    [switch]$InstallBundle,  # copy the bundle to %AppData%\Autodesk\ApplicationPlugins (implies -Bundle)
    [switch]$Installer,      # also build dist\CTSRevitPlugin_Setup_<version>.exe with Inno Setup 6 (implies -Bundle)
    [switch]$Clean           # delete artifacts\ and dist\ before building
)

$ErrorActionPreference = 'Stop'

$root       = Split-Path -Parent $MyInvocation.MyCommand.Path
$project    = Join-Path $root 'src\CTSRevitPlugin\CTSRevitPlugin.csproj'
$artifacts  = Join-Path $root 'artifacts'
$dist       = Join-Path $root 'dist'
$template   = Join-Path $root 'deploy\CTSRevitPlugin.addin.template'
$name       = 'CTSRevitPlugin'
$appVersion = '5.0.0'

if ($InstallBundle -or $Installer) { $Bundle = $true }

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'dotnet SDK not found. Install the .NET 8 SDK (or newer): https://dotnet.microsoft.com/download'
}

if ($Clean) {
    foreach ($dir in @($artifacts, $dist)) {
        if (Test-Path $dir) { Remove-Item $dir -Recurse -Force }
    }
}

function New-AddinXml([string]$assemblyPath) {
    return (Get-Content $template -Raw).Replace('__ASSEMBLY__', $assemblyPath)
}

$built  = @()
$failed = @()

# ---------------------------------------------------------------- build
foreach ($v in $Versions) {
    Write-Host ''
    Write-Host "=== Revit $v  ($Configuration) ===" -ForegroundColor Cyan

    & dotnet build $project -c $Configuration "-p:RevitVersion=$v" --nologo --verbosity minimal
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Build FAILED for Revit $v" -ForegroundColor Red
        $failed += $v
        continue
    }

    $out = Join-Path $artifacts "bin\$v\$Configuration"
    $dll = Join-Path $out "$name.dll"
    if (-not (Test-Path $dll)) {
        Write-Host "DLL not found after build: $dll" -ForegroundColor Red
        $failed += $v
        continue
    }
    $built += $v

    if ($Install) {
        $addinDir = Join-Path $env:ProgramData "Autodesk\Revit\Addins\$v"
        New-Item -ItemType Directory -Force -Path $addinDir | Out-Null
        $addinFile = Join-Path $addinDir "$name.addin"
        Set-Content -Path $addinFile -Value (New-AddinXml $dll) -Encoding UTF8
        Write-Host "Add-in registered: $addinFile"
    }
}

# ---------------------------------------------------------------- bundle
if ($Bundle -and $built.Count -gt 0) {
    Write-Host ''
    Write-Host '=== Bundle ===' -ForegroundColor Cyan

    $bundleDir = Join-Path $dist "$name.bundle"
    if (Test-Path $bundleDir) { Remove-Item $bundleDir -Recurse -Force }
    New-Item -ItemType Directory -Force -Path (Join-Path $bundleDir 'Contents') | Out-Null

    $xml = @()
    $xml += '<?xml version="1.0" encoding="utf-8"?>'
    $xml += '<ApplicationPackage SchemaVersion="1.0" AutodeskProduct="Revit" ProductType="Application" Name="CTS Revit Plugin" Description="CTS BIM Revit Plugin" AppVersion="' + $appVersion + '" Author="CTS BIM" UniqueId="3827cd7e-5f63-4b60-9333-121413f204f9" ProductCode="{AB4AF7B8-F556-4A3F-A0EB-E7D335186D47}">'
    $xml += '  <CompanyDetails Name="CTS BIM" />'

    foreach ($v in $built) {
        $src  = Join-Path $artifacts "bin\$v\$Configuration"
        $dest = Join-Path $bundleDir "Contents\$v"
        New-Item -ItemType Directory -Force -Path $dest | Out-Null
        Copy-Item -Path (Join-Path $src '*') -Destination $dest -Recurse -Force

        # Inside the bundle the .addin sits next to the DLL, so the path is relative.
        Set-Content -Path (Join-Path $dest "$name.addin") -Value (New-AddinXml "$name.dll") -Encoding UTF8

        $xml += '  <Components Description="Revit ' + $v + '">'
        $xml += '    <RuntimeRequirements OS="Win64" Platform="Revit" SeriesMin="R' + $v + '" SeriesMax="R' + $v + '" />'
        $xml += '    <ComponentEntry AppName="' + $name + '" Version="' + $appVersion + '" ModuleName="./Contents/' + $v + '/' + $name + '.addin" AppDescription="CTS BIM Revit Plugin" AppType=".NET Assembly" LoadOnRevitStartup="True" />'
        $xml += '  </Components>'
    }
    $xml += '</ApplicationPackage>'
    Set-Content -Path (Join-Path $bundleDir 'PackageContents.xml') -Value ($xml -join "`r`n") -Encoding UTF8

    $zip = Join-Path $dist "$name.bundle.zip"
    if (Test-Path $zip) { Remove-Item $zip -Force }
    Compress-Archive -Path $bundleDir -DestinationPath $zip -Force
    Write-Host "Bundle: $bundleDir"
    Write-Host "Zip   : $zip"

    if ($InstallBundle) {
        $plugins = Join-Path $env:APPDATA 'Autodesk\ApplicationPlugins'
        New-Item -ItemType Directory -Force -Path $plugins | Out-Null
        $target = Join-Path $plugins "$name.bundle"
        if (Test-Path $target) { Remove-Item $target -Recurse -Force }
        Copy-Item -Path $bundleDir -Destination $target -Recurse -Force
        Write-Host "Installed to: $target"
    }
}

# ---------------------------------------------------------------- installer (.exe)
if ($Installer) {
    if ($failed.Count -gt 0) {
        Write-Host 'Skipping installer: some versions failed to build.' -ForegroundColor Yellow
    }
    else {
        Write-Host ''
        Write-Host '=== Installer ===' -ForegroundColor Cyan
        $iscc = @(
            "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
            "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
            "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
        ) | Where-Object { Test-Path $_ } | Select-Object -First 1
        if (-not $iscc) { throw 'Inno Setup 6 not found. Install it from https://jrsoftware.org/isdl.php' }
        & $iscc (Join-Path $root 'installer\CTSRevitPlugin.iss')
        if ($LASTEXITCODE -ne 0) { throw 'Inno Setup failed.' }
        Write-Host "Installer: $dist\CTSRevitPlugin_Setup_$appVersion.exe"
    }
}

# ---------------------------------------------------------------- summary
Write-Host ''
Write-Host '=== Summary ===' -ForegroundColor Cyan
foreach ($v in $built)  { Write-Host "  OK    Revit $v  ->  artifacts\bin\$v\$Configuration" -ForegroundColor Green }
foreach ($v in $failed) { Write-Host "  FAIL  Revit $v" -ForegroundColor Red }
if ($failed.Count -gt 0) { exit 1 }
