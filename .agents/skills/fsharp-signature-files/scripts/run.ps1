#Requires -Version 7.4
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false

$failures = @()
$artifacts = Join-Path ([IO.Path]::GetTempPath()) "fsharp-signature-files-proof-$PID"
$work = Join-Path ([IO.Path]::GetTempPath()) "fsharp-signature-files-work-$PID"
New-Item -ItemType Directory -Force -Path (Join-Path $work 'breaks') | Out-Null
$env:FSHARP_SIGNATURE_PROOF_ROOT = $artifacts

function Invoke-Build([string]$ProjectDir) {
    Push-Location $ProjectDir
    try {
        $output = dotnet build -c Release 2>&1 | ForEach-Object { "$_" }
        [pscustomobject]@{ ExitCode = $LASTEXITCODE; Text = ($output -join "`n") }
    } finally { Pop-Location }
}

function Assert-Line([string]$Name, [string[]]$Lines, [string]$Line, [bool]$Present) {
    $found = $Lines -ccontains $Line
    Write-Output ("surface {0,-48} {1}" -f $Name, $(if ($found -eq $Present) { 'ok' } else { 'MISMATCH' }))
    if ($found -ne $Present) { $script:failures += "Surface '$Name': expected '$Line' to be $(if ($Present) { 'present' } else { 'absent' })." }
}

