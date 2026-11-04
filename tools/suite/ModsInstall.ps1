<#
  Mods\Placemaker for the game queue. Dot-sourced by GameQueue.ps1 (the dispatcher) and by Run-Suite.ps1 -DeployFrom
  (the queue's game host).

  The rule: whenever no queue game runs, Mods\Placemaker holds the USER'S build.
  * The host swaps a request's frozen build in right before it launches the game (after it holds the suite lock and has
    checked that no other game runs) and swaps the user's build back the moment that game has exited.
  * Nothing is written while any Cities2.exe runs: a restore that finds a game open stays pending (install.json) and the
    dispatcher finishes it as soon as no game runs (it does not exit before).
  * Every change is a verified copy into <queue>\staging (same volume as Mods\, but not under it, so the game never loads
    it as a second mod) and then two renames, under <queue>\install.lock. A change cut off half way is finished by the
    next reconcile from what install.json says.
  * <queue>\user-build\ (mod\ + manifest.json) is the copy of the user's build; it is only ever replaced by a newer,
    verified copy, and the one it replaces is kept as <queue>\user-build.prev\.
  * A build someone installed on purpose while the queue was not using the folder (dotnet build -c Release, a -Direct
    run, the user's playground) becomes the user's build: it is what gets put back from then on. Only a folder nobody
    has written for 10 s is taken (a deploy from a checkout without Deploy-Mod.ps1 copies file by file).
  * The csproj's Deploy (dotnet build -c Release) goes through MI-DeployOnPurpose (Deploy-Mod.ps1): under install.lock,
    refused while a game runs or while a queue game owns the folder, staged and swapped in like the queue's own installs.
#>

$script:MIRoot = if ($env:PLACEMAKER_GAME_QUEUE) { $env:PLACEMAKER_GAME_QUEUE } else { Join-Path $env:USERPROFILE '.placemaker\game-queue' }
# tests: a stand-in Mods\Placemaker (only with a stand-in queue root, so a test can never touch the real folder)
$script:MITest = [bool]($env:PLACEMAKER_GAME_QUEUE -and $env:PLACEMAKER_GAME_QUEUE_MODS)
# a stand-in queue root without a stand-in Mods folder would keep a second install.json over the real Mods\Placemaker
$script:MIRefuse = if ($env:PLACEMAKER_GAME_QUEUE -and -not $env:PLACEMAKER_GAME_QUEUE_MODS) { 'PLACEMAKER_GAME_QUEUE is set without PLACEMAKER_GAME_QUEUE_MODS: a stand-in queue never touches the real Mods\Placemaker' } else { '' }
# tests: a stand-in runs folder, only together with a stand-in queue root (a variable left in a shell never moves the
# real queue's suite lock)
$script:MIRuns = if ($env:PLACEMAKER_GAME_QUEUE -and $env:PLACEMAKER_GAME_QUEUE_RUNS) { $env:PLACEMAKER_GAME_QUEUE_RUNS } else { 'C:\Users\jack\cs2-area-tool\.forensics\suite\runs' }
# The version of this install state machine, written into install.json: a copy older than the one that last wrote it
# refuses to touch Mods\Placemaker (two versions never take turns on the same state).
# 2: install.json carries 'mi'; unreadable state files stop every change instead of reading as missing.
$script:MIVersion = 2
$script:MI = @{
    Root = $script:MIRoot
    Mods = $(if ($script:MITest) { $env:PLACEMAKER_GAME_QUEUE_MODS } else { Join-Path $env:USERPROFILE 'AppData\LocalLow\Colossal Order\Cities Skylines II\Mods\Placemaker' })
    User = Join-Path $script:MIRoot 'user-build'
    Prev = Join-Path $script:MIRoot 'user-build.prev'
    State = Join-Path $script:MIRoot 'install.json'
    Lock = Join-Path $script:MIRoot 'install.lock'
    Stage = Join-Path $script:MIRoot 'staging'
    Log = Join-Path $script:MIRoot 'dispatcher.log'
    SuiteLock = Join-Path (Split-Path $script:MIRuns) 'lock'   # Run-Suite's lock (one for every worktree)
}
# Seconds a folder must have gone unwritten before a build found there is taken as installed on purpose.
$script:MIQuietSec = 10

# ReShade (dxgi.dll in the game's folder, with its DLSS 5 addon) hangs the GPU in the runner's test games (their window
# changes size after launch: DXGI device removed, the boot times out). Every runner launch switches it off (dxgi.dll
# renamed, reshade-off.txt remembers it); it comes back the moment no game runs (MI-ReshadeBack: the host's restore, the
# dispatcher's reconcile, also after a cut-off run), so the user's own game always starts with it.
$script:MIGameDir = 'C:\Program Files (x86)\Steam\steamapps\common\Cities Skylines II'
$script:MIReshadeOffName = 'dxgi.dll.off-for-placemaker-tests'
$script:MIReshadeMark = Join-Path $script:MIRoot 'reshade-off.txt'

function MI-ReshadeOff {
    if ($script:MITest) { return }
    $on = Join-Path $script:MIGameDir 'dxgi.dll'
    $off = Join-Path $script:MIGameDir $script:MIReshadeOffName
    if (-not (Test-Path -LiteralPath $on)) { return }
    if (Test-Path -LiteralPath $off) { MI-Log "ReShade left on: both dxgi.dll and $($script:MIReshadeOffName) are there (nothing renamed)"; return }
    try {
        Rename-Item -LiteralPath $on -NewName $script:MIReshadeOffName -ErrorAction Stop
        New-Item -ItemType Directory -Force -Path (Split-Path $script:MIReshadeMark) | Out-Null
        Set-Content -LiteralPath $script:MIReshadeMark -Value (Get-Date).ToString('o') -Encoding ASCII
        MI-Log "ReShade off for the runner's game (dxgi.dll renamed to $($script:MIReshadeOffName))"
    } catch { MI-Log "could not switch ReShade off: $($_.Exception.Message)" }
}

function MI-ReshadeBack([string]$why) {
    if ($script:MITest -or -not (Test-Path -LiteralPath $script:MIReshadeMark)) { return }
    if (@(MI-Games).Count -gt 0) { return }   # never under a running game: once it has closed
    $on = Join-Path $script:MIGameDir 'dxgi.dll'
    $off = Join-Path $script:MIGameDir $script:MIReshadeOffName
    if ((Test-Path -LiteralPath $off) -and -not (Test-Path -LiteralPath $on)) {
        try { Rename-Item -LiteralPath $off -NewName 'dxgi.dll' -ErrorAction Stop }
        catch { MI-Log "could not switch ReShade back on ($why): $($_.Exception.Message)"; return }
    }
    Remove-Item -LiteralPath $script:MIReshadeMark -Force -ErrorAction SilentlyContinue
    MI-Log "ReShade back on ($why)"
}

function MI-Log([string]$m) {
    $line = '{0:yyyy-MM-dd HH:mm:ss} [{1}] install: {2}' -f (Get-Date), $PID, $m
    for ($i = 0; $i -lt 5; $i++) { try { [IO.File]::AppendAllText($script:MI.Log, $line + "`r`n", (New-Object Text.UTF8Encoding $false)); break } catch { Start-Sleep -Milliseconds 40 } }
}

# Every Cities2.exe on the machine (the queue's own included: nothing is written while any game runs).
function MI-Games {
    if ($script:MITest) {
        return @(foreach ($f in 'fake-user-game', 'fake-game-running') { if (Test-Path -LiteralPath (Join-Path $script:MI.Root $f)) { 0 } })
    }
    return @(Get-Process -Name 'Cities2' -ErrorAction SilentlyContinue | ForEach-Object { $_.Id })
}
# The Paradox launcher of Cities: Skylines II: the user is starting the game. The queue installs and launches nothing
# meanwhile (a restore of the user's build still goes ahead: it is what their game should load). Only CS2's counts:
# Crusader Kings III, Europa Universalis IV, Hearts of Iron IV, Stellaris and Victoria 3 ship a dowser.exe of their own,
# and all of them start the one shared 'Paradox Launcher.exe'. So: dowser.exe only from the game's folder
# (...\Cities Skylines II\Launcher\dowser.exe), and the shared launcher (and its bootstrapper) only when its command line
# names that folder (--pdxlGameDir ...\Cities Skylines II\Launcher, as the launcher's own log shows for every launch).
# A process whose path and command line cannot be read counts (the user first). The launcher closes itself a few seconds
# after it starts the game, so one open for over $MILauncherMaxMin minutes without a game is no longer waited for.
$script:MILauncherMaxMin = 60
$script:MILauncherCache = @{}
function MI-IsCs2Launcher($p) {
    $k = "$($p.Id)/$(try { $p.StartTime.Ticks } catch { 0 })"
    if ($script:MILauncherCache.ContainsKey($k)) { return $script:MILauncherCache[$k] }
    $where = ''
    if ($p.ProcessName -eq 'dowser') { try { $where = "$($p.Path)" } catch { } }
    if (-not $where) {
        try { $c = Get-CimInstance Win32_Process -Filter "ProcessId=$($p.Id)" -ErrorAction Stop; $where = "$($c.ExecutablePath) $($c.CommandLine)".Trim() } catch { }
    }
    $cs2 = (-not $where) -or ($where -match 'Cities Skylines II')
    if ($script:MILauncherCache.Count -gt 200) { $script:MILauncherCache.Clear() }
    $script:MILauncherCache[$k] = $cs2
    return $cs2
}
function MI-Launchers {
    if ($script:MITest) { return @(if (Test-Path -LiteralPath (Join-Path $script:MI.Root 'fake-launcher')) { 0 }) }
    $r = @()
    foreach ($p in @(Get-Process -Name 'dowser', 'Paradox Launcher', 'bootstrapper-v2' -ErrorAction SilentlyContinue)) {
        if (-not (MI-IsCs2Launcher $p)) { continue }
        try { if (((Get-Date) - $p.StartTime).TotalMinutes -gt $script:MILauncherMaxMin) { continue } } catch { }
        $r += $p.Id
    }
    return $r
}

function MI-Hash([string]$s) {
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($s))) -replace '-', '').ToLowerInvariant() } finally { $sha.Dispose() }
}

