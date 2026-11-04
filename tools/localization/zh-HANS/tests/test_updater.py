import json
import pathlib
import shutil
import subprocess
import tempfile
import atexit
import os
import hashlib
import base64

ROOT = pathlib.Path(__file__).resolve().parents[1]
TEMP = tempfile.TemporaryDirectory(prefix='placemaker-localization-test-')
FIX = pathlib.Path(TEMP.name).resolve()
assert FIX.parent == pathlib.Path(tempfile.gettempdir()).resolve()
atexit.register(TEMP.cleanup)
PKG = FIX / 'package'
CACHE = FIX / 'cache'
PS = os.environ.get('PLACEMAKER_TEST_POWERSHELL', r'C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe')

def save(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, ensure_ascii=False), encoding='utf-8')

def read(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))

def run(action='Check', expected=0, **kwargs):
    args = [PS, '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', str(PKG / 'Update-Localization.ps1'), '-Action', action]
    for key, value in kwargs.items():
        args += ['-' + key, str(value)]
    p = subprocess.run(args, capture_output=True)
    output = p.stdout.decode('utf-8', errors='replace') + p.stderr.decode('utf-8', errors='replace')
    assert (p.returncode == 0) == (expected == 0), output
    return output

PKG.mkdir(parents=True, exist_ok=True)
shutil.copy2(ROOT / 'Update-Localization.ps1', PKG)
save(PKG / 'Data/Translations.json', [{'en':'Alpha','zh':'甲'}, {'en':'Count {0}','zh':'数量 {0}'}])
save(PKG / 'Data/Baseline.json', [{'key':'Placemaker.A','en':'Alpha'}, {'key':'Placemaker.B','en':'Count {0}'}])
save(PKG / 'Data/UserTranslations.json', [])
versions = []
for n in (3, 10):
    mod = CACHE / f'163638_{n}'
    mod.mkdir(parents=True, exist_ok=True)
    (mod / 'Placemaker.dll').write_bytes(b'TEST FIXTURE - NEVER LOAD')
    save(mod / 'Lang/en-US.core.json', {'Placemaker.A':'Alpha', 'Placemaker.B':'Count {0}'})
    versions.append(mod)
mod = versions[-1]
patch = mod / 'Lang/zh-HANS.zz-placemaker-zhcn.json'
report = PKG / 'Reports/Latest.json'
pending = PKG / 'Reports/PendingTranslations.json'
state = mod / '.placemaker-zhcn-install.json'
originals = {p: p.read_bytes() for p in mod.rglob('*') if p.is_file()}

run(CacheRoot=CACHE)
assert read(report)['modPath'] == str(mod)
assert read(report)['pendingKeys'] == 0
assert not patch.exists()
print('PASS: numeric newest-version discovery and check-only mode')

run('Install', ModPath=mod)
first = patch.read_bytes()
run('Install', ModPath=mod)
assert patch.read_bytes() == first
assert all(p.read_bytes() == data for p, data in originals.items())
run('Uninstall', ModPath=mod)
assert not patch.exists() and not state.exists()
print('PASS: install, repeat install, preserve originals, uninstall')

old_patch = b'{"original":"saved bytes"}\r\n'
patch.write_bytes(old_patch)
run('Install', ModPath=mod)
run('Install', ModPath=mod)
run('Uninstall', ModPath=mod)
assert patch.read_bytes() == old_patch
patch.unlink()
print('PASS: backup and byte-exact restoration of a pre-existing patch')

save(mod / 'Lang/en-US.core.json', {'Placemaker.A':'Rewritten Alpha','Placemaker.B':'Count {0}', 'Placemaker.C':'Brand new {1}', 'Placemaker.D':'Alpha'})
run('Install', ModPath=mod)
r = read(report)
assert (r['addedKeysSinceBaseline'],r['changedEnglishSinceBaseline'],r['pendingKeys'],r['translatedKeys']) == (2,1,2,2)
assert 'Placemaker.A' not in read(patch)
assert read(patch)['Placemaker.D'] == '甲'
rows = read(pending)
assert {x['reason'] for x in rows} == {'new-key','changed-english'}
for row in rows:
    row['zh'] = '改写的甲' if row['key'] == 'Placemaker.A' else '全新 {1}'
