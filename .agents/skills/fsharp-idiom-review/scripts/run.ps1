#Requires -Version 7.2
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false

Push-Location $PSScriptRoot
try {
    $output = @(dotnet fsi --nologo Idioms.fsx 2>&1 | ForEach-Object { "$_" })
    $exitCode = $LASTEXITCODE
    $output
    $passed = @($output | Where-Object { $_ -like 'PASS *' }).Count
    if ($exitCode -ne 0 -or $passed -eq 0 -or ($output | Where-Object { $_ -like 'FAIL *' -or $_ -match 'error FS[0-9]+:' })) {
        throw "Idioms.fsx failed. Exit: $exitCode"
    }
} finally {
    Pop-Location
}