# What a folder holds: a hash over every file (path, size, content), and its Placemaker.dll.
function MI-FolderInfo([string]$dir) {
    $r = [ordered]@{ exists = $false; hash = ''; dll = ''; dllTime = ''; files = 0; bytes = [int64]0 }
    if (-not (Test-Path -LiteralPath $dir -PathType Container)) { return $r }
    $r.exists = $true
    $root = (Get-Item -LiteralPath $dir -Force).FullName.TrimEnd('\')
    $items = @{}
    foreach ($f in Get-ChildItem -LiteralPath $dir -Recurse -File -Force) { $items[$f.FullName.Substring($root.Length + 1).ToLowerInvariant()] = $f }
    $keys = [string[]]@($items.Keys)
    [Array]::Sort($keys, [StringComparer]::Ordinal)
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        $lines = foreach ($rel in $keys) {
            $f = $items[$rel]
            $h = MI-FileSha $sha $f.FullName
            $r.files++; $r.bytes += $f.Length
            if ($rel -eq 'placemaker.dll') { $r.dll = $h.Substring(0, [math]::Min(8, $h.Length)).ToLowerInvariant(); $r.dllTime = $f.LastWriteTime.ToString('o') }
            "${rel}:$($f.Length):$h"
        }
    } finally { $sha.Dispose() }
    $r.hash = if ($r.files -gt 0) { (MI-Hash (@($lines) -join "`n")).Substring(0, 16) } else { 'empty' }
    return $r
}
# SHA-256 of a file (read with every share mode: the game may hold it), upper-case hex like Get-FileHash, and fast
# (a cmdlet per file made hashing a 634-file build take seconds).
function MI-FileSha($sha, [string]$path) {
    try {
        $fs = [IO.File]::Open($path, 'Open', 'Read', 'ReadWrite, Delete')
        try { return ([BitConverter]::ToString($sha.ComputeHash($fs)) -replace '-', '') } finally { $fs.Dispose() }
    } catch { return 'unreadable' }
}
# The first 8 hex of Placemaker.dll's SHA-256 ('' when there is none).
function MI-DllHash([string]$dir) {
    $p = Join-Path $dir 'Placemaker.dll'
    if (-not (Test-Path -LiteralPath $p)) { return '' }
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return (MI-FileSha $sha $p).Substring(0, 8).ToLowerInvariant() } finally { $sha.Dispose() }
}