save(pending, rows)
run('Install', ModPath=mod)
assert read(report)['pendingKeys'] == 0 and len(read(PKG / 'Data/UserTranslations.json')) == 2
run('Check', ModPath=mod)
assert read(report)['pendingKeys'] == 0
print('PASS: changed text rejection, exact-text reuse for new key, pending translation import and persistence')

invalid = FIX / 'invalid.json'
save(invalid, [{'key':'Placemaker.C','en':'Brand new {1}','zh':'遗漏参数'}])
before = patch.read_bytes()
run('Install', expected=1, ModPath=mod, ImportFile=invalid)
assert patch.read_bytes() == before
print('PASS: placeholder mismatch blocks installation without modifying installed file')

patch.write_bytes(b'{"custom":"user edited"}')
run('Install', expected=1, ModPath=mod)
run('Uninstall', expected=1, ModPath=mod)
assert patch.read_bytes() == b'{"custom":"user edited"}'
patch.unlink()
run('Uninstall', ModPath=mod)
print('PASS: user-edited patch protection and recovery')

save(mod / 'Lang/en-US.core.json', {'Placemaker.A':'Alpha'})
run('Check', ModPath=mod)
assert read(report)['removedKeysSinceBaseline'] == 1
assert len(read(PKG / 'Payload/Lang/zh-HANS.zz-placemaker-zhcn.json')) == 1
print('PASS: removed-key pruning')

save(pending, [{'key':'Placemaker.Old','en':'Old text {0}','zh':'旧文本 {0}'}])
run('Check', ModPath=mod)
assert read(report)['rejectedStaleImports'] == 1
assert any(x['key'] == 'Placemaker.Old' and x['zh'] == '旧文本 {0}' for x in read(PKG / 'Data/UserTranslations.json'))
assert 'Placemaker.Old' not in read(PKG / 'Payload/Lang/zh-HANS.zz-placemaker-zhcn.json')
print('PASS: obsolete completed translations preserved without applying to changed/removed keys')

run('Install', ModPath=FIX, expected=1)
print('PASS: invalid mod directory rejected')

old_patch = b'{"old":"recover me"}'
patch.write_bytes(old_patch)
save(state, {'version':2, 'hadOriginal':True, 'originalBase64':base64.b64encode(old_patch).decode(), 'installedHash':'not-installed-yet', 'pending':True, 'previousHash':hashlib.sha256(old_patch).hexdigest()})
run('Uninstall', ModPath=mod)
assert patch.read_bytes() == old_patch
patch.unlink()
print('PASS: recovery from interrupted installation before replacement')

save(PKG / 'Data/Supplemental.json', [{'key':'Placemaker.UI.X','en':'Useful text','zh':'实用文本','reference':'Placemaker.mjs','prefix':'Placemaker.UI.','checkEnglish':True}])
(mod / 'Placemaker.mjs').write_text('"Placemaker.UI." + "X"; "Useful text";', encoding='utf-8')
run('Check', ModPath=mod)
assert read(report)['supplementalKeys'] == 1
(mod / 'Placemaker.mjs').write_text('"Placemaker.UI." + "X"; "Changed text";', encoding='utf-8')
run('Check', ModPath=mod)
assert read(report)['supplementalKeys'] == 0 and read(report)['supplementalReviewCount'] == 1
print('PASS: supplemental UI source guard excludes changed fallback text')

