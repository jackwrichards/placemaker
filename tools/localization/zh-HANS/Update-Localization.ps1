[CmdletBinding()]
param(
    [ValidateSet('Install', 'Build', 'Check', 'Uninstall')][string]$Action = 'Install',
    [string]$ModPath,
    [string]$SourcePath,
    [string]$CacheRoot = (Join-Path $env:USERPROFILE 'AppData\LocalLow\Colossal Order\Cities Skylines II\.cache\Mods\pdx_mods'),
    [string]$ImportFile
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2
$Root = $PSScriptRoot
$PatchName = 'zh-HANS.zz-placemaker-zhcn.json'
$Utf8 = New-Object System.Text.UTF8Encoding($false)

function Read-Json([string]$Path) {
    return (ConvertFrom-Json -InputObject ([IO.File]::ReadAllText($Path, [Text.Encoding]::UTF8)))
}
function Write-Json([string]$Path, $Value) {
    $parent = Split-Path -Parent $Path
    if (!(Test-Path -LiteralPath $parent)) { [IO.Directory]::CreateDirectory($parent) | Out-Null }
    $json = ConvertTo-Json -InputObject $Value -Depth 20
    $temp = $Path + '.tmp-' + [Guid]::NewGuid().ToString('N')
    [IO.File]::WriteAllText($temp, $json + "`r`n", $Utf8)
    if (Test-Path -LiteralPath $Path) { [IO.File]::Replace($temp, $Path, [NullString]::Value) }
    else { [IO.File]::Move($temp, $Path) }
}
function New-StringMap { return ,([Collections.Generic.Dictionary[string,string]]::new([StringComparer]::Ordinal)) }
function New-ObjectMap { return ,([Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)) }
function Add-Pair($Map, $Row, [bool]$Replace = $false) {
    foreach ($field in @('key','en','zh')) {
        if (!$Row.PSObject.Properties[$field] -or !($Row.$field -is [string])) { throw "Invalid $field field in translation row" }
    }
    if ([string]::IsNullOrWhiteSpace($Row.key)) { throw 'Empty translation key' }
    if (!$Map.ContainsKey($Row.key)) { $Map[$Row.key] = New-StringMap }
    if (!$Replace -and $Map[$Row.key].ContainsKey($Row.en) -and $Map[$Row.key][$Row.en] -cne $Row.zh) {
        throw "Conflicting key+English translation: $($Row.key) / $($Row.en)"
    }
    $Map[$Row.key][$Row.en] = $Row.zh
}
function Add-Evidence([string]$English, [string]$Chinese) {
    if (!$evidence.ContainsKey($English)) { $evidence[$English] = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal) }
    [void]$evidence[$English].Add($Chinese)
}
function Find-Translation([string]$Key, [string]$English) {
    if ($overrides.ContainsKey($Key) -and $overrides[$Key].ContainsKey($English)) {
        return [pscustomobject]@{ zh=$overrides[$Key][$English]; via='user' }
    }
    if ($keyMemory.ContainsKey($Key) -and $keyMemory[$Key].ContainsKey($English)) {
        return [pscustomobject]@{ zh=$keyMemory[$Key][$English]; via='key-and-english' }
    }
    # Multiple known translations mean context is required. Never guess at a new key.
    if ($memory.ContainsKey($English) -and !$ambiguous.Contains($English)) {
        return [pscustomobject]@{ zh=$memory[$English]; via='english-memory' }
    }
    return $null
}
function Assert-Translation([string]$English, [string]$Chinese, [string]$Label) {
    if ([string]::IsNullOrWhiteSpace($Chinese)) { throw "译文不能为空：$Label" }
    $pattern = '\{[^{}]+\}|<[^>]+>'
    [string[]]$a = @([regex]::Matches($English, $pattern) | ForEach-Object { $_.Value })
    [string[]]$b = @([regex]::Matches($Chinese, $pattern) | ForEach-Object { $_.Value })
    [Array]::Sort($a, [StringComparer]::Ordinal)
    [Array]::Sort($b, [StringComparer]::Ordinal)
    if (($a -join "`n") -cne ($b -join "`n")) { throw "译文的占位符或标签与英文不匹配：$Label" }
}
function File-Hash([string]$Path) {
    $stream = [IO.File]::OpenRead($Path)
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($sha.ComputeHash($stream))).Replace('-', '') }
    finally { $stream.Dispose(); $sha.Dispose() }
}
function Assert-OwnedPatch($State, [string]$Path) {
    if (!(Test-Path -LiteralPath $Path)) { return }
    $actual = File-Hash $Path
    if ($actual -eq $State.installedHash) { return }
    if ($State.PSObject.Properties['pending'] -and $State.pending -and $actual -eq $State.previousHash) { return }
    throw '已安装的汉化文件被手动修改。请先将其移出 Lang 备份，再重试；建议将补译存入 Data\UserTranslations.json。'
}