# Read with every share mode, so a writer can swap the file in (File.Replace) while it is being read.
function MI-ReadJson([string]$p) {
    if (-not (Test-Path -LiteralPath $p)) { return $null }
    for ($i = 0; $i -lt 3; $i++) {
        try {
            $fs = [IO.File]::Open($p, 'Open', 'Read', 'ReadWrite, Delete')
            try { $text = (New-Object IO.StreamReader($fs, [Text.Encoding]::UTF8)).ReadToEnd() } finally { $fs.Dispose() }
            return ($text | ConvertFrom-Json)
        } catch { Start-Sleep -Milliseconds 50 }
    }
    return $null
}
# The same, for the install state read under install.lock: a file that is there but cannot be read (an antivirus or a
# backup tool holding it) throws, so it is never taken for a missing one (a queue build saved as the user's).
function MI-ReadJsonStrict([string]$p) {
    if (-not (Test-Path -LiteralPath $p)) { return $null }
    $err = ''
    for ($i = 0; $i -lt 10; $i++) {
        try {
            $fs = [IO.File]::Open($p, 'Open', 'Read', 'ReadWrite, Delete')
            try { $text = (New-Object IO.StreamReader($fs, [Text.Encoding]::UTF8)).ReadToEnd() } finally { $fs.Dispose() }
            $o = $text | ConvertFrom-Json
            if ($null -ne $o) { return $o }
            $err = 'it is empty'
        } catch { $err = $_.Exception.Message }
        if (-not (Test-Path -LiteralPath $p)) { return $null }
        Start-Sleep -Milliseconds 200
    }
    throw "$p is there but cannot be read ($err): nothing is changed until it can"
}

# Seconds since anything in $dir was created or written (files and folders); a large number when it does not exist.
function MI-QuietFor([string]$dir) {
    if (-not (Test-Path -LiteralPath $dir)) { return 1e9 }
    $d = Get-Item -LiteralPath $dir -Force
    $latest = if ($d.LastWriteTimeUtc -gt $d.CreationTimeUtc) { $d.LastWriteTimeUtc } else { $d.CreationTimeUtc }
    foreach ($f in Get-ChildItem -LiteralPath $dir -Recurse -Force -ErrorAction SilentlyContinue) {
        if ($f.LastWriteTimeUtc -gt $latest) { $latest = $f.LastWriteTimeUtc }
        if ($f.CreationTimeUtc -gt $latest) { $latest = $f.CreationTimeUtc }
    }
    return [math]::Max(0, ([datetime]::UtcNow - $latest).TotalSeconds)
}

