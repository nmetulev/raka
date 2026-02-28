<#
.SYNOPSIS
    Builds Raka CLI binaries and NuGet packages.

.DESCRIPTION
    Produces:
      artifacts/cli/raka-win-x64/     — CLI for x64
      artifacts/cli/raka-win-arm64/   — CLI for ARM64
      artifacts/nuget/                — NuGet packages

.PARAMETER Configuration
    Build configuration (default: Release).

.PARAMETER Runtime
    Specific runtime to build (win-x64 or win-arm64). Default: both.

.PARAMETER SkipNuGet
    Skip NuGet package creation.

.PARAMETER SkipCli
    Skip CLI build.

.EXAMPLE
    .\build.ps1
    .\build.ps1 -Runtime win-x64
    .\build.ps1 -SkipNuGet
#>
param(
    [string]$Configuration = "Release",
    [string]$Runtime,
    [switch]$SkipNuGet,
    [switch]$SkipCli
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$artifactsDir = Join-Path $root "artifacts"

# Read version from Directory.Build.props
[xml]$buildProps = Get-Content (Join-Path $root "Directory.Build.props")
$version = $buildProps.Project.PropertyGroup.Version
Write-Host "Building Raka v$version" -ForegroundColor Cyan
Write-Host "Configuration: $Configuration" -ForegroundColor Gray

# Clean artifacts
if (Test-Path $artifactsDir) {
    Remove-Item $artifactsDir -Recurse -Force
}
New-Item -ItemType Directory -Path $artifactsDir -Force | Out-Null

# --- CLI Binaries ---
if (-not $SkipCli) {
    $runtimes = if ($Runtime) { @($Runtime) } else { @("win-x64", "win-arm64") }
    $cliProject = Join-Path $root "src\Raka.Cli\Raka.Cli.csproj"

    foreach ($rid in $runtimes) {
        Write-Host "`nBuilding CLI for $rid..." -ForegroundColor Yellow
        $outDir = Join-Path $artifactsDir "cli\raka-$rid"

        dotnet publish $cliProject `
            --configuration $Configuration `
            --runtime $rid `
            --self-contained true `
            --output $outDir `
            -p:PublishSingleFile=true `
            -p:IncludeNativeLibrariesForSelfExtract=true `
            -p:EnableCompressionInSingleFile=true

        if ($LASTEXITCODE -ne 0) {
            Write-Error "CLI build failed for $rid"
            exit 1
        }

        # Create zip
        $zipPath = Join-Path $artifactsDir "cli\raka-$rid-v$version.zip"
        Compress-Archive -Path "$outDir\*" -DestinationPath $zipPath -Force
        Write-Host "  -> $zipPath" -ForegroundColor Green
    }
}

# --- NuGet Packages ---
if (-not $SkipNuGet) {
    Write-Host "`nPacking NuGet packages..." -ForegroundColor Yellow
    $nugetDir = Join-Path $artifactsDir "nuget"
    New-Item -ItemType Directory -Path $nugetDir -Force | Out-Null

    # Pack DevTools (includes Protocol via PrivateAssets)
    dotnet pack (Join-Path $root "src\Raka.DevTools\Raka.DevTools.csproj") `
        --configuration $Configuration `
        --output $nugetDir

    if ($LASTEXITCODE -ne 0) {
        Write-Error "DevTools NuGet pack failed"
        exit 1
    }

    Write-Host "  -> $nugetDir" -ForegroundColor Green
    Get-ChildItem $nugetDir -Filter *.nupkg | ForEach-Object {
        Write-Host "     $($_.Name)" -ForegroundColor Green
    }
}

# --- Summary ---
Write-Host "`n--- Build Complete ---" -ForegroundColor Cyan
Write-Host "Version:   $version"
Write-Host "Artifacts: $artifactsDir"

if (-not $SkipCli) {
    Get-ChildItem (Join-Path $artifactsDir "cli") -Filter *.zip | ForEach-Object {
        $size = [math]::Round($_.Length / 1MB, 1)
        Write-Host "  CLI: $($_.Name) ($($size) MB)"
    }
}

if (-not $SkipNuGet) {
    Get-ChildItem (Join-Path $artifactsDir "nuget") -Filter *.nupkg | ForEach-Object {
        $size = [math]::Round($_.Length / 1KB, 0)
        Write-Host "  NuGet: $($_.Name) ($($size) KB)"
    }
}
