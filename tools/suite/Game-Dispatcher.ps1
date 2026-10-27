<#
.SYNOPSIS
  The game queue's dispatcher: one per machine, started on demand by a client (Run-Suite, Show, Send-Suite, Play),
  gone again when the line has been empty for a while. Nobody runs it by hand.

.DESCRIPTION
  Works the line in %USERPROFILE%\.placemaker\game-queue (see GameQueue.ps1):
  * Single instance: dispatcher.lock, held open for its whole life (PID, start time and protocol version inside; a lock
    nobody holds is stale and taken over). A client with a newer protocol asks it to hand over when the game is free.
  * Batching: while a game is open with build K it runs the oldest request with build K next (fed into the open game's
    inbox, no relaunch), as long as the head of the line still starts within -StarveMinutes of asking; another build
    waits for the current request, then the game quits cleanly and is relaunched with it. Land made to order is laid out
    in the open game when its request arrives (SuiteRunner.Land.cs). A request that does not fit the open game's test
    grid, or got 'no-block' there, gets a fresh launch.
  * Mods\Placemaker (ModsInstall.ps1): the game host installs a request's frozen build after it holds the suite lock and
    has checked that no other game runs, and puts the user's build back the moment its game has exited. Between queue
    games the user's build is installed. Nothing is written while any Cities2.exe runs: a restore that finds a game open
    is finished here as soon as no game runs, and the dispatcher does not exit before.
  * The user's playground (Play.ps1) goes next after the current request; the queue pauses until the user closes it.
    Its build stays installed afterwards (it is what the user played).
  * Never starts a game while a Cities2.exe it did not start is running (the user playing); never kills one.
  * Crash or hang: Run-Suite collects the logs; the request is marked failed (a crash during boot is retried once, a
    game window closed by hand is retried once after a pause).
  * Results: each request's own folder (summary.json, scenarios, shots, its own part of the logs, contact.html) in the
    requester's ResultsRoot.
  * After -IdleMinutes with an empty line the game quits; once the user's build is back the lock is released and it exits.
  * The user first: when a game it did not start closes (the user's, a playground), or its own game is stopped from
    outside (Steam's Stop, Task Manager, the window closed), it waits -UserGraceSec before launching a game, so the user
    can start theirs; when the user starts a game (or the Paradox launcher) while its game is open, the request in the
    game finishes and its game quits.
  * It runs from a copy of its scripts in the queue folder (GameQueue.ps1, Q-StartDispatcher); dispatcher\good.json
    names the last copy that ran well.
#>
param(
    [double]$IdleMinutes = 10,
    [double]$StarveMinutes = 10,
    [int]$ExitGraceSec = 20,
    [int]$UserGraceSec = 120,
    # started by a client: start the real dispatcher (hidden) and exit, so it is nobody's child
    [switch]$Detach
)
$ErrorActionPreference = 'Stop'
if ($Detach) {
    $pass = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSCommandPath`"", '-IdleMinutes', $IdleMinutes, '-StarveMinutes', $StarveMinutes, '-ExitGraceSec', $ExitGraceSec, '-UserGraceSec', $UserGraceSec)
    if ($env:PLACEMAKER_GAME_QUEUE -and $env:PLACEMAKER_GAME_QUEUE_IDLE) { $pass[6] = $env:PLACEMAKER_GAME_QUEUE_IDLE }   # tests
    if ($env:PLACEMAKER_GAME_QUEUE -and $env:PLACEMAKER_GAME_QUEUE_GRACE) { $pass[12] = $env:PLACEMAKER_GAME_QUEUE_GRACE }   # tests
    Start-Process -FilePath 'powershell.exe' -ArgumentList ($pass -join ' ') -WindowStyle Hidden -WorkingDirectory $env:USERPROFILE | Out-Null
    exit 0
}
. (Join-Path $PSScriptRoot 'GameQueue.ps1')
Q-Init
$script:forensics = Split-Path $script:QMainRuns
$script:suiteLock = Join-Path $script:forensics 'lock'
$script:settingsPending = Join-Path $script:forensics 'user-settings\pending.txt'
if ($env:PLACEMAKER_GAME_QUEUE -and $env:PLACEMAKER_GAME_QUEUE_USERDATA) { $script:QUserData = $env:PLACEMAKER_GAME_QUEUE_USERDATA }   # tests

function D-Log([string]$m) {
    $line = '{0:yyyy-MM-dd HH:mm:ss} [{1}] {2}' -f (Get-Date), $PID, $m
    for ($i = 0; $i -lt 5; $i++) { try { [IO.File]::AppendAllText($script:Q.Log, $line + "`r`n", (New-Object Text.UTF8Encoding $false)); break } catch { Start-Sleep -Milliseconds 40 } }
}
function D-Inner($e) { while ($e.InnerException) { $e = $e.InnerException }; return $e }

# ───────────── single instance: dispatcher.lock held open for the dispatcher's life ─────────────
# Nobody else can open it for writing while this process lives; when it dies (killed or not) the handle goes with it,
# and the PID inside is only information (a lock file whose holder is gone is stale and taken over). It is never deleted.
try { $script:lockStream = [IO.File]::Open($script:Q.Lock, 'OpenOrCreate', 'ReadWrite', 'Read') }
catch {
    $e = D-Inner $_.Exception
    if (($e.HResult -band 0xFFFF) -in @(32, 33)) { exit 0 }   # another dispatcher holds it: it serves the line
    D-Log "could not open $($script:Q.Lock): $($e.GetType().Name): $($e.Message)"
    exit 1
}
$oldText = ''
try { $buf = New-Object byte[] ([int]$script:lockStream.Length); [void]$script:lockStream.Read($buf, 0, $buf.Length); $oldText = [Text.Encoding]::UTF8.GetString($buf) } catch { }
$old = $null; try { if ($oldText.Trim()) { $old = $oldText | ConvertFrom-Json } } catch { }
if ($old -and $old.pid -and $old.pid -ne $PID -and -not $old.down) { D-Log "took over the lock of dispatcher PID $($old.pid) (from $($old.at)): it is gone without shutting down (killed or crashed)" }
$lockBytes = [Text.Encoding]::UTF8.GetBytes((([ordered]@{ pid = $PID; start = (Q-ProcStart $PID); at = (Get-Date).ToString('o'); v = $script:QVersion; script = $PSCommandPath }) | ConvertTo-Json -Compress))
$script:lockStream.SetLength(0); $script:lockStream.Write($lockBytes, 0, $lockBytes.Length); $script:lockStream.Flush()
D-Log "dispatcher up (PID $PID, v$($script:QVersion), $PSCommandPath)$(if ($script:QFake) { ", FAKE host $script:QFake" })"
# a stand-in queue folder (tests) never runs real game hosts, and never with a second install state over the real Mods
if ($env:PLACEMAKER_GAME_QUEUE -and -not ($script:QFake -and $env:PLACEMAKER_GAME_QUEUE_MODS)) {
    D-Log "PLACEMAKER_GAME_QUEUE is set without both PLACEMAKER_GAME_QUEUE_FAKE and PLACEMAKER_GAME_QUEUE_MODS: a stand-in queue never runs the real game or touches the real Mods\Placemaker; not starting"
    try { $script:lockStream.Dispose() } catch { }
    exit 1
}

# A newer client's request to hand over (retire.json): only while that client lives and a request of the newer protocol
# is in line; a request whose writer is gone, or that is not newer, is removed (it could otherwise stop every older
# dispatcher for ever).
function D-RetireWanted($queued) {
    $rt = Q-ReadJson $script:Q.Retire
    if (-not $rt) { return $null }
    $drop = ''
    if ([int]$rt.v -le $script:QVersion) { $drop = "not newer (v$($rt.v))" }
    elseif (-not (Q-Alive $rt.pid $rt.start)) { $drop = "its client (PID $($rt.pid)) is gone" }
    if ($drop) { Remove-Item -LiteralPath $script:Q.Retire -Force -ErrorAction SilentlyContinue; D-Log "ignored a request to hand over from $($rt.by): $drop"; return $null }
    if (-not @($queued | Where-Object { [int]$_.v -gt $script:QVersion }).Count) { return $null }
    return $rt
}
try { D-RetireWanted @(Q-Queued) | Out-Null } catch { D-Log "reading retire.json: $($_.Exception.Message)" }
try { Q-SeedHistory } catch { D-Log "seeding the ETA history failed: $($_.Exception.Message)" }

$script:sess = $null          # the game this dispatcher runs (persisted in session.json)
$script:active = $null        # @{ req; dir; feed; takenAt } the request in the game now
$script:launches = 0
$script:idleSince = Get-Date
$script:note = ''
$script:paused = $false
$script:bootFailures = @{}
$script:startFailures = @{}
$script:feedFailures = @{}
$script:stoppedBoots = @{}      # boots stopped from outside, per build key (3: its requests fail)
$script:orphanQuit = @{}        # runId -> @{ at; sent }
$script:userGame = ''           # the note while a game the user started (or the Paradox launcher) is open
$script:userSeen = $null        # @{ at; kind } while such a game is open (its closing starts the grace below)
$script:userGraceUntil = [datetime]::MinValue
$script:userGraceNote = ''
# the grace outlives a dispatcher that stops meanwhile (the next one waits it out too)
function D-SaveGrace { Q-WriteJson (Join-Path $script:Q.Root 'grace.json') ([ordered]@{ until = $script:userGraceUntil.ToString('o'); note = $script:userGraceNote }) }
try { $g = Q-ReadJson (Join-Path $script:Q.Root 'grace.json'); if ($g -and $g.until -and (Q-Date $g.until) -gt (Get-Date)) { $script:userGraceUntil = Q-Date $g.until; $script:userGraceNote = "$($g.note)" } } catch { }
$script:goodWritten = $false
$script:nextGc = [datetime]::MinValue   # the first clean-up runs in the loop, after the first status.json
$script:sessMaxSec = 7.5 * 3600          # a queue game takes no request that would still run then (its host stops it at 8 h)
$script:hostBackoff = [datetime]::MinValue
$script:backoffNote = ''
$script:lastReconcile = [datetime]::MinValue
$script:lastReconcileMsg = ''
$script:lastSettingsCheck = [datetime]::MinValue
$script:cmdCache = @{}
$script:sumCache = @{ path = ''; time = [datetime]::MinValue; rows = @() }

function D-SaveSession { if ($script:sess) { Q-WriteJson $script:Q.Session $script:sess } elseif (Test-Path -LiteralPath $script:Q.Session) { Remove-Item -LiteralPath $script:Q.Session -Force } }

# Folder moves: retried (antivirus, an indexer, a reader can hold a file for a moment); $false when it did not happen.
function D-MoveDir([string]$from, [string]$to) {
    for ($i = 0; $i -lt 6; $i++) {
        if (-not (Test-Path -LiteralPath $from)) { return $false }
        try { [IO.Directory]::Move($from, $to); return $true } catch { $err = $_.Exception.Message; Start-Sleep -Milliseconds 400 }
    }
    D-Log "could not move $from to ${to}: $err"
    return $false
}

# ───────────── other games on the machine ─────────────

# A process's command line, cached by PID and start time (WMI is slow).
function D-CmdLine($p) {
    $k = "$($p.Id)/$(Q-Ticks $p)"
    if ($script:cmdCache.ContainsKey($k)) { return $script:cmdCache[$k] }
    $cmd = ''
    try { $cmd = "$((Get-CimInstance Win32_Process -Filter "ProcessId=$($p.Id)").CommandLine)" } catch { }
    if ($cmd) { if ($script:cmdCache.Count -gt 200) { $script:cmdCache.Clear() }; $script:cmdCache[$k] = $cmd }
    return $cmd
}

# Cities2.exe processes this dispatcher did not start, classified, and the Paradox launcher (the user starting the game).
# Never touched (an earlier dispatcher's game excepted).
function D-Foreign {
    if ($script:QFake) {
        $r = @()
        if (Test-Path -LiteralPath (Join-Path $script:Q.Root 'fake-user-game')) { $r += [pscustomobject]@{ pid = 0; kind = 'user'; runId = '' } }
        if (Test-Path -LiteralPath (Join-Path $script:Q.Root 'fake-launcher')) { $r += [pscustomobject]@{ pid = 0; kind = 'launcher'; runId = '' } }
        return $r
    }
    $r = @()
    foreach ($p in @(Get-Process -Name 'Cities2' -ErrorAction SilentlyContinue)) {
        $cmd = D-CmdLine $p
        $tok = if ($cmd -match '-placemakerSuite=(\S+)') { $Matches[1].Trim('"') } else { '' }
        if ($script:sess -and $tok -eq $script:sess.id) { continue }
        # a command line that cannot be read (a process exiting): our own game when it is the PID in our heartbeat
        if (-not $cmd -and $script:sess) { $b = D-Beat; if ($b -and [int]$b.pid -eq $p.Id) { continue } }
        $kind = if (-not $tok) { 'user' } elseif ($tok -like 'queue-*') { 'orphan' } elseif ($tok -like 'play-*') { 'playground' } else { 'runner' }
        $r += [pscustomobject]@{ pid = $p.Id; kind = $kind; runId = $tok }
    }
    foreach ($id in @(MI-Launchers)) { $r += [pscustomobject]@{ pid = $id; kind = 'launcher'; runId = '' } }
    return $r
}

# The suite lock's holder is alive (PID and process start time; an older lock without procStart: the process must have
# started before the lock was written, else Windows reused the PID).
function D-LockAlive($l) {
    if (-not $l -or -not $l.pid) { return $false }
    if ($l.procStart) { return (Q-Alive $l.pid $l.procStart) }
    $p = Get-Process -Id ([int]$l.pid) -ErrorAction SilentlyContinue
    if (-not $p) { return $false }
    try { return ($p.StartTime -le ([datetime]$l.start).AddSeconds(5)) } catch { return $true }
}
# A Run-Suite holding the suite lock: outside the queue (a -Direct run), or with -IncludeOurs also our own host.
function D-SuiteLockHolder([switch]$IncludeOurs) {
    if ($script:QFake) { return $null }
    $l = Q-ReadJson $script:suiteLock
    if (-not $l -or -not $l.pid) { return $null }
    if (-not $IncludeOurs -and $script:sess -and $l.pid -eq $script:sess.hostPid) { return $null }
    if (D-LockAlive $l) { return $l }
    return $null
}

function D-WaitNote($foreign, $holder) {
    $f = @($foreign) | Select-Object -First 1
    if ($f) {
        switch ($f.kind) {
            'user' { return "waiting: the user's own game is open (Cities2.exe PID $($f.pid)); the queue goes on when it closes" }
            'launcher' { return "waiting: the user is starting the game (the Paradox launcher is open, PID $($f.pid)); the queue goes on when it closes" }
            'playground' { return "waiting: the user's playground is open (run $($f.runId)); the queue goes on when they close the game" }
            'orphan' { return "waiting: an earlier queue game (run $($f.runId)) is closing" }
            default { return "waiting: a game runner started outside the queue has the game (run $($f.runId), PID $($f.pid))" }
        }
    }
    if ($holder) { return "waiting: a game runner started outside the queue holds the suite lock (run $($holder.runId), PID $($holder.pid))" }
    return ''
}

# A game an earlier (dead) dispatcher left: ask it to quit through its inbox (again once its inbox appears: a game still
# booting has none yet), kill it only if it will not.
function D-HandleOrphans($foreign) {
    foreach ($f in @($foreign | Where-Object { $_.kind -eq 'orphan' })) {
        $inbox = Join-Path (Join-Path $script:QMainRuns $f.runId) 'inbox'
        if (-not $script:orphanQuit.ContainsKey($f.runId)) {
            $script:orphanQuit[$f.runId] = @{ at = (Get-Date); sent = $false }
            D-Log "an earlier queue game is still open (run $($f.runId), PID $($f.pid)): asking it to quit"
        }
        $o = $script:orphanQuit[$f.runId]
        if (-not $o.sent -and (Test-Path -LiteralPath $inbox)) {
            $o.sent = [bool](Q-WriteText (Join-Path $inbox ((Get-Date -Format 'yyyyMMdd-HHmmss-fff') + '-quit.json')) '{"command":"quit"}')
            if ($o.sent) { $o.at = Get-Date; D-Log "asked the earlier queue game $($f.runId) to quit through its inbox" }
        }
        if (((Get-Date) - $o.at).TotalSeconds -gt 240) {
            D-Log "the earlier queue game (run $($f.runId), PID $($f.pid)) did not quit in 240 s: stopping it (a queue game, not the user's)"
            Stop-Process -Id $f.pid -Force -ErrorAction SilentlyContinue
            $o.at = Get-Date
        }
    }
}

# ───────────── the user's build and settings, when nobody uses the game ─────────────

# Mods\Placemaker back to the user's build when a queue build was left there (a restore that found a game open, a host
# that was killed). Only when no game runs and no game host (ours or another) holds the suite lock.
function D-RestoreUserBuild([string]$why, [switch]$Now) {
    if (-not (MI-Pending)) { return $true }
    if ($script:sess) { return $false }   # our own game runs: its host puts the user's build back when it exits
    if (-not $Now -and ((Get-Date) - $script:lastReconcile).TotalSeconds -lt 15) { return $false }
    $script:lastReconcile = Get-Date
    $g = @(MI-Games)
    if ($g.Count -gt 0) {
        $m = "Mods\Placemaker still has the queue's build $((MI-State).label): the user's goes back once Cities2.exe (PID $($g -join ', ')) has closed (nothing is written while a game runs)"
        if ($m -ne $script:lastReconcileMsg) { D-Log $m; $script:lastReconcileMsg = $m }
        return $false
    }
    if ($script:sess -and (Q-Alive $script:sess.hostPid $script:sess.hostStart)) { return $false }
    if (D-SuiteLockHolder -IncludeOurs) { return $false }
    $r = MI-Reconcile $why   # logs what it did itself
    return [bool]$r.ok
}

# The user's game settings back when a host was cut off before its own restore (Run-Suite -RestoreOnly).
function D-RestoreSettings([string]$why, [switch]$Now) {
    if ($script:QFake -or (-not $Now -and ((Get-Date) - $script:lastSettingsCheck).TotalSeconds -lt 60)) { return }
    $script:lastSettingsCheck = Get-Date
    if (-not (Test-Path -LiteralPath $script:settingsPending)) { return }
    if (@(MI-Games).Count -gt 0 -or (D-SuiteLockHolder -IncludeOurs)) { return }
    if ($script:sess -and (Q-Alive $script:sess.hostPid $script:sess.hostStart)) { return }
    $out = & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'Run-Suite.ps1') -RestoreOnly -ResultsRoot $script:QMainRuns 2>&1 | ForEach-Object { "$_" }
    D-Log "the user's game settings were still the suite's (a game host left them pending, or was cut off): Run-Suite -RestoreOnly ($why): $(@($out) -join ' / ')"
}

# ───────────── games: launch, watch, quit ─────────────

function D-Grid($req, $queued) {
    if ($req.session.grid) { return $req.session.grid }
    $flat = 0; $zoned = 0; $slope = 0
    foreach ($r in @($queued | Where-Object { $_.key -eq $req.key })) { $flat += [int]$r.blocks.Flat; $zoned += [int]$r.blocks.Zoned; $slope += [int]$r.blocks.Slope }
    $nx = [math]::Max(16, [math]::Max([math]::Ceiling(($flat + 2) / 2), [math]::Max($zoned, [math]::Ceiling($slope / 2))))
    return "$([math]::Min(30, $nx))x7"
}

# One command-line argument for powershell.exe -File (backslashes before a quote, and at the end of a quoted argument, doubled).
function D-Quote([string]$a) {
    if ($a -notmatch '[\s;"]' -and $a -ne '') { return $a }
    return '"' + (($a -replace '(\\*)"', '$1$1\"') -replace '(\\+)$', '$1$1') + '"'
}

# The game host is this dispatcher's own checked copy of Run-Suite.ps1 (and the ModsInstall.ps1 next to it): the code
# that saves, switches and restores the user's settings, installs into Mods\Placemaker and checks for the user's game is
# never a requester's (an agent's edited or stale copy). The request's frozen build gives only the mod (-DeployFrom) and
# the game's inputs (-HostData: suite\schema.json and suite\scenarios\).
function D-StartHost($req, [string]$runId, [string[]]$extra) {
    $script = if ($script:QFake) { $script:QFake } else { Join-Path $PSScriptRoot 'Run-Suite.ps1' }
    $s = $req.session
    $a = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $script, '-Direct', '-NoBuild', '-RunId', $runId, '-Label', $req.build.git, '-DeployFrom', (Join-Path $req.build.bundle 'mod'), '-HostData', (Join-Path $req.build.bundle 'host'))
    if ($s.allMods) { $a += '-AllMods' }
    if ($s.keepGraphics) { $a += '-KeepGraphics' }
    if ($s.noTerrain) { $a += '-NoTerrain' }
    if ($s.userModSettings) { $a += '-UserModSettings' }
    if ($s.window) { $a += @('-Window', $s.window) }
    if ($s.look) { $a += @('-Look', $s.look) }
    if ($s.pace) { $a += @('-Pace', $s.pace) }
    if ($s.opt) { $a += @('-Opt', $s.opt) }
    if ($s.pregrowSeconds -gt 0) { $a += @('-PregrowSeconds', [string]$s.pregrowSeconds) }
    $a += $extra
    $argLine = ($a | ForEach-Object { D-Quote $_ }) -join ' '
    $out = Join-Path $script:Q.Logs "$runId.host.txt"
    $p = Start-Process -FilePath 'powershell.exe' -ArgumentList $argLine -NoNewWindow -PassThru -RedirectStandardOutput $out -RedirectStandardError "$out.err"
    D-Log "started the game host for run $runId (PID $($p.Id)): $argLine"
    return $p
}

# Every queued request with this frozen build leaves the line as failed (its build cannot be installed or booted).
function D-FailBuild($queued, [string]$key, [string]$bundle, [string]$message) {
    foreach ($r in @($queued | Where-Object { ($key -and $_.key -eq $key) -or ($bundle -and $_.build.bundle -eq $bundle) })) {
        $a = D-Take $r
        if (-not $a) { continue }
        $script:active = $a
        D-Finish 'failed' 1 $message $null
    }
}

function D-StartSession($req, $queued) {
    $bad = Q-BundleOk $req.build.bundle
    if ($bad) { D-Log "$($req.id) ($($req.who)): $bad"; D-FailBuild $queued '' $req.build.bundle "$bad; run the command again"; return $false }
    Q-Touch $req.build.bundle
    $id = 'queue-' + (Get-Date -Format 'MMdd-HHmmss')
    $grid = D-Grid $req $queued
    $p = D-StartHost $req $id @('-Set', 'queue', '-Queue', '-Grid', $grid, '-ResultsRoot', $script:QMainRuns)
    $script:sess = [ordered]@{
        id = $id; kind = 'queue'; key = $req.key; build = $req.build.hash; label = $req.build.label; bundle = $req.build.bundle; who = $req.who
        dir = (Join-Path $script:QMainRuns $id); hostPid = $p.Id; hostStart = (Q-Ticks $p); startedAt = (Get-Date).ToString('o'); readyAt = ''; quitAt = ''; quitWhy = ''
        served = 0; grid = $grid; capacity = $null; firstRequest = $req.id; requestId = ''
        launchLine = ('{0:yyyy-MM-dd HH:mm:ss} launch {1} build {2} ({3}) for {4} ({5}), grid {6}' -f (Get-Date), $id, $req.build.hash, $req.build.label, $req.id, $req.who, $grid)
    }
    D-SaveSession
    D-Log "launching game $id for build $($req.build.hash) ($($req.who)), grid $grid"
    return $true
}

function D-StartPlay($req) {
    $bad = Q-BundleOk $req.build.bundle
    if ($bad) { D-Log "$($req.id) ($($req.who)): $bad"; return $bad }
    Q-Touch $req.build.bundle
    $a = @('-Playground', '-ResultsRoot', $req.resultsRoot)
    if ($req.session.grid) { $a += @('-Grid', $req.session.grid) }
    $p = D-StartHost $req $req.runId $a
    $script:sess = [ordered]@{
        id = $req.runId; kind = 'play'; key = ''; build = $req.build.hash; label = $req.build.label; bundle = $req.build.bundle; who = $req.who
        dir = $req.resultsDir; hostPid = $p.Id; hostStart = (Q-Ticks $p); startedAt = (Get-Date).ToString('o'); readyAt = ''; quitAt = ''; quitWhy = ''
        served = 0; grid = $req.session.grid; capacity = $null; firstRequest = $req.id; requestId = $req.id
        launchLine = ('{0:yyyy-MM-dd HH:mm:ss} launch {1} PLAYGROUND build {2} ({3}) for {4} ({5})' -f (Get-Date), $req.runId, $req.build.hash, $req.build.label, $req.id, $req.who)
    }
    D-SaveSession
    D-Log "the user's playground $($req.runId) for $($req.who): build $($req.build.hash)"
    return ''
}

# launches.log and the launch count: only games that were really started (ready, or started but did not boot), not a
# host that stopped before starting the game (another game was open).
function D-CountLaunch([string]$how) {
    if (-not $script:sess -or $script:sess.launchCounted) { return }
    $script:sess['launchCounted'] = $true; D-SaveSession
    $script:launches++
    try { [IO.File]::AppendAllText($script:Q.Launches, "$($script:sess.launchLine): $how`r`n", (New-Object Text.UTF8Encoding $false)) } catch { }
}

function D-Beat {
    if (-not $script:sess) { return $null }
    $b = Q-ReadJson (Join-Path $script:sess.dir 'heartbeat.json')
    if ($b -and $b.runId -ne $script:sess.id) { return $null }
    return $b
}

# Stops the queue's own game the sure way: its PID when it is verified as ours; and always through its host, which knows
# the PID it started (stop-game.txt in the run folder: the host kills that PID and ends as 'killed').
function D-StopGame([string]$why) {
    $gp = D-GamePid -Loose
    if ($gp) { D-Log "${why}: stopping game $($script:sess.id) (PID $gp, started by the queue)"; Stop-Process -Id $gp -Force -ErrorAction SilentlyContinue }
    else { D-Log "${why}: asking the host of game $($script:sess.id) to stop the game it started" }
    Q-WriteText (Join-Path $script:sess.dir 'stop-game.txt') "$why ($((Get-Date).ToString('o')))" | Out-Null
}

function D-QuitSession([string]$why) {
    if (-not $script:sess -or $script:sess.quitAt) { return }
    $inbox = Join-Path $script:sess.dir 'inbox'
    New-Item -ItemType Directory -Force -Path $inbox | Out-Null
    Q-WriteText (Join-Path $inbox ((Get-Date -Format 'yyyyMMdd-HHmmss-fff') + '-quit.json')) '{"command":"quit"}' | Out-Null
    $script:sess.quitAt = (Get-Date).ToString('o'); $script:sess.quitWhy = $why
    D-SaveSession
    D-Log "closing game $($script:sess.id): $why"
}

# The queue game's Cities2.exe PID (from its heartbeat), only when that process really is our game: Cities2.exe with this
# session's -placemakerSuite token (a PID Windows has reused is never killed). -Loose (when the command line cannot be
# read): Cities2.exe started after this session's host started it.
function D-GamePid([switch]$Loose) {
    $b = D-Beat
    if (-not $b -or -not $b.pid) { return 0 }
    $gp = [int]$b.pid
    if ($script:QFake) { return $gp }
    $p = Get-Process -Id $gp -ErrorAction SilentlyContinue
    if (-not $p -or $p.ProcessName -ne 'Cities2') { return 0 }
    $cmd = D-CmdLine $p
    if ($cmd -match ('-placemakerSuite="?' + [regex]::Escape($script:sess.id) + '("|\s|$)')) { return $gp }
    if ($Loose -and -not $cmd) { try { if ($p.StartTime -ge (Q-Date $script:sess.startedAt).AddSeconds(-5)) { return $gp } } catch { } }
    return 0
}

# The STOP line of a host that stopped before launching the game.
function D-HostStop([string]$runId) {
    return (@(Get-Content -LiteralPath (Join-Path $script:Q.Logs "$runId.host.txt") -ErrorAction SilentlyContinue | Where-Object { $_ -match 'STOP' } | Select-Object -Last 1) -join ' ')
}

# ───────────── requests: take, feed, deliver ─────────────

function D-Take($req) {
    $from = Join-Path $script:Q.Requests $req.id
    $to = Join-Path $script:Q.Active $req.id
    if (-not (D-MoveDir $from $to)) { return $null }   # gone (cancelled) or held
    $r = Q-ReadJson (Join-Path $to 'request.json')
    if (-not $r) {
        # the client is told why (not "its folder was deleted")
        D-Log "$($req.id): its request.json could not be read once taken from the line: failed"
        $doneDir = Join-Path $script:Q.Done $req.id
        if (-not (D-MoveDir $to $doneDir)) { Remove-Item -LiteralPath $to -Recurse -Force -ErrorAction SilentlyContinue }
        Q-WriteJsonSure (Join-Path $doneDir 'result.json') ([ordered]@{ id = $req.id; who = $req.who; kind = $req.kind; status = 'failed'; code = 1; message = 'the queue could not read its request.json once it was its turn (run the command again)'; finishedAt = (Get-Date).ToString('o') }) | Out-Null
        return $null
    }
    return @{ req = $r; dir = $to; feed = $null; takenAt = (Get-Date) }
}

# summary.json rows of a game (read again only when the file changed).
function D-SummaryRows([string]$dir) {
    $p = Join-Path $dir 'summary.json'
    $t = try { (Get-Item -LiteralPath $p -ErrorAction Stop).LastWriteTimeUtc } catch { [datetime]::MinValue }
    if ($script:sumCache.path -eq $p -and $script:sumCache.time -eq $t) { return $script:sumCache.rows }
    $s = Q-ReadJson $p
    $rows = if ($s) { @($s.scenarios | Where-Object { $_ }) } else { @() }
    $script:sumCache = @{ path = $p; time = $t; rows = $rows }
    return $rows
}

function D-TimeoutSec($req) {
    if ($req.timeoutMin -gt 0) { return [int]$req.timeoutMin * 60 }
    $rates = Q-Rates
    return [int][math]::Max(300 + 150 * [int]$req.scenarios + 120 * [int]$req.land, 300 + 3 * (Q-WorkSec $req $rates))
}

# Why the request in the game must be stopped ('' while it is fine): no result in its time limit, or a stall, no build
# finished for 5 times the estimate of the one running and at least 6 min (a hang the game's heartbeat does not show).
function D-Overdue($a, [datetime]$now) {
    $f = $a.feed
    if (($now - (Q-Date $f.fedAt)).TotalSeconds -gt [double]$f.timeoutSec) { return "no result after $($f.timeoutSec) s" }
    $rp = @($a.req.rowPics); $k = [int]$a.rowsSeen
    # a request that gave its own time budget (Run-Suite -TimeoutMin: the AI builder's builds take minutes each) is held
    # to that budget only, not to the per-build stall estimate
    if ($a.req.timeoutMin -gt 0) { return '' }
    if ($rp.Count -gt 0 -and $k -lt $rp.Count -and $a.rowAt) {
        $rates = Q-Rates
        $est = Q-RowSec ([int]$rp[$k]) ([bool]@($a.req.rowLand)[$k]) $rates "$(@($a.req.rowType)[$k])"
        if ($k -eq 0) { $est += $rates.overhead + [int]$a.req.land * $rates.land }   # the first build also waits for the request's land
        $limit = [math]::Max(360, 5 * $est)
        $since = ($now - $a.rowAt).TotalSeconds
        if ($since -gt $limit) { return "build $($k + 1) of $($rp.Count) made no progress for $(Q-Duration $since) (5 times its estimate, at least 6 min)" }
    }
    return ''
}

# Feeds the active request into the open game's inbox. Throws when it cannot (the caller puts it back in line).
function D-Feed {
    $a = $script:active
    $req = $a.req
    $payloadFile = Join-Path $a.dir 'payload.json'
    if (-not (Test-Path -LiteralPath $payloadFile)) { throw "its payload.json is missing" }
    $payload = [IO.File]::ReadAllText($payloadFile)
    $inbox = Join-Path $script:sess.dir 'inbox'
    New-Item -ItemType Directory -Force -Path $inbox | Out-Null
    $name = (Get-Date -Format 'yyyyMMdd-HHmmss-fff') + '-' + ($req.who -replace '[^A-Za-z0-9_-]', '-')   # no '.': a name ending in '.result' is never read
    $rowsBefore = @(D-SummaryRows $script:sess.dir).Count
    $now = Get-Date
    # where this request's part of the game's logs starts
    $logPm = Q-FileLength (Join-Path $script:QUserData 'Logs\Placemaker.log')
    $logPlayer = Q-FileLength (Join-Path $script:QUserData 'Player.log')
    $a.feed = [ordered]@{ inbox = $name; session = $script:sess.id; sessionDir = $script:sess.dir; fedAt = $now.ToString('o'); rowsBefore = $rowsBefore; timeoutSec = (D-TimeoutSec $req); launched = ($script:sess.served -eq 0); killed = ''; logPm = $logPm; logPlayer = $logPlayer }
    Q-WriteJson (Join-Path $a.dir 'feed.json') $a.feed
    if (-not (Q-WriteText (Join-Path $inbox "$name.json") $payload)) { throw "could not write $inbox\$name.json" }
    $a.rowsSeen = 0; $a.rowAt = $now
    D-Log "fed $($req.id) ($($req.who), $($req.scenarios) build(s)) into game $($script:sess.id) as $name"
}

function D-H([string]$s) { return [Net.WebUtility]::HtmlEncode($s) }
function D-SafeId([string]$id) { foreach ($c in [IO.Path]::GetInvalidFileNameChars()) { $id = $id.Replace($c, '_') }; return $id.Replace('+', 'x').Replace(' ', '_') }   # SuiteShots.Safe

# The request's own results folder, in the requester's ResultsRoot: what a direct run would have left there. Builds,
# scenario files and pictures are numbered from 01 and keep the ids the request asked for (the shared game makes ids
# unique across requests: 'park-default+' is the second park-default of that game); its own part of the logs.
# Pictures: a hard link to the game's own file (same volume: no second copy on disk), else a copy.
function D-LinkOrCopy([string]$src, [string]$dst) {
    if (Test-Path -LiteralPath $dst) { Remove-Item -LiteralPath $dst -Force -ErrorAction SilentlyContinue }
    if ("$src$dst" -notmatch '[\[\]*?]') { try { New-Item -ItemType HardLink -Path $dst -Value $src -ErrorAction Stop | Out-Null; return } catch { } }
    Copy-Item -LiteralPath $src -Destination $dst -Force
}

function D-Publish($a, $result, [string]$status, [string]$message) {
    $req = $a.req; $feed = $a.feed
    $note = ''
    $dir = $req.resultsDir
    $sd = $feed.sessionDir
    $reserved = Join-Path $dir '.reserved'   # the client reserved the folder when it asked (Q-UniqueRunId): it is ours
    if ((Test-Path -LiteralPath $dir) -and @(Get-ChildItem -LiteralPath $dir -Force -ErrorAction SilentlyContinue | Where-Object { $_.Name -ne '.reserved' }).Count -gt 0) {
        $old = "$dir.old-$(Get-Date -Format 'yyyyMMdd-HHmmss')"; try { Move-Item -LiteralPath $dir -Destination $old -ErrorAction Stop } catch { }
    }
    foreach ($sub in '', 'shots', 'scenarios', 'logs') { New-Item -ItemType Directory -Force -Path (Join-Path $dir $sub) | Out-Null }
    $sum = Q-ReadJson (Join-Path $sd 'summary.json')
    $all = @(if ($sum) { $sum.scenarios | Where-Object { $_ } })   # @() around the if: one row must stay an array (Windows PowerShell has no .Count on a lone object)
    $count = if ($result -and $result.scenarios) { @($result.scenarios).Count } else { [math]::Max(0, $all.Count - [int]$feed.rowsBefore) }
    $rows = @()
    $scFiles = @{}
    $idMap = @{}
    $used = @{}
    for ($k = 0; $k -lt $count; $k++) {
        $i = [int]$feed.rowsBefore + $k
        if ($i -ge $all.Count) { break }
        $row = $all[$i] | ConvertTo-Json -Depth 12 | ConvertFrom-Json
        $sid = "$($row.id)"
        $want = $sid
        if ($sid.EndsWith('+')) { $want = $sid.TrimEnd('+'); while ($used.ContainsKey($want)) { $want += '+' } }
        $used[$want] = $true
        $oldPrefix = ('{0:D2}-{1}' -f ($i + 1), (D-SafeId $sid)); $newPrefix = ('{0:D2}-{1}' -f ($k + 1), (D-SafeId $want))
        $f = Get-ChildItem -LiteralPath (Join-Path $sd 'scenarios') -Filter ('{0:D2}-*.json' -f ($i + 1)) -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($f) { $dst = Join-Path $dir "scenarios\$newPrefix.json"; Copy-Item -LiteralPath $f.FullName -Destination $dst -Force; $scFiles["$k"] = $dst }
        if ($row.shots) {
            foreach ($p in $row.shots.PSObject.Properties) {
                $rel = "$($p.Value)"
                if ($rel -like 'ERROR*' -or -not $rel) { continue }
                $leaf = Split-Path $rel -Leaf
                $newRel = if ($leaf.StartsWith("$oldPrefix.")) { (Split-Path $rel) + '/' + $newPrefix + $leaf.Substring($oldPrefix.Length) } else { $rel }
                $newRel = $newRel.TrimStart('/').Replace('\', '/')
                $src = Join-Path $sd $rel
                $dst = Join-Path $dir $newRel
                New-Item -ItemType Directory -Force -Path (Split-Path $dst) | Out-Null
                if (Test-Path -LiteralPath $src) { D-LinkOrCopy $src $dst }
                $p.Value = $newRel
            }
        }
        if ($want -ne $sid) { $row.id = $want; $row | Add-Member -NotePropertyName sessionId -NotePropertyValue $sid -Force }
        if ($status -ne 'served' -and "$($row.status)" -eq 'running') {
            # the build the game was doing when it went away
            $row.status = 'interrupted'
            $row | Add-Member -NotePropertyName failure -NotePropertyValue "the game went away during this build ($message)" -Force
            $note = "build $($k + 1) of $([int]$req.scenarios) ($want) was running when the game went away"
        }
        $idMap["$k"] = @{ id = $want; sid = $sid; shots = $row.shots }
        $rows += $row
    }
    # logs: this request's part only (from where the game's logs stood when it was fed); the session's copies once the
    # game is gone, else the live files
    $hostGone = -not (Q-Alive $script:sess.hostPid $script:sess.hostStart)
    foreach ($n in 'Placemaker.log', 'Player.log') {
        $sessCopy = Join-Path $sd "logs\$n"
        $live = if ($n -eq 'Player.log') { Join-Path $script:QUserData 'Player.log' } else { Join-Path $script:QUserData "Logs\$n" }
        $from = if ($n -eq 'Player.log') { [int64]$feed.logPlayer } else { [int64]$feed.logPm }
        $src = if ($hostGone -and (Test-Path -LiteralPath $sessCopy)) { $sessCopy } else { $live }
        Q-CopyShared $src (Join-Path $dir "logs\$n") $from | Out-Null
    }
    Q-CopyShared (Join-Path $sd 'suite.log') (Join-Path $dir 'suite.log') | Out-Null
    $cat = Join-Path $sd 'catalog.json'
    if (Test-Path -LiteralPath $cat) {
        Copy-Item -LiteralPath $cat -Destination (Join-Path $dir 'catalog.json') -Force
        # the requester's suite\catalog.json only when the types, styles or options changed (not for the assembly line)
        $repoCat = Join-Path $req.repo 'suite\catalog.json'
        if (Test-Path -LiteralPath (Split-Path $repoCat)) {
            $new = Q-ReadJson $cat; $have = Q-ReadJson $repoCat
            if ($new -and (-not $have -or (($new.types | ConvertTo-Json -Depth 10 -Compress) -ne ($have.types | ConvertTo-Json -Depth 10 -Compress)))) { Copy-Item -LiteralPath $cat -Destination $repoCat -Force -ErrorAction SilentlyContinue }
        }
    }
    $fedAt = (Q-Date $feed.fedAt)
    foreach ($ad in (Join-Path $script:QUserData 'ModsData\Placemaker\audit'), (Join-Path $sd 'audit')) {
        if (-not (Test-Path -LiteralPath $ad)) { continue }
        foreach ($f in Get-ChildItem -LiteralPath $ad -File | Where-Object { $_.LastWriteTime -ge $fedAt.AddSeconds(-2) }) {
            New-Item -ItemType Directory -Force -Path (Join-Path $dir 'audit') | Out-Null
            Copy-Item -LiteralPath $f.FullName -Destination (Join-Path $dir "audit\$($f.Name)") -Force
        }
    }
    $now = Get-Date
    $waited = ($fedAt - (Q-Date $req.created)).TotalSeconds
    $ran = ($now - $fedAt).TotalSeconds
    if ($status -ne 'served') {
        # what the requester needs after a crash: the build that was running, and how many never ran (a crash writes no
        # row for the build it was in: its id comes from the request's own scenarios, when they are not expanded)
        $missing = [math]::Max(0, [int]$req.scenarios - $rows.Count)
        if (-not $note -and $missing -gt 0) {
            $pl = Q-ReadJson (Join-Path $a.dir 'payload.json')
            $scs = @(if ($pl -and $pl.scenarios) { $pl.scenarios } elseif ($pl -and -not $pl.command) { $pl })
            $nextId = if (@($scs | Where-Object { $_.expand }).Count -eq 0 -and $rows.Count -lt $scs.Count) { "$($scs[$rows.Count].id)" } else { '' }
            $note = "build $($rows.Count + 1) of $([int]$req.scenarios)$(if ($nextId) { " ($nextId)" }) was running when the game went away"
            $missing--
        }
        $parts = @(); if ($note) { $parts += $note }; if ($missing -gt 0) { $parts += "$missing of $([int]$req.scenarios) build(s) never ran" }
        $note = $parts -join '; '
        if ($note) { $message = "$message; $note" }
    }
    $byStatus = ($rows | Group-Object { "$($_.status)" } | ForEach-Object { "$($_.Count) $($_.Name)" }) -join ', '
    $shotCount = 0; foreach ($r in $rows) { if ($r.shots) { $shotCount += @($r.shots.PSObject.Properties).Count } }
    $headline = "$(if ($status -eq 'served') { 'finished' } else { "$($status.ToUpperInvariant()): $message" }). $($rows.Count) scenarios ($byStatus). Shots: $shotCount. Run in the queue's game $($feed.session)."
    $queueInfo = [ordered]@{ request = $req.id; who = $req.who; status = $status; message = $message; session = $feed.session; sessionDir = $sd; launched = [bool]$feed.launched; enqueued = (Q-Date $req.created).ToString('o'); fedAt = $feed.fedAt; finishedAt = $now.ToString('o'); waitedSec = [math]::Round($waited); ranSec = [math]::Round($ran); build = $req.build }
    $out = [ordered]@{ runId = $req.runId; set = $req.set; git = $req.build.label; queue = $queueInfo; headline = $headline; phase = $(if ($status -eq 'served') { 'done' } else { 'aborted' }) }
    if ($sum) { foreach ($k in 'grid', 'capacity', 'plots', 'landLaidOutLater', 'look', 'placemakerSettings', 'profile', 'fidelity', 'map', 'codeMods', 'dllHash', 'assembly', 'wrongBuild', 'gitMismatch') { if ($sum.$k) { $out[$k] = $sum.$k } } }
    $out['scenarios'] = $rows
    $out['scenarioCount'] = $rows.Count
    Q-WriteJson (Join-Path $dir 'summary.json') $out
    # summary.md and contact.html
    $md = @("> **Host: $status** (label $($req.build.label), queue game $($feed.session)$(if ($feed.launched) { ', launched for this request' } else { ', already open' }); waited $(Q-Duration $waited), ran $(Q-Duration $ran))$(if ($message) { " - $message" })", '', "# Game run $($req.runId) ($($req.set))", '', "**$headline**", '',
        'Read the logs: `logs/Placemaker.log` and `logs/Player.log` (this request''s part of the queue game''s logs), `suite.log` (the whole game''s runner steps). Pictures in order: `contact.html`. Each scenario''s steps: `scenarios/NN-<id>.json`.', '',
        '| # | scenario | block | status | s | shots |', '|---|---|---|---|---|---|')
    $html = New-Object Text.StringBuilder
    [void]$html.Append('<!doctype html><html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>Run ' + (D-H $req.runId) + '</title><style>:root{--bg:#f6f6f4;--fg:#1d1d1b;--dim:#6b6b66;--card:#fff;--line:#dcdcd6;--fail:#b3261e}@media (prefers-color-scheme:dark){:root{--bg:#161615;--fg:#ececea;--dim:#a3a39d;--card:#21211f;--line:#3a3a37;--fail:#ff8a80}}body{background:var(--bg);color:var(--fg);font:14px/1.4 system-ui,sans-serif;margin:0 16px 40px}h1{font-size:20px}h2{font-size:16px;margin:28px 0 6px}.row{display:flex;flex-wrap:wrap;gap:10px}.shot{background:var(--card);border:1px solid var(--line);border-radius:6px;padding:6px;width:min(400px,100%);box-sizing:border-box}.shot img{width:100%;height:auto;display:block}.cap{font-size:12px;color:var(--dim);margin-top:4px}.bad{color:var(--fail);font-weight:600}</style></head><body>')
    [void]$html.Append('<h1>Run ' + (D-H $req.runId) + '</h1><p>' + (D-H $headline) + '</p>')
    $n = 0
    foreach ($r in $rows) {
        $md += "| $($n + 1) | $($r.id) | $($r.block) $($r.kind) | $($r.status)$(if ($r.failure) { ': ' + $r.failure }) | $($r.sec) | $(if ($r.shots) { @($r.shots.PSObject.Properties).Count } else { 0 }) |"
        [void]$html.Append('<h2>' + ($n + 1) + '. ' + (D-H $r.id) + '</h2><div class="cap">' + (D-H "$($r.block) $($r.kind), status $($r.status), $($r.sec) s") + '</div>')
        $sf = $scFiles["$n"]
        if ($sf) {
            $sc = Q-ReadJson $sf
            foreach ($x in @($sc.steps | Where-Object { $_ -and $_.error })) { [void]$html.Append('<div class="bad">step ' + $x.i + ' ' + (D-H $x.do) + ': ' + (D-H "$($x.error)") + '</div>') }
        }
        [void]$html.Append('<div class="row">')
        if ($r.shots) { foreach ($p in $r.shots.PSObject.Properties) { $v = "$($p.Value)"; if ($v -like 'ERROR*') { [void]$html.Append('<div class="shot bad">' + (D-H "$($p.Name): $v") + '</div>') } else { [void]$html.Append('<div class="shot"><a href="' + (D-H $v) + '"><img loading="lazy" src="' + (D-H $v) + '" alt="' + (D-H $p.Name) + '"></a><div class="cap">' + (D-H $p.Name) + '</div></div>') } } }
        [void]$html.Append('</div>')
        $n++
    }
    [void]$html.Append('</body></html>')
    [IO.File]::WriteAllText((Join-Path $dir 'summary.md'), ($md -join "`r`n") + "`r`n", (New-Object Text.UTF8Encoding $false))
    [IO.File]::WriteAllText((Join-Path $dir 'contact.html'), $html.ToString(), (New-Object Text.UTF8Encoding $false))
    if ($result) {
        $r2 = $result | ConvertTo-Json -Depth 12 | ConvertFrom-Json
        $k = 0
        foreach ($s in @($r2.scenarios | Where-Object { $_ })) {
            $m = $idMap["$k"]
            if ($m) {
                if ($m.id -ne $m.sid) { $s.id = $m.id; $s | Add-Member -NotePropertyName sessionId -NotePropertyValue $m.sid -Force }
                if ($s.shots -and $m.shots) { foreach ($p in $s.shots.PSObject.Properties) { $nv = $m.shots.($p.Name); if ($nv) { $p.Value = $nv } } }
            }
            if ($scFiles["$k"]) { $s | Add-Member -NotePropertyName resultFile -NotePropertyValue $scFiles["$k"] -Force }
            $k++
        }
        if ($r2.PSObject.Properties['contact']) { $r2.contact = Join-Path $dir 'contact.html' }
        Q-WriteJson (Join-Path $dir 'result.json') $r2
    }
    Q-WriteJson (Join-Path $dir 'host.json') ([ordered]@{ runId = $req.runId; set = $req.set; git = $req.build.git; dllHash = $req.build.dllHash; label = $req.build.label; status = $status; queue = $queueInfo })
    Remove-Item -LiteralPath $reserved -Force -ErrorAction SilentlyContinue
    return @{ waited = $waited; ran = $ran; rows = $rows; message = $message }
}

function D-InstalledLine {
    $ub = MI-ReadJson (Join-Path $script:MI.User 'manifest.json')
    return "your build is not left installed: Mods\Placemaker gets the user's build ($(if ($ub) { MI-Describe $ub } else { 'as before' })) back the moment the queue's game exits"
}

# Finishes the active request: its result for the client, its folder in done\.
function D-Finish([string]$status, [int]$code, [string]$message, $result) {
    $a = $script:active
    $req = $a.req
    $pub = @{ waited = 0; ran = 0; rows = @(); message = $message }
    if ($a.feed -and $req.kind -ne 'play' -and $script:sess) { try { $pub = D-Publish $a $result $status $message } catch { D-Log "publishing $($req.id) failed: $($_.Exception.Message) at $($_.InvocationInfo.ScriptLineNumber)" } }
    if ($pub.message) { $message = $pub.message }
    if ($req.kind -eq 'play' -and $script:sess -and $script:sess.startedAt) { $pub.waited = ((Q-Date $script:sess.startedAt) - (Q-Date $req.created)).TotalSeconds; $pub.ran = ((Get-Date) - (Q-Date $script:sess.startedAt)).TotalSeconds }
    $res = [ordered]@{
        id = $req.id; who = $req.who; kind = $req.kind; status = $status; code = $code; message = $message
        resultsDir = $req.resultsDir; runId = $req.runId; session = $(if ($a.feed) { $a.feed.session } elseif ($script:sess) { $script:sess.id } else { '' }); launched = $(if ($a.feed) { [bool]$a.feed.launched } else { $true })
        waitedSec = [math]::Round($pub.waited); ranSec = [math]::Round($pub.ran); finishedAt = (Get-Date).ToString('o'); installed = $(if ($req.kind -eq 'play') { MI-StatusLine } else { D-InstalledLine })
        build = $req.build
    }
    $doneDir = Join-Path $script:Q.Done $req.id
    if (-not (D-MoveDir $a.dir $doneDir)) { New-Item -ItemType Directory -Force -Path $doneDir | Out-Null }
    if (-not (Q-WriteJsonSure (Join-Path $doneDir 'result.json') $res)) { D-Log "could not write $(Join-Path $doneDir 'result.json'): its client will say STUCK" }
    if (Test-Path -LiteralPath $a.dir) { Remove-Item -LiteralPath $a.dir -Recurse -Force -ErrorAction SilentlyContinue }   # the move failed: the result is in done\ all the same
    D-Log "finished $($req.id) ($($req.who)): $status$(if ($message) { " - $message" }) (waited $(Q-Duration $pub.waited), ran $(Q-Duration $pub.ran))"
    # history for the ETA
    if ($status -eq 'served' -and $pub.rows -and @($pub.rows).Count -gt 0) {
        $sum = 0.0; foreach ($r in $pub.rows) { if ($r.sec) { $sum += [double]$r.sec } }
        Q-HistoryAddRows $pub.rows @($req.rowLand) @($req.rowType)
        Q-HistoryAdd 'overhead' ([math]::Max(0.5, $pub.ran - $sum))
    }
    $script:active = $null
    if ($script:sess -and $script:sess.kind -eq 'queue') { $script:sess.served = [int]$script:sess.served + 1; D-SaveSession }
    $script:idleSince = Get-Date
}

# Back in line (at its old place: its seq is kept). When it cannot go back, it is finished as failed (never left in active\).
function D-Requeue([string]$why, [switch]$Fresh, [switch]$Retried) {
    $a = $script:active
    $req = $a.req
    if ($Fresh -or $Retried) {
        if ($Fresh) { $req | Add-Member -NotePropertyName fresh -NotePropertyValue $true -Force }
        if ($Retried) { $req | Add-Member -NotePropertyName retried -NotePropertyValue $true -Force }
        Q-WriteJson (Join-Path $a.dir 'request.json') $req
    }
    Remove-Item -LiteralPath (Join-Path $a.dir 'feed.json') -Force -ErrorAction SilentlyContinue
    if (D-MoveDir $a.dir (Join-Path $script:Q.Requests $req.id)) {
        D-Log "put $($req.id) ($($req.who)) back in line: $why"
        $script:active = $null
    } else {
        D-Finish 'failed' 1 "it could not be put back in line ($why)" $null
    }
}

# Folders in active\ that this dispatcher is not running (a feed that failed half way, a move that did not happen):
# finished when their result is there, else put back in line.
function D-ReconcileActive {
    foreach ($d in @(Get-ChildItem -LiteralPath $script:Q.Active -Directory -ErrorAction SilentlyContinue)) {
        if ($script:active -and $d.FullName -eq (Get-Item -LiteralPath $script:active.dir -ErrorAction SilentlyContinue).FullName) { continue }
        if ((Get-Date) - $d.LastWriteTime -lt [timespan]::FromSeconds(5)) { continue }   # being moved in right now
        if (Test-Path -LiteralPath (Join-Path $script:Q.Done "$($d.Name)\result.json")) { Remove-Item -LiteralPath $d.FullName -Recurse -Force -ErrorAction SilentlyContinue; continue }
        $req = Q-ReadJson (Join-Path $d.FullName 'request.json')
        if (-not $req) { D-Log "removed $($d.FullName): no request.json"; Remove-Item -LiteralPath $d.FullName -Recurse -Force -ErrorAction SilentlyContinue; continue }
        $keep = $script:active
        $script:active = @{ req = $req; dir = $d.FullName; feed = $null; takenAt = (Get-Date) }
        $f = Q-ReadJson (Join-Path $d.FullName 'feed.json')
        if ($f -and -not $keep) {
            $feed = [ordered]@{}; foreach ($p in $f.PSObject.Properties) { $feed[$p.Name] = $p.Value }
            $script:active.feed = $feed
            $res = Join-Path $f.sessionDir "inbox\$($f.inbox).result.json"
            if ($script:sess -and $f.session -eq $script:sess.id) {
                if (Test-Path -LiteralPath $res) { D-Finish 'served' 0 '' (Q-ReadJson $res) }
                else { D-Log "$($req.id) ($($req.who)) is in the game but was not tracked: following it again" }   # stays $script:active
                continue
            }
            if (Test-Path -LiteralPath $res) { $tmpSess = $script:sess; $script:sess = [ordered]@{ hostPid = 0; hostStart = 0; kind = 'queue'; id = $f.session; dir = $f.sessionDir; served = 1 }; D-Finish 'served' 0 '' (Q-ReadJson $res); $script:sess = $tmpSess; $script:active = $keep; continue }
            $script:active.feed = $null
        }
        D-Log "$($req.id) ($($req.who)) was in active\ but not in the game"
        D-Requeue 'it was taken from the line but never run'
        $script:active = $keep
    }
}

# ───────────── capacity of the open game's test grid ─────────────

function D-Used {
    $u = @{ Flat = @{}; Zoned = @{}; Slope = @{}; Rough = @{}; Survive = @{} }
    if (-not $script:sess) { return $u }
    foreach ($r in D-SummaryRows $script:sess.dir) { if ($r.block -and $r.kind -and $u.ContainsKey("$($r.kind)")) { $u["$($r.kind)"][$r.block] = $true } }
    return $u
}
function D-Fits($r) {
    if (-not $script:sess -or -not $script:sess.capacity) { return $true }
    $u = D-Used
    foreach ($k in 'Flat', 'Zoned', 'Slope', 'Rough', 'Survive') {
        $need = [int]$r.blocks.$k
        if ($need -le 0) { continue }
        $cap = [int]$script:sess.capacity.($k.ToLowerInvariant())
        if ($need -gt $cap - $u[$k].Count) { return $false }
    }
    return $true
}
# The test blocks of each kind the open game's grid still has once the request in it is done ($null: not known yet), for
# the plan (Q-Plan applies the open game's grid the way the real choice does).
function D-FreeAfter {
    if (-not $script:sess -or $script:sess.kind -ne 'queue') { return $null }
    $cap = $script:sess.capacity
    # still booting: the blocks of the grid it was launched with (an estimate for the plan only, as the game lays a N x 7
    # grid out: 16 x 7 has 29 flat, 16 zoned, 32 slope, 3 rough, 1 survive); the real choice waits for the game's own count
    if (-not $cap -and "$($script:sess.grid)" -match '^(\d+)x7$') { $n = [int]$Matches[1]; $cap = [pscustomobject]@{ flat = 2 * $n - 3; zoned = $n; slope = 2 * $n; rough = 3; survive = 1 } }
    if (-not $cap) { return $null }
    $u = D-Used
    $free = @{}
    foreach ($k in 'Flat', 'Zoned', 'Slope', 'Rough', 'Survive') { $free[$k] = [int]$cap.($k.ToLowerInvariant()) - $u[$k].Count }
    if ($script:active -and $script:active.feed) {
        # the request in the game still takes the rest of its blocks
        $rows = @(D-SummaryRows $script:sess.dir | Select-Object -Skip ([int]$script:active.feed.rowsBefore))
        foreach ($k in @($free.Keys)) { $have = @($rows | Where-Object { "$($_.kind)" -eq $k }).Count; $free[$k] -= [math]::Max(0, [int]$script:active.req.blocks.$k - $have) }
    }
    return $free
}

# ───────────── status for the clients ─────────────

function D-Status([string]$state, $queued) {
    $rates = Q-Rates
    if ($script:active) { $queued = @($queued | Where-Object { $_.id -ne $script:active.req.id }) }   # taken this round: it is the current one, not in line
    $now = Get-Date
    $cur = $null
    $remain = 0.0
    if ($script:active -and $script:active.feed) {
        $a = $script:active
        $req = $a.req
        $runSec = ($now - (Q-Date $a.feed.fedAt)).TotalSeconds
        # progress: the rows this request has finished (the game writes summary.json after each build)
        if ($script:sess) {
            $done = [math]::Max(0, @(D-SummaryRows $script:sess.dir | Where-Object { "$($_.status)" -ne 'running' }).Count - [int]$a.feed.rowsBefore)
            if ($done -ne [int]$a.rowsSeen) { $a.rowsSeen = $done; $a.rowAt = $now }
        }
        if (-not $a.rowAt) { $a.rowAt = Q-Date $a.feed.fedAt }
        $rl = Q-RunningLeft $req ([int]$a.rowsSeen) ($now - $a.rowAt).TotalSeconds $runSec $rates
        $remain = $rl.left
        $cur = [ordered]@{ id = $req.id; who = $req.who; kind = $req.kind; scenarios = $req.scenarios; done = $rl.done; total = $rl.total; runningSec = [math]::Round($runSec); etaSec = [math]::Round($rl.left); overSec = [math]::Round($rl.over); build = $req.build.hash }
    } elseif ($script:sess -and $script:sess.kind -eq 'play') {
        $cur = [ordered]@{ id = $script:sess.requestId; who = $script:sess.who; kind = 'play'; scenarios = 0; done = 0; total = 0; runningSec = [math]::Round(($now - (Q-Date $script:sess.startedAt)).TotalSeconds); etaSec = 0; overSec = 0; build = $script:sess.build }
    }
    $planSess = $null
    $gameState = ''
    $bootOver = 0
    # by the time the next request is chosen, the one in the game has been served (a fresh game's first request is the
    # only one that fits whatever it needs)
    $servedAfter = if ($script:sess) { [int]$script:sess.served + $(if ($script:active -and $script:active.feed) { 1 } else { 0 }) } else { 0 }
    if ($script:sess) {
        if ($script:sess.kind -eq 'play') { $planSess = @{ key = $null; served = 0; remainSec = 0; unknown = $true }; $gameState = 'playground' }
        elseif ($script:sess.quitAt) { $gameState = 'closing'; $planSess = @{ key = $null; served = 0; remainSec = [math]::Max(3, $rates.quit - ($now - (Q-Date $script:sess.quitAt)).TotalSeconds) } }
        elseif (-not $script:sess.readyAt) {
            # a boot slower than usual: the ETA keeps a floor that grows with the overrun (not stuck at a few seconds), and
            # the clients say how far over its usual boot it is
            $gameState = 'starting'
            $bootLeft = $rates.boot - ($now - (Q-Date $script:sess.startedAt)).TotalSeconds
            if ($bootLeft -lt 10) { $bootOver = [math]::Max(0, -$bootLeft); $bootLeft = 10 + 0.25 * $bootOver }
            $planSess = @{ key = $script:sess.key; served = $servedAfter; remainSec = $bootLeft + $remain; free = (D-FreeAfter) }
        }
        else { $gameState = 'ready'; $planSess = @{ key = $script:sess.key; served = $servedAfter; remainSec = $remain; free = (D-FreeAfter) } }
    }
    if ($script:paused) { if ($planSess) { $planSess.unknown = $true } else { $planSess = @{ key = $null; served = 0; remainSec = 0; unknown = $true } } }
    elseif (-not $script:sess) {
        $until = $script:hostBackoff; if ($script:userGraceUntil -gt $until) { $until = $script:userGraceUntil }
        if ($until -gt $now) { $planSess = @{ key = $null; served = 0; remainSec = ($until - $now).TotalSeconds } }
    }
    $plan = @(Q-Plan $queued $planSess $rates ($StarveMinutes * 60))
    $st = [ordered]@{
        updated = $now.ToString('o'); dispatcherPid = $PID; v = $script:QVersion; state = $state; note = $script:note; paused = $script:paused
        game = $(if ($script:sess) { [ordered]@{ id = $script:sess.id; kind = $script:sess.kind; build = $script:sess.build; label = $script:sess.label; who = $script:sess.who; state = $gameState; served = $script:sess.served; startedAt = $script:sess.startedAt; readyAt = $script:sess.readyAt; grid = $script:sess.grid; bootOverSec = [math]::Round($bootOver) } } else { $null })
        current = $cur; queued = @($queued | Where-Object { $_ }).Count; plan = $plan; launches = $script:launches; installed = (MI-StatusLine)
        rates = [ordered]@{ bootSec = [math]::Round($rates.boot); quitSec = [math]::Round($rates.quit); perBuildSec = [math]::Round($rates.base, 1); perPictureSec = [math]::Round($rates.pic, 1); landSec = [math]::Round($rates.land, 1); overheadSec = [math]::Round($rates.overhead, 1) }
    }
    Q-WriteJson $script:Q.Status $st
}

# ───────────── housekeeping ─────────────

function D-TrimLog([string]$p, [int64]$max = 4MB, [int64]$keep = 1MB) {
    try {
        if ((Q-FileLength $p) -le $max) { return }
        $fs = [IO.File]::Open($p, 'Open', 'Read', 'ReadWrite, Delete')
        try { [void]$fs.Seek(-$keep, 'End'); $sr = New-Object IO.StreamReader($fs, [Text.Encoding]::UTF8); $text = $sr.ReadToEnd() } finally { $fs.Dispose() }
        $text = $text.Substring([math]::Max(0, $text.IndexOf("`n") + 1))
        Q-WriteText $p ("(older lines trimmed $(Get-Date -Format 'yyyy-MM-dd HH:mm'))`r`n" + $text) | Out-Null
    } catch { }
}

# The copy of the scripts this dispatcher runs from (dispatcher\<hash>\tools\suite: the hash), '' when it runs elsewhere.
function D-MyCopy {
    $root = Split-Path (Split-Path $PSScriptRoot)
    if ((Split-Path $root) -eq $script:Q.Disp) { return (Split-Path $root -Leaf) }
    return ''
}
# Once a round has gone well: this copy is the fallback for a client whose own copy does not start.
function D-MarkGood {
    if ($script:goodWritten) { return }
    $script:goodWritten = $true
    $h = D-MyCopy
    if (-not $h) { return }
    $g = Q-ReadJson (Join-Path $script:Q.Disp 'good.json')
    if ($g -and $g.hash -eq $h) { return }
    if ($g -and [int]$g.v -gt $script:QVersion) { return }   # a newer one ran well already
    Q-WriteJson (Join-Path $script:Q.Disp 'good.json') ([ordered]@{ hash = $h; v = $script:QVersion; at = (Get-Date).ToString('o'); pid = $PID })
}

# The clean-up. Each pass deletes for at most -BudgetSec (a big backlog after a busy day takes several passes, a minute
# apart, so status.json stays fresh and no client gives up as STUCK); the loop runs it after status.json is written.
function D-Gc([switch]$Now, [double]$BudgetSec = 10) {
    if (-not $Now -and (Get-Date) -lt $script:nextGc) { return }
    $script:nextGc = (Get-Date).AddMinutes(10)
    $deadline = (Get-Date).AddSeconds($BudgetSec)
    $more = { if ((Get-Date) -gt $deadline) { $script:nextGc = (Get-Date).AddMinutes(1); return $false }; return $true }
    foreach ($d in Get-ChildItem -LiteralPath $script:Q.Done -Directory -ErrorAction SilentlyContinue | Where-Object { $_.LastWriteTime -lt (Get-Date).AddHours(-24) }) { if (-not (& $more)) { return }; Remove-Item -LiteralPath $d.FullName -Recurse -Force -ErrorAction SilentlyContinue }
    # a client that died while writing its request, a cancel cut off half way
    foreach ($d in Get-ChildItem -LiteralPath $script:Q.Requests -Directory -Force -ErrorAction SilentlyContinue | Where-Object { $_.Name.StartsWith('.') -and $_.LastWriteTime -lt (Get-Date).AddHours(-1) }) { if (-not (& $more)) { return }; Remove-Item -LiteralPath $d.FullName -Recurse -Force -ErrorAction SilentlyContinue }
    # frozen builds (7.5 MB, 650 files each): kept while a request or the open game uses them, else 2 h after their last use
    $keepFor = {
        $k = @{}
        foreach ($root in $script:Q.Requests, $script:Q.Active) {
            foreach ($d in Get-ChildItem -LiteralPath $root -Directory -ErrorAction SilentlyContinue) { if ($d.Name.StartsWith('.')) { continue }; $r = Q-ReadJson (Join-Path $d.FullName 'request.json'); if ($r) { $k[$r.build.hash] = $true } }
        }
        if ($script:sess) { $k[$script:sess.build] = $true }
        return $k
    }
    $keep = & $keepFor
    foreach ($b in Get-ChildItem -LiteralPath $script:Q.Bundles -Directory -Force -ErrorAction SilentlyContinue) {
        if ($b.Name.StartsWith('.')) {
            # a freeze, a replacement or a clean-up that was cut off
            if ($b.LastWriteTime -lt (Get-Date).AddHours(-1)) { if (-not (& $more)) { return }; Remove-Item -LiteralPath $b.FullName -Recurse -Force -ErrorAction SilentlyContinue }
            continue
        }
        if ($keep.ContainsKey($b.Name) -or $b.LastWriteTime -ge (Get-Date).AddHours(-2)) { continue }
        if (-not (& $more)) { return }
        # looked at again right before: a client reusing it touches it first (Q-Snapshot), a request may have come in
        $again = Get-Item -LiteralPath $b.FullName -Force -ErrorAction SilentlyContinue
        if (-not $again -or $again.LastWriteTime -ge (Get-Date).AddHours(-2) -or (& $keepFor).ContainsKey($b.Name)) { continue }
        # renamed away first, so a half-deleted build never sits under its name (a client would reuse it)
        $gone = Join-Path $script:Q.Bundles ('.gc-' + $b.Name)
        if (D-MoveDir $b.FullName $gone) { Remove-Item -LiteralPath $gone -Recurse -Force -ErrorAction SilentlyContinue }
    }
    foreach ($f in Get-ChildItem -LiteralPath $script:Q.Logs -File -ErrorAction SilentlyContinue | Where-Object { $_.LastWriteTime -lt (Get-Date).AddDays(-3) }) { if (-not (& $more)) { return }; Remove-Item -LiteralPath $f.FullName -Force -ErrorAction SilentlyContinue }
    # copies of the dispatcher's scripts: the running one and the last one that ran well stay, others go after a week
    if (Test-Path -LiteralPath $script:Q.Disp) {
        $good = Q-ReadJson (Join-Path $script:Q.Disp 'good.json')
        $mine = D-MyCopy
        foreach ($d in Get-ChildItem -LiteralPath $script:Q.Disp -Directory -Force -ErrorAction SilentlyContinue) {
            if ($d.Name -eq $mine -or ($good -and $d.Name -eq $good.hash)) { continue }
            $age = if ($d.Name.StartsWith('.')) { (Get-Date).AddHours(-1) } else { (Get-Date).AddDays(-7) }
            if ($d.LastWriteTime -lt $age) { if (-not (& $more)) { return }; Remove-Item -LiteralPath $d.FullName -Recurse -Force -ErrorAction SilentlyContinue }
        }
    }
    if (-not $script:QFake -and (Test-Path -LiteralPath $script:QMainRuns)) {
        # the queue's own game folders (every request has its own copy of its results): 3 days; so do results that a
        # request with the same run id moved aside (<run>.old-<time>), and a reserved results folder that never got any
        foreach ($d in Get-ChildItem -LiteralPath $script:QMainRuns -Directory -ErrorAction SilentlyContinue | Where-Object { ($_.Name -like 'queue-*' -or $_.Name -like '*.old-*') -and $_.LastWriteTime -lt (Get-Date).AddDays(-3) }) {
            if ($script:sess -and $d.Name -eq $script:sess.id) { continue }
            if (-not (& $more)) { return }
            Remove-Item -LiteralPath $d.FullName -Recurse -Force -ErrorAction SilentlyContinue
        }
        foreach ($d in Get-ChildItem -LiteralPath $script:QMainRuns -Directory -ErrorAction SilentlyContinue | Where-Object { $_.LastWriteTime -lt (Get-Date).AddDays(-1) }) {
            if (Test-Path -LiteralPath (Join-Path $d.FullName '.reserved')) { if (-not (& $more)) { return }; Q-Unreserve $d.FullName }
        }
        # Show.ps1's set files (a command killed while waiting leaves its own behind)
        $show = Join-Path $script:forensics 'show'
        foreach ($f in Get-ChildItem -LiteralPath $show -File -Filter '*.json' -ErrorAction SilentlyContinue | Where-Object { $_.LastWriteTime -lt (Get-Date).AddDays(-3) }) { if (-not (& $more)) { return }; Remove-Item -LiteralPath $f.FullName -Force -ErrorAction SilentlyContinue }
    }
    D-TrimLog $script:Q.Log
    D-TrimLog $script:Q.Launches
}

# Requests whose client is gone leave the line.
function D-DropDead($queued) {
    $alive = @()
    foreach ($r in @($queued)) {
        if ($r.clientPid -and -not (Q-Alive $r.clientPid $r.clientStart)) {
            $d = Join-Path $script:Q.Requests $r.id
            $doneDir = Join-Path $script:Q.Done $r.id
            if (D-MoveDir $d $doneDir) {
                Q-WriteJsonSure (Join-Path $doneDir 'result.json') ([ordered]@{ id = $r.id; who = $r.who; status = 'dropped'; code = 2; message = 'the command that asked is gone (Ctrl+C or killed): taken out of the line' }) | Out-Null
                D-Log "dropped $($r.id) ($($r.who)): its client PID $($r.clientPid) is gone"
            }
            continue
        }
        $alive += $r
    }
    return $alive
}

# ───────────── adopt what a dead dispatcher left ─────────────

# Every step here is guarded on its own: a failure is logged and the dispatcher still reaches its loop (a dispatcher that
# died here would leave every client waiting).
function D-Startup([string]$what, [scriptblock]$block) {
    try { . $block } catch { D-Log "start-up: $what failed: $($_.Exception.Message) at $($_.InvocationInfo.ScriptLineNumber): $($_.InvocationInfo.Line.Trim())" }
}
$script:lastQueued = @{}
# a first status.json at once: clients see a live dispatcher while it adopts and restores (never STUCK for that)
D-Startup 'the first status' { D-Status 'starting' @(Q-Queued) }
D-Startup 'adopting the game of a gone dispatcher' {
    $prev = Q-ReadJson $script:Q.Session
    if ($prev) {
        if (Q-Alive $prev.hostPid $prev.hostStart) {
            $script:sess = [ordered]@{}
            foreach ($p in $prev.PSObject.Properties) { $script:sess[$p.Name] = $p.Value }
            D-Log "adopted game $($prev.id) ($($prev.kind), build $($prev.build)) from a dispatcher that is gone"
        } else {
            D-Log "the game $($prev.id) of a gone dispatcher has ended"
            Remove-Item -LiteralPath $script:Q.Session -Force
        }
    }
}
foreach ($d in @(Get-ChildItem -LiteralPath $script:Q.Active -Directory -ErrorAction SilentlyContinue)) {
  D-Startup "adopting $($d.Name)" {
    $req = Q-ReadJson (Join-Path $d.FullName 'request.json')
    if (-not $req) { Remove-Item -LiteralPath $d.FullName -Recurse -Force -ErrorAction SilentlyContinue; return }
    if ($script:active) { return }   # one request is in the game at a time; D-ReconcileActive puts any other back in line
    $feed = Q-ReadJson (Join-Path $d.FullName 'feed.json')
    $a = @{ req = $req; dir = $d.FullName; feed = $null; takenAt = (Get-Date) }
    if ($feed) { $a.feed = [ordered]@{}; foreach ($p in $feed.PSObject.Properties) { $a.feed[$p.Name] = $p.Value } }
    $script:active = $a
    if ($req.kind -eq 'play') {
        if ($script:sess -and $script:sess.kind -eq 'play' -and $script:sess.requestId -eq $req.id) { D-Log "adopted the user's playground request $($req.id)" }
        else { D-Finish 'served' 0 'the playground was closed while no dispatcher was watching' $null }   # never reopened
        return
    }
    if ($script:sess -and $feed -and $feed.session -eq $script:sess.id) {
        D-Log "adopted the request in the game: $($req.id) ($($req.who))"
        # a dispatcher that died between feed.json and the inbox file: feed it again
        $inbox = Join-Path $script:sess.dir 'inbox'
        $fedFile = Join-Path $inbox "$($feed.inbox).json"
        if (-not (Test-Path -LiteralPath $fedFile) -and -not (Test-Path -LiteralPath (Join-Path $inbox "done\$($feed.inbox).json")) -and -not (Test-Path -LiteralPath (Join-Path $inbox "$($feed.inbox).result.json"))) {
            $pf = Join-Path $d.FullName 'payload.json'
            if (Test-Path -LiteralPath $pf) { Q-WriteText $fedFile ([IO.File]::ReadAllText($pf)) | Out-Null; $a.feed.fedAt = (Get-Date).ToString('o'); Q-WriteJson (Join-Path $d.FullName 'feed.json') $a.feed; D-Log "$($req.id) had not reached the game's inbox: fed it again" }
            else { D-Requeue 'its payload is missing' }
        }
        return
    }
    $inboxResult = if ($feed) { Join-Path $feed.sessionDir "inbox\$($feed.inbox).result.json" } else { '' }
    if ($inboxResult -and (Test-Path -LiteralPath $inboxResult)) {
        $tmpSess = $script:sess
        $script:sess = [ordered]@{ hostPid = 0; hostStart = 0; kind = 'queue'; id = $feed.session; dir = $feed.sessionDir; served = 1 }
        D-Finish 'served' 0 '' (Q-ReadJson $inboxResult)
        $script:sess = $tmpSess
    } else { D-Requeue 'its game ended with the dispatcher that ran it' }
  }
}

D-Startup "putting the user's build back" { D-RestoreUserBuild 'a dispatcher started' -Now | Out-Null }
D-Startup "putting the user's settings back" { D-RestoreSettings 'a dispatcher started' }

# ───────────── the loop ─────────────

$state = 'idle'
try {
    $errors = 0
    while ($true) {
      try {
        $now = Get-Date
        $queued = D-DropDead (Q-Queued)
        # requests that left the line by themselves (Ctrl+C, a killed command, a deleted folder): said in the log
        $ids = @{}; foreach ($r in $queued) { $ids["$($r.id)"] = "$($r.who)" }
        foreach ($id in @($script:lastQueued.Keys)) {
            if ($ids.ContainsKey($id) -or (Test-Path -LiteralPath (Join-Path $script:Q.Active $id)) -or (Test-Path -LiteralPath (Join-Path $script:Q.Done $id))) { continue }
            D-Log "$id ($($script:lastQueued[$id])) left the line (its command was stopped, or its folder was deleted)"
        }
        $script:lastQueued = $ids
        $foreign = @(D-Foreign)
        D-HandleOrphans $foreign
        $holder = D-SuiteLockHolder
        $script:note = ''
        $script:paused = $false
        D-ReconcileActive
        # a game the queue did not start (the user's, a playground, a runner outside the queue) or the Paradox launcher
        $users = @($foreign | Where-Object { $_.kind -ne 'orphan' })
        $script:userGame = D-WaitNote $users $null
        if ($users.Count -gt 0) { $script:userSeen = @{ at = $now; kind = "$($users[0].kind)" } }
        elseif ($script:userSeen) {
            # it has just closed: the user may start the game again (or their own after a playground): wait before launching
            $script:userGraceUntil = $now.AddSeconds($UserGraceSec)
            $script:userGraceNote = "$(if ($script:userSeen.kind -in 'user', 'launcher', 'playground') { "the user's game closed" } else { 'a game the queue did not start closed' }) at $($now.ToString('HH:mm:ss')): the queue waits $(Q-Duration $UserGraceSec) before launching its own (time for the user to start theirs again)"
            D-SaveGrace
            D-Log $script:userGraceNote
            $script:userSeen = $null
            # what the queue still owes goes back now, before the user can start the game again (not on the next check)
            if (-not $script:sess) { D-RestoreUserBuild 'the user''s game closed' -Now | Out-Null; D-RestoreSettings 'the user''s game closed' -Now }
        }

        if ($script:sess) {
            $hostAlive = Q-Alive $script:sess.hostPid $script:sess.hostStart
            if ($script:sess.kind -eq 'play') {
                $state = 'playground'
                $script:note = "the user's playground is open ($($script:sess.who), run $($script:sess.id)); the queue goes on when they close the game"
                $script:paused = $true
                $beat = D-Beat
                if (-not $script:sess.readyAt -and $beat -and "$($beat.phase)" -eq 'playground') {
                    $script:sess.readyAt = $now.ToString('o'); D-SaveSession
                    D-CountLaunch ("ready in {0:F0} s" -f ($now - (Q-Date $script:sess.startedAt)).TotalSeconds)
                    D-Log ("the playground {0} is ready ({1:F0} s): the controls are the user's; the queue waits until the game is closed" -f $script:sess.id, ($now - (Q-Date $script:sess.startedAt)).TotalSeconds)
                }
                if (-not $hostAlive) {
                    $stop = if (-not $script:sess.readyAt) { D-HostStop $script:sess.id } else { '' }
                    if (-not $stop) { D-CountLaunch $(if ($script:sess.readyAt) { 'closed' } else { 'closed before it was ready' }) }
                    D-Log "the user's playground $($script:sess.id) closed$(if ($stop) { " before it opened: $stop" })"
                    if ($script:active) { if ($stop) { D-Finish 'failed' 1 "the playground did not open: $stop" $null } else { D-Finish 'served' 0 '' $null } }
                    if (-not $stop) {
                        $script:userGraceUntil = $now.AddSeconds($UserGraceSec)
                        $script:userGraceNote = "the user closed the playground at $($now.ToString('HH:mm:ss')): the queue waits $(Q-Duration $UserGraceSec) before launching its own game (time for the user to start theirs)"
                        D-SaveGrace
                    }
                    $script:sess = $null; D-SaveSession
                    $script:idleSince = Get-Date
                }
            } else {
                $beat = D-Beat
                if (-not $script:sess.readyAt -and $beat -and "$($beat.phase)" -like 'queue*') {
                    $script:sess.readyAt = $now.ToString('o')
                    $boot = ($now - (Q-Date $script:sess.startedAt)).TotalSeconds
                    Q-HistoryAdd 'boot' $boot
                    $sum = Q-ReadJson (Join-Path $script:sess.dir 'summary.json')
                    if ($sum -and $sum.capacity) { $script:sess.capacity = $sum.capacity }
                    D-SaveSession
                    D-Log ("game {0} ready in {1:F0} s (grid {2})" -f $script:sess.id, $boot, $script:sess.grid)
                    D-CountLaunch ("ready in {0:F0} s" -f $boot)
                    $script:idleSince = $now
                    $script:startFailures.Remove($script:sess.key)
                }
                if (-not $hostAlive) {
                    # the game and its host are gone: a clean quit, a crash or a hang (Run-Suite collected the logs and
                    # put the user's build back; D-RestoreUserBuild finishes that if it could not)
                    $hj = Q-ReadJson (Join-Path $script:sess.dir 'host.json')
                    $how = if ($hj) { "$($hj.status)" } else { 'unknown' }
                    # stopped from outside: not asked by the queue, not a crash, not the host's own watchdog. The user's way to
                    # free the game: Steam's Stop, Task Manager, the window's close button
                    $asked = [bool]$script:sess.quitAt -or [bool]($script:active -and $script:active.feed -and $script:active.feed.killed)
                    $fromOutside = (-not $asked) -and ($how -in @('closed-externally', 'unknown-exit', 'unknown', 'gone'))
                    if ($script:sess.quitAt) { Q-HistoryAdd 'quit' ($now - (Q-Date $script:sess.quitAt)).TotalSeconds }
                    D-Log "game $($script:sess.id) ended: $how$(if ($script:sess.quitWhy) { " (closed: $($script:sess.quitWhy))" })$(if ($fromOutside) { ' - stopped from outside the queue' })"
                    if ($script:active) {
                        $f = $script:active.feed
                        $resFile = if ($f) { Join-Path $script:sess.dir "inbox\$($f.inbox).result.json" } else { '' }
                        if ($resFile -and (Test-Path -LiteralPath $resFile)) { D-Finish 'served' 0 '' (Q-ReadJson $resFile) }
                        elseif ($f -and $f.killed) { D-Finish 'timeout' 1 "$(if ($f.killedWhy) { $f.killedWhy } else { "no result after $($f.timeoutSec) s" }): the game was stopped (status $how); the logs are in the results folder" $null }
                        elseif ($f -and $how -eq 'quit' -and -not $script:sess.quitAt -and @(D-SummaryRows $script:sess.dir).Count -le [int]$f.rowsBefore -and (Select-String -LiteralPath (Join-Path $script:sess.dir 'suite.log') -Pattern 'queue: idle timeout' -SimpleMatch -Quiet -ErrorAction SilentlyContinue)) {
                            # fed in the moment the game quit by itself (its own idle limit, e.g. after a dispatcher restart
                            # reset ours): it never started, so it goes back in line at its place
                            D-Requeue "the game quit by itself (its own idle limit) as it was fed; it never started"
                        }
                        elseif ($f -and $fromOutside -and -not $script:active.req.retried) { D-Requeue "the queue's game was stopped from outside while it ran (status $how; it goes again once)" -Retried }
                        elseif ($f) { D-Finish 'failed' $(if ($script:active.req.kind -eq 'send') { 4 } else { 1 }) "the game went away while running it (status $how$(if ($how -eq 'crashed') { ': a crash, see logs/Player.log' })); the logs are in the results folder" $null }
                        else { D-Requeue "the game ended before it was fed (status $how)" }
                    } elseif (-not $script:sess.readyAt -and -not $script:sess.quitAt -and -not (Test-Path -LiteralPath (Join-Path $script:sess.dir 'host.json'))) {
                        # the host stopped before it launched the game: another game or runner (waited for), or the build
                        # could not be installed (a request that fails 3 times leaves the line)
                        $why = D-HostStop $script:sess.id
                        $fromOutside = $false
                        if ($why -match 'Cities2\.exe|a game is running|launcher is open|holds the suite lock|did not quit') { D-Log "the game host for $($script:sess.id) stopped before launching the game: $why - waiting" }
                        else {
                            $k = $script:sess.key
                            $script:startFailures[$k] = 1 + [int]$script:startFailures[$k]
                            D-Log "the game host for $($script:sess.id) stopped before launching the game: $why (attempt $($script:startFailures[$k]) of 3)"
                            if ($script:startFailures[$k] -ge 3) {
                                D-FailBuild $queued $k '' "the game could not be started for this build 3 times: $why (see $($script:Q.Logs)\$($script:sess.id).host.txt)"
                                $script:startFailures.Remove($k)
                            }
                        }
                        $script:hostBackoff = $now.AddSeconds(15); $script:backoffNote = 'waiting a moment before starting the game again'
                    } elseif (-not $script:sess.readyAt -and -not $script:sess.quitAt) {
                        D-CountLaunch "did not boot (status $how)"
                        $k = $script:sess.key
                        if ($fromOutside) {
                            # stopped while it booted: not this build's fault, but not for ever either
                            $script:stoppedBoots[$k] = 1 + [int]$script:stoppedBoots[$k]
                            D-Log "game $($script:sess.id) was stopped from outside while it booted (status $how; $($script:stoppedBoots[$k]) of 3)"
                            if ($script:stoppedBoots[$k] -ge 3) { D-FailBuild $queued $k '' "the queue's game for this build was stopped 3 times while it booted (status $how): see $($script:sess.dir)\host.log"; $script:stoppedBoots.Remove($k) }
                        } else {
                            $script:bootFailures[$k] = 1 + [int]$script:bootFailures[$k]
                            D-Log "game $($script:sess.id) did not boot (status $how; attempt $($script:bootFailures[$k]))"
                            if ($script:bootFailures[$k] -ge 2) {
                                D-FailBuild $queued $k '' "the game did not boot twice for this build (status $how): see $($script:sess.dir)\host.log"
                                $script:bootFailures.Remove($k)
                            }
                        }
                    }
                    if ($hj -and [int]$hj.pid -gt 0) { D-CountLaunch "ended before it was ready (status $how)" }   # it did start (once per game)
                    if ($fromOutside) {
                        $script:userGraceUntil = $now.AddSeconds($UserGraceSec)
                        $script:userGraceNote = "the queue's game was stopped from outside at $($now.ToString('HH:mm:ss')) (status $how): the queue waits $(Q-Duration $UserGraceSec) before launching again (time for the user to start their own game)"
                        D-SaveGrace
                    }
                    $script:sess = $null; D-SaveSession
                    $script:idleSince = Get-Date
                    $state = 'idle'
                    D-RestoreUserBuild "after game $how" -Now | Out-Null
                }
                elseif ($script:active -and $script:active.feed) {
                    $state = 'running'
                    $f = $script:active.feed
                    $resFile = Join-Path $script:sess.dir "inbox\$($f.inbox).result.json"
                    if (Test-Path -LiteralPath $resFile) {
                        Start-Sleep -Milliseconds 300
                        $res = Q-ReadJson $resFile
                        $rows = @($res.scenarios | Where-Object { $_ })
                        $noBlock = @($rows | Where-Object { $_.status -eq 'no-block' })
                        if ($noBlock.Count -gt 0 -and -not $f.launched -and -not $script:active.req.fresh) {
                            D-Requeue "$($noBlock.Count) scenario(s) found no free test block (or no room for their land) in game $($script:sess.id): a fresh launch for it" -Fresh
                        } else {
                            $code = if ($res.error) { 1 } else { 0 }
                            D-Finish 'served' $code $(if ($res.error) { "$($res.error)" } else { '' }) $res
                        }
                    } elseif (-not $f.killed -and ($overdue = D-Overdue $script:active $now)) {
                        $gp = D-GamePid
                        $f.killed = $now.ToString('o'); $f['killedWhy'] = $overdue
                        Q-WriteJson (Join-Path $script:active.dir 'feed.json') $f
                        if ($gp) { D-Log "$($script:active.req.id): ${overdue}: stopping game $($script:sess.id) (PID $gp, started by the queue)"; Stop-Process -Id $gp -Force -ErrorAction SilentlyContinue }
                        else { D-Log "$($script:active.req.id): ${overdue}, and the game's PID cannot be checked: closing it through its inbox"; D-QuitSession 'timeout' }
                    } elseif ($f.killed -and ($now - (Q-Date $f.killed)).TotalSeconds -gt 60) {
                        # it is still there a minute later (the kill did not take, or the quit sits behind the hung file)
                        D-StopGame "$($script:active.req.id) got no result and the game did not stop"
                        $f.killed = $now.ToString('o'); Q-WriteJson (Join-Path $script:active.dir 'feed.json') $f
                    } elseif ($script:userGame) {
                        $script:note = "$($script:userGame) - the request in the queue's game finishes, then the queue's game quits"; $script:paused = $true
                    }
                }
                elseif ($script:sess.quitAt) {
                    $state = 'closing'
                    if (($now - (Q-Date $script:sess.quitAt)).TotalSeconds -gt 240) {
                        D-StopGame "game $($script:sess.id) did not close in 240 s"
                        $script:sess.quitAt = $now.ToString('o'); D-SaveSession
                    }
                    if ($script:userGame) { $script:note = "$($script:userGame) - the queue's game is closing"; $script:paused = $true }
                }
                elseif ($script:userGame) {
                    # the user started a game (or the launcher) while the queue's game is open: it gets out of the way (the
                    # request in it has finished): a ready game quits cleanly, a game still booting is stopped (nothing in
                    # it yet), so the user's build is back before the user's game loads it
                    $state = 'closing'; $script:note = "$($script:userGame) - the queue's game is closing"; $script:paused = $true
                    if ($script:active) { D-Requeue 'the user started their own game before it was fed' }
                    D-QuitSession 'the user started their own game'
                    if (-not $script:sess.readyAt) { D-StopGame 'the user started their own game while the queue''s game was booting' }
                }
                elseif (-not $script:sess.readyAt) { $state = 'starting' }
                elseif ($script:active) {
                    # taken but not fed (adopted without a feed): feed it now
                    $state = 'running'
                    try { D-Feed } catch { D-Log "feeding $($script:active.req.id) failed: $($_.Exception.Message)"; D-Requeue "it could not be fed: $($_.Exception.Message)" }
                }
                else {
                    # ready and idle: the next request for this game, or close it for another build
                    $state = 'ready'
                    $fits = { param($r) ($script:sess.served -eq 0) -or (D-Fits $r) }   # a fresh game takes its first request whatever it needs
                    $next = Q-SelectNext $queued $script:sess.key $script:sess.served $fits 0 ($StarveMinutes * 60) (Q-Rates)
                    if ($next) {
                        $same = $next.kind -ne 'play' -and $next.key -eq $script:sess.key -and -not ($next.fresh -and $script:sess.served -gt 0)
                        # its host stops a game after 8 h (Run-Suite -TimeoutMin): a request that would still run at 7.5 h
                        # gets a fresh game instead of being cut off in the middle
                        $openSec = ($now - (Q-Date $script:sess.startedAt)).TotalSeconds
                        $tooOld = $same -and ($openSec + (Q-WorkSec $next (Q-Rates))) -gt $script:sessMaxSec
                        if ($same -and -not $tooOld -and ($script:sess.served -eq 0 -or (D-Fits $next))) {
                            $a = D-Take $next
                            if ($a) {
                                $script:active = $a
                                try { D-Feed; $state = 'running' }
                                catch {
                                    $m = $_.Exception.Message
                                    $script:feedFailures[$a.req.id] = 1 + [int]$script:feedFailures[$a.req.id]
                                    D-Log "feeding $($a.req.id) failed: $m (attempt $($script:feedFailures[$a.req.id]))"
                                    if ($script:feedFailures[$a.req.id] -ge 2) { D-Finish 'failed' 1 "it could not be fed into the game: $m" $null } else { D-Requeue "it could not be fed: $m" }
                                }
                            }
                        } else {
                            $why = if ($next.kind -eq 'play') { "the user's playground ($($next.who)) is next" } elseif ($tooOld) { "it has been open for $(Q-Duration $openSec) and its host stops a game at 8 h: a fresh game for $($next.who)" } elseif ($next.key -ne $script:sess.key) { "next is build $($next.build.hash) ($($next.who))" } else { "$($next.who) needs a fresh test grid" }
                            D-QuitSession $why
                        }
                    } elseif (($now - $script:idleSince).TotalMinutes -ge $IdleMinutes) {
                        D-QuitSession "nothing in line for $IdleMinutes min"
                    }
                }
            }
        }
        else {
            # no game of ours: first the user's build and settings, if a host could not put them back
            $restored = D-RestoreUserBuild 'no game is open'
            D-RestoreSettings 'no game is open'
            $next = Q-SelectNext $queued $null 0 $null 0 ($StarveMinutes * 60)
            $wait = D-WaitNote $foreign $holder
            $retire = D-RetireWanted $queued
            if ($retire -and -not $script:active -and $restored) {
                D-Log "a client with a newer queue (v$($retire.v), $($retire.by)) is in line: handing over to it"
                break
            }
            if ($next -and $wait) {
                $state = 'waiting'; $script:note = $wait; $script:paused = $true
            } elseif ($next -and $script:hostBackoff -gt $now) {
                $state = 'waiting'; $script:note = $script:backoffNote
            } elseif ($next -and $next.kind -ne 'play' -and $script:userGraceUntil -gt $now) {
                # the user's own Play.ps1 does not wait for this: it is the user asking
                $state = 'waiting'; $script:note = $script:userGraceNote
            } elseif ($next -and $script:active) {
                $state = 'waiting'   # D-ReconcileActive deals with it next round
            } elseif ($next -and $next.kind -eq 'play') {
                $a = D-Take $next
                if ($a) {
                    $script:active = $a
                    $bad = D-StartPlay $a.req
                    if ($bad) { D-Finish 'failed' 1 "the playground could not start: $bad" $null } else { $state = 'playground' }
                }
            } elseif ($next) {
                # a game for the head's build; its requests stay in line and are fed once it is ready
                if (D-StartSession $next $queued) { $state = 'starting' }
            } else {
                $state = 'idle'
                if ($wait) { $script:note = $wait }
                if (MI-Pending) {
                    $script:note = "waiting to put the user's build back into Mods\Placemaker: " + $(if ($wait) { $wait } else { 'a game host is still finishing' })
                } elseif (($now - $script:idleSince).TotalSeconds -ge $ExitGraceSec -and @(Get-ChildItem -LiteralPath $script:Q.Active -Directory -ErrorAction SilentlyContinue).Count -eq 0) {
                    D-Log "the line is empty, no game is open and the user's build is installed ($(MI-StatusLine)): shutting down"
                    break
                }
            }
        }
        D-Status $state $queued
        D-Gc
        D-MarkGood
        Start-Sleep -Milliseconds 1500
        $errors = 0
      } catch {
        $errors++
        D-Log "dispatcher error ($errors in a row): $($_.Exception.Message) at $($_.InvocationInfo.ScriptLineNumber): $($_.InvocationInfo.Line.Trim()) | $($_.ScriptStackTrace -replace "`r?`n", " < ")"
        if ($errors -ge 20) { throw }
        Start-Sleep -Seconds 3
      }
    }
}
catch { D-Log "dispatcher error: $($_.Exception.Message) at $($_.InvocationInfo.ScriptLineNumber): $($_.InvocationInfo.Line.Trim()) | $($_.ScriptStackTrace -replace "`r?`n", " < ")"; throw }
finally {
    if (-not $script:sess) { try { D-RestoreUserBuild 'the dispatcher stops' -Now | Out-Null } catch { } }
    try { D-RestoreSettings 'the dispatcher stops' -Now } catch { }
    try { $st = Q-ReadJson $script:Q.Status; if ($st) { $st.state = 'stopped'; $st.updated = (Get-Date).ToString('o'); Q-WriteJson $script:Q.Status $st } } catch { }
    D-Log "dispatcher down$(if (MI-Pending) { " (the user's build is NOT back yet: $(MI-StatusLine); the next dispatcher puts it back)" })"
    # marked as a clean shutdown (the next dispatcher does not report a crash), then released
    try { $b = [Text.Encoding]::UTF8.GetBytes((([ordered]@{ pid = $PID; start = (Q-ProcStart $PID); v = $script:QVersion; down = (Get-Date).ToString('o') }) | ConvertTo-Json -Compress)); $script:lockStream.SetLength(0); $script:lockStream.Write($b, 0, $b.Length); $script:lockStream.Flush() } catch { }
    try { $script:lockStream.Dispose() } catch { }   # the file stays: a lock nobody holds is free
}
