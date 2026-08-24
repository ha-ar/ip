<#
.SYNOPSIS
    Sets up a Windows machine to build and run this WPF app (ip.csproj) from source.

.DESCRIPTION
    - Verifies Windows + winget are available.
    - Installs the .NET 10 SDK if missing.
    - Restores NuGet packages.
    - Publishes a self-contained, single-file win-x64 build (Properties/PublishProfiles/FolderProfile.pubxml).
    - Optionally launches the built exe elevated (the app requires Administrator per app.manifest).

.USAGE
    Run from an elevated PowerShell prompt in the project root:
        .\setup-windows.ps1
        .\setup-windows.ps1 -Run          # also launch the app after building
        .\setup-windows.ps1 -SkipBuild     # only install prerequisites
#>

[CmdletBinding()]
param(
    [switch]$Run,
    [switch]$SkipBuild,
    [switch]$NoClean
)

$ErrorActionPreference = 'Stop'

function Write-Step($msg) {
    Write-Host "`n==> $msg" -ForegroundColor Cyan
}

function Test-Command($name) {
    return [bool](Get-Command $name -ErrorAction SilentlyContinue)
}

# --- 1. Sanity checks -------------------------------------------------------

if ($PSVersionTable.PSVersion.Major -lt 5) {
    throw "PowerShell 5.1+ is required."
}

if (-not [System.Environment]::OSVersion.Platform -eq 'Win32NT') {
    throw "This script must run on Windows."
}

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Warning "Not running elevated. Building will still work, but the app itself requires Administrator to launch (see app.manifest)."
}

# --- 2. .NET 10 SDK ----------------------------------------------------------

Write-Step "Checking for .NET SDK"

$needsSdk = $true
if (Test-Command 'dotnet') {
    $sdks = dotnet --list-sdks 2>$null
    if ($sdks -match '^10\.') {
        Write-Host "Found .NET 10 SDK." -ForegroundColor Green
        $needsSdk = $false
    }
}

if ($needsSdk) {
    if (Test-Command 'winget') {
        Write-Step "Installing .NET 10 SDK via winget"
        # --source winget avoids querying the msstore source, which can fail with
        # a certificate error on some machines and aborts the whole command.
        winget install --id Microsoft.DotNet.SDK.10 --source winget --accept-source-agreements --accept-package-agreements
        if ($LASTEXITCODE -ne 0) {
            throw "winget install failed (exit code $LASTEXITCODE). Install the .NET 10 SDK manually from https://dotnet.microsoft.com/download/dotnet/10.0 and re-run this script."
        }

        # Refresh PATH in this process from the machine + user env vars, since a
        # freshly-installed dotnet won't be visible until the shell restarts otherwise.u
        $env:Path = [System.Environment]::GetEnvironmentVariable('Path', 'Machine') + ';' +
                    [System.Environment]::GetEnvironmentVariable('Path', 'User')

        if (-not (Test-Command 'dotnet')) {
            throw "dotnet SDK installed but not found on PATH in this session. Close and reopen PowerShell, then re-run this script."
        }
    } else {
        throw "winget not found and .NET 10 SDK is missing. Install it manually from https://dotnet.microsoft.com/download/dotnet/10.0 and re-run this script."
    }
}

# --- 3. Restore + publish ----------------------------------------------------

function Test-NetworkDrive([string]$path) {
    $driveLetter = (Resolve-Path $path).Path.Substring(0, 2)
    $disk = Get-CimInstance -ClassName Win32_LogicalDisk -Filter "DeviceID='$driveLetter'" -ErrorAction SilentlyContinue
    # DriveType 4 = Network
    return ($disk -and $disk.DriveType -eq 4)
}

$sourceDir = $PSScriptRoot
$buildDir = $sourceDir
$stagedLocally = $false

if (-not $SkipBuild -and (Test-NetworkDrive $sourceDir)) {
    # MSBuild's implicit `Page Include="**/*.xaml"` glob (used by the WPF SDK) is
    # known to fail with "BG1002: File '**/*.xaml' cannot be found" when the project
    # lives on a mapped network drive. Stage a local copy and build from there instead.
    Write-Step "Detected network drive ($sourceDir) - staging a local copy to build from"
    $buildDir = Join-Path $env:LOCALAPPDATA 'ip-build\src'
    if (Test-Path $buildDir) {
        Remove-Item $buildDir -Recurse -Force
    }
    New-Item -ItemType Directory -Path $buildDir -Force | Out-Null

    robocopy $sourceDir $buildDir /E /XD bin obj .git .vs /NFL /NDL /NJH /NJS | Out-Null
    if ($LASTEXITCODE -ge 8) {
        throw "robocopy failed staging project to $buildDir (exit code $LASTEXITCODE)."
    }
    $stagedLocally = $true
    Write-Host "Staged to $buildDir" -ForegroundColor Green
}

Push-Location $buildDir
try {
    if (-not $SkipBuild) {
        if (-not $NoClean) {
            # bin/ and obj/ can carry stale, machine-specific paths (e.g. copied over
            # from another OS/machine) that break the WPF XAML-compile step with
            # errors like "BG1002: File '**/*.xaml' cannot be found". Safe to delete;
            # both are build output, not source.
            Write-Step "Cleaning stale bin/ and obj/ output"
            foreach ($dir in 'bin', 'obj') {
                $path = Join-Path $buildDir $dir
                if (Test-Path $path) {
                    Remove-Item $path -Recurse -Force
                }
            }
        }

        Write-Step "Restoring NuGet packages"
        dotnet restore ip.csproj
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet restore failed (exit code $LASTEXITCODE)."
        }

        Write-Step "Publishing (Release, win-x64, self-contained, single-file)"
        dotnet publish ip.csproj -p:PublishProfile=FolderProfile -c Release
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet publish failed (exit code $LASTEXITCODE)."
        }

        $publishedExe = Join-Path $buildDir 'bin\Release\net10.0-windows\publish\win-x64\ip.exe'
        if (-not (Test-Path $publishedExe)) {
            throw "Publish completed but expected exe not found at $publishedExe"
        }

        if ($stagedLocally) {
            # Copy the build output back next to the source for convenience; this is a
            # plain file copy (no MSBuild globbing involved), so the network drive is fine here.
            $destDir = Join-Path $sourceDir 'bin\Release\net10.0-windows\publish\win-x64'
            New-Item -ItemType Directory -Path $destDir -Force | Out-Null
            robocopy (Split-Path $publishedExe) $destDir /E /NFL /NDL /NJH /NJS | Out-Null
            if ($LASTEXITCODE -ge 8) {
                throw "robocopy failed copying publish output back to $destDir (exit code $LASTEXITCODE)."
            }
            Write-Host "`nBuild succeeded: $publishedExe" -ForegroundColor Green
            Write-Host "Also copied to: $destDir\ip.exe" -ForegroundColor Green
        } else {
            Write-Host "`nBuild succeeded: $publishedExe" -ForegroundColor Green
        }
    }

    if ($Run) {
        Write-Step "Launching app (elevated, as required by app.manifest)"
        Start-Process -FilePath $publishedExe -Verb RunAs
    }
}
finally {
    Pop-Location
}