# All further checks use synthetic fixtures; never load the placeholder DLL.
save(PKG / 'Data/Translations.json', [
    {'en':'Stands','zh':'看台'}, {'en':'Bench','zh':'长椅'},
    {'en':'Alpha','zh':'甲'}, {'en':'Count {0}','zh':'数量 {0}'},
])
save(PKG / 'Data/KeyTranslations.json', [
    {'key':'Placemaker.Sports.Stands','en':'Stands','zh':'看台'},
    {'key':'Placemaker.Food.Stands','en':'Stands','zh':'摊位'},
    {'key':'Placemaker.Park.Bench','en':'Bench','zh':'长椅'},
    {'key':'Placemaker.Work.Bench','en':'Bench','zh':'工作台'},
    {'key':'Placemaker.A','en':'Alpha','zh':'键甲'},
    {'key':'Placemaker.A','en':'Rewritten Alpha','zh':'改写的甲'},
])
save(PKG / 'Data/UserTranslations.json', [])
save(pending, [])
save(mod / 'Lang/en-US.core.json', {
    'Placemaker.Sports.Stands':'Stands', 'Placemaker.Food.Stands':'Stands',
    'Placemaker.Park.Bench':'Bench', 'Placemaker.Work.Bench':'Bench',
    'Placemaker.Unknown.Stands':'Stands', 'Placemaker.Unknown.Bench':'Bench',
    'Placemaker.A':'Alpha', 'Placemaker.B':'Count {0}', 'Placemaker.C':'Count {0}',
})
run('Build', ModPath=mod)
built = read(PKG / 'Payload/Lang/zh-HANS.zz-placemaker-zhcn.json')
assert built['Placemaker.Sports.Stands'] == '看台'
assert built['Placemaker.Food.Stands'] == '摊位'
assert built['Placemaker.Work.Bench'] == '工作台'
assert built['Placemaker.A'] == '键甲'
assert built['Placemaker.C'] == '数量 {0}'
assert 'Placemaker.Unknown.Stands' not in built and 'Placemaker.Unknown.Bench' not in built
assert read(report)['unresolvedAmbiguousPairs'] == 2
assert all(row['ambiguousEnglish'] for row in read(pending))
print('PASS: contextual key+English priority, ambiguous Stands/Bench fallback blocked, unambiguous reuse')

save(PKG / 'Data/UserTranslations.json', [
    {'key':'Placemaker.A','en':'Alpha','zh':'用户甲'},
    {'key':'Placemaker.A','en':'Rewritten Alpha','zh':'用户新甲'},
])
run('Build', ModPath=mod)
assert read(PKG / 'Payload/Lang/zh-HANS.zz-placemaker-zhcn.json')['Placemaker.A'] == '用户甲'
doc = read(mod / 'Lang/en-US.core.json')
doc['Placemaker.A'] = 'Rewritten Alpha'
save(mod / 'Lang/en-US.core.json', doc)
run('Build', ModPath=mod)
assert read(PKG / 'Payload/Lang/zh-HANS.zz-placemaker-zhcn.json')['Placemaker.A'] == '用户新甲'
doc['Placemaker.A'] = 'Unknown rewrite'
save(mod / 'Lang/en-US.core.json', doc)
run('Build', ModPath=mod)
assert 'Placemaker.A' not in read(PKG / 'Payload/Lang/zh-HANS.zz-placemaker-zhcn.json')
assert next(x for x in read(pending) if x['key'] == 'Placemaker.A')['previousZh'] == '用户甲'
print('PASS: user override highest priority, multiple English revisions retained, changed text not misapplied')

repo = FIX / 'source-repository'
save(repo / 'Lang/en-US.first.json', {'Placemaker.A':'Alpha', 'Placemaker.Shared':'Field area', 'Placemaker.Food.Stands':'Stands'})
save(repo / 'Lang/en-US.second.json', {'Placemaker.Shared':'Crowd field', 'Placemaker.B':'Count {0}', 'Placemaker.New':'New original'})
save(repo / 'Lang/en-US.json', {'Placemaker.Plain':'Plain'})
key_rows = read(PKG / 'Data/KeyTranslations.json') + [
    {'key':'Placemaker.Shared','en':'Field area','zh':'场地面积'},
    {'key':'Placemaker.Shared','en':'Crowd field','zh':'观众场地'},
    {'key':'Placemaker.Plain','en':'Plain','zh':'普通'},
]
save(PKG / 'Data/KeyTranslations.json', key_rows)
repo_before = {str(p.relative_to(repo)): p.read_bytes() for p in repo.rglob('*') if p.is_file()}
run('Build', SourcePath=repo)
native = PKG / 'Payload/Native/Lang'
assert read(native / 'zh-HANS.first.json') == {'Placemaker.A':'用户甲', 'Placemaker.Shared':'场地面积', 'Placemaker.Food.Stands':'摊位'}
assert read(native / 'zh-HANS.second.json') == {'Placemaker.Shared':'观众场地', 'Placemaker.B':'数量 {0}'}
assert read(native / 'zh-HANS.json') == {'Placemaker.Plain':'普通'}
assert read(report)['mode'] == 'source' and read(report)['supplementalKeys'] == 0
assert read(report)['pendingKeys'] == 1
assert 'modPath' not in read(report) and str(repo) not in report.read_text(encoding='utf-8-sig')
assert repo_before == {str(p.relative_to(repo)): p.read_bytes() for p in repo.rglob('*') if p.is_file()}
assert not (repo / 'Placemaker.dll').exists()
print('PASS: read-only repository build without DLL, native per-file key+English resolution, unknowns omitted, path-free report')

