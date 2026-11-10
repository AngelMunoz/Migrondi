#!/usr/bin/env pwsh
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
dotnet fsi ./Shape.fsx
exit $LASTEXITCODE
