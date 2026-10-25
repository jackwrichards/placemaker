<#
  The Outgrow score, offline (no game): builds OutgrowBench from this checkout, replays the boot camp town's recorded passes
  (.forensics/outgrow-bench/bootcamp, recorded in the game 24 Oct) with this checkout's planner, prints its score and
  breakdown, then the black-box towns' scores alone (.forensics/blackbox: random towns, not to be opened).
    powershell -File tools/suite/Outgrow-Score.ps1                       # the planner as it is
    powershell -File tools/suite/Outgrow-Score.ps1 -Knob emergent=0      # with knobs (several: -Knob a=1,b=2)
    powershell -File tools/suite/Outgrow-Score.ps1 -Out <dir>            # also keep the drawings' data (node tools/suite/GardenMap.js <dir> offline)
  Score = coverage % + plain % (gardens with 6 edges or fewer) - 5 x (spikes under 45 + notches over 315 degrees, per 100
  gardens) - 2 x (fence gaps + crossings, per 100 gardens). Higher is better.
#>
param(
    [string[]]$Knob = @(),
    [string]$Out = '',
    [switch]$NoBuild
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path (Split-Path $PSScriptRoot)
# (the main checkout's .forensics, from any worktree)
$main = Split-Path (Resolve-Path (& git -C $repo rev-parse --git-common-dir | ForEach-Object { if ([IO.Path]::IsPathRooted($_)) { $_ } else { Join-Path $repo $_ } })).Path
$bench = Join-Path $main '.forensics\outgrow-bench\bootcamp'
$box = Join-Path $main '.forensics\blackbox'
$proj = Join-Path $repo 'Tests\Placemaker.OutgrowBench'
if (-not $NoBuild) {
    $b = & dotnet build $proj -c Release 2>&1
    if ($LASTEXITCODE -ne 0) { $b | Select-String ' error ' | Select-Object -First 10 | ForEach-Object { $_.Line }; exit 1 }
}
$exe = Join-Path $proj 'bin\Release\net9.0\Placemaker.OutgrowBench.exe'
$knobs = @()
foreach ($k in ($Knob -join ',').Split(',', [StringSplitOptions]::RemoveEmptyEntries)) { $knobs += '--knob'; $knobs += $k.Trim() }
if (-not $Out) { $Out = Join-Path $env:TEMP ('outgrow-score-' + [guid]::NewGuid().ToString('N').Substring(0, 8)) }
& $exe --bootcamp (Join-Path $bench 'passes') --rounds 2 --scenario (Join-Path $repo 'suite\scenarios\outgrow-bootcamp.json') --roads (Join-Path $bench 'roads.json') --out $Out @knobs |
    Where-Object { $_ -notmatch '^\s+round \d' -and $_ -notmatch 'bootcamp-offline\.txt$' }
'--- thinned town (a random town with houses taken out and a few put in: gaps to fill; 25 Oct) ---'
$thin = Join-Path $main '.forensics\outgrow-bench\thinned'
& $exe --bootcamp (Join-Path $thin 'passes') --rounds 2 --places (Join-Path $thin 'places.json') --roads (Join-Path $thin 'roads.json') --out (Join-Path $Out 'thinned') @knobs |
    Where-Object { $_ -notmatch '^\s+round \d' -and $_ -notmatch 'bootcamp-offline\.txt$' -and $_ -notmatch 'replayed in order' }
'--- black box (random towns; the scores alone) ---'
& $exe --blackbox $box @knobs
