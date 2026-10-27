param(
    [Parameter(Mandatory)][string]$Baseline,
    [Parameter(Mandatory)][string]$Candidate,
    [string]$Output = 'artifacts/performance/comparison',
    [ValidateSet('pgo-on', 'pgo-off', 'tiered-off')][string]$Config = 'pgo-on',
    [string]$Cases = 'all',
    [ValidateRange(1, 2147483647)][int]$Rounds = 5,
    [ValidateRange(0, 2147483647)][int]$WarmupMs = 500,
    [ValidateRange(1, 2147483647)][int]$SampleMs = 80,
    [switch]$Bytes,
    [switch]$Compile
)
$ErrorActionPreference = 'Stop'
$Baseline = (Resolve-Path -LiteralPath $Baseline).Path
$Candidate = (Resolve-Path -LiteralPath $Candidate).Path
$null = New-Item -ItemType Directory -Force -Path $Output
$oldPgo = $env:DOTNET_TieredPGO
$oldTiered = $env:DOTNET_TieredCompilation
function Median($values) {
    $sorted = @($values | Sort-Object)
    $middle = [int][Math]::Floor($sorted.Count / 2)
    if ($sorted.Count % 2 -eq 0) { return ($sorted[$middle - 1] + $sorted[$middle]) / 2 }
    return $sorted[$middle]
}
$results = @{ baseline = @{}; candidate = @{} }
try {
    $env:DOTNET_TieredPGO = if ($Config -eq 'pgo-on') { '1' } else { '0' }
    $env:DOTNET_TieredCompilation = if ($Config -eq 'tiered-off') { '0' } else { '1' }
    for ($round = 0; $round -lt $Rounds; $round++) {
        $order = if ($round % 2 -eq 0) { @('baseline', 'candidate') } else { @('candidate', 'baseline') }
        foreach ($side in $order) {
            $dll = if ($side -eq 'baseline') { $Baseline } else { $Candidate }
            $probeArgs = @($dll, '--case', $Cases, '--warmup-ms', $WarmupMs, '--sample-ms', $SampleMs)
            if ($Bytes) { $probeArgs += '--bytes' }
            if ($Compile) { $probeArgs += '--compile' }
            $lines = @(& dotnet @probeArgs)
            if ($LASTEXITCODE -ne 0) { throw "Probe failed: $side, round $round" }
            $header = $lines[0] | ConvertFrom-Json
            $expectedMode = if ($Compile) { 'compilation' } else { 'execution' }
            if ($header.mode -ne $expectedMode) { throw "Probe mode mismatch: $side; rebuild both probes from the same source" }
            $expectedStringMode = if ($Bytes) { 'bytes' } else { 'utf16' }
            if ($header.stringMode -ne $expectedStringMode) { throw "Probe string mode mismatch: $side" }
            $lines | Set-Content -LiteralPath (Join-Path $Output "$Config-$round-$side.jsonl")
            foreach ($line in $lines) {
                $row = $line | ConvertFrom-Json
                if (!$row.name) { continue }
                if (!$results[$side].ContainsKey($row.name)) { $results[$side][$row.name] = @() }
                $results[$side][$row.name] += $row
            }
        }
        Write-Host "Completed $Config round $($round + 1)/$Rounds"
    }
    if ($results.baseline.Count -eq 0) { throw 'No workload results were recorded' }
    if (Compare-Object @($results.baseline.Keys) @($results.candidate.Keys) -CaseSensitive) {
        throw 'Baseline and candidate reported different workload sets'
    }
    $summary = foreach ($name in $results.baseline.Keys | Sort-Object) {
        $base = $results.baseline[$name]
        $candidateRows = $results.candidate[$name]
        if ($base.Count -ne $Rounds -or $candidateRows.Count -ne $Rounds) {
            throw "Missing or duplicate process results: $name"
        }
        foreach ($row in @($base) + @($candidateRows)) {
            if ($row.bytecodeHash -ne $base[0].bytecodeHash -or $row.checksum -ne $base[0].checksum) {
                throw "Bytecode/result mismatch: $name"
            }
        }
        $baseNs = Median $base.ns
        $candidateNs = Median $candidateRows.ns
        [pscustomobject]@{
            Case = $name
            BaselineNs = [Math]::Round($baseNs, 1)
            CandidateNs = [Math]::Round($candidateNs, 1)
            ChangePercent = [Math]::Round(100 * ($candidateNs / $baseNs - 1), 2)
            BaselineBytes = [Math]::Round((Median $base.bytes), 1)
            CandidateBytes = [Math]::Round((Median $candidateRows.bytes), 1)
        }
    }
    $summary | Export-Csv -LiteralPath (Join-Path $Output "$Config-summary.csv") -NoTypeInformation
    $summary | Format-Table -AutoSize
} finally {
    $env:DOTNET_TieredPGO = $oldPgo
    $env:DOTNET_TieredCompilation = $oldTiered
}