# The Run-Suite holding the suite lock, when that process is alive (PID and start time); $null otherwise.
function MI-SuiteLockHolder {
    if ($script:MITest) { $f = Join-Path $script:MI.Root 'fake-suite-lock'; if (Test-Path -LiteralPath $f) { return [pscustomobject]@{ pid = 0; runId = 'fake' } }; return $null }
    $l = MI-ReadJson $script:MI.SuiteLock
    if (-not $l -or -not $l.pid) { return $null }
    $p = Get-Process -Id ([int]$l.pid) -ErrorAction SilentlyContinue
    if (-not $p) { return $null }
    if ($l.procStart) { try { if ([math]::Abs($p.StartTime.ToUniversalTime().Ticks - [int64]$l.procStart) -gt 20000000) { return $null } } catch { } }
    return $l
}
function MI-WriteJson([string]$p, $o) {
    $tmp = "$p.$PID.tmp"
    [IO.File]::WriteAllText($tmp, ($o | ConvertTo-Json -Depth 6), (New-Object Text.UTF8Encoding $false))
    for ($i = 0; $i -lt 20; $i++) {
        try { if (Test-Path -LiteralPath $p) { [IO.File]::Replace($tmp, $p, [NullString]::Value) } else { [IO.File]::Move($tmp, $p) }; return } catch { Start-Sleep -Milliseconds 50 }
    }
    throw "could not write $p"
}

# -Strict (under install.lock): an install.json that is there but cannot be read throws. Without it (a look from
# outside the lock), such a file reads as 'unreadable', which counts as pending (a reconcile under the lock follows).
function MI-State([switch]$Strict) {
    $s = if ($Strict) { MI-ReadJsonStrict $script:MI.State } else { MI-ReadJson $script:MI.State }
    if (-not $s -and -not $Strict -and (Test-Path -LiteralPath $script:MI.State)) { return [pscustomobject]@{ state = 'unreadable'; queueHash = ''; queueDll = ''; label = ''; by = ''; at = '' } }
    if (-not $s -or -not $s.state) { return [pscustomobject]@{ state = 'user'; queueHash = ''; queueDll = ''; label = ''; by = ''; at = '' } }
    return $s
}
function MI-SetState([string]$state, [hashtable]$extra = @{}) {
    $o = [ordered]@{ state = $state; mi = $script:MIVersion; queueHash = ''; queueDll = ''; label = ''; by = "$PID"; at = (Get-Date).ToString('o') }
    foreach ($k in $extra.Keys) { $o[$k] = $extra[$k] }
    MI-WriteJson $script:MI.State $o
}

# One holder at a time (the dispatcher and a host may both want the folder); a holder that dies releases it.
function MI-WithLock([scriptblock]$block) {
    if ($script:MIRefuse) { throw $script:MIRefuse }
    New-Item -ItemType Directory -Force -Path $script:MI.Root | Out-Null
    $fs = $null
    for ($i = 0; $i -lt 600 -and -not $fs; $i++) {
        try { $fs = [IO.File]::Open($script:MI.Lock, 'OpenOrCreate', 'ReadWrite', 'None') } catch { Start-Sleep -Milliseconds 200 }
    }
    if (-not $fs) { throw 'install.lock: another process has held it for 2 min' }
    try {
        # a newer install state machine wrote the state last: this (older) copy changes nothing
        $cur = MI-ReadJsonStrict $script:MI.State
        if ($cur -and [int]$cur.mi -gt $script:MIVersion) { throw "install.json was written by a newer ModsInstall.ps1 (v$([int]$cur.mi); this one, in $PSScriptRoot, is v$($script:MIVersion)): it does not touch Mods\Placemaker. Merge master into that worktree" }
        return (& $block)
    } finally { $fs.Dispose() }
}

function MI-Move([string]$from, [string]$to) {
    for ($i = 0; ; $i++) {
        try { [IO.Directory]::Move($from, $to); return }
        catch { if ($i -ge 9 -or -not (Test-Path -LiteralPath $from)) { throw }; Start-Sleep -Milliseconds 300 }
    }
}
function MI-Remove([string]$dir) {
    for ($i = 0; $i -lt 5 -and (Test-Path -LiteralPath $dir); $i++) { try { Remove-Item -LiteralPath $dir -Recurse -Force -ErrorAction Stop } catch { Start-Sleep -Milliseconds 300 } }
    return -not (Test-Path -LiteralPath $dir)
}

