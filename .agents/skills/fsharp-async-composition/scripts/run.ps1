#Requires -Version 7.2
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false

Push-Location $PSScriptRoot
try {
    $proofs = @(dotnet fsi Proofs.fsx 2>&1 | ForEach-Object { "$_" })
    $proofs
    if ($LASTEXITCODE -ne 0 -or -not ($proofs -match '^PASS ')) {
        throw "Proofs.fsx failed. Exit: $LASTEXITCODE"
    }

    $crash = @(dotnet fsi StartCrash.fsx 2>&1 | ForEach-Object { "$_" })
    $crashText = $crash -join "`n"
    if ($LASTEXITCODE -eq 0 -or $crashText -match 'process survived|error FS\d+' -or $crashText -notmatch 'unhandled in Async\.Start') {
        throw "Expected an unhandled exception in Async.Start to stop the process.`n$($crash -join "`n")"
    }
    'PASS Async.Start: an unhandled exception stops the process'

    $build = @(dotnet build StateMachine/StateMachine.fsproj -c Release --no-incremental -nologo 2>&1 | ForEach-Object { "$_" })
    if ($LASTEXITCODE -ne 0) { throw "StateMachine build failed.`n$($build -join "`n")" }
    $warnings = @($build | Select-String -Pattern '([A-Za-z]+\.fs)\(\d+,\d+\): warning FS3511' | ForEach-Object { $_.Matches[0].Groups[1].Value } | Sort-Object -Unique)
    if ($warnings.Count -ne 1 -or $warnings[0] -ne 'Dynamic.fs') {
        throw "Expected FS3511 only in Dynamic.fs. Found: $($warnings -join ', ')`n$($build -join "`n")"
    }
    'PASS FS3511: a let rec inside task makes a dynamic state machine; the same code with the let rec outside compiles statically'
} finally {
    Pop-Location
}
