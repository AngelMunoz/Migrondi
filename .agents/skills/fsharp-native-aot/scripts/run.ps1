#Requires -Version 7.2
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false

$expected = [ordered]@{
    'classic-s'                         = @('same', '')
    'classic-d'                         = @('throws', 'EXN NotSupportedException')
    'classic-f'                         = @('throws', 'EXN NotSupportedException')
    'classic-b'                         = @('throws', 'EXN NotSupportedException')
    'failwithf-classic'                 = @('differs', 'OK NotSupportedException:')
    'interp-d'                          = @('same', '')
    'interp-f'                          = @('same', '')
    'interp-flags'                      = @('same', '')
    'sprintf-interp'                    = @('same', '')
    'failwithf-interp'                  = @('same', '')
    'A-record-plain'                    = @('same', '')
    'A-some-int'                        = @('same', '')
    'A-list-int'                        = @('same', '')
    'A-int'                             = @('throws', 'EXN NotSupportedException')
    'string-union'                      = @('differs', 'OK Rect')
    'A-union'                           = @('differs', 'OK Rect')
    'A-tuple'                           = @('differs', 'OK ()')
    'interp-A-tuple'                    = @('differs', 'OK ()')
    'A-map'                             = @('differs', 'OK ')
    'string-record-with-union'          = @('differs', 'OK { Id = 1\n  Shape = Rect }')
    'string-record-with-tuple'          = @('differs', 'OK { Id = 1\n  Pair = () }')
    'string-record-with-map'            = @('differs', 'OK { Id = 1\n  Items = ')
    'string-union-override'             = @('same', '')
    'A-union-override'                  = @('differs', 'OK Err')
    'string-record-with-override-union' = @('differs', 'OK { Id = 1\n  Code = Err }')
    'log-union-arg'                     = @('differs', 'OK Shape Rect')
    'log-interp-union'                  = @('differs', 'OK Shape Rect')
    'log-plain-args'                    = @('same', '')
    'json-reflection'                   = @('same', '')
    'json-context-plain-option'         = @('same', '')
    'json-context-fsharp-converters'    = @('same', '')
    'quotation-eval'                    = @('throws', 'EXN InvalidOperationException')
}

function Read-Results([string[]]$Lines) {
    $map = @{}
    foreach ($line in $Lines) {
        $parts = $line -split ' \| ', 2
        if ($parts.Count -eq 2) { $map[$parts[0]] = $parts[1] }
    }
    $map
}

function Invoke-Checked([string]$What, [scriptblock]$Command) {
    $output = & $Command 2>&1
    if ($LASTEXITCODE -ne 0) { throw "$What failed. Exit: $LASTEXITCODE`n$($output -join "`n")" }
    $output
}

$rid = [System.Runtime.InteropServices.RuntimeInformation]::RuntimeIdentifier
$exe = if ($IsWindows) { 'Probe.exe' } else { 'Probe' }
$work = Join-Path ([IO.Path]::GetTempPath()) "fsharp-native-aot-proof-$PID"

Push-Location $PSScriptRoot
try {
    Invoke-Checked 'JIT build' { dotnet build Probe -c Release -o "$work/jit" } | Out-Null
    $jit = Read-Results (Invoke-Checked 'JIT run' { dotnet "$work/jit/Probe.dll" })

    $publish = Invoke-Checked 'AOT publish' { dotnet publish Probe -c Release -r $rid -o "$work/aot" }
    $aot = Read-Results (Invoke-Checked 'AOT run' { & "$work/aot/$exe" })

    $failures = @()
    foreach ($name in $expected.Keys) {
        $j = $jit[$name]; $a = $aot[$name]
        if ($null -eq $j -or $null -eq $a) { $failures += "$name missing from output"; continue }
        $actual =
            if ($j -eq $a) { 'same' }
            elseif ($j.StartsWith('OK ') -and $a.StartsWith('EXN ')) { 'throws' }
            else { 'differs' }
        Write-Output ("{0,-34} {1,-8} JIT: {2}" -f $name, $actual, $j)
        if ($actual -ne 'same') { Write-Output ("{0,-34} {1,-8} AOT: {2}" -f '', '', $a) }
        $wantVerdict, $wantNative = $expected[$name]
        if ($actual -ne $wantVerdict) { $failures += "$name expected $wantVerdict, got $actual" }
        elseif ($actual -eq 'throws' -and $a -ne $wantNative) { $failures += "$name expected native '$wantNative', got '$a'" }
        elseif ($actual -eq 'differs' -and -not $a.StartsWith($wantNative)) { $failures += "$name expected native text starting with '$wantNative', got '$a'" }
    }

    if ($jit['json-reflection'] -notmatch '^EXN InvalidOperationException') {
        $failures += 'json-reflection must throw under JIT because PublishAot disables reflection serialization'
    }
    if ($jit['json-context-plain-option'] -notmatch '"proxy":\{"value":"p"\}') {
        $failures += 'json-context-plain-option must show the {"value":...} option shape'
    }
    if ($aot['json-context-fsharp-converters'] -notmatch 'roundtrip=True$') {
        $failures += 'json-context-fsharp-converters must round-trip in the native exe'
    }
    foreach ($code in 'IL2026', 'IL3050') {
        if (-not ($publish | Select-String -Pattern "Program\.fs.*warning ${code}:.*JsonSerializer\.Serialize")) {
            $failures += "expected $code at the json-reflection call site in Program.fs"
        }
    }

    Invoke-Checked 'WarnGate clean publish' {
        dotnet publish WarnGate -c Release -r $rid -o "$work/gate-clean"
    } | Out-Null
    $gate = dotnet publish WarnGate -c Release -r $rid -o "$work/gate-user" `
        -p:DefineConstants=USER_WARNING "-p:IntermediateOutputPath=$work/gate-user-obj/" 2>&1
    if ($LASTEXITCODE -eq 0 -or -not ($gate -match 'error IL2026')) {
        $failures += 'WarnGate must fail with error IL2026 when user code calls reflection JSON'
    }
    $detailed = dotnet publish WarnGate -c Release -r $rid -o "$work/gate-detailed" `
        -p:TrimmerSingleWarn=false "-p:IntermediateOutputPath=$work/gate-detailed-obj/" 2>&1
    if ($LASTEXITCODE -eq 0) {
        $failures += 'WarnGate with TrimmerSingleWarn=false must fail on detailed FSharp.Core warnings'
    }
    Write-Output 'WarnGate: summary codes suppressed, user-code IL2026 fails, TrimmerSingleWarn=false fails.'

    if ($failures) { throw "Proof failed:`n$($failures -join "`n")" }
    Write-Output "All $($expected.Count) probes and all three WarnGate checks match the expected results."
} finally {
    Pop-Location
    Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
    Remove-Item -Recurse -Force (Join-Path ([IO.Path]::GetTempPath()) 'fsharp-native-aot-proof') -ErrorAction SilentlyContinue
}