# A verified copy of $src in a new staging folder (outside Mods\): its hash must be $expect.
function MI-Stage([string]$src, [string]$expect) {
    New-Item -ItemType Directory -Force -Path $script:MI.Stage | Out-Null
    $dst = Join-Path $script:MI.Stage ('in-' + [guid]::NewGuid().ToString('N').Substring(0, 12))
    New-Item -ItemType Directory -Force -Path $dst | Out-Null
    try {
        foreach ($c in Get-ChildItem -LiteralPath $src -Force) { Copy-Item -LiteralPath $c.FullName -Destination $dst -Recurse -Force -ErrorAction Stop }
        $got = (MI-FolderInfo $dst).hash
        if ($got -ne $expect) { throw "the copy of $src differs from what it should be ($got, not $expect)" }
        return $dst
    } catch { MI-Remove $dst | Out-Null; throw }
}

# Mods\Placemaker <- $stage by two renames; returns the folder that was there before (moved into staging), or $null.
function MI-SwapIn([string]$stage) {
    $mods = $script:MI.Mods
    $g = @(MI-Games)
    if ($g.Count -gt 0) { throw "a game is running (Cities2.exe PID $($g -join ', ')): Mods\Placemaker is not touched while a game runs" }
    New-Item -ItemType Directory -Force -Path (Split-Path $mods) | Out-Null
    $out = $null
    if (Test-Path -LiteralPath $mods) { $out = Join-Path $script:MI.Stage ('out-' + [guid]::NewGuid().ToString('N').Substring(0, 12)); MI-Move $mods $out }
    try { MI-Move $stage $mods } catch { if ($out) { try { MI-Move $out $mods } catch { MI-Log "could not move $out back to Mods\Placemaker: $($_.Exception.Message)" } }; throw }
    return $out
}

function MI-ReadUser {
    $m = MI-ReadJsonStrict (Join-Path $script:MI.User 'manifest.json')   # under install.lock: unreadable is not missing
    if ($m) { return $m }
    if (-not (Test-Path -LiteralPath $script:MI.User)) {
        # a replacement cut off between its two renames: the previous copy is still in staging
        $old = Get-ChildItem -LiteralPath $script:MI.Stage -Directory -Filter 'user-old-*' -ErrorAction SilentlyContinue | Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName 'manifest.json') } | Sort-Object LastWriteTime -Descending | Select-Object -First 1
        if ($old) { MI-Move $old.FullName $script:MI.User; MI-Log 'recovered the copy of the user''s build from an interrupted replacement'; return (MI-ReadJsonStrict (Join-Path $script:MI.User 'manifest.json')) }
    }
    return $null
}

# Makes the folder $mod (a verified copy, moved, not copied again) or "nothing installed" the user's build.
function MI-SetUser($info, [string]$mod, [string]$why) {
    New-Item -ItemType Directory -Force -Path $script:MI.Stage | Out-Null
    $new = Join-Path $script:MI.Stage ('user-new-' + [guid]::NewGuid().ToString('N').Substring(0, 12))
    New-Item -ItemType Directory -Force -Path $new | Out-Null
    if ($mod) { MI-Move $mod (Join-Path $new 'mod') }
    MI-WriteJson (Join-Path $new 'manifest.json') ([ordered]@{ had = [bool]$info.exists; hash = $info.hash; dll = $info.dll; dllTime = $info.dllTime; files = $info.files; bytes = $info.bytes; at = (Get-Date).ToString('o'); why = $why })
    $old = $null
    if (Test-Path -LiteralPath $script:MI.User) { $old = Join-Path $script:MI.Stage ('user-old-' + [guid]::NewGuid().ToString('N').Substring(0, 12)); MI-Move $script:MI.User $old }
    MI-Move $new $script:MI.User
    if ($old) {
        # the one it replaces is kept (user-build.prev): a wrong adoption never loses the user's build for good
        if (Test-Path -LiteralPath $script:MI.Prev) { $gone = Join-Path $script:MI.Stage ('prev-gone-' + [guid]::NewGuid().ToString('N').Substring(0, 12)); MI-Move $script:MI.Prev $gone; MI-Remove $gone | Out-Null }
        MI-Move $old $script:MI.Prev
    }
}

# Copies what Mods\Placemaker holds now into user-build (verified against $cur, taken again if the folder changes meanwhile).
function MI-Snapshot($cur, [string]$why) {
    for ($try = 0; $try -lt 3; $try++) {
        if (-not $cur.exists) { MI-SetUser $cur '' $why; return }
        try { $copy = MI-Stage $script:MI.Mods $cur.hash; MI-SetUser $cur $copy $why; return }
        catch { if ($try -ge 2) { throw }; Start-Sleep -Seconds 1; $cur = MI-FolderInfo $script:MI.Mods }
    }
}

function MI-Describe($i) { if (-not $i -or -not ($i.had -or $i.exists)) { return 'nothing installed' }; return "Placemaker.dll $($i.dll) of $(if ($i.dllTime) { ([datetime]$i.dllTime).ToString('yyyy-MM-dd HH:mm') })" }

