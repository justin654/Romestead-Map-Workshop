<#
.SYNOPSIS
  Build a self-contained single-file release of MapWorkshop and zip it up.

.EXAMPLE
  .\publish.ps1
  .\publish.ps1 -Version 0.2.0
#>
param(
    [string]$Version = "",
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

$publishDir = Join-Path $PSScriptRoot "bin\$Configuration\net8.0-windows\$Runtime\publish"
$artifactDir = Join-Path $PSScriptRoot "artifacts"

if (Test-Path $publishDir) { Remove-Item -Recurse -Force $publishDir }
New-Item -ItemType Directory -Force -Path $artifactDir | Out-Null

$args = @(
    "publish",
    "-c", $Configuration,
    "-r", $Runtime,
    "--self-contained", "true",
    "/p:PublishSingleFile=true",
    "/p:IncludeNativeLibrariesForSelfExtract=true"
)
if ($Version) { $args += @("/p:Version=$Version", "/p:AssemblyVersion=$Version.0", "/p:FileVersion=$Version.0") }

Write-Host "dotnet $($args -join ' ')"
dotnet @args
if ($LASTEXITCODE -ne 0) { throw "publish failed" }

$tag = if ($Version) { "v$Version" } else { Get-Date -Format "yyyyMMdd-HHmm" }
$zip = Join-Path $artifactDir "MapWorkshop-$Runtime-$tag.zip"
if (Test-Path $zip) { Remove-Item $zip }

# Bundle the exe with the README + LICENSE so the zip is self-explanatory.
$stage = Join-Path $env:TEMP "MapWorkshop-stage-$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Force -Path $stage | Out-Null
try {
    Copy-Item -Path (Join-Path $publishDir "MapWorkshop.exe") -Destination $stage
    Copy-Item -Path (Join-Path $PSScriptRoot "README.md")     -Destination $stage
    Copy-Item -Path (Join-Path $PSScriptRoot "LICENSE")       -Destination $stage
    Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $zip
} finally {
    Remove-Item -Recurse -Force $stage -ErrorAction SilentlyContinue
}

Write-Host ""
Write-Host "Published: $zip"
$size = (Get-Item $zip).Length / 1MB
Write-Host ("  zip size: {0:F1} MB" -f $size)