# Pending imports for a non-winning duplicate key are still valid in native mode.
extra = FIX / 'cross-file-import.json'
save(extra, [{'key':'Placemaker.Shared','en':'Field area','zh':'用户场地面积'}])
run('Build', SourcePath=repo, ImportFile=extra)
assert read(native / 'zh-HANS.first.json')['Placemaker.Shared'] == '用户场地面积'
assert read(native / 'zh-HANS.second.json')['Placemaker.Shared'] == '观众场地'
assert read(report)['importedTranslations'] == 1 and read(report)['rejectedStaleImports'] == 0
before_native = {p.name:p.read_bytes() for p in native.glob('*.json')}
run('Check', SourcePath=repo / 'Lang')
assert before_native == {p.name:p.read_bytes() for p in native.glob('*.json')}
run('Install', SourcePath=repo, expected=1)
run('Uninstall', SourcePath=repo, expected=1)
run('Check', SourcePath=repo, ModPath=mod, expected=1)
(repo / 'Lang/en-US.first.json').unlink()
save(repo / 'Lang/en-US.second.json', {'Placemaker.B':'Count {0}'})
run('Build', SourcePath=repo)
assert not (native / 'zh-HANS.first.json').exists()
assert 'Placemaker.Shared' not in read(native / 'zh-HANS.second.json')
assert any(row['key'] == 'Placemaker.Shared' for row in read(PKG / 'Data/UserTranslations.json'))
print('PASS: source duplicate-pair imports, deterministic rebuild, mode guards, deleted files/keys pruned without losing stored translations')

save(PKG / 'Data/KeyTranslations.json', key_rows + [{'key':'Placemaker.Shared','en':'Field area','zh':'另一条'}])
before_native = {p.name:p.read_bytes() for p in native.glob('*.json')}
run('Build', SourcePath=repo, expected=1)
assert before_native == {p.name:p.read_bytes() for p in native.glob('*.json')}
save(PKG / 'Data/KeyTranslations.json', key_rows)
save(invalid, [{'key':'Placemaker.B','en':'Count {0}','zh':'数量 {0} {0}'}])
run('Build', SourcePath=repo, ImportFile=invalid, expected=1)
assert before_native == {p.name:p.read_bytes() for p in native.glob('*.json')}
save(invalid, [{'key':'Placemaker.X','en':'<b>{name}</b>','zh':'<i>{name}</i>'}])
run('Build', SourcePath=repo, ImportFile=invalid, expected=1)
assert before_native == {p.name:p.read_bytes() for p in native.glob('*.json')}
print('PASS: conflicting exact pairs, repeated placeholders and changed markup rejected before output changes')

# A completed restoration with its pending record still present must be retryable.
old_patch = b'{"old":"restored-original"}'
patch.write_bytes(old_patch)
save(state, {'version':2,'hadOriginal':True,'originalBase64':base64.b64encode(old_patch).decode(),
             'installedHash':hashlib.sha256(old_patch).hexdigest(), 'pending':True,'previousHash':'previous-installed-hash'})