$sourceMode = ![string]::IsNullOrWhiteSpace($SourcePath)
if ($sourceMode) {
    if ($Action -eq 'Install' -or $Action -eq 'Uninstall') { throw '-SourcePath supports only -Action Build or Check.' }
    if (![string]::IsNullOrWhiteSpace($ModPath)) { throw 'Use either -SourcePath or -ModPath, not both.' }
    $SourcePath = (Resolve-Path -LiteralPath $SourcePath).ProviderPath
    $ReferenceRoot = $SourcePath
    $LangPath = Join-Path $SourcePath 'Lang'
    if (!(Test-Path -LiteralPath $LangPath -PathType Container)) {
        $LangPath = $SourcePath
        if ((Split-Path -Leaf $SourcePath) -eq 'Lang') { $ReferenceRoot = Split-Path -Parent $SourcePath }
    }
    $sourceName = Split-Path -Leaf $SourcePath
    Write-Host "Source build: $sourceName"
} else {
if ([string]::IsNullOrWhiteSpace($ModPath)) {
    $candidates = @(Get-ChildItem -LiteralPath $CacheRoot -Directory | Where-Object {
        $_.Name -match '^163638_[0-9]+$' -and (Test-Path -LiteralPath (Join-Path $_.FullName 'Placemaker.dll'))
    } | Sort-Object { [long]($_.Name -split '_')[-1] } -Descending)
    if ($candidates.Count -eq 0) { throw '未找到 Placemaker，请用 -ModPath 指定包含 Placemaker.dll 的文件夹。' }
    $ModPath = $candidates[0].FullName
    if ($candidates.Count -gt 1) { Write-Host '发现多个缓存版本，已选择数字后缀最大的版本；如需其他版本，请用 -ModPath 指定。' }
}
$ModPath = (Resolve-Path -LiteralPath $ModPath).ProviderPath
$ReferenceRoot = $ModPath
$LangPath = Join-Path $ModPath 'Lang'
if (!(Test-Path -LiteralPath (Join-Path $ModPath 'Placemaker.dll')) -or !(Test-Path -LiteralPath $LangPath)) {
    throw 'mod 目录无效：必须包含 Placemaker.dll 和 Lang 文件夹。'
}
$Target = Join-Path $LangPath $PatchName
$StatePath = Join-Path $ModPath '.placemaker-zhcn-install.json'
Write-Host "Placemaker: $ModPath"
if (($Action -eq 'Install' -or $Action -eq 'Uninstall') -and (Get-Process -Name Cities2 -ErrorAction SilentlyContinue)) {
    throw '请先完全退出城市天际线2，再安装或卸载汉化包。'
}
if ($Action -eq 'Install' -and (Test-Path -LiteralPath $StatePath)) { Assert-OwnedPatch (Read-Json $StatePath) $Target }

if ($Action -eq 'Uninstall') {
    if (!(Test-Path -LiteralPath $StatePath)) {
        if (Test-Path -LiteralPath $Target) { throw '没有自动安装记录，已保留现有汉化文件。如需卸载，请手工将其移出 Lang。' }
        Write-Host '这个版本尚未安装本汉化包。'
        return
    }
    $state = Read-Json $StatePath
    Assert-OwnedPatch $state $Target
    if ($state.hadOriginal) {
        $tempRestore = $Target + '.tmp-' + [Guid]::NewGuid().ToString('N')
        [IO.File]::WriteAllBytes($tempRestore, [Convert]::FromBase64String($state.originalBase64))
        # Keep restoration retryable if execution stops after restoring the bytes.
        $priorHash = ''
        if (Test-Path -LiteralPath $Target) { $priorHash = File-Hash $Target }
        $state.installedHash = File-Hash $tempRestore
        $state | Add-Member -MemberType NoteProperty -Name pending -Value $true -Force
        $state | Add-Member -MemberType NoteProperty -Name previousHash -Value $priorHash -Force
        Write-Json $StatePath $state
        if (Test-Path -LiteralPath $Target) { [IO.File]::Replace($tempRestore, $Target, [NullString]::Value) }
        else { [IO.File]::Move($tempRestore, $Target) }
    } elseif (Test-Path -LiteralPath $Target) {
        Remove-Item -LiteralPath $Target
    }
    Remove-Item -LiteralPath $StatePath
    Write-Host '卸载完成，原版 mod 文件及存档已保留。'
    return
}
}

