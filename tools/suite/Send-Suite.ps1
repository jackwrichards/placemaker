<#
.SYNOPSIS
  Sends scenarios (or a grow command) to the game and waits until they are done: through the machine-wide game queue
  by default (it runs in the queue's game with your current build, after the requests ahead of it), or with -Direct /
  -RunId to a session you started yourself (Run-Suite.ps1 -Direct -Queue).

.EXAMPLE
  powershell -File tools/suite/Send-Suite.ps1 -File suite/scenarios/examples/a4-edit-rebuild.json
  powershell -File tools/suite/Send-Suite.ps1 -Json '{"id":"zoom","on":"parking-entrance","steps":[{"do":"camera","target":"entrance","view":"closeup","shot":"entrance"}]}'
  powershell -File tools/suite/Send-Suite.ps1 -Json '{"command":"grow","seconds":40}'
  powershell -File tools/suite/Send-Suite.ps1 -Quit

.DESCRIPTION
  Checks the file against suite/schema.json (unknown keys, steps, targets, views, enum values are errors; keys starting
  with '_' are comments), drops it into <run>/inbox under a time-stamped name, waits for <name>.result.json and prints
  per scenario what happened (status, steps the runner could not do), its pictures and where the logs are. While waiting
  it watches the game: a stale heartbeat or a dead game ends the wait at once.
  Exit code: 0 done, 1 the runner reported an error or timed out, 2 usage/schema error, 4 the game went away.
  -RunId defaults to the newest run whose heartbeat says it is in the queue.
#>
param(
    [string]$File = '',
    [string]$Json = '',
    [switch]$Quit,
    [string]$RunId = '',
    [int]$TimeoutSec = 480,
    [switch]$NoWait,
    [string]$ResultsRoot = 'C:\Users\jack\cs2-area-tool\.forensics\suite\runs',
    # The game queue (default without -RunId): the file waits its turn in the machine-wide line and runs in the queue's
    # game with your current build (bin\Release as it is; -Build rebuilds it first). -Direct (or -RunId) feeds a session
    # you started yourself with Run-Suite -Direct -Queue.
    [switch]$Direct,
    [switch]$Build,
    [string]$Who = '',
    # -Fresh: a game of its own, launched for this file (never one that has run other requests): e.g. to load a save
    # in a game that has never seen the objects the save names (suite/README.md, "Save and reload").
    [switch]$Fresh
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$queued = -not $Direct -and -not $RunId
function Beat($dir) { $hb = Join-Path $dir 'heartbeat.json'; if (-not (Test-Path $hb)) { return $null }; try { return Get-Content $hb -Raw | ConvertFrom-Json } catch { return $null } }
if ($queued -and ($Quit -or ($Json -match '"command"\s*:\s*"quit"'))) { Write-Host 'the game queue opens and closes its game itself: nothing to quit (Send-Suite -Direct -Quit closes a session you started yourself)'; exit 0 }
if (-not $queued -and -not $RunId) {
    $live = Get-ChildItem $ResultsRoot -Directory | Sort-Object LastWriteTime -Descending | Where-Object {
        $b = Beat $_.FullName
        if (-not $b) { return $false }
        return ($b.phase -like 'queue*' -or $b.phase -eq 'grow') -and (((Get-Date).ToUniversalTime() - [datetime]::Parse($b.utc).ToUniversalTime()).TotalSeconds -lt 30)
    } | Select-Object -First 1
    if (-not $live) { Write-Host 'no suite session is waiting in the queue (start one with Run-Suite.ps1 -Direct -Queue)'; exit 2 }
    $RunId = $live.Name
}
if (-not $queued) {
    $runDir = Join-Path $ResultsRoot $RunId
    $inbox = Join-Path $runDir 'inbox'
    if (-not (Test-Path $inbox)) { Write-Host "run $RunId has no inbox"; exit 2 }
}
if ($Quit) { $text = '{"command":"quit"}'; $base = 'quit' }
elseif ($File) { $text = Get-Content -LiteralPath $File -Raw -Encoding UTF8; $base = [IO.Path]::GetFileNameWithoutExtension($File) }
elseif ($Json) { $text = $Json; $base = 'inline' }
else { Write-Host 'give -File, -Json or -Quit'; exit 2 }
$doc = $text | ConvertFrom-Json   # fail here on bad JSON, not in the game

# ───────────── schema check (suite/schema.json) ─────────────
$schema = Get-Content (Join-Path $repo 'suite\schema.json') -Raw | ConvertFrom-Json
$errors = New-Object System.Collections.ArrayList
function Names($o) { if ($o) { return @($o.PSObject.Properties | ForEach-Object { $_.Name }) } else { return @() } }
function CheckEnums($enums, $obj, $where) {
    foreach ($e in (Names $enums)) {
        $v = $obj.$e
        if ($v -is [string] -and -not (@($enums.$e) | Where-Object { $_ -ieq $v })) { [void]$errors.Add("${where}: '$e' must be one of $(@($enums.$e) -join ', '), not '$v'") }
    }
}
function CheckCamera($cam, $where) {
    foreach ($k in (Names $cam)) { if (-not $k.StartsWith('_') -and -not (@($schema.camera.keys) -contains $k)) { [void]$errors.Add("${where} camera: unknown key '$k'") } }
    CheckEnums $schema.camera.enums $cam "$where camera"
}
function CheckScenario($sc) {
    $id = $sc.id
    foreach ($k in (Names $sc)) { if (-not $k.StartsWith('_') -and -not (@($schema.scenarioKeys) -contains $k)) { [void]$errors.Add("scenario ${id}: unknown key '$k'") } }
    CheckEnums $schema.scenarioEnums $sc "scenario $id"
    $i = 0
    foreach ($st in @($sc.steps)) {
        if (-not $st) { continue }
        $d = $st.do
        $spec = $schema.steps.$d
        if (-not $spec) { [void]$errors.Add("scenario $id step ${i}: unknown step '$d' (steps: $((Names $schema.steps) -join ', '))") }
        else {
            $allowed = @($spec.keys) + @($schema.commonStepKeys)
            foreach ($k in (Names $st)) { if (-not $k.StartsWith('_') -and -not ($allowed -contains $k)) { [void]$errors.Add("scenario $id step $i ($d): unknown key '$k' (keys: $(($allowed | Sort-Object) -join ', '))") } }
            CheckEnums $spec.enums $st "scenario $id step $i ($d)"
            CheckEnums $schema.commonEnums $st "scenario $id step $i ($d)"
            if ($d -eq 'camera') { CheckEnums $schema.camera.enums $st "scenario $id step $i" }
            if ($st.camera) { CheckCamera $st.camera "scenario $id step $i" }
        }
        $i++
    }
}
if (-not $doc.command) {
    if ($doc.scenarios) { foreach ($sc in @($doc.scenarios)) { CheckScenario $sc } } else { CheckScenario $doc }
    if ($errors.Count -gt 0) { Write-Host 'SCHEMA ERRORS (nothing sent):'; $errors | ForEach-Object { Write-Host "  $_" }; exit 2 }
}

if ($queued) {
    # the machine-wide game queue: waits its turn, prints its place and an ETA, then the same lines as below
    . (Join-Path $PSScriptRoot 'GameQueue.ps1')
    $q = Invoke-QueuedSend $repo $text $base @{ Build = [bool]$Build; Fresh = [bool]$Fresh; Who = $Who; ResultsRoot = $ResultsRoot; TimeoutSec = $(if ($PSBoundParameters.ContainsKey('TimeoutSec')) { $TimeoutSec } else { 0 }) }
    if (-not $q.result) { exit $q.code }
    $runDir = $q.resultsDir
    $result = $q.result
} else {
$name = (Get-Date -Format 'yyyyMMdd-HHmmss-fff') + '-' + $base
$tmp = Join-Path $inbox "$name.tmp"
[IO.File]::WriteAllText($tmp, $text, (New-Object Text.UTF8Encoding $false))
Move-Item $tmp (Join-Path $inbox "$name.json")
Write-Host "sent $name to run $RunId"
if ($NoWait -or $Quit) { exit 0 }
$result = Join-Path $inbox "$name.result.json"
$t0 = Get-Date
$gamePid = 0
$hostJson = Join-Path ([IO.Path]::GetDirectoryName($ResultsRoot)) 'active.json'
if (Test-Path $hostJson) { try { $a = Get-Content $hostJson -Raw | ConvertFrom-Json; if ($a.runId -eq $RunId) { $gamePid = $a.pid } } catch { } }
while (-not (Test-Path $result)) {
    if (((Get-Date) - $t0).TotalSeconds -gt $TimeoutSec) { Write-Host "no result after $TimeoutSec s"; exit 1 }
    Start-Sleep -Milliseconds 500
    $b = Beat $runDir
    if ($b) {
        $age = ((Get-Date).ToUniversalTime() - [datetime]::Parse($b.utc).ToUniversalTime()).TotalSeconds
        if ($age -gt 30) { Write-Host "the game stopped answering: heartbeat $([math]::Round($age)) s old (phase $($b.phase), scenario $($b.scenario))"; exit 4 }
        if ($b.phase -in @('quit', 'aborted', 'done')) { Write-Host "the session ended (phase $($b.phase)) before the result"; exit 4 }
    }
    if ($gamePid -and -not (Get-Process -Id $gamePid -ErrorAction SilentlyContinue)) { Write-Host "the game (PID $gamePid) is gone (last phase $($b.phase), scenario $($b.scenario))"; exit 4 }
}
}
Start-Sleep -Milliseconds 200
$r = Get-Content $result -Raw | ConvertFrom-Json
$code = 0
if ($r.error) { Write-Host "ERROR: $($r.error)"; $code = 1 }
foreach ($s in @($r.scenarios)) {
    if (-not $s) { continue }
    $why = if ($s.refused) { ": $($s.refused)" } elseif ($s.failure) { ": $($s.failure)" } else { '' }
    Write-Host ("{0}: {1}{2} on {3} {4} in {5} s" -f $s.id, $s.status, $why, $s.block, $s.kind, $s.sec)
    foreach ($e in @($s.stepErrors)) { if ($e) { Write-Host "  runner could not: $e" } }
    if ($s.shots) { foreach ($p in $s.shots.PSObject.Properties) { Write-Host ("  shot {0}: {1}" -f $p.Name, (Join-Path $runDir $p.Value)) } }
    Write-Host "  steps: $($s.resultFile)"
}
if ($r.grow) { Write-Host ("grow: " + ($r.grow | ConvertTo-Json -Compress)) }
if ($queued) { Write-Host "logs: $(Join-Path $runDir 'logs\Placemaker.log') and $(Join-Path $runDir 'logs\Player.log') (your request's part of the queue game's logs), runner: $(Join-Path $runDir 'suite.log')" }
else { Write-Host "logs (the game writes them as it goes): $(Join-Path $env:USERPROFILE 'AppData\LocalLow\Colossal Order\Cities Skylines II\Logs\Placemaker.log'), runner: $(Join-Path $runDir 'suite.log')" }
Write-Host "pictures in order: $(Join-Path $runDir 'contact.html')"
exit $code