# A verified copy of the user's build waiting in staging (prepared while the queue's game runs), so that putting it
# back when the game exits is two renames: the moment Mods\Placemaker holds the queue's build without a game is short.
function MI-ReadyDir($ub) { return (Join-Path $script:MI.Stage ('ready-' + $ub.hash)) }
function MI-PrepareRestore {
    try {
        MI-WithLock {
            $ub = MI-ReadUser
            if (-not $ub -or -not $ub.had) { return }
            $ready = MI-ReadyDir $ub
            if ((Test-Path -LiteralPath $ready) -and (Test-Path -LiteralPath "$ready.ok")) { return }
            MI-Remove $ready | Out-Null
            $s = MI-Stage (Join-Path $script:MI.User 'mod') $ub.hash
            MI-Move $s $ready
            [IO.File]::WriteAllText("$ready.ok", $ub.hash)
        } | Out-Null
    } catch { MI-Log "could not prepare the user's build for a quick restore: $($_.Exception.Message)" }
}

# Puts the user's build back from user-build (a verified copy, swapped in).
function MI-PutBackUser($ub) {
    if ($ub.had) {
        $ready = MI-ReadyDir $ub
        $usedReady = (Test-Path -LiteralPath $ready) -and (Test-Path -LiteralPath "$ready.ok") -and "$([IO.File]::ReadAllText("$ready.ok"))".Trim() -eq $ub.hash
        $stage = if ($usedReady) { $ready } else { MI-Stage (Join-Path $script:MI.User 'mod') $ub.hash }
        $out = MI-SwapIn $stage
        if ($usedReady) { Remove-Item -LiteralPath "$ready.ok" -Force -ErrorAction SilentlyContinue }
    } else {
        $g = @(MI-Games); if ($g.Count -gt 0) { throw 'a game is running' }
        $out = $null
        if (Test-Path -LiteralPath $script:MI.Mods) { $out = Join-Path $script:MI.Stage ('out-' + [guid]::NewGuid().ToString('N').Substring(0, 12)); MI-Move $script:MI.Mods $out }
    }
    if ($out) { MI-Remove $out | Out-Null }
}

# Brings Mods\Placemaker back to the user's build when the queue left (or half left) one of its own there, and takes a
# build someone installed on purpose as the user's. Call with install.lock held (MI-Reconcile takes it).
function MI-ReconcileLocked([string]$why) {
    $g = @(MI-Games)
    $st = MI-State -Strict
    if ($g.Count -gt 0) {
        if ($st.state -ne 'user') { return @{ ok = $false; pending = $true; message = "a game is running (Cities2.exe PID $($g -join ', ')): the user's build goes back when it has closed" } }
        return @{ ok = $true; pending = $false; message = '' }
    }
    MI-ReshadeBack $why
    $ub = MI-ReadUser
    # the usual case, fast (the moment a queue game exits): the queue's own build is there, the user's goes back
    if ($ub -and $st.state -eq 'queue' -and $st.queueDll -and (MI-DllHash $script:MI.Mods) -eq $st.queueDll) {
        MI-SetState 'restoring' @{ queueHash = "$($st.queueHash)"; queueDll = "$($st.queueDll)"; label = "$($st.label)" }
        MI-PutBackUser $ub
        MI-SetState 'user'
        $m = "put back the user's build ($(MI-Describe $ub); $why)"; MI-Log $m
        return @{ ok = $true; pending = $false; message = $m; changed = $true }
    }
    # leftovers of an operation that was cut off (nothing is in progress while the lock is held)
    $keepReady = if ($ub) { Split-Path (MI-ReadyDir $ub) -Leaf } else { '' }
    foreach ($d in Get-ChildItem -LiteralPath $script:MI.Stage -Directory -ErrorAction SilentlyContinue | Where-Object { ($ub -or $_.Name -notlike 'user-old-*') -and $_.Name -ne $keepReady -and $_.LastWriteTime -lt (Get-Date).AddMinutes(-10) }) { MI-Remove $d.FullName | Out-Null; Remove-Item -LiteralPath "$($d.FullName).ok" -Force -ErrorAction SilentlyContinue }
    $cur = MI-FolderInfo $script:MI.Mods
    # a build being copied in right now (a deploy that copies file by file) is never taken half way: wait until it is quiet
    $busy = @{ ok = $false; pending = $true; busy = $true; message = "Mods\Placemaker is being written right now (a build installed outside the queue): waiting until nothing has been written for $($script:MIQuietSec) s" }
    if (-not $ub) {
        if ($cur.exists -and (MI-QuietFor $script:MI.Mods) -lt $script:MIQuietSec) { return $busy }
        if ($st.state -ne 'user') { MI-Log "no copy of the user's build to put back ($why): keeping what Mods\Placemaker holds ($(MI-Describe $cur)) as the user's" }
        MI-Snapshot $cur "first use ($why)"
        MI-SetState 'user'
        $m = "kept the installed build as the user's: $(MI-Describe $cur)"; MI-Log $m
        return @{ ok = $true; pending = $false; message = $m; changed = $false }
    }
    if ($st.state -eq 'user') {
        if ($cur.hash -eq $ub.hash) { return @{ ok = $true; pending = $false; message = ''; changed = $false } }
        if ((MI-QuietFor $script:MI.Mods) -lt $script:MIQuietSec) { return $busy }
        MI-Snapshot $cur "installed on purpose ($why)"
        $m = "a build was installed outside the queue ($(MI-Describe $cur)): it is the user's build from now on (was $(MI-Describe $ub))"; MI-Log $m
        return @{ ok = $true; pending = $false; message = $m; changed = $false }
    }
    # deploying | queue | restoring: the queue had (or was putting) a build of its own there
    if ($cur.hash -eq $ub.hash) { MI-SetState 'user'; return @{ ok = $true; pending = $false; message = ''; changed = $false } }
    # the queue's build was there and someone else is writing over it right now: let them finish first
    if ($st.state -eq 'queue' -and (MI-QuietFor $script:MI.Mods) -lt $script:MIQuietSec) { return $busy }
    if (-not $cur.exists -or -not $cur.dll -or $cur.dll -eq $st.queueDll -or $cur.dll -eq $ub.dll -or $st.state -ne 'queue') {
        MI-SetState 'restoring' @{ queueHash = "$($st.queueHash)"; queueDll = "$($st.queueDll)"; label = "$($st.label)" }
        MI-PutBackUser $ub
        MI-SetState 'user'
        $m = "put back the user's build ($(MI-Describe $ub); $why)"; MI-Log $m
        return @{ ok = $true; pending = $false; message = $m; changed = $true }
    }
    # another Placemaker.dll than the queue's: installed on purpose after the queue's game had closed
    if ((MI-QuietFor $script:MI.Mods) -lt $script:MIQuietSec) { return $busy }
    MI-Snapshot $cur "installed on purpose over the queue's build ($why)"
    MI-SetState 'user'
    $m = "a build was installed outside the queue over the queue's ($(MI-Describe $cur)): it is the user's build from now on"; MI-Log $m
    return @{ ok = $true; pending = $false; message = $m; changed = $false }
}

