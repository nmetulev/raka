<#
.SYNOPSIS
    Builds Raka CLI binaries and NuGet packages.

.DESCRIPTION
    Produces:
      artifacts/cli/raka-win-x64/     — CLI for x64
      artifacts/cli/raka-win-arm64/   — CLI for ARM64
      artifacts/nuget/                — NuGet packages
      artifacts/msix/                 — Signed MSIX packages + install helpers

.PARAMETER Configuration
    Build configuration (default: Release).

.PARAMETER Runtime
    Specific runtime to build (win-x64 or win-arm64). Default: both.

.PARAMETER SkipNuGet
    Skip NuGet package creation.

.PARAMETER SkipCli
    Skip CLI build.

.PARAMETER SkipMsix
    Skip MSIX package creation.

.EXAMPLE
    .\build.ps1
    .\build.ps1 -Runtime win-x64
    .\build.ps1 -SkipNuGet
    .\build.ps1 -SkipMsix
#>
param(
    [string]$Configuration = "Release",
    [string]$Runtime,
    [switch]$SkipNuGet,
    [switch]$SkipCli,
    [switch]$SkipMsix
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
            --output $outDir

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

# --- MSIX Packages ---
if (-not $SkipMsix -and -not $SkipCli) {
    $runtimes = if ($Runtime) { @($Runtime) } else { @("win-x64", "win-arm64") }
    $msixDir = Join-Path $artifactsDir "msix"
    $msixLayoutDir = Join-Path $artifactsDir "msix-layout"
    $msixSourceDir = Join-Path $root "msix"
    $msixManifestPath = Join-Path $msixSourceDir "appxmanifest.xml"
    $msixAssetsPath = Join-Path $msixSourceDir "Assets"
    $certPath = "devcert.pfx"
    $msixVersion = "$version.0"

    New-Item -ItemType Directory -Path $msixDir -Force | Out-Null

    Write-Host "`nPackaging MSIX (v$msixVersion)..." -ForegroundColor Yellow

    # Generate certificate if it doesn't exist
    if (-not (Test-Path $certPath)) {
        Write-Host "  Generating dev certificate..." -ForegroundColor Gray
        winapp cert generate --publisher "CN=Raka" --output "$certPath" --if-exists skip
        if ($LASTEXITCODE -ne 0) {
            Write-Error "Certificate generation failed"
            exit 1
        }
    } else {
        Write-Host "  Using existing certificate: $certPath" -ForegroundColor Gray
    }

    # Map runtime identifiers to MSIX architectures
    $archMap = @{ "win-x64" = "x64"; "win-arm64" = "arm64" }

    foreach ($rid in $runtimes) {
        $arch = $archMap[$rid]
        Write-Host "`n  Creating MSIX for $arch..." -ForegroundColor Yellow

        # Create layout directory
        $layoutDir = Join-Path $msixLayoutDir $arch
        if (Test-Path $layoutDir) {
            Remove-Item $layoutDir -Recurse -Force
        }
        New-Item -ItemType Directory -Path $layoutDir -Force | Out-Null

        # Copy published CLI binary
        $cliOutDir = Join-Path $artifactsDir "cli\raka-$rid"
        Copy-Item (Join-Path $cliOutDir "raka.exe") $layoutDir -Force

        # Copy assets
        Copy-Item $msixAssetsPath (Join-Path $layoutDir "Assets") -Recurse -Force

        # Copy and patch manifest
        [xml]$manifest = Get-Content $msixManifestPath
        $manifest.Package.Identity.Version = $msixVersion
        $manifest.Package.Identity.SetAttribute("ProcessorArchitecture", $arch)
        $manifest.Save((Join-Path $layoutDir "AppxManifest.xml"))

        # Package and sign
        $msixOutput = Join-Path $msixDir "raka_${msixVersion}_${arch}.msix"
        winapp package $layoutDir --cert "$certPath" --skip-pri --output "$msixOutput" --executable "raka.exe"
        if ($LASTEXITCODE -ne 0) {
            Write-Error "MSIX packaging failed for $arch"
            exit 1
        }

        Write-Host "  -> $msixOutput" -ForegroundColor Green
    }

    # Copy install helpers
    $installAssetsDir = Join-Path $msixSourceDir "install-assets"
    Copy-Item (Join-Path $installAssetsDir "install.ps1") $msixDir -Force
    Copy-Item (Join-Path $installAssetsDir "install.cmd") $msixDir -Force

    $readmeContent = Get-Content (Join-Path $installAssetsDir "README.md") -Raw
    $readmeContent = $readmeContent -replace '\[version\]', $msixVersion
    $readmeContent | Set-Content (Join-Path $msixDir "README.md") -Encoding UTF8

    Write-Host "`n  Install helpers copied to $msixDir" -ForegroundColor Green

    # Clean up layout directory and temporary cert
    if (Test-Path $msixLayoutDir) {
        Remove-Item $msixLayoutDir -Recurse -Force
    }
    if (Test-Path $certPath) {
        Remove-Item $certPath -Force
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

if (-not $SkipMsix -and -not $SkipCli) {
    $msixDir = Join-Path $artifactsDir "msix"
    if (Test-Path $msixDir) {
        Get-ChildItem $msixDir -Filter *.msix | ForEach-Object {
            $size = [math]::Round($_.Length / 1MB, 1)
            Write-Host "  MSIX: $($_.Name) ($($size) MB)"
        }
    }
}