run('Uninstall', ModPath=mod)
assert patch.read_bytes() == old_patch and not state.exists()
patch.unlink()
print('PASS: retry after interrupted uninstall restoration')

# PowerShell dictionary adapter exposes keys as properties; "Keys" must not mask get_Keys().
save(PKG / 'Data/Translations.json', [{'en':'Keys','zh':'按键'}, {'en':'Count {0}','zh':'数量 {0}'}])
save(PKG / 'Data/UserTranslations.json', [{'key':'Placemaker.Keyboard','en':'Keys','zh':'键位'}])
save(repo / 'Lang/en-US.second.json', {'Placemaker.Keyboard':'Keys','Placemaker.NewKeyboard':'Keys',
                                      'Placemaker.CaseTokens':'{A} {a}'})
case_import = FIX / 'case-placeholders.json'
save(case_import, [{'key':'Placemaker.CaseTokens','en':'{A} {a}','zh':'{a} 然后 {A}'}])
run('Build', SourcePath=repo, ImportFile=case_import)
assert read(native / 'zh-HANS.second.json')['Placemaker.Keyboard'] == '键位'
assert 'Placemaker.NewKeyboard' not in read(native / 'zh-HANS.second.json')
assert read(native / 'zh-HANS.second.json')['Placemaker.CaseTokens'] == '{a} 然后 {A}'
print('PASS: English Keys does not shadow dictionary metadata; ordinal placeholder comparison')

save(PKG / 'Data/Supplemental.json', [{'key':'Placemaker.UI.Extra','en':'Extra UI text','zh':'补充界面文本',
    'reference':'UI/src/extra.ts','runtimeReference':'Placemaker.mjs','checkEnglish':True}])
ui = repo / 'UI/src/extra.ts'
ui.parent.mkdir(parents=True)
ui.write_text('localize("Placemaker.UI.Extra", "Extra UI text");',encoding='utf8')
run('Build', SourcePath=repo)
assert read(native / 'zh-HANS.ui-extras.json') == {'Placemaker.UI.Extra':'补充界面文本'}
assert read(report)['supplementalKeys'] == 1 and read(report)['supplementalReviewCount'] == 0
run('Build', SourcePath=repo / 'Lang')
assert read(native / 'zh-HANS.ui-extras.json') == {'Placemaker.UI.Extra':'补充界面文本'}
ui_import = FIX / 'ui-import.json'
save(ui_import, [{'key':'Placemaker.UI.Extra','en':'Extra UI text','zh':'用户补充文本'}])
run('Build', SourcePath=repo, ImportFile=ui_import)
assert read(native / 'zh-HANS.ui-extras.json') == {'Placemaker.UI.Extra':'用户补充文本'}
assert read(report)['importedTranslations'] == 1 and read(report)['rejectedStaleImports'] == 0
ui.write_text('localize("Placemaker.UI.Extra", "Rewritten UI text");',encoding='utf8')
run('Build', SourcePath=repo)
assert not (native / 'zh-HANS.ui-extras.json').exists()
assert read(report)['supplementalKeys'] == 0 and read(report)['supplementalReviewCount'] == 1
(mod / 'Placemaker.mjs').write_text('"Placemaker.UI.Extra"; "Extra UI text";',encoding='utf8')
run('Build', ModPath=mod)
assert read(PKG / 'Payload/Lang/zh-HANS.zz-placemaker-zhcn.json')['Placemaker.UI.Extra'] == '用户补充文本'
assert read(report)['supplementalKeys'] == 1
print('PASS: source/runtime supplemental references, native UI extras, changed supplemental text excluded')

before_native = {p.name:p.read_bytes() for p in native.glob('*.json')}
save(PKG / 'Data/Supplemental.json', [{'key':'Placemaker.UI.Extra','en':'Extra UI text','zh':'补充界面文本',
    'reference':'../outside.txt','checkEnglish':True}])
run('Build', SourcePath=repo, expected=1)
assert before_native == {p.name:p.read_bytes() for p in native.glob('*.json')}
print('PASS: out-of-tree supplemental references rejected without changing exports')
print('ALL TESTS PASSED')
