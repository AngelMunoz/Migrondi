#!/usr/bin/env pwsh
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
dotnet run --project ./Shape
exit $LASTEXITCODE
