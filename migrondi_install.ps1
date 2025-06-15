param (
    [string]$Version,
    [switch]$Latest,
    [string]$DownloadPath, # New parameter for download location
    [switch]$AddToProfile # New parameter to add to PATH in $PROFILE
)

$RepoOwner = "AngelMunoz"
$RepoName = "Migrondi"

# Auto-detect platform
$os = ""
$arch = ""

if ([System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Windows)) {
    $os = "win"
}
elseif ([System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Linux)) {
    $os = "linux"
}
elseif ([System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::OSX)) {
    $os = "osx"
}
else {
    Write-Error "Unsupported operating system."
    exit 1
}

$runtimeArch = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture
switch ($runtimeArch) {
    ([System.Runtime.InteropServices.Architecture]::X64) { $arch = "x64" }
    ([System.Runtime.InteropServices.Architecture]::Arm64) { $arch = "arm64" }
    default {
        Write-Error "Unsupported architecture: $runtimeArch"
        exit 1
    }
}

$selectedPlatform = "$os-$arch"
Write-Host "Detected platform: $selectedPlatform"

# Determine the effective directory for download and extraction
$DefaultInstallBaseDir = Join-Path -Path $env:LOCALAPPDATA -ChildPath "Migrondi" # Default to $env:LOCALAPPDATA/Migrondi
$EffectiveDownloadDir = $DefaultInstallBaseDir

if ($PSBoundParameters.ContainsKey('DownloadPath') -and -not [string]::IsNullOrEmpty($DownloadPath)) {
    $EffectiveDownloadDir = $DownloadPath
}

# Ensure the target directory exists, create if not
if (-not (Test-Path $EffectiveDownloadDir)) {
    Write-Host "Target directory '$EffectiveDownloadDir' does not exist. Creating it..."
    try {
        New-Item -ItemType Directory -Path $EffectiveDownloadDir -Force -ErrorAction Stop | Out-Null
        Write-Host "Successfully created directory: $EffectiveDownloadDir"
    } catch {
        Write-Error "Failed to create directory '$EffectiveDownloadDir': $_"
        exit 1
    }
}
# Ensure $EffectiveDownloadDir is an absolute path for robustness
$EffectiveDownloadDir = Resolve-Path -Path $EffectiveDownloadDir

# Determine the release tag based on parameters
if ($PSBoundParameters.ContainsKey('Version') -and -not [string]::IsNullOrEmpty($Version)) {
    $releaseTag = $Version
    Write-Host "Using specified version: $releaseTag"
} else {
    # No Version provided, or -Latest was used (which doesn't matter if Version isn't there as latest is default)
    $latestReleaseUrl = "https://api.github.com/repos/$RepoOwner/$RepoName/releases/latest"
    Write-Host "Fetching latest release information (version not specified or -Latest flag used)..."
    try {
        $latestReleaseInfo = Invoke-RestMethod -Uri $latestReleaseUrl -ErrorAction Stop
        $releaseTag = $latestReleaseInfo.tag_name
        Write-Host "Using latest release tag: $releaseTag"
    } catch {
        Write-Error "Failed to fetch latest release information: $_"
        exit 1
    }
}

# $selectedPlatform is like "win-x64", "linux-arm64", etc.
# Asset names in the release are like "win-x64.zip", "linux-arm64.zip"
$targetAssetFilename = "$selectedPlatform.zip"
# $outputFileName = $targetAssetFilename # Local file will be named like "win-x64.zip" # This line is effectively replaced by $ZipFilePath

$downloadUrl = "https://github.com/$RepoOwner/$RepoName/releases/download/$releaseTag/$targetAssetFilename"

$ZipFilePath = Join-Path -Path $EffectiveDownloadDir -ChildPath $targetAssetFilename
$ExtractionDirName = "migrondi" # Name of the subdirectory for extracted content
$ExtractionDirPath = Join-Path -Path $EffectiveDownloadDir -ChildPath $ExtractionDirName

Write-Host "Downloading $targetAssetFilename to $ZipFilePath from $downloadUrl..."

