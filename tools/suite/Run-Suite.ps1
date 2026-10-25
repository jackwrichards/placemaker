<#
.SYNOPSIS
  Runs the Placemaker game runner: one game launch, a new game on Plains reshaped into a test grid, the steps of every
  scenario done like a person, settled pictures in order, the game's logs collected. It judges nothing: read the logs.

.DESCRIPTION
  powershell -File tools/suite/Run-Suite.ps1 -Set core
  powershell -File tools/suite/Run-Suite.ps1 -Set core -Queue          # keep the game open for Send-Suite afterwards
  powershell -File tools/suite/Run-Suite.ps1 -Set queue                # fixture only, then the queue
  powershell -File tools/suite/Run-Suite.ps1 -Set path\to\my-set.json  # any set file
  powershell -File tools/suite/Run-Suite.ps1 -RestoreOnly              # put the user's settings back after a cut-off run

  1. Takes the suite lock (.forensics/suite/lock); refuses while another Run-Suite holds it. If a Cities2.exe is running:
     when it is a suite game this host left behind (its command line has -placemakerSuite), asks it to quit through its
     inbox and waits; any other game is the user's and the run stops (exit 2) — it is never touched.
  2. Builds and deploys this worktree's mod (Mods/Placemaker; it stays installed) and labels the run with the git hash
     and the SHA-256 of the deployed DLL (the game checks it loaded that DLL). A dirty tree's diff is saved in the run.
  3. Saves the user's settings (root *.coc, launcher-settings.json, continue_game.json, ModsSettings/, the top-level
     files of ModsData/Placemaker), switches to the suite profile (window at the shot size, no vsync, muted, full detail
     without LOD cross-fade, no clouds or cloud shadows), arms the run and starts Cities2.exe with only local mods.
  4. Watches the heartbeat; the mod quits through the game's own quit. Only the PID this script started is killed, and
     only when the clean quit did not happen in time or the heartbeat stopped.
  5. Collects the logs, classifies how the game ended (quit / closed-externally / crashed / killed), puts the user's
     settings back, writes the status at the top of summary.md, and prints which Placemaker.dll stays installed.
  Results: .forensics/suite/runs/<runId>/ (logs/, suite.log, summary.md, contact.html, shots/*.png, scenarios/*.json).
#>
param(
    # look | look-one | smoke | core | full | steps | examples | any suite/scenarios/<name>.json or a path to a set file; bootstrap = fixture only; queue = no set
    [string]$Set = 'smoke',
    [string]$RunId = '',
    [switch]$NoBuild,
    [int]$TimeoutMin = 0,
    [int]$MaxScenarios = 0,
    # Only Placemaker loads (-disableModding -disableCodeModding-). -AllMods loads the user's whole playset (slow boot).
    [switch]$AllMods,
    [switch]$KeepGraphics,
    [switch]$NoTerrain,
    [string]$Window = '1600x900',
    [float]$PregrowSeconds = 75,
    [string]$Opt = '',
    [switch]$Queue,
    # Grid size in blocks, e.g. '14x9' (default: sized for the set).
    [string]$Grid = '',
    # Keep the user's Placemaker options (default: Placemaker's defaults are pinned for the run, and recorded).
    [switch]$UserModSettings,
    # suite = readable deterministic look (noon, fixed exposure, no weather/wind/icons); game = the map's own.
    [ValidateSet('suite', 'game')][string]$Look = 'suite',
    [ValidateSet('human', 'settled', 'user')][string]$Pace = 'human',
    [switch]$RestoreOnly,
    [switch]$Playground,
    [switch]$Menu,       # with -Playground: stop at the main menu (no new game, no grid)
    [switch]$DevMode,   # the game's developer mode (-developerMode: debug menus)
    [string]$ResultsRoot = 'C:\Users\jack\cs2-area-tool\.forensics\suite\runs',
    # The game queue (GameQueue.ps1): by default the run waits its turn in the machine-wide line and the queue's game runs
    # it; -Direct launches the game here and now (debugging; refused while any other game is open). -Who names you in line.
    [switch]$Direct,
    [string]$Who = '',
    # The run's label instead of this checkout's git hash (the queue runs a frozen copy of the requester's build).
    [string]$Label = '',
    # The queue's game host: install this frozen build (a bundle's mod\) once the suite lock is held and no other game
    # runs, and put the user's build back the moment the game has exited (ModsInstall.ps1). A playground's build stays.
    [string]$DeployFrom = '',
    # The queue's game host runs from the dispatcher's own checked copy of this script (and of ModsInstall.ps1): only the
    # game's inputs come from the request's frozen build (a bundle's host\: suite\schema.json, suite\scenarios\). The code
    # that saves, switches and restores the user's settings and Mods\Placemaker is never a requester's copy.
    [string]$HostData = ''
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
if (-not $Direct -and -not $RestoreOnly -and ($Queue -or $Set -in @('bootstrap', 'queue'))) {
    Write-Host "STOP: -Queue, -Set queue and -Set bootstrap keep a game of your own open outside the game queue: add -Direct (debugging the runner only). To run scenarios in the queue's game use Send-Suite.ps1 -File."
    exit 2
}
if (-not $Direct -and -not $RestoreOnly) {
    . (Join-Path $PSScriptRoot 'GameQueue.ps1')
    exit (Invoke-QueuedRun $repo @{ Set = $Set; RunId = $RunId; NoBuild = $NoBuild; TimeoutMin = $TimeoutMin; MaxScenarios = $MaxScenarios; AllMods = $AllMods; KeepGraphics = $KeepGraphics
            NoTerrain = $NoTerrain; Window = $Window; PregrowSeconds = $PregrowSeconds; Opt = $Opt; Grid = $Grid; UserModSettings = $UserModSettings; Look = $Look; Pace = $Pace
            Playground = $Playground; ResultsRoot = $ResultsRoot; Who = $Who })
}
if ($HostData) { $repo = (Resolve-Path -LiteralPath $HostData).Path }   # the game's inputs from the frozen build
# Mods\Placemaker's install state machine and the Paradox launcher check: this script's own copy (the dispatcher's, for
# the queue's game host)
. (Join-Path $PSScriptRoot 'ModsInstall.ps1')
$gameDir ='C:\Program Files (x86)\Steam\steamapps\common\Cities Skylines II'
$exe = Join-Path $gameDir 'Cities2.exe'
$ud = Join-Path $env:USERPROFILE 'AppData\LocalLow\Colossal Order\Cities Skylines II'
$suiteData = Join-Path $ud 'ModsData\Placemaker\suite'
$modsData = Join-Path $ud 'ModsData\Placemaker'
$forensics = Split-Path $ResultsRoot
$saved = Join-Path $forensics 'user-settings'
$lockFile = Join-Path $forensics 'lock'
$activeFile = Join-Path $forensics 'active.json'
New-Item -ItemType Directory -Force -Path $ResultsRoot | Out-Null

function Write-NoBom([string]$path, [string]$text) { [System.IO.File]::WriteAllText($path, $text, (New-Object System.Text.UTF8Encoding $false)) }

$script:hostLog = $null
function Say([string]$m) {
    $line = '{0:HH:mm:ss} {1}' -f (Get-Date), $m
    Write-Host $line
    if ($script:hostLog) { try { [IO.File]::AppendAllText($script:hostLog, "$line`r`n", (New-Object Text.UTF8Encoding $false)) } catch { } }
}

# Games that are not this run's: every Cities2.exe but ours, and CS2's Paradox launcher (the user is starting the game;
# MI-Launchers: another Paradox game's launcher does not count). Checked again right before the settings are switched
# and right before the game is started.
$script:ourPid = 0
function Get-OtherGames {
    $r = @()
    foreach ($p in @(Get-Process -Name 'Cities2' -ErrorAction SilentlyContinue)) { if ($p.Id -ne $script:ourPid) { $r += "Cities2.exe PID $($p.Id)" } }
    foreach ($id in @(MI-Launchers)) { $r += "the Paradox launcher of Cities: Skylines II PID $id" }
    return $r
}

# The user's settings and Placemaker data: saved before the run, put back after it (or at the start of the next run).
function Get-SettingsFiles {
    $r = @(Get-ChildItem -Path $ud -File | Where-Object { $_.Extension -eq '.coc' -or $_.Name -in @('launcher-settings.json', 'continue_game.json') })
    $ms = Join-Path $ud 'ModsSettings'
    if (Test-Path $ms) { $r += @(Get-ChildItem -Path $ms -File -Recurse) }
    if (Test-Path $modsData) { $r += @(Get-ChildItem -Path $modsData -File) }   # catalog-dump.json, remembered lists (top level only)
    return $r
}

# Not while a game that is not ours runs (the user's): it would be writing under it, and a game the user just started may
# already have read the suite profile (it saves it back on exit). The restore then stays pending (pending.txt), and the
# game queue's dispatcher does it once no game runs; files the user's game made meanwhile are kept (foreign.txt).
# The run's own one-launch arming (runOnce.txt: the next game launch merges its tokens into its command line, and
# run.json) goes at once, whatever runs: a game the user starts must never pick it up.
function Disarm {
    $ro = Join-Path $ud 'runOnce.txt'; if (Test-Path -LiteralPath $ro) { Remove-Item -LiteralPath $ro -Force -ErrorAction SilentlyContinue }
    foreach ($f in @('run.json', 'run.active.json')) { $p = Join-Path $suiteData $f; if (Test-Path -LiteralPath $p) { Remove-Item -LiteralPath $p -Force -ErrorAction SilentlyContinue } }
}
# Files the suite run itself changes (the suite profile, Placemaker's pinned options and data): the saved copy always
# comes back, even when a game the user started later saved them again (that game ran on the suite profile).
function Test-SuiteWritten([string]$rel) {
    $r = $rel.ToLowerInvariant()
    return ($r -in @('settings.coc', 'launcher-settings.json', 'continue_game.json')) -or $r.StartsWith('modssettings\placemaker') -or $r.StartsWith('modsdata\placemaker\')
}
# When the run's game was last there: ended.txt (written by the host the moment its game is gone), else the run's last
# heartbeat (a host that was cut off) plus a margin for the game's own exit; $null when not known.
function Get-RunEnd {
    $e = Join-Path $saved 'ended.txt'
    if (Test-Path -LiteralPath $e) { try { return [datetime]::Parse((Get-Content -LiteralPath $e -Raw).Trim(), [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::RoundtripKind).ToLocalTime() } catch { } }
    try {
        $rid = (Get-Content -LiteralPath (Join-Path $saved 'pending.txt') -Raw).Trim()
        $hbf = Join-Path (Join-Path $ResultsRoot $rid) 'heartbeat.json'
        if ($rid -and (Test-Path -LiteralPath $hbf)) { return (Get-Item -LiteralPath $hbf).LastWriteTime.AddSeconds(30) }
    } catch { }
    return $null
}
# -RunEnded: the caller's game is gone now (the host's own restore): recorded first, so a restore that has to wait (a
# game the user started) later knows which files changed after the run.
function Restore-Settings([string]$why, [switch]$RunEnded) {
    MI-ReshadeBack $why
    $pending = Join-Path $saved 'pending.txt'
    if (-not (Test-Path -LiteralPath $pending)) { return }
    Disarm
    $endFile = Join-Path $saved 'ended.txt'
    if ($RunEnded -and -not (Test-Path -LiteralPath $endFile)) { Set-Content -LiteralPath $endFile -Value (Get-Date).ToString('o') -Encoding ASCII }
    $other = @(Get-OtherGames | Where-Object { $_ -like 'Cities2.exe*' })
    $foreignMark = Join-Path $saved 'foreign.txt'
    if ($other.Count -gt 0) {
        Set-Content -LiteralPath $foreignMark -Value "$($other -join ', ') ran while the restore was due ($why)" -Encoding UTF8
        Say "not putting the user's settings back now: a game that is not this run's is running ($($other -join ', ')); the game queue puts them back once it has closed (or run Run-Suite.ps1 -RestoreOnly then)"
        return
    }
    $keepNew = Test-Path -LiteralPath $foreignMark
    # a restore that comes late (a host cut off, then the user played): what changed after the run is the user's
    $endAt = Get-RunEnd
    $list = @(Get-Content -LiteralPath (Join-Path $saved 'files.txt'))
    $n = 0
    foreach ($rel in $list) {
        $src = Join-Path $saved "files\$rel"
        $dst = Join-Path $ud $rel
        if (-not (Test-Path -LiteralPath $src)) { continue }
        if ($endAt -and -not (Test-SuiteWritten $rel) -and (Test-Path -LiteralPath $dst) -and (Get-Item -LiteralPath $dst).LastWriteTime -gt $endAt) { Say "kept $rel (the user's game changed it after the run)"; continue }
        New-Item -ItemType Directory -Force -Path (Split-Path $dst) | Out-Null
        Copy-Item -LiteralPath $src -Destination $dst -Force
        $n++
    }
    $known = @{}; foreach ($rel in $list) { $known[$rel.ToLowerInvariant()] = $true }
    foreach ($f in Get-SettingsFiles) {
        $rel = $f.FullName.Substring($ud.Length + 1)
        if ($known.ContainsKey($rel.ToLowerInvariant())) { continue }
        if ($keepNew) { Say "kept new settings file $rel (the user's game ran meanwhile)"; continue }
        if (-not $endAt) { Say "kept new settings file $rel (when the run ended is not known)"; continue }
        if ($f.LastWriteTime -gt $endAt) { Say "kept new settings file $rel (made after the run, by the user's game)"; continue }
        Remove-Item -LiteralPath $f.FullName -Force; Say "removed new settings file $rel"
    }
    Remove-Item -LiteralPath $pending -Force
    Remove-Item -LiteralPath $endFile -Force -ErrorAction SilentlyContinue
    if ($keepNew) { Remove-Item -LiteralPath $foreignMark -Force -ErrorAction SilentlyContinue }
    Say "user settings and Placemaker data put back ($n files, $why)"
}

$script:lockStream = $null
function Release-Lock { if ($script:lockStream) { try { $script:lockStream.Dispose() } catch { }; $script:lockStream = $null } }   # the file stays: a lock nobody holds open is free
# The lock's holder is alive: its PID runs and is the same process (Windows reuses PIDs; a lock without procStart is from
# an older Run-Suite: then the process must have started before the lock was written).
function Test-LockHolder($l) {
    if (-not $l -or -not $l.pid) { return $false }
    $p = Get-Process -Id ([int]$l.pid) -ErrorAction SilentlyContinue
    if (-not $p) { return $false }
    try { $st = $p.StartTime } catch { return $true }   # cannot tell (an elevated process): alive
    if ($l.procStart) { return [math]::Abs($st.ToUniversalTime().Ticks - [int64]$l.procStart) -lt 20000000 }
    try { return $st -le ([datetime]$l.start).AddSeconds(5) } catch { return $true }
}
# The queue's host: the user's build back in Mods\Placemaker once our game is gone (ModsInstall.ps1). A playground's
# build stays once the playground has opened (MI-AdoptInstalled then: the user played it); one that never opened goes.
$script:queueInstall = $false
$script:playAdopted = $false
function Restore-UserBuild([string]$why) {
    if (-not $script:queueInstall -or $script:playAdopted) { return }
    $r = MI-Reconcile $why
    if ($r.ok) { $script:queueInstall = $false; Say "the user's build is back in Mods\Placemaker$(if ($r.message) { ": $($r.message)" })" }
    else { Say "the user's build is not back yet: $($r.message) (the game queue's dispatcher puts it back once no game runs)" }
}

if ($Playground) { $UserModSettings = $true }   # the user plays with their own Placemaker options

# ───────────── 1. lock and preflight ─────────────
# The suite lock is held open for the whole run: nobody else can open it for writing while this process lives, and the
# handle goes when it dies (killed or not). Taking it is one step, so two runners can never both hold it; the JSON inside
# says who holds it. It is never deleted.
New-Item -ItemType Directory -Force -Path (Split-Path $lockFile) | Out-Null
try { $script:lockStream = [System.IO.File]::Open($lockFile, 'OpenOrCreate', 'ReadWrite', 'Read') }
catch {
    $l = $null; try { $l = Get-Content -LiteralPath $lockFile -Raw | ConvertFrom-Json } catch { }
    Write-Host "STOP: another Run-Suite (PID $($l.pid), run $($l.runId)) holds the suite lock"; exit 2
}
# a Run-Suite of an older checkout writes the lock and closes it: then its JSON is what says it is alive
$l = $null; try { $buf = New-Object byte[] ([int]$script:lockStream.Length); [void]$script:lockStream.Read($buf, 0, $buf.Length); $l = [Text.Encoding]::UTF8.GetString($buf) | ConvertFrom-Json } catch { }
if ($l -and $l.pid -and [int]$l.pid -ne $PID -and (Test-LockHolder $l)) { Release-Lock; Write-Host "STOP: another Run-Suite (PID $($l.pid), run $($l.runId)) holds the suite lock"; exit 2 }
try {
    $bytes = [System.Text.Encoding]::UTF8.GetBytes((@{ pid = $PID; procStart = (Get-Process -Id $PID).StartTime.ToUniversalTime().Ticks; runId = $(if ($RestoreOnly) { 'restore-only' } else { $RunId }); start = (Get-Date).ToString('o') } | ConvertTo-Json -Compress))
    $script:lockStream.SetLength(0); $script:lockStream.Write($bytes, 0, $bytes.Length); $script:lockStream.Flush()
} catch { Release-Lock; Write-Host "STOP: could not take the suite lock: $($_.Exception.Message)"; exit 2 }
# -RestoreOnly also holds the suite lock (a run that starts meanwhile cannot save its settings under this restore)
if ($RestoreOnly) { try { Restore-Settings 'restore only' } finally { Release-Lock }; exit 0 }

try {
if (-not $RunId) { $RunId = (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + ([IO.Path]::GetFileNameWithoutExtension($Set)) }
$runDir = Join-Path $ResultsRoot $RunId
if (Test-Path -LiteralPath $runDir) {
    $old = "$runDir.old-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
    Move-Item -LiteralPath $runDir -Destination $old
    Write-Host "run folder $RunId existed: moved to $old"
}
New-Item -ItemType Directory -Force -Path $runDir | Out-Null
$script:hostLog = Join-Path $runDir 'host.log'
$t0 = Get-Date

$running = @(Get-Process -Name 'Cities2' -ErrorAction SilentlyContinue)
foreach ($p in $running) {
    $cmd = ''
    try { $cmd = (Get-CimInstance Win32_Process -Filter "ProcessId=$($p.Id)").CommandLine } catch { }
    if ($cmd -match '-placemakerSuite=(\S+)') {
        $orphanRun = $Matches[1]
        Say "a suite game this host left behind is running (PID $($p.Id), run $orphanRun): asking it to quit through its inbox"
        $inbox = Join-Path (Join-Path $ResultsRoot $orphanRun) 'inbox'
        if (Test-Path $inbox) { Write-NoBom (Join-Path $inbox ((Get-Date -Format 'yyyyMMdd-HHmmss-fff') + '-quit.json')) '{"command":"quit"}' }
        $w0 = Get-Date
        while ((Get-Process -Id $p.Id -ErrorAction SilentlyContinue) -and ((Get-Date) - $w0).TotalSeconds -lt 180) { Start-Sleep -Seconds 2 }
        if (Get-Process -Id $p.Id -ErrorAction SilentlyContinue) { Say "STOP: the orphaned suite game (PID $($p.Id)) did not quit within 180 s; it was not killed"; exit 2 }
        Say 'the orphaned suite game quit'
    } else {
        Say ("STOP: Cities2.exe is running (PID {0}) and it is not a suite game; the suite never touches the user's session." -f $p.Id)
        exit 2
    }
}
Restore-Settings 'left over from an earlier run'

# ───────────── 2. build + deploy, identity ─────────────
Push-Location $repo
try {
    $git = if ($Label) { $Label } else { (git rev-parse --short HEAD).Trim() }
    $dirty = if ($Label) { $null } else { (git status --porcelain --untracked-files=no) }
    if ($dirty) {
        $git = "$git-dirty"
        git diff HEAD | Out-File -FilePath (Join-Path $runDir 'dirty.patch') -Encoding utf8
        Say "the tree is dirty: its diff is saved as dirty.patch"
    }
    if (-not $NoBuild) {
        Say "building and deploying $git ..."
        $env:PLACEMAKER_SUITE_LOCK_PID = "$PID"   # Deploy-Mod.ps1: this run holds the suite lock itself
        $out = & dotnet build Placemaker.csproj -c Release 2>&1
        if ($LASTEXITCODE -ne 0) { $out | Select-Object -Last 30 | ForEach-Object { Say $_ }; Say 'STOP: build failed'; exit 3 }
        Say 'build + deploy ok'
    }
} finally { Pop-Location }
if ($DeployFrom) {
    # the queue's frozen build, swapped in only now: the suite lock is ours and no other game runs (checked above)
    . (Join-Path $PSScriptRoot 'ModsInstall.ps1')
    $ir = MI-Install $DeployFrom $Label "run $RunId"
    if (-not $ir.ok) { Say "STOP: could not install the build: $($ir.message)"; exit 5 }
    $script:queueInstall = $true
    Say "installed the queue's build $Label into Mods\Placemaker$(if (-not $Playground) { ' (the user''s build goes back when the game has exited)' } else { ' (it stays installed once the playground is open: the user plays it)' })"
}
$dll = Join-Path $ud 'Mods\Placemaker\Placemaker.dll'
$dllInfo = Get-Item $dll
$dllHash = (Get-FileHash $dll -Algorithm SHA256).Hash.Substring(0, 8).ToLowerInvariant()
$label = "$git+$dllHash"
Say ("installed Placemaker.dll {0} bytes, {1:yyyy-MM-dd HH:mm:ss}, sha256 {2}" -f $dllInfo.Length, $dllInfo.LastWriteTime, $dllHash)

# ───────────── 3. save the user's settings, suite profile ─────────────
# again now (the build install took a moment): the user may have started their game meanwhile
$other = @(Get-OtherGames)
if ($other.Count -gt 0) { Say ("STOP: Cities2.exe is running or starting ({0}) and it is not this run's; the suite never touches the user's session." -f ($other -join ', ')); exit 2 }
if (Test-Path (Join-Path $saved 'files')) { Remove-Item (Join-Path $saved 'files') -Recurse -Force }
New-Item -ItemType Directory -Force -Path (Join-Path $saved 'files') | Out-Null
$rels = @()
foreach ($f in Get-SettingsFiles) {
    $rel = $f.FullName.Substring($ud.Length + 1)
    $dst = Join-Path $saved "files\$rel"
    New-Item -ItemType Directory -Force -Path (Split-Path $dst) | Out-Null
    Copy-Item -LiteralPath $f.FullName -Destination $dst -Force
    $rels += $rel
}
Set-Content -Path (Join-Path $saved 'files.txt') -Value $rels -Encoding UTF8
Remove-Item -LiteralPath (Join-Path $saved 'ended.txt') -Force -ErrorAction SilentlyContinue   # the new run has not ended
Set-Content -Path (Join-Path $saved 'pending.txt') -Value $RunId -Encoding ASCII
Say ("saved {0} settings and data files" -f $rels.Count)
$auditBefore = @{}
$auditDir = Join-Path $modsData 'audit'
if (Test-Path $auditDir) { Get-ChildItem $auditDir -File | ForEach-Object { $auditBefore[$_.Name] = $true } }

$w, $h = $Window -split 'x'
if (-not $KeepGraphics) {
    $ls = Join-Path $ud 'launcher-settings.json'
    if (Test-Path $ls) {
        $j = Get-Content -LiteralPath $ls -Raw -Encoding UTF8 | ConvertFrom-Json
        $j.system.display_mode = 'windowed'
        $j.system.windowed_resolution = $Window
        $j.system.vsync = $false
        Write-NoBom $ls ($j | ConvertTo-Json -Depth 5)
    }
    $sc = Join-Path $ud 'Settings.coc'
    if (Test-Path $sc) {
        $t = Get-Content -LiteralPath $sc -Raw -Encoding UTF8
        $t = $t -replace '"masterVolume":\s*[0-9.]+', '"masterVolume": 0'
        $t = $t -replace '"menuVolume":\s*[0-9.]+', '"menuVolume": 0'
        $t = $t -replace '"radioActive":\s*true', '"radioActive": false'
        $t = $t -replace '"(uiVolume|ingameVolume|radioVolume)":\s*[0-9.]+', '"$1": 0'
        # the game saves a section only once a value differs from its default: with default volumes there is no audio
        # section to change, and the game plays its music. Add one, muted (the user's file comes back after the run).
        if ($t -notmatch '(?m)^Audio Settings\s*$') {
            $t = $t.TrimEnd() + "`r`nAudio Settings`r`n{`n    `"masterVolume`": 0,`n    `"menuVolume`": 0,`n    `"ingameVolume`": 0,`n    `"radioActive`": false,`n    `"radioVolume`": 0`n}`r`n"
        } elseif ($t -notmatch '"masterVolume"') {
            # a section without the master volume (left at its default, full): the user, 24 Oct, "make sure the sound is off"
            $t = $t -replace '(?m)(^Audio Settings\s*\r?\n\{)', "`$1`n    `"masterVolume`": 0,"
        }
        # the lowest graphics a picture still reads in (the user, 24 Oct: "crank the graphics down to the lowest"): no
        # shadows, ambient occlusion, reflections, fog, volumetrics, anti-aliasing or water flow. Model detail stays full
        # (levelOfDetail below): at low detail fences, hedges and props drop out of the pictures.
        $t = $t -replace '("enabled":\s*)true(,\s*"directionalShadowResolution")', '${1}false$2'
        $t = $t -replace '("enabled":\s*)true(,\s*"maxPixelRadius")', '${1}false$2'
        $t = $t -replace '("enabled":\s*)true(,\s*"enabledTransparent")', '${1}false$2'
        $t = $t -replace '("enabled":\s*)true(,\s*"fullscreen":\s*\w+,\s*"raySteps")', '${1}false$2'
        $t = $t -replace '("enabled":\s*)true(,\s*"budget")', '${1}false$2'
        $t = $t -replace '("enabled":\s*)true(,\s*"@type":\s*"Game\.Settings\.FogQualitySettings")', '${1}false$2'
        $t = $t -replace '"antiAliasingMethod":\s*"[A-Za-z]+"', '"antiAliasingMethod": "None"'
        $t = $t -replace '"waterflow":\s*true', '"waterflow": false'
        $t = $t -replace '"vSync":\s*true', '"vSync": false'
        $t = $t -replace '"displayMode":\s*"[A-Za-z]+"', '"displayMode": "Window"'
        $t = $t -replace '"volumetricCloudsEnabled":\s*true', '"volumetricCloudsEnabled": false'
        $t = $t -replace '"distanceCloudsEnabled":\s*true', '"distanceCloudsEnabled": false'
        $t = $t -replace '"distanceCloudsShadows":\s*true', '"distanceCloudsShadows": false'
        $t = $t -replace '"lodCrossFade":\s*true', '"lodCrossFade": false'
        $t = $t -replace '"edgeScrolling":\s*true', '"edgeScrolling": false'
        $t = $t -replace '"levelOfDetail":\s*[0-9.]+', '"levelOfDetail": 1'
        $t = $t -replace '"resolution":\s*\{[^}]*\}', ('"resolution": {"width": ' + $w + ', "height": ' + $h + '}')
        $t = $t -replace '("enabled":\s*)true(,\s*"isAdaptive")', '${1}false$2'
        $t = $t -replace '("enabled":\s*)true(,\s*"nearSampleCount")', '${1}false$2'
        $t = $t -replace '("enabled":\s*)true(,\s*"sampleCount")', '${1}false$2'
        Write-NoBom $sc $t
    }
    Say "suite profile: $Window window, vsync off, full model detail without LOD cross-fade, lowest effects (no shadows, AO, reflections, fog, volumetrics, clouds, anti-aliasing, DOF, motion blur, dynamic resolution), muted (all volumes 0, radio off)"
}

# ───────────── arm ─────────────
New-Item -ItemType Directory -Force -Path $suiteData | Out-Null
$setName = if ($Set -in @('bootstrap', 'queue')) { 'smoke' } else { $Set }
$scenarioFile = if ($setName -match "[\/.]" -and (Test-Path -LiteralPath $setName -PathType Leaf)) { (Resolve-Path -LiteralPath $setName).Path } else { Join-Path $repo "suite\scenarios\$setName.json" }
if (-not (Test-Path -LiteralPath $scenarioFile)) { Say "STOP: no scenario set $scenarioFile"; Restore-Settings 'stopped' -RunEnded; exit 2 }
$schemaFile = Join-Path $repo 'suite\schema.json'
$cfg = [ordered]@{
    runId = $RunId; set = $Set; outDir = $runDir; scenarioFile = $scenarioFile; schemaFile = $schemaFile; git = $git; dllHash = $dllHash
    expiresUtc = (Get-Date).ToUniversalTime().AddMinutes(25).ToString('o')
    speed = 4; pregrowSeconds = $PregrowSeconds; maxScenarios = $MaxScenarios
    fixtureOnly = ($Set -eq 'bootstrap' -or [bool]$Playground); quit = (-not $Playground); playground = [bool]$Playground; shotWidth = [int]$w; shotHeight = [int]$h; noTerrain = [bool]$NoTerrain; queue = [bool]$Queue
    userModSettings = [bool]$UserModSettings; look = $Look; pace = $Pace; profile = $(if ($AllMods) { 'full' } else { 'clean' })
}
if ($Menu) { $cfg['menuOnly'] = $true }
if ($Grid -match '^(\d+)x(\d+)$') { $cfg['nx'] = [int]$Matches[1]; $cfg['nz'] = [int]$Matches[2] }
foreach ($kv in ($Opt -split ';' | Where-Object { $_ -match '=' })) {
    $k, $v = $kv -split '=', 2
    $k = $k.Trim(); $v = $v.Trim()
    if ($v -match '^(true|false)$') { $cfg[$k] = ($v -eq 'true') } elseif ($v -match '^-?[0-9.]+$') { $cfg[$k] = [double]$v } else { $cfg[$k] = $v }
}
Write-NoBom (Join-Path $suiteData 'run.json') ($cfg | ConvertTo-Json -Depth 3)
Say ("run.json: " + (($cfg.GetEnumerator() | Where-Object { $_.Key -notin @('outDir', 'scenarioFile', 'schemaFile', 'expiresUtc') } | ForEach-Object { "$($_.Key)=$($_.Value)" }) -join ' '))
$modFlags = if ($AllMods) { '' } else { '-disableModding -disableCodeModding- ' }
$windowFlags = "-screen-fullscreen 0 -screen-width $w -screen-height $h"
$gameArgs = "$modFlags-noSplash $windowFlags -placemakerSuite=$RunId"
if ($DevMode) { $gameArgs += ' -developerMode' }
Set-Content -Path (Join-Path $ud 'runOnce.txt') -Value "-placemakerSuite=$RunId" -Encoding ASCII -NoNewline

# ───────────── launch ─────────────
$launchedAt = Get-Date
$proc = $null
$status = 'not started'; $phaseTimes = [ordered]@{}; $ourPid = 0; $lastPhase = ''
try {
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $exe
    $psi.Arguments = $gameArgs
    $psi.WorkingDirectory = $gameDir
    $psi.UseShellExecute = $false
    $psi.EnvironmentVariables['SteamAppId'] = '949230'
    $psi.EnvironmentVariables['SteamGameId'] = '949230'
    # the last look before the start: a game the user started since the checks above wins (the settings stay pending
    # while it runs, the user's build goes back once no game runs)
    $other = @(Get-OtherGames)
    if ($other.Count -gt 0) { $status = 'not started'; Disarm; Say ("STOP: Cities2.exe is running or starting ({0}) and it is not this run's; the game was not started." -f ($other -join ', ')); exit 2 }   # the finally puts back what it may
    # ReShade hangs the GPU in a test game: off for this launch, back on once no game runs (ModsInstall.ps1)
    MI-ReshadeOff
    $proc = [System.Diagnostics.Process]::Start($psi)
    $ourPid = $proc.Id
    $script:ourPid = $ourPid
    Write-NoBom $activeFile (@{ pid = $ourPid; runId = $RunId; start = $launchedAt.ToString('o'); runDir = $runDir } | ConvertTo-Json -Compress)
    Say "started Cities2.exe PID $ourPid $gameArgs"
    if ($script:queueInstall -and -not $Playground) { MI-PrepareRestore }   # while it boots: the user's build ready to swap back in at once

    $bootLimit = if ($AllMods) { 600 } else { 240 }
    $staleLimit = if ($AllMods) { 240 } else { 120 }
    if ($TimeoutMin -le 0) { $TimeoutMin = switch ($Set) { 'bootstrap' { 15 } 'smoke' { 30 } 'queue' { 480 } default { 90 } } }
    if ($Queue -and $TimeoutMin -lt 480) { $TimeoutMin = 480 }
    $hb = Join-Path $runDir 'heartbeat.json'
    $lastBeat = $null; $lastBeatChange = Get-Date; $everBeat = $false; $status = 'running'
    while ($true) {
        [void]$proc.WaitForExit(2000)   # 2 s, or less the moment the game exits
        $now = Get-Date
        $alive = [bool](Get-Process -Id $ourPid -ErrorAction SilentlyContinue)
        $beat = $null
        if (Test-Path -LiteralPath $hb) {
            try { $beat = Get-Content -LiteralPath $hb -Raw -ErrorAction Stop | ConvertFrom-Json } catch { $beat = $null }
            if ($beat -and ($beat.runId -ne $RunId -or ($beat.pid -and $beat.pid -ne $ourPid))) { $beat = $null }
        }
        if ($beat) {
            $everBeat = $true
            if ($beat.utc -ne $lastBeat) { $lastBeat = $beat.utc; $lastBeatChange = $now }
            if ($beat.phase -ne $lastPhase) {
                $lastPhase = $beat.phase
                $phaseTimes[$beat.phase] = [math]::Round(($now - $launchedAt).TotalSeconds, 1)
                Say ("phase {0} at {1:F0} s (game says {2} s since process start)" -f $beat.phase, ($now - $launchedAt).TotalSeconds, $beat.sinceStart)
                if ($Playground -and $script:queueInstall -and $beat.phase -eq 'playground' -and -not $script:playAdopted) {
                    # the user has the controls now: the build they play stays installed afterwards
                    $ar = MI-AdoptInstalled "the user's playground $RunId"; Say $ar.message
                    $script:playAdopted = [bool]$ar.ok
                }
            }
        }
        # the game queue's dispatcher asks this host to stop the game it started (no result in time, the user's game)
        if (Test-Path -LiteralPath (Join-Path $runDir 'stop-game.txt')) { $status = 'killed'; Say "the game queue asked to stop the game: $(Get-Content -LiteralPath (Join-Path $runDir 'stop-game.txt') -Raw)"; break }
        if (-not $alive) {
            if ($lastPhase -eq 'quit') { $status = 'quit'; Say 'game exited after the clean quit' }
            else { $status = 'gone'; Say "game process gone in phase '$lastPhase' without the suite's quit" }
            break
        }
        $elapsed = ($now - $launchedAt).TotalSeconds
        if (-not $everBeat -and $elapsed -gt $bootLimit) { $status = 'boot-timeout'; Say "no heartbeat after $bootLimit s"; break }
        if ($Playground -and $phaseTimes.Contains('playground')) { continue }   # the user plays (and later quits): no watchdog, no timeout, never killed; wait for the game to close
        if ($everBeat -and ($now - $lastBeatChange).TotalSeconds -gt $staleLimit) { $status = 'hung'; Say "heartbeat stale for $staleLimit s in phase '$lastPhase'"; break }
        if ($elapsed -gt $TimeoutMin * 60) { $status = 'timeout'; Say "overall timeout ($TimeoutMin min)"; break }
        if ($beat -and $beat.phase -eq 'quit') {
            $quitAt = Get-Date
            [void]$proc.WaitForExit(90000)   # returns the moment the game has exited
            if (Get-Process -Id $ourPid -ErrorAction SilentlyContinue) { $status = 'quit-timeout'; Say 'clean quit did not finish in 90 s' }
            else { $status = 'quit'; Say ("game exited cleanly {0:F0} s after the quit request" -f ((Get-Date) - $quitAt).TotalSeconds) }
            break
        }
    }
    if ($ourPid -ne 0 -and (Get-Process -Id $ourPid -ErrorAction SilentlyContinue)) {
        Say "killing PID $ourPid (status $status)"
        Stop-Process -Id $ourPid -Force
        Start-Sleep -Seconds 3
        if ($status -eq 'running') { $status = 'killed' }
    }
}
finally {
    # ───────────── collect logs, classify, put the settings back ─────────────
    # first, the moment the game is gone, before the user can start theirs: the user's build and settings
    Restore-UserBuild "game $RunId exited"
    Restore-Settings "after run $RunId" -RunEnded
    $logs = Join-Path $runDir 'logs'
    New-Item -ItemType Directory -Force -Path $logs | Out-Null
    foreach ($f in @('Player.log')) { $p = Join-Path $ud $f; if (Test-Path $p) { Copy-Item $p $logs -Force } }
    Get-ChildItem (Join-Path $ud 'Logs') -File -ErrorAction SilentlyContinue | Where-Object { $_.LastWriteTime -ge $launchedAt.AddSeconds(-5) } | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $logs -Force }
    if (Test-Path $auditDir) {
        $moved = 0
        Get-ChildItem -LiteralPath $auditDir -File | Where-Object { -not $auditBefore.ContainsKey($_.Name) } | ForEach-Object { New-Item -ItemType Directory -Force -Path (Join-Path $runDir 'audit') | Out-Null; Move-Item -LiteralPath $_.FullName -Destination (Join-Path $runDir 'audit') -Force; $moved++ }
        if ($moved -gt 0) { Say "moved $moved build audit files the run wrote into the run folder" }
    }
    if ($status -eq 'gone') {
        $player = Join-Path $logs 'Player.log'
        $crashDir = Join-Path $env:TEMP 'Colossal Order\Cities Skylines II\Crashes'
        $newCrash = (Test-Path $crashDir) -and @(Get-ChildItem $crashDir -Directory -ErrorAction SilentlyContinue | Where-Object { $_.LastWriteTime -ge $launchedAt }).Count -gt 0
        $text = if (Test-Path -LiteralPath $player) { Get-Content -LiteralPath $player -Raw } else { '' }
        # Unity's crash marker, and Mono's native crash report (a crash in the game's own native code)
        if ($newCrash -or $text -match 'Crash!!!|Native Crash Reporting|Got a SIG[A-Z]+ while executing native code') { $status = 'crashed' }
        elseif ($text -match 'Game terminated successfully') { $status = 'closed-externally' }
        else { $status = 'unknown-exit' }
        Say "classified the exit: $status"
    }
    if (Test-Path $activeFile) { Remove-Item $activeFile -Force }
}

$total = ((Get-Date) - $t0).TotalSeconds
$hostInfo = [ordered]@{ runId = $RunId; set = $Set; git = $git; dllHash = $dllHash; label = $label; status = $status; launchedAt = $launchedAt.ToString('o'); totalSec = [math]::Round($total, 1); phaseAtSec = $phaseTimes; pid = $ourPid; profile = $cfg.profile }
($hostInfo | ConvertTo-Json -Depth 4) | Set-Content -LiteralPath (Join-Path $runDir 'host.json') -Encoding UTF8
# the catalog of types, presets and options this build offers (for scenario writers)
$cat = Join-Path $runDir 'catalog.json'
if (Test-Path -LiteralPath $cat) { Copy-Item -LiteralPath $cat -Destination (Join-Path $repo 'suite\catalog.json') -Force -ErrorAction SilentlyContinue }
$md = Join-Path $runDir 'summary.md'
$top = @("> **Host: $status** (label $label, profile $($cfg.profile), total $([math]::Round($total)) s; phases s after launch: $(($phaseTimes.GetEnumerator() | ForEach-Object { "$($_.Key) $($_.Value)" }) -join ', '))")
if ($status -ne 'quit') { $top += "> **The run did not end with the suite's own quit: results after phase '$lastPhase' are missing.**" }
$top += ''
$body = if (Test-Path -LiteralPath $md) { Get-Content -LiteralPath $md -Raw -Encoding UTF8 } else { "# Game run $RunId`r`n(no summary written by the game)`r`n" }
Write-NoBom $md (($top -join "`r`n") + "`r`n" + $body)
Say "done: $status in $([math]::Round($total)) s -> $runDir"
if ($DeployFrom) { Say "installed now: $(MI-StatusLine)" }
else { Say "installed now: Mods/Placemaker/Placemaker.dll $label ($([math]::Round($dllInfo.Length / 1KB)) KB) - the user's normal game loads this build" }
# a queue session's pictures are published per request (each has its own contact sheet): no session-wide image
if (-not $Queue) { try { & (Join-Path $PSScriptRoot 'Contact-Suite.ps1') -RunId $RunId -ResultsRoot $ResultsRoot } catch { Say "contact sheet image failed: $($_.Exception.Message)" } }
if ($status -ne 'quit') { exit 1 }
exit 0
}
finally { Restore-UserBuild "host $RunId ended"; Release-Lock }