function MI-Reconcile([string]$why) {
    try { return (MI-WithLock { MI-ReconcileLocked $why }) }
    catch { $m = "could not put the user's build back ($why): $($_.Exception.Message)"; MI-Log $m; return @{ ok = $false; pending = $true; message = $m } }
}

# True when Mods\Placemaker may hold a queue build, or ReShade is still off for a runner's game (a restore is due once no
# game runs).
function MI-Pending { return ((MI-State).state -ne 'user') -or (-not $script:MITest -and (Test-Path -LiteralPath $script:MIReshadeMark)) }

# Installs the frozen build in $src (a bundle's mod\) for a queue game: the user's build is made current first, then the
# frozen build is copied to staging, verified and swapped in. Returns @{ ok; message; installed = folder info }.
function MI-Install([string]$src, [string]$label, [string]$by) {
    if (-not (Test-Path -LiteralPath (Join-Path $src 'Placemaker.dll'))) { return @{ ok = $false; message = "the frozen build $src has no Placemaker.dll" } }
    try {
        $srcInfo = MI-FolderInfo $src
        # a frozen build is installed only as it was frozen (bundle.json next to mod\ records its folder hash)
        $bj = MI-ReadJson (Join-Path (Split-Path $src) 'bundle.json')
        if ($bj -and $bj.modHash -and $bj.modHash -ne $srcInfo.hash) { return @{ ok = $false; message = "the frozen build $src is damaged (its files hash to $($srcInfo.hash), it was frozen as $($bj.modHash)); run the command again" } }
        $l = @(MI-Launchers)
        if ($l.Count -gt 0) { return @{ ok = $false; message = "the Paradox launcher is open (PID $($l -join ', ')): the user is starting the game; nothing was installed" } }
        return (MI-WithLock {
            $r = MI-ReconcileLocked "before installing $label"
            for ($i = 0; $i -lt 30 -and $r.busy; $i++) { Start-Sleep -Seconds 2; $r = MI-ReconcileLocked "before installing $label" }
            if (-not $r.ok) { return @{ ok = $false; message = $r.message } }
            $ub = MI-ReadUser
            $stage = MI-Stage $src $srcInfo.hash
            MI-SetState 'deploying' @{ queueHash = $srcInfo.hash; queueDll = $srcInfo.dll; label = $label }
            # cut off here: state 'deploying' makes the next reconcile put the user's build back (or find it untouched)
            try { $out = MI-SwapIn $stage }
            catch { MI-Remove $stage | Out-Null; throw }
            MI-SetState 'queue' @{ queueHash = $srcInfo.hash; queueDll = $srcInfo.dll; label = $label }
            if ($out) {
                $o = MI-FolderInfo $out
                if ($ub -and $o.hash -eq $ub.hash) { MI-Remove $out | Out-Null }
                else { MI-SetUser $o $out 'installed outside the queue just before a queue install'; MI-Log "a build installed outside the queue just before this install ($(MI-Describe $o)) is the user's build now" }
            }
            MI-Log "installed $label for $by ($(MI-Describe $srcInfo)); the user's build ($(MI-Describe $ub)) goes back when the game has exited"
            return @{ ok = $true; message = ''; installed = $srcInfo }
        })
    } catch { $m = "could not install ${label}: $($_.Exception.Message)"; MI-Log $m; return @{ ok = $false; message = $m } }
}