try {
    $sample = Invoke-Build (Join-Path $PSScriptRoot 'Sample')
    if ($sample.ExitCode -ne 0) {
        $failures += "Sample build failed:`n$($sample.Text)"
    } else {
        $dll = Join-Path $artifacts 'Sample/bin/Release/net10.0/Sample.dll'
        $surface = @(& (Join-Path $PSScriptRoot 'public-surface.ps1') -Assembly $dll)
        if ($LASTEXITCODE -ne 0) { $failures += "public-surface.ps1 exited with $LASTEXITCODE`n$($surface -join "`n")" }
        $again = @(& (Join-Path $PSScriptRoot 'public-surface.ps1') -Assembly $dll)
        if (($surface -join "`n") -cne ($again -join "`n")) { $failures += 'public-surface.ps1 output is not stable between two runs.' }

        Assert-Line 'abstract type is public' $surface 'type Sample.Email' $true
        Assert-Line 'constructor function is public' $surface 'Sample.EmailModule | static create(input: System.String) : Microsoft.FSharp.Core.FSharpResult<Sample.Email, System.String>' $true
        Assert-Line 'accessor is public' $surface 'Sample.EmailModule | static value(email: Sample.Email) : System.String' $true
        Assert-Line 'helper left out of the .fsi is hidden' $surface 'Sample.EmailModule | static normalize(input: System.String) : System.String' $false
        Assert-Line 'internal helper is hidden' $surface 'Sample.EmailModule | static isValid(input: System.String) : System.Boolean' $false
        Assert-Line 'record field of abstract type is hidden' $surface 'Sample.Email | Value : System.String with get' $false
        Assert-Line 'record constructor of abstract type is hidden' $surface 'Sample.Email | new(value: System.String)' $false
        Assert-Line 'listed interface is public' $surface 'Sample.Ledger | implements System.IDisposable' $true
        Assert-Line 'public operator is listed' $surface 'Sample.Ledger | static op_Addition(ledger: Sample.Ledger, _entries: System.Int32) : Sample.Ledger' $true
        Assert-Line 'union with qualified access is public' $surface 'type Sample.Status' $true

        [xml]$doc = Get-Content -Raw (Join-Path $artifacts 'Sample/bin/Release/net10.0/Sample.xml')
        $summaries = @{}
        foreach ($m in $doc.doc.members.member) { $summaries[$m.name] = "$($m.summary)".Trim() }
        $createDoc = $summaries['M:Sample.EmailModule.create(System.String)']
        $valueDoc = $summaries['M:Sample.EmailModule.value(Sample.Email)']
        Write-Output ("xmldoc  {0,-48} {1}" -f 'create', $createDoc)
        Write-Output ("xmldoc  {0,-48} {1}" -f 'value', $valueDoc)
        if ($createDoc -cne 'Parses an email address. Leading and trailing spaces are removed.') { $failures += "The XML doc of create is '$createDoc'. Expected the .fsi text." }
        if ($null -ne $valueDoc) { $failures += "The XML doc of value is '$valueDoc'. Expected no entry, because its doc comment is only in the .fs file." }

        $tests = Invoke-Build (Join-Path $PSScriptRoot 'Sample.Tests')
        if ($tests.ExitCode -ne 0) { $failures += "Sample.Tests build failed:`n$($tests.Text)" }
        else {
            $out = @(dotnet (Join-Path $artifacts 'Sample.Tests/bin/Release/net10.0/Sample.Tests.dll') 2>&1 | ForEach-Object { "$_".TrimEnd() })
            Write-Output ("ivt     {0,-48} {1}" -f 'Sample.Tests output', ($out -join ' / '))
            if (($out -join "`n") -cne "isValid a@b | true`nisValid ab | false") { $failures += "Sample.Tests printed:`n$($out -join "`n")" }
        }
    }

    $gen = Join-Path $work 'Generate'
    Copy-Item (Join-Path $PSScriptRoot 'Generate') $gen -Recurse
    foreach ($name in 'global.json', 'Directory.Build.props', 'Directory.Build.targets', 'Directory.Packages.props') { Copy-Item (Join-Path $PSScriptRoot $name) $work }
    Push-Location $gen
    try { $genOut = dotnet build -c Release '-p:OtherFlags=--allsigs' 2>&1 | ForEach-Object { "$_" }; $genExit = $LASTEXITCODE } finally { Pop-Location }
    $orderFsi = Join-Path $gen 'Orders.fsi'
    if ($genExit -ne 0 -or -not (Test-Path $orderFsi)) { $failures += "--allsigs did not write Orders.fsi next to Orders.fs:`n$($genOut -join "`n")" }
    else {
        $generated = Get-Content -Raw $orderFsi
        foreach ($want in 'val roundPrice:', 'val private taxRate:', 'val total:', '/// The order total') {
            $has = $generated.Contains($want)
            Write-Output ("allsigs {0,-48} {1}" -f $want, $(if ($has) { 'present' } else { 'missing' }))
            if (-not $has) { $failures += "--allsigs output does not contain '$want':`n$generated" }
        }
        $objFsi = @(Get-ChildItem (Join-Path $artifacts 'Generate/obj') -Recurse -Filter *.fsi -ErrorAction SilentlyContinue)
        Write-Output ("allsigs {0,-48} {1}" -f '.fsi files under obj/', $objFsi.Count)
        if ($objFsi.Count -eq 0) { $failures += '--allsigs wrote no .fsi files for the generated files under obj/.' }
    }

    foreach ($case in Get-ChildItem (Join-Path $PSScriptRoot 'breaks') -Directory) {
        $dir = Join-Path $work "breaks/$($case.Name)"
        Copy-Item $case.FullName $dir -Recurse
        foreach ($name in 'global.json', 'Directory.Build.props', 'Directory.Build.targets', 'Directory.Packages.props') { Copy-Item (Join-Path $PSScriptRoot $name) $dir }
        $source = (Get-ChildItem $dir -File -Include *.fs, *.fsi -Recurse | ForEach-Object { Get-Content -Raw $_.FullName }) -join "`n"
        $codes = @([regex]::Matches($source, '//\s*expect:\s*(FS\d+)') | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique)
        $texts = @([regex]::Matches($source, '//\s*expect-text:\s*(.+?)\s*$', 'Multiline') | ForEach-Object { $_.Groups[1].Value })
        $expectSuccess = $source -match '//\s*expect-exit:\s*0'
        $r = Invoke-Build $dir
        $reported = @([regex]::Matches($r.Text, '(?:error|warning) (FS\d+)') | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique)
        Write-Output ("break   {0,-48} exit={1} reported={2} expected={3}" -f $case.Name, $r.ExitCode, ($reported -join ','), ($codes -join ','))
        if ($codes.Count -eq 0) { $failures += "$($case.Name) has no // expect: line."; continue }
        if ($expectSuccess -and $r.ExitCode -ne 0) { $failures += "$($case.Name) failed. Expected a build with warnings only.`n$($r.Text)" }
        if (-not $expectSuccess -and $r.ExitCode -eq 0) { $failures += "$($case.Name) compiled. Expected $($codes -join ', ')." }
        if (($codes -join ',') -cne ($reported -join ',')) { $failures += "$($case.Name) reported $($reported -join ', '). Expected $($codes -join ', ').`n$($r.Text)" }
        $missing = @($texts | Where-Object { -not $r.Text.Contains($_) })
        if ($missing) { $failures += "$($case.Name) did not report: $($missing -join ' | ')" }
    }
} finally {
    Remove-Item Env:FSHARP_SIGNATURE_PROOF_ROOT -ErrorAction SilentlyContinue
    foreach ($path in $work, $artifacts) {
        if (Test-Path $path) { Remove-Item -Recurse -Force -LiteralPath $path -ErrorAction SilentlyContinue }
    }
}

if ($failures) {
    $failures | ForEach-Object { Write-Error $_ -ErrorAction Continue }
    exit 1
}
Write-Output 'All signature file proofs passed.'
