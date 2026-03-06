#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Install MSIX package and its certificate
.DESCRIPTION
    This script extracts and installs the certificate from an MSIX package to the local machine's
    Trusted People certificate store, then installs the package itself. This provides a complete
    installation experience in one command.
.PARAMETER PackagePath
    Path to the MSIX package file. If not specified, searches for .msix files in the current directory.
.PARAMETER CertPassword
    Password for the certificate if it's password-protected (optional).
.EXAMPLE
    .\install.ps1
    .\install.ps1 -PackagePath "raka_0.3.1.0_x64.msix"
#>

param(
    [Parameter(Mandatory=$false)]
    [string]$PackagePath,

    [Parameter(Mandatory=$false)]
    [SecureString]$CertPassword,

    [Parameter(Mandatory=$false)]
    [switch]$Elevated
)

$ErrorActionPreference = "Stop"

# Unblock downloaded files to avoid "downloaded from internet" warnings
Write-Host "Checking for blocked files..." -ForegroundColor Gray
$ScriptPath = $PSCommandPath
if ($ScriptPath -and (Test-Path $ScriptPath)) {
    try {
        Unblock-File -Path $ScriptPath -ErrorAction SilentlyContinue
        Write-Host "  - Unblocked installer script" -ForegroundColor Gray
    } catch { }
}

try {
    Get-ChildItem -Path (Split-Path $ScriptPath -Parent) -File | ForEach-Object {
        Unblock-File -Path $_.FullName -ErrorAction SilentlyContinue
    }
    Write-Host "  - Unblocked bundle files" -ForegroundColor Gray
} catch { }

trap {
    Write-Host ""
    Write-Host "========================================" -ForegroundColor Red
    Write-Host "  ERROR OCCURRED" -ForegroundColor Red
    Write-Host "========================================" -ForegroundColor Red
    Write-Host ""
    Write-Host "Error: $_" -ForegroundColor Red
    Write-Host ""

    if ($_.Exception) {
        Write-Host "Details:" -ForegroundColor Yellow
        Write-Host $_.Exception.Message -ForegroundColor Yellow
        Write-Host ""
    }

    if ($Elevated) {
        Write-Host "Press Enter to close this window..." -ForegroundColor Cyan
        Read-Host
    }

    exit 1
}

Write-Host ""
Write-Host "====================================" -ForegroundColor Cyan
Write-Host "  Raka MSIX Package Installer" -ForegroundColor Cyan
Write-Host "====================================" -ForegroundColor Cyan
Write-Host ""

# Check if running as Administrator
$currentPrincipal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
$isAdmin = $currentPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

if (-not $isAdmin) {
    Write-Host "[INFO] This script needs administrator privileges to install certificates." -ForegroundColor Yellow
    Write-Host ""

    $response = Read-Host "Would you like to elevate to Administrator? (Y/N)"

    if ($response -eq 'Y' -or $response -eq 'y') {
        Write-Host ""
        Write-Host "[ELEVATE] Restarting script with administrator privileges..." -ForegroundColor Blue
        Write-Host ""

        $ScriptDir = Split-Path $PSCommandPath -Parent
        $arguments = "-NoProfile -ExecutionPolicy Bypass -Command `"Set-Location '$ScriptDir'; & '$PSCommandPath' -Elevated"

        if (-not [string]::IsNullOrEmpty($PackagePath)) {
            $PackagePath = Resolve-Path $PackagePath -ErrorAction SilentlyContinue
            if ($PackagePath) {
                $arguments += " -PackagePath '$PackagePath'"
            }
        }

        $arguments += "`""

        try {
            Start-Process PowerShell -Verb RunAs -ArgumentList $arguments
            Write-Host "[INFO] Script is running in elevated window. Check that window for results." -ForegroundColor Cyan
            Write-Host ""
            exit 0
        } catch {
            Write-Error "Failed to elevate: $_"
            Write-Host ""
            Read-Host "Press Enter to exit"
            exit 1
        }
    } else {
        Write-Host ""
        Write-Host "[CANCELLED] Certificate installation requires administrator privileges." -ForegroundColor Yellow
        Write-Host ""
        exit 1
    }
}