try {
    Invoke-WebRequest -Uri $downloadUrl -OutFile $ZipFilePath -ErrorAction Stop
    Write-Host "Successfully downloaded to $ZipFilePath"

    # $extractPath = Join-Path -Path $PSScriptRoot -ChildPath "migrondi" # Old logic
    if (-not (Test-Path $ExtractionDirPath)) {
        New-Item -ItemType Directory -Path $ExtractionDirPath | Out-Null
    }

    Write-Host "Extracting $ZipFilePath to $ExtractionDirPath..."
    Expand-Archive -Path $ZipFilePath -DestinationPath $ExtractionDirPath -Force -ErrorAction Stop
    Write-Host "Successfully extracted to $ExtractionDirPath"

    Remove-Item -Path $ZipFilePath
    Write-Host "Removed $ZipFilePath"

    # Create proxy script
    $ProxyScriptFilePath = Join-Path -Path $EffectiveDownloadDir -ChildPath "migrondi.ps1"
    $ProxyScriptContent = @"
param(
    [Parameter(ValueFromRemainingArguments = `$true)]
    [object[]]`$Arguments
)
# This script assumes Migrondi.exe is in a subdirectory named '$ExtractionDirName' relative to this script's location.
`$ExePath = Join-Path `$PSScriptRoot "$ExtractionDirName" "Migrondi.exe"
& `$ExePath `$Arguments
"@
    Set-Content -Path $ProxyScriptFilePath -Value $ProxyScriptContent
    Write-Host "Created Migrondi proxy script at $ProxyScriptFilePath"

    # Add to profile by default, unless -AddToProfile:$false is specified
    if (-not ($PSBoundParameters.ContainsKey('AddToProfile') -and $AddToProfile -eq $false)) {
        $MigrondiProxyDirActualResolved = $EffectiveDownloadDir # This is the actual resolved path where migrondi.ps1 is
        
        $PathStringForProfileFile = "" # This will hold either the literal '$env:...' or the resolved custom path
        $PathExistenceCheckRegex = "" # Regex to check if it's already there

        # Resolve the conceptual default directory for comparison
        $ConceptualDefaultInstallDirResolved = ""
        try {
            $ConceptualDefaultInstallDirResolved = Resolve-Path (Join-Path -Path $env:LOCALAPPDATA -ChildPath "Migrondi") -ErrorAction Stop
        } catch {
            # This case should be rare, means $env:LOCALAPPDATA might be problematic or non-existent.
            # Fallback to using the $EffectiveDownloadDir as a literal path if resolution fails.
            Write-Warning "Could not resolve default install directory based on \$env:LOCALAPPDATA. Using absolute path for profile update."
            $ConceptualDefaultInstallDirResolved = "" # Ensure it doesn't match if resolution failed
        }

        if (($ConceptualDefaultInstallDirResolved -ne "") -and ($MigrondiProxyDirActualResolved -eq $ConceptualDefaultInstallDirResolved)) {
            # Default directory case: use literal $env:LOCALAPPDATA in the profile string
            $PathStringForProfileFile = Join-Path -Path '$env:LOCALAPPDATA' -ChildPath "Migrondi" # Literal string for profile
            $EscapedPathForRegex = [regex]::Escape($PathStringForProfileFile) # Escape the literal string for regex
            $PathExistenceCheckRegex = ('\$env:PATH\s*[+\-]?=\s*.*' + $EscapedPathForRegex)
        } else {
            # Custom directory case (or if default path resolution failed): use the resolved $EffectiveDownloadDir
            $PathStringForProfileFile = $MigrondiProxyDirActualResolved
            $EscapedPathForRegex = [regex]::Escape($MigrondiProxyDirActualResolved)
            $PathExistenceCheckRegex = ('\$env:PATH\s*[+\-]?=\s*.*' + $EscapedPathForRegex)
        }

        Write-Host "Attempting to add '$PathStringForProfileFile' to PATH in PowerShell profile ($PROFILE)..."

        # Ensure the profile file exists, create if not
        if (-not (Test-Path $PROFILE)) {
            try {
                Write-Host "Profile file ($PROFILE) does not exist. Creating it..."
                New-Item -Path $PROFILE -ItemType File -Force -ErrorAction Stop | Out-Null
                Write-Host "Successfully created profile file: $PROFILE"
            } catch {
                Write-Error "Failed to create profile file ($PROFILE): $_. Please create it manually and add '$PathStringForProfileFile' to your PATH."
            }
        }
        
        if (Test-Path $PROFILE) {
            try {
                $ProfileContent = Get-Content $PROFILE -Raw -ErrorAction SilentlyContinue
                
                # Check if the directory is already in a line that modifies $env:PATH
                if ($ProfileContent -match $PathExistenceCheckRegex) {
                    Write-Host "'$PathStringForProfileFile' appears to be already configured in the PATH in $PROFILE."
                } else {
                    $PathSeparator = [System.IO.Path]::PathSeparator
                    $Comment = "# Added by migrondi_install.ps1 to include Migrondi CLI"
                    $PathAddCommand = "`$env:PATH += '$PathSeparator$PathStringForProfileFile'"
                    
                    $FinalCommandToAdd = ""
                    $BaseContentToAdd = "`n$Comment`n$PathAddCommand"

                    if (-not [string]::IsNullOrEmpty($ProfileContent) -and $ProfileContent[-1] -ne "`n" -and $ProfileContent[-1] -ne "`r") {
                        $FinalCommandToAdd = "`n" + $BaseContentToAdd # Extra newline if profile not empty and no trailing newline
                    } else {
                        $FinalCommandToAdd = $BaseContentToAdd
                    }

                    Add-Content -Path $PROFILE -Value $FinalCommandToAdd -ErrorAction Stop
                    Write-Host "Successfully added '$PathStringForProfileFile' to PATH in $PROFILE."
                    Write-Host "Please restart your PowerShell session or run '. $PROFILE' to apply the changes."
                }
            } catch {
                Write-Error "Failed to update $($PROFILE): $_"
            }
        }
    }

} catch {
    Write-Error "Failed to download the asset: $_"
    # Attempt to list available assets for the release tag if download fails
    $releaseAssetsUrl = "https://api.github.com/repos/$RepoOwner/$RepoName/releases/tags/$releaseTag"
    try {
        Write-Host "Fetching available assets for tag $releaseTag..."
        $releaseInfo = Invoke-RestMethod -Uri $releaseAssetsUrl
        Write-Host "Available assets for release $($releaseTag):" # Corrected variable interpolation
        $releaseInfo.assets | ForEach-Object { Write-Host "- $($_.name)" }
    } catch {
        Write-Warning "Could not retrieve asset list for tag $releaseTag."
    }
    exit 1
}
