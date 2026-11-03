<#
.SYNOPSIS
  Builds the hot neighbourhood-polish planner (Core/Neighbours/YardStitch.cs) under a fresh name and prints its path.

.DESCRIPTION
  The game runner can load it into a game that is already open with this worktree's bin\Release build (lab op "hotload"
  with "path"), so a planner change is tried in seconds: no mod rebuild, no relaunch. Send the scenario WITHOUT -Build
  (the queue then sees the same build and reuses the open game). Anything outside YardStitch.cs still needs a real build.

  powershell -File tools/hot/Build-Hot.ps1
#>
$ErrorActionPreference = 'Stop'
$repo = Resolve-Path (Join-Path $PSScriptRoot '..\..')
if (-not (Test-Path (Join-Path $repo 'bin\Release\Placemaker.dll'))) { throw 'bin\Release\Placemaker.dll is missing: build the mod once first' }
$name = 'PlacemakerHot_' + (Get-Date -Format 'HHmmss_fff')
$out = & dotnet build (Join-Path $PSScriptRoot 'PlacemakerHot.csproj') -c Release "-p:HotName=$name" -nologo -v:q 2>&1
if ($LASTEXITCODE -ne 0) { $out | Select-String -Pattern 'error' | ForEach-Object { $_.Line }; throw 'hot build failed' }
$dll = Join-Path $repo "bin\Hot\$name.dll"
if (-not (Test-Path $dll)) { throw "no $dll" }
Write-Output $dll