Write-Host "[INFO] Running with administrator privileges" -ForegroundColor Green
Write-Host ""

$ScriptDir = Split-Path $PSCommandPath -Parent

# Find the package if not specified
if ([string]::IsNullOrEmpty($PackagePath)) {
    Write-Host "[SEARCH] Looking for MSIX package in script directory..." -ForegroundColor Blue
    Write-Host "[INFO] Script directory: $ScriptDir" -ForegroundColor Gray

    $CurrentArch = $env:PROCESSOR_ARCHITECTURE
    $ArchPattern = switch ($CurrentArch) {
        "AMD64" { "*_x64*.msix" }
        "ARM64" { "*_arm64*.msix" }
        default { "*.msix" }
    }

    Write-Host "[INFO] Detected architecture: $CurrentArch, looking for: $ArchPattern" -ForegroundColor Gray

    $packages = Get-ChildItem -Path $ScriptDir -Filter $ArchPattern | Select-Object -First 1

    if ($null -eq $packages) {
        Write-Host ""
        Write-Warning "No matching .msix file found for architecture: $CurrentArch"
        Write-Host "Looking for any .msix file..." -ForegroundColor Yellow
        $packages = Get-ChildItem -Path $ScriptDir -Filter "*.msix" | Select-Object -First 1

        if ($null -eq $packages) {
            Write-Error "No .msix files found in script directory: $ScriptDir"
            exit 1
        }
    }

    $PackagePath = $packages.FullName
    Write-Host "[FOUND] Using package: $($packages.Name)" -ForegroundColor Green
}

# Validate package exists
if (-not (Test-Path $PackagePath)) {
    Write-Error "Package not found at: $PackagePath"
    exit 1
}

$PackagePath = Resolve-Path $PackagePath
Write-Host "[INFO] Package: $PackagePath" -ForegroundColor Gray
Write-Host ""

# Create temporary directory for extraction
$TempDir = Join-Path $env:TEMP "msix-cert-install-$(Get-Random)"
New-Item -ItemType Directory -Path $TempDir -Force | Out-Null

