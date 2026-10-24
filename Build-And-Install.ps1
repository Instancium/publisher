<#
.SYNOPSIS
    Builds InstantPublisher and installs it into the system-wide data directory.
.DESCRIPTION
    Publishes the InstantPublisher project (Release by default) into a temporary staging
    folder, then copies the publish output into the target install directory.
    An existing appsettings.json in the target directory is NEVER overwritten:
    the previously deployed configuration is preserved as-is.
.PARAMETER Configuration
    Build configuration passed to dotnet publish. Default: Release.
.PARAMETER InstallDir
    Destination directory. Default: C:\ProgramData\Instancium\InstantPublisher.
.PARAMETER ProjectPath
    Path to the InstantPublisher project file.
    Default: src\InstantPublisher\InstantPublisher.csproj relative to this script.
.EXAMPLE
    .\Build-And-Install.ps1
.EXAMPLE
    .\Build-And-Install.ps1 -Configuration Debug -InstallDir 'D:\Tools\InstantPublisher'
#>
[CmdletBinding()]
param(
    [string] $Configuration = 'Release',
    [string] $InstallDir = 'C:\ProgramData\Instancium\Publisher',
    [string] $ProjectPath = (Join-Path $PSScriptRoot 'src\Instancium.Publisher\Instancium.Publisher.csproj')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$ConfigFileName = 'appsettings.json'

function Write-Step {
    param([string] $Message)
    Write-Host "==> $Message" -ForegroundColor Cyan
}

if (-not (Test-Path -LiteralPath $ProjectPath -PathType Leaf)) {
    throw "Project file not found: $ProjectPath"
}

$dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnetCommand) {
    throw "dotnet CLI not found in PATH. Install the .NET 10 SDK first."
}

$stagingDir = Join-Path ([System.IO.Path]::GetTempPath()) ("InstanciumPublisher_publish_" + [System.Guid]::NewGuid().ToString('N'))

Write-Step "Publishing project ($Configuration) to staging: $stagingDir"
& dotnet publish $ProjectPath -c $Configuration -o $stagingDir --nologo -v minimal
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

try {
    if (-not (Test-Path -LiteralPath $InstallDir -PathType Container)) {
        Write-Step "Creating install directory: $InstallDir"
        try {
            New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null
        }
        catch {
            throw "Unable to create '$InstallDir'. Run the script from an elevated shell or adjust directory permissions. Inner error: $($_.Exception.Message)"
        }
    }

    $preserveConfig = Test-Path -LiteralPath (Join-Path $InstallDir $ConfigFileName) -PathType Leaf
    if ($preserveConfig) {
        Write-Step "Existing $ConfigFileName detected in target: it will be preserved."
    }

    Write-Step "Copying publish output to: $InstallDir"
    $stagingRoot = (Resolve-Path -LiteralPath $stagingDir).Path.TrimEnd('\')
    $copiedCount = 0

    Get-ChildItem -LiteralPath $stagingRoot -Recurse -File | ForEach-Object {
        $relativePath = $_.FullName.Substring($stagingRoot.Length).TrimStart('\')

        if ($preserveConfig -and $relativePath -ieq $ConfigFileName) {
            Write-Host "    skip (preserve): $relativePath" -ForegroundColor DarkGray
            return
        }

        $destination = Join-Path $InstallDir $relativePath
        $destinationDir = Split-Path -Path $destination -Parent
        if (-not (Test-Path -LiteralPath $destinationDir -PathType Container)) {
            New-Item -ItemType Directory -Path $destinationDir -Force | Out-Null
        }

        Copy-Item -LiteralPath $_.FullName -Destination $destination -Force
        $copiedCount++
    }

    Write-Step "Copied files: $copiedCount."
    if ($preserveConfig) {
        Write-Host "    NOTE: deployed $ConfigFileName was kept; merge new settings manually if the schema changed." -ForegroundColor Yellow
    }

    Write-Step "Installation completed: $InstallDir"
}
finally {
    Write-Step "Cleaning staging: $stagingDir"
    Remove-Item -LiteralPath $stagingDir -Recurse -Force -ErrorAction SilentlyContinue
}