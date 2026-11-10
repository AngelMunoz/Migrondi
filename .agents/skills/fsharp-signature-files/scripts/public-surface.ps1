#Requires -Version 7.4
param(
    [Parameter(Mandatory)][string]$Assembly,
    [string]$OutFile = ''
)
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false

function ConvertTo-FSharpString([string]$Value) { '"' + $Value.Replace('\', '\\').Replace('"', '\"') + '"' }

$path = (Resolve-Path -LiteralPath $Assembly).Path
$dir = Join-Path ([IO.Path]::GetTempPath()) "public-surface-$([guid]::NewGuid().ToString('N').Substring(0, 8))"
New-Item -ItemType Directory -Path $dir | Out-Null
$probe = Join-Path $dir 'probe.fsx'
$script = (Join-Path $PSScriptRoot 'PublicSurface.fsx').Replace('\', '/')
@(
    "#load `"$script`""
    "PublicSurface.print $(ConvertTo-FSharpString $path)"
) | Set-Content -Path $probe -Encoding utf8

# dotnet selects its SDK from global.json above the working directory.
Push-Location $PSScriptRoot
try {
    $lines = @(dotnet fsi --nologo $probe)
    $exitCode = $LASTEXITCODE
} finally {
    Pop-Location
    Remove-Item -Recurse -Force $dir -ErrorAction SilentlyContinue
}
if ($exitCode -eq 0 -and $OutFile) { Set-Content -Path $OutFile -Value $lines -Encoding utf8 }
$lines
exit $exitCode