[string[]]$sources = @([IO.Directory]::GetFiles($LangPath, 'en-US.*.json'))
if (Test-Path -LiteralPath (Join-Path $LangPath 'en-US.json')) { $sources += (Join-Path $LangPath 'en-US.json') }
[Array]::Sort($sources, [StringComparer]::OrdinalIgnoreCase)
if ($sources.Count -eq 0) { throw '未发现英文语言文件，这个版本可能已改变语言文件格式。' }
$english = New-StringMap
$origins = New-StringMap
$sourceDocs = New-ObjectMap
$currentPairs = New-ObjectMap
$sourceHashes = @()
$duplicates = @()
$inputCount = 0
foreach ($source in $sources) {
    $doc = Read-Json $source
    $name = [IO.Path]::GetFileName($source)
    $sourceDocs[$name] = $doc
    $sourceHashes += [pscustomobject]@{ file=$name; sha256=(File-Hash $source) }
    foreach ($p in $doc.PSObject.Properties) {
        if (!($p.Value -is [string])) { throw "语言文件包含非文本值：$source : $($p.Name)" }
        $inputCount++
        if ($english.ContainsKey($p.Name) -and $english[$p.Name] -cne $p.Value) {
            $duplicates += [pscustomobject]@{ key=$p.Name; previous=$english[$p.Name]; winner=$p.Value; file=[IO.Path]::GetFileName($source) }
        }
        $english[$p.Name] = $p.Value
        $origins[$p.Name] = [IO.Path]::GetFileName($source)
        if (!$currentPairs.ContainsKey($p.Name)) { $currentPairs[$p.Name] = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal) }
        [void]$currentPairs[$p.Name].Add($p.Value)
    }
}
if ($english.Count -eq 0) { throw 'English language files contain no entries.' }
$memory = New-StringMap
$evidence = New-ObjectMap
$memoryPath = Join-Path $Root 'Data\Translations.json'
if (Test-Path -LiteralPath $memoryPath) {
foreach ($row in @(Read-Json $memoryPath)) {
    Assert-Translation $row.en $row.zh $row.en
    if ($memory.ContainsKey($row.en)) { throw "译文库中的英文原文重复：$($row.en)" }
    $memory[$row.en] = $row.zh
    Add-Evidence $row.en $row.zh
}
}
$keyMemory = New-ObjectMap
$keyMemoryPath = Join-Path $Root 'Data\KeyTranslations.json'
if (Test-Path -LiteralPath $keyMemoryPath) {
    foreach ($row in @(Read-Json $keyMemoryPath)) {
        Assert-Translation $row.en $row.zh $row.key
        Add-Pair $keyMemory $row
        Add-Evidence $row.en $row.zh
    }
}
$activeSupplemental = @()
$supplementalSeen = New-StringMap
$supplementalReview = @()
$supplementalPath = Join-Path $Root 'Data\Supplemental.json'
# Source and packaged builds can point at different files containing the same UI text.
if (Test-Path -LiteralPath $supplementalPath) {
    foreach ($row in @(Read-Json $supplementalPath)) {
        if ($english.ContainsKey($row.key)) { continue }
        $reference = $row.reference
        if (!$sourceMode -and $row.PSObject.Properties['runtimeReference']) { $reference = $row.runtimeReference }
        $refPath = [IO.Path]::GetFullPath((Join-Path $ReferenceRoot $reference))
        $refPrefix = $ReferenceRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
        if (!$refPath.StartsWith($refPrefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'Supplemental reference must stay inside the source/mod directory.' }
        if (!(Test-Path -LiteralPath $refPath -PathType Leaf)) {
            $supplementalReview += [pscustomobject]@{ key=$row.key; en=$row.en; zh=$row.zh; reason='Reference file missing'; reference=$reference }
            continue
        }
        $needle = $row.key
        if ($row.PSObject.Properties['prefix']) { $needle = $row.prefix }
        $refText = [IO.File]::ReadAllText($refPath, [Text.Encoding]::UTF8)
        if (!$refText.Contains($needle) -or ($row.PSObject.Properties['checkEnglish'] -and $row.checkEnglish -and !$refText.Contains($row.en))) {
            $supplementalReview += [pscustomobject]@{ key=$row.key; en=$row.en; zh=$row.zh; reason='UI key or fallback text changed; review Supplemental.json'; reference=$reference }
            continue
        }
        Assert-Translation $row.en $row.zh $row.key
        if ($supplementalSeen.ContainsKey($row.key)) { throw "补充词典键重复：$($row.key)" }
        $supplementalSeen[$row.key] = $row.en
        $activeSupplemental += $row
        Add-Evidence $row.en $row.zh
        if (!$currentPairs.ContainsKey($row.key)) { $currentPairs[$row.key] = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal) }
        [void]$currentPairs[$row.key].Add($row.en)
    }
}
$baseline = New-StringMap
foreach ($row in @(Read-Json (Join-Path $Root 'Data\Baseline.json'))) { $baseline[$row.key] = $row.en }
$userPath = Join-Path $Root 'Data\UserTranslations.json'
$userRows = @()
if (Test-Path -LiteralPath $userPath) { $userRows = @(Read-Json $userPath) }
$pendingPath = Join-Path $Root 'Reports\PendingTranslations.json'
if ([string]::IsNullOrWhiteSpace($ImportFile) -and (Test-Path -LiteralPath $pendingPath)) { $ImportFile = $pendingPath }
$imported = 0
$rejected = 0
if (![string]::IsNullOrWhiteSpace($ImportFile)) {
    foreach ($row in @(Read-Json $ImportFile)) {
        if ([string]::IsNullOrWhiteSpace($row.zh)) { continue }
        Assert-Translation $row.en $row.zh $row.key
        $userRows = @($userRows | Where-Object { !($_.key -ceq $row.key -and $_.en -ceq $row.en) })
        $userRows += [pscustomobject]@{ key=$row.key; en=$row.en; zh=$row.zh }
        if (!$currentPairs.ContainsKey($row.key) -or !$currentPairs[$row.key].Contains($row.en)) { $rejected++ }
        else { $imported++ }
    }
}
$overrides = New-ObjectMap
foreach ($row in $userRows) {
    Assert-Translation $row.en $row.zh $row.key
    Add-Pair $overrides $row $true
}
foreach ($key in $overrides.get_Keys()) { foreach ($en in $overrides[$key].get_Keys()) { Add-Evidence $en $overrides[$key][$en] } }
$ambiguous = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($en in $evidence.get_Keys()) { if ($evidence[$en].Count -gt 1) { [void]$ambiguous.Add($en) } }
$translations = [ordered]@{}
$pending = @()
$added = 0
$changed = 0
$keyMatches = 0
$memoryMatches = 0
$userMatches = 0
[string[]]$keys = @($english.get_Keys())
[Array]::Sort($keys, [StringComparer]::Ordinal)
foreach ($key in $keys) {
    $en = $english[$key]
    $reason = 'untranslated'
    if (!$baseline.ContainsKey($key)) { $added++; $reason = 'new-key' }
    elseif ($baseline[$key] -cne $en) { $changed++; $reason = 'changed-english' }
    $match = Find-Translation $key $en
    if ($null -ne $match) {
        $translations[$key] = $match.zh
        switch ($match.via) { 'user' { $userMatches++ }; 'key-and-english' { $keyMatches++ }; 'english-memory' { $memoryMatches++ } }
    } else {
        $old = ''
        if ($baseline.ContainsKey($key)) {
            $previous = Find-Translation $key $baseline[$key]
            if ($null -ne $previous) { $old = $previous.zh }
        }
        $pending += [pscustomobject]@{ key=$key; file=$origins[$key]; en=$en; zh=''; reason=$reason; ambiguousEnglish=$ambiguous.Contains($en); previousZh=$old }
    }
}
$removed = @($baseline.get_Keys() | Where-Object { !$english.ContainsKey($_) }).Count
$translatedEnglishCount = $translations.Count
$supplementalCount = 0
$supplementalTranslations = [ordered]@{}
foreach ($row in $activeSupplemental) {
    $zh = $row.zh
    if ($overrides.ContainsKey($row.key) -and $overrides[$row.key].ContainsKey($row.en)) { $zh = $overrides[$row.key][$row.en] }
    $translations[$row.key] = $zh
    $supplementalTranslations[$row.key] = $zh
    $supplementalCount++
}
$nativeDocs = [ordered]@{}
if ($sourceMode) {
    foreach ($source in $sources) {
        $name = [IO.Path]::GetFileName($source)
        $targetName = 'zh-HANS' + $name.Substring('en-US'.Length)
        $native = [ordered]@{}
        foreach ($p in $sourceDocs[$name].PSObject.Properties) {
            # Do not collapse cross-file duplicate keys with different English meanings.
            $match = Find-Translation $p.Name $p.Value
            if ($null -ne $match) { $native[$p.Name] = $match.zh }
            elseif (!@($pending | Where-Object { $_.key -ceq $p.Name -and $_.en -ceq $p.Value }).Count) {
                $reason = 'untranslated'
                if (!$baseline.ContainsKey($p.Name)) { $reason = 'new-key' }
                elseif ($baseline[$p.Name] -cne $p.Value) { $reason = 'changed-english' }
                $pending += [pscustomobject]@{ key=$p.Name; file=$name; en=$p.Value; zh=''; reason=$reason; ambiguousEnglish=$ambiguous.Contains($p.Value); previousZh='' }
            }
        }
        $nativeDocs[$targetName] = $native
    }
    if ($supplementalTranslations.Count -gt 0) {
        if ($nativeDocs.Contains('zh-HANS.ui-extras.json')) { throw 'Supplemental export conflicts with en-US.ui-extras.json.' }
        $nativeDocs['zh-HANS.ui-extras.json'] = $supplementalTranslations
    }
}
$ambiguousPending = @($pending | Where-Object { $_.ambiguousEnglish }).Count
# Validation is complete before persisting imports or build output.
if (($imported + $rejected) -gt 0) { Write-Json $userPath @($userRows) }
$outputHashes = @()
if ($sourceMode) {
    $payload = Join-Path $Root 'Payload\Native\Lang'
    if (!(Test-Path -LiteralPath $payload)) { [IO.Directory]::CreateDirectory($payload) | Out-Null }
    foreach ($name in $nativeDocs.Keys) {
        $file = Join-Path $payload $name
        Write-Json $file $nativeDocs[$name]
        $outputHashes += [pscustomobject]@{ file=$name; sha256=(File-Hash $file); translatedEntries=$nativeDocs[$name].Count }
    }
    # Prune only our generated JSON files in this dedicated export directory.
    foreach ($file in @([IO.Directory]::GetFiles($payload, 'zh-HANS*.json'))) {
        if (!$nativeDocs.Contains([IO.Path]::GetFileName($file))) { Remove-Item -LiteralPath $file }
    }
} else {
    $payload = Join-Path $Root ('Payload\Lang\' + $PatchName)
    Write-Json $payload $translations
    $outputHashes += [pscustomobject]@{ file=$PatchName; sha256=(File-Hash $payload); translatedEntries=$translations.Count }
}
Write-Json $pendingPath @($pending)
Write-Json (Join-Path $Root 'Reports\SupplementalReview.json') @($supplementalReview)
$report = [ordered]@{
    createdAt=[DateTimeOffset]::UtcNow.ToString('o'); mode=$(if ($sourceMode) { 'source' } else { 'installed-mod' }); action=$Action;
    englishFiles=$sources.Count; inputEntries=$inputCount; uniqueKeys=$english.Count;
    translatedKeys=$translatedEnglishCount; pendingKeys=$pending.Count; supplementalKeys=$supplementalCount; supplementalReviewCount=$supplementalReview.Count; patchKeys=$translations.Count;
    coveragePercent=[math]::Round(100.0*$translatedEnglishCount/$english.Count,2);
    keyAndEnglishMatches=$keyMatches; englishMemoryMatches=$memoryMatches; userOverrideMatches=$userMatches;
    ambiguousEnglishStrings=$ambiguous.Count; unresolvedAmbiguousPairs=$ambiguousPending;
    addedKeysSinceBaseline=$added; changedEnglishSinceBaseline=$changed; removedKeysSinceBaseline=$removed;
    importedTranslations=$imported; rejectedStaleImports=$rejected; duplicateKeyConflicts=@($duplicates);
    sourceFiles=@($sourceHashes); outputFiles=@($outputHashes)
}
if ($sourceMode) { $report.sourceName=$sourceName }
else { $report.modPath=$ModPath; $report.modDllSha256=(File-Hash (Join-Path $ModPath 'Placemaker.dll')); $report.patchSha256=(File-Hash $payload) }
Write-Json (Join-Path $Root 'Reports\Latest.json') $report
Write-Host ("已译：{0}/{1}；待译 pending：{2}；新增键：{3}；英文改写：{4}；界面补充：{5}" -f $translatedEnglishCount,$english.Count,$pending.Count,$added,$changed,$supplementalCount)
if ($rejected -gt 0) { Write-Warning "有 $rejected 条补译已不匹配当前版本，已保存在 UserTranslations.json 中，本次未应用。" }
if ($pending.Count -gt 0) {
    Write-Warning '新版有文本需要补译。请填写 Reports\PendingTranslations.json 中的 zh，再运行本工具。未译文本暂时使用原版语言回退。'
}
if ($ambiguousPending -gt 0) { Write-Warning '部分英文存在不同语义的译文，已停止跨键复用。请按 key 上下文补译。' }
if ($supplementalReview.Count -gt 0) { Write-Warning ('界面后备文本发生变化，需要复核 {0} 条。请查看 Reports\SupplementalReview.json。' -f $supplementalReview.Count) }
if ($Action -eq 'Install') {
    $hadOriginal = Test-Path -LiteralPath $Target
    $originalBase64 = ''
    if (Test-Path -LiteralPath $StatePath) {
        $state = Read-Json $StatePath
        Assert-OwnedPatch $state $Target
        $hadOriginal = $state.hadOriginal
        $originalBase64 = $state.originalBase64
    } elseif ($hadOriginal) {
        $originalBase64 = [Convert]::ToBase64String([IO.File]::ReadAllBytes($Target))
    }
    $previousHash = ''
    if (Test-Path -LiteralPath $Target) { $previousHash = File-Hash $Target }
    $nextState = [ordered]@{ version=2; hadOriginal=$hadOriginal; originalBase64=$originalBase64; installedHash=(File-Hash $payload); pending=$true; previousHash=$previousHash }
    # Persist the original bytes before changing the installed file. An interrupted
    # transaction can be retried or uninstalled with either the old or new hash.
    Write-Json $StatePath $nextState
    $tempTarget = $Target + '.tmp-' + [Guid]::NewGuid().ToString('N')
    Copy-Item -LiteralPath $payload -Destination $tempTarget
    if (Test-Path -LiteralPath $Target) { [IO.File]::Replace($tempTarget, $Target, [NullString]::Value) }
    else { [IO.File]::Move($tempTarget, $Target) }
    $nextState.pending = $false
    $nextState.previousHash = ''
    Write-Json $StatePath $nextState
    Write-Host '安装完成。请将游戏语言设为简体中文，然后重新启动游戏。'
} else {
    Write-Host "已生成：$payload"
    Write-Host '未改动游戏或 mod 文件。'
}