# The user's playground: the build they play stays installed afterwards (as a direct Play.ps1 always did).
function MI-AdoptInstalled([string]$why) {
    try {
        return (MI-WithLock {
            $cur = MI-FolderInfo $script:MI.Mods
            MI-Snapshot $cur $why
            MI-SetState 'user'
            $m = "$(MI-Describe $cur) is the user's build from now on ($why)"; MI-Log $m
            return @{ ok = $true; message = $m }
        })
    } catch { $m = "could not keep the playground's build as the user's: $($_.Exception.Message)"; MI-Log $m; return @{ ok = $false; message = $m } }
}

# The csproj's Deploy (dotnet build -c Release; tools\suite\Deploy-Mod.ps1): installs bin\Release + UI\build into
# Mods\Placemaker on purpose, as the user's build. Refused while any game runs, and while a queue game owns the folder
# (a game host holds the suite lock with the queue's build installed); a restore the queue still owes is done first.
# $allowPid: the Run-Suite -Direct whose own build this is (it holds the suite lock itself).
function MI-DeployOnPurpose([string[]]$sources, [int]$allowPid = 0) {
    $script:miSwapped = $false
    try {
        return (MI-WithLock {
            $g = @(MI-Games)
            if ($g.Count -gt 0) { return @{ ok = $false; message = "a game is running (Cities2.exe PID $($g -join ', ')): close it first. Nothing was installed (the build is in bin\Release; the game queue takes it from there: build with -p:Deploy=false)" } }
            $st = MI-State -Strict
            if ($st.state -ne 'user') {
                $h = MI-SuiteLockHolder
                if ($h -and (-not $allowPid -or [int]$h.pid -ne $allowPid)) { return @{ ok = $false; message = "the game queue's game host (run $($h.runId), PID $($h.pid)) has its build $($st.label) in Mods\Placemaker for its game: install when that game has closed (tools\suite\Queue.ps1 shows the line). Nothing was installed" } }
                $r = MI-ReconcileLocked 'before a deploy'
                if (-not $r.ok) { return @{ ok = $false; message = "$($r.message). Nothing was installed" } }
            }
            New-Item -ItemType Directory -Force -Path $script:MI.Stage | Out-Null
            $stage = Join-Path $script:MI.Stage ('deploy-' + [guid]::NewGuid().ToString('N').Substring(0, 12))
            New-Item -ItemType Directory -Force -Path $stage | Out-Null
            try {
                foreach ($s in $sources) { if ($s -and (Test-Path -LiteralPath $s)) { foreach ($c in Get-ChildItem -LiteralPath $s -Force) { Copy-Item -LiteralPath $c.FullName -Destination $stage -Recurse -Force -ErrorAction Stop } } }
                if (-not (Test-Path -LiteralPath (Join-Path $stage 'Placemaker.dll'))) { throw "no Placemaker.dll in $($sources -join ' + ')" }
                $info = MI-FolderInfo $stage
                $out = MI-SwapIn $stage
                $script:miSwapped = $true
            } catch { MI-Remove $stage | Out-Null; throw }
            if ($out) { MI-Remove $out | Out-Null }
            # the user's build from now on: a verified copy of what is installed now
            $copy = MI-Stage $script:MI.Mods $info.hash
            MI-SetUser $info $copy 'installed with dotnet build (Deploy)'
            MI-SetState 'user'
            $m = "installed $(MI-Describe $info) into Mods\Placemaker; it is the user's build from now on"; MI-Log $m
            return @{ ok = $true; message = $m; installed = $info }
        })
    } catch {
        if ($script:miSwapped) { $m = "installed, but its copy as the user's build failed ($($_.Exception.Message)); the game queue takes it as the user's build at its next look"; MI-Log $m; return @{ ok = $true; message = $m } }
        return @{ ok = $false; message = "$($_.Exception.Message). Nothing was installed" }
    }
}

# One line for people: what Mods\Placemaker holds and what is put back.
function MI-StatusLine {
    $st = MI-State; $ub = MI-ReadJson (Join-Path $script:MI.User 'manifest.json')
    $u = if ($ub) { MI-Describe $ub } else { 'not recorded yet' }
    switch ($st.state) {
        'user' { return "Mods\Placemaker: the user's build ($u)" }
        'queue' { return "Mods\Placemaker: the queue's build $($st.label) while its game runs; the user's ($u) goes back when it exits" }
        default { return "Mods\Placemaker: $($st.state) $($st.label); the user's build ($u) goes back as soon as no game runs" }
    }
}
