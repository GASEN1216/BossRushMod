#!/usr/bin/env python3
"""只允许同步持有的失败 SaveFile 释放自己留下的官方 saving 闩。"""
from pathlib import Path
import re
import sys

sys.path.insert(0, str(Path(__file__).resolve().parent))
from cs_source_util import clean_source

ROOT = Path(__file__).resolve().parents[1]


def method(source, signature):
    assert source.count(signature) == 1, signature
    start = source.index(signature)
    opening = source.index('{', start)
    depth = 0
    for end in range(opening, len(source)):
        depth += (source[end] == '{') - (source[end] == '}')
        if depth == 0:
            return source[opening + 1:end]
    raise AssertionError(signature)


def main():
    source = clean_source((ROOT / 'Common/Lifecycle/BossRushSaveFileThrottle.cs').read_text(encoding='utf-8-sig'))
    body = method(source, 'internal static void RunSaveFile(')
    assert re.search(r'if\s*\(SavesSystem\.IsSaving\)\s*throw\s+new\s+InvalidOperationException', body), 'must reject an already running save before invoking it'
    assert re.search(r'try\s*\{\s*saveFile\(\);\s*\}\s*catch\s*\{', body), 'owned synchronous save must be the complete protected operation'
    assert 'typeof(SavesSystem).GetField("saving", BindingFlags.Static | BindingFlags.NonPublic)' in body, 'must use the official private static saving field'
    assert re.search(r'if\s*\(savingField != null && savingField\.FieldType == typeof\(bool\) && SavesSystem\.IsSaving\)\s*savingField\.SetValue\(null, false\);', body), 'only a verified bool latch left true by this failed call may be released'
    assert body.rstrip().endswith('throw;\n            }'), 'the original failure must propagate into the existing deferred retry chain'
    for relative in ('Common/Lifecycle/BossRushSaveCoordinatorEngine.cs', 'ModeH/ModeHSaveFlushCoordinator.cs'):
        code = clean_source((ROOT / relative).read_text(encoding='utf-8-sig'))
        assert code.count('BossRushSaveFileThrottle.RunSaveFile(() => { SavesSystem.SaveFile(false); });') == 1, relative + ': unique physical save must run inside the owned wrapper'
    print('SaveFileOwnedFailureRecoveryGuard: PASS')
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