try {
    Write-Host "[EXTRACT] Extracting certificate from package..." -ForegroundColor Blue

    $MsixExtractPath = Join-Path $TempDir "msix"
    New-Item -ItemType Directory -Path $MsixExtractPath -Force | Out-Null
    $MsixAsZip = Join-Path $TempDir "package.zip"
    Copy-Item $PackagePath $MsixAsZip -Force
    Expand-Archive -Path $MsixAsZip -DestinationPath $MsixExtractPath -Force

    Write-Host "[CERT] Extracting certificate information..." -ForegroundColor Blue

    $signature = Get-AuthenticodeSignature -FilePath $PackagePath
    $cert = $null

    if ($signature -and $signature.SignerCertificate) {
        $cert = $signature.SignerCertificate
        Write-Host "  - Found certificate in package signature" -ForegroundColor Gray
    } else {
        Write-Host "  - No signature in package, trying to extract manually..." -ForegroundColor Gray

        $signatureFile = Get-ChildItem -Path $MsixExtractPath -Filter "AppxSignature.p7x" -Recurse | Select-Object -First 1

        if ($null -eq $signatureFile) {
            Write-Host ""
            Write-Warning "No signature found in MSIX package. The package may not be signed."
            Write-Host ""
            exit 1
        }

        Write-Host "  - Found signature file, extracting certificate..." -ForegroundColor Gray
        try {
            $p7xBytes = [System.IO.File]::ReadAllBytes($signatureFile.FullName)
            $signedCms = New-Object System.Security.Cryptography.Pkcs.SignedCms
            $signedCms.Decode($p7xBytes)
            $cert = $signedCms.Certificates[0]
            Write-Host "  - Extracted certificate from AppxSignature.p7x" -ForegroundColor Gray
        } catch {
            Write-Error "Failed to extract certificate from signature file: $_"
            exit 1
        }
    }

    if ($null -eq $cert) {
        Write-Error "Could not extract certificate from package"
        exit 1
    }

    Write-Host ""
    Write-Host "Certificate Details:" -ForegroundColor White
    Write-Host "  Subject:    $($cert.Subject)" -ForegroundColor Gray
    Write-Host "  Issuer:     $($cert.Issuer)" -ForegroundColor Gray
    Write-Host "  Expires:    $($cert.NotAfter)" -ForegroundColor Gray
    Write-Host "  Thumbprint: $($cert.Thumbprint)" -ForegroundColor Gray
    Write-Host ""

    # Check if certificate is already installed
    $existingCert = Get-ChildItem -Path Cert:\LocalMachine\TrustedPeople | Where-Object { $_.Thumbprint -eq $cert.Thumbprint }

    if ($existingCert) {
        Write-Host "[INFO] Certificate is already installed in Trusted People store!" -ForegroundColor Green
    } else {
        Write-Host "[INSTALL] Installing certificate to Trusted People store..." -ForegroundColor Blue

        $store = New-Object System.Security.Cryptography.X509Certificates.X509Store("TrustedPeople", "LocalMachine")
        $store.Open("ReadWrite")
        $store.Add($cert)
        $store.Close()

        Write-Host "[SUCCESS] Certificate installed successfully!" -ForegroundColor Green
    }

    Write-Host ""

    # Check for existing Raka packages
    Write-Host "[INSTALL] Installing MSIX package..." -ForegroundColor Blue
    Write-Host "  Package: $PackagePath" -ForegroundColor Gray
    Write-Host ""

    $existingPackages = Get-AppxPackage | Where-Object { $_.Name -eq 'Raka' }
    if ($existingPackages) {
        Write-Host "[CHECK] Found existing Raka package(s):" -ForegroundColor Yellow
        foreach ($pkg in $existingPackages) {
            Write-Host "  - $($pkg.Name) v$($pkg.Version)" -ForegroundColor Yellow
        }
        Write-Host ""
        $response = Read-Host "Uninstall existing package(s) before installing? (Y/N)"
        if ($response -eq 'Y' -or $response -eq 'y') {
            foreach ($pkg in $existingPackages) {
                Write-Host "[REMOVE] Removing $($pkg.Name) v$($pkg.Version)..." -ForegroundColor Blue
                Remove-AppxPackage -Package $pkg.PackageFullName -ErrorAction SilentlyContinue
                Write-Host "  - Removed $($pkg.Name)" -ForegroundColor Gray
            }
            Write-Host ""
        }
    }

    try {
        Add-AppxPackage -Path $PackagePath -ErrorAction Stop
        Write-Host "[SUCCESS] MSIX package installed successfully!" -ForegroundColor Green
        Write-Host ""
        Write-Host "Raka has been installed. You can now use 'raka' from your terminal." -ForegroundColor Cyan
    } catch {
        Write-Host ""
        Write-Warning "Failed to install MSIX package automatically: $_"
        Write-Host ""
        Write-Host "You can try installing manually:" -ForegroundColor Yellow
        Write-Host "  1. Double-click the .msix file" -ForegroundColor Yellow
        Write-Host "  2. Or run: Add-AppxPackage -Path '$PackagePath'" -ForegroundColor Yellow
        Write-Host ""
    }

    Write-Host ""
    Write-Host "[DONE] Installation complete!" -ForegroundColor Green
    Write-Host ""

} finally {
    if (Test-Path $TempDir) {
        Remove-Item $TempDir -Recurse -Force -ErrorAction SilentlyContinue
    }

    if ($Elevated) {
        Write-Host ""
        Read-Host "Press Enter to exit"
    }
}
