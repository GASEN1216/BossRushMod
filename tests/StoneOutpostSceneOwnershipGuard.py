"""独立前哨场景的延迟加载、卸载和光照必须由同一个会话回收；前哨撤离读秒与官方 CountDownArea 同语义。"""
import re
from pathlib import Path

from cs_source_util import clean_source

ROOT = Path(__file__).resolve().parents[1]
FILES = {
    'lease': 'DebugAndTools/ArenaPrototype/StoneOutpostSceneLease.cs',
    'session': 'DebugAndTools/ArenaPrototype/ArenaPrototypeSession.cs',
    'lighting': 'DebugAndTools/ArenaPrototype/ArenaPrototypeLighting.cs',
    'search': 'DebugAndTools/ArenaPrototype/StoneOutpostSearchPoint.cs',
    'host': 'ModBehaviour.cs',
}
# 撤离读秒的结构锚点。天空岛同款写法见 SkyIslandSession.Update 与 SkyIslandPlayerEntryGuard（CR-2026-09-10-008）。
EXIT_RING = 'Vector3.Distance(player.transform.position, exitMarker.position) < 1.3f'
RESET_BRANCH = 'else if (outpost && extractionHeld >= 0)'
HELD_INIT = 'if (extractionHeld < 0) extractionHeld = 0f;'
HELD_ADVANCE = 'if (View.ActiveView == null) extractionHeld += Time.deltaTime;'
HELD_REMAINING = 'float remaining = ExtractionHold - extractionHeld;'
HELD_WRITE = re.compile(r'\bextractionHeld\s*(?:[-+*/%]?=(?!=)|\+\+|--)|(?:\+\+|--)\s*extractionHeld\b')
PROTOTYPE_EXIT = re.compile(r'^if \(!outpost\) \{ Close\(true, "exit_marker"\); return; \}$')
OUTPOST_EXTRACT = re.compile(r'^if \(remaining <= 0\) \{ .*\bClose\(true, "outpost_extract"\); return; \}$')


def block_after(text, start):
    """从 start 之后第一个 { 起按括号配对切出块体，返回 (块体, 块尾之后的下标)；切不出返回 (None, -1)。"""
    open_at = text.find('{', start) if start >= 0 else -1
    if open_at < 0:
        return None, -1
    depth = 0
    for index in range(open_at, len(text)):
        if text[index] == '{':
            depth += 1
        elif text[index] == '}':
            depth -= 1
            if depth == 0:
                return text[open_at + 1:index], index + 1
    return None, -1


def extraction_errors(session):
    """前哨撤离读秒必须与官方 CountDownArea 同语义；session 须是剥过注释的源码（注释掉的累加不算接线）。

    官方按 Time.time 计时、任何 View 打开时不推进；TimeScaleManager 在暂停菜单与拍照模式把 timeScale 压到 0，
    而 PauseMenu 是 UIPanel 不是 View。游戏时间与 View 门缺一道，开着界面站在圈里的玩家都会被送回。
    按出口圆环分支的块结构逐行判断：整段找子串时，进场宽限、离圈分支里的同名 token 会让破坏照样绿。
    """
    label = FILES['session'] + ': '
    errors = []
    if not re.search(r'^[ \t]*private const float ExtractionHold = 3f;[ \t]*$', session, re.M):
        errors.append(label + '前哨撤离停留秒数必须是 private const float ExtractionHold = 3f;')
    if not re.search(r'^[ \t]*private float extractionHeld = -1;[ \t]*$', session, re.M):
        errors.append(label + '撤离读条初值必须是「不在圈里」：private float extractionHeld = -1;')
    if len(re.findall(r'\bextractionHeld\s*\+=', session)) != 1:
        errors.append(label + '撤离停留秒数只能有一处累加（出口圆环分支里那一行）')
    if session.count('"outpost_extract"') != 1:
        errors.append(label + '前哨撤离只能有一个完成点 Close(true, "outpost_extract")')
    update, _ = block_after(session, session.find('private void Update()'))
    if update is None or update.count(EXIT_RING) != 1:
        errors.append(label + 'Update 里找不到唯一的出口圆环判定：' + EXIT_RING)
        return errors
    ring_at = update.index(EXIT_RING)
    header = update[update.rfind('\n', 0, ring_at) + 1:ring_at].strip()
    ring, ring_end = block_after(update, ring_at)
    rest = update[ring_end:].lstrip() if ring is not None else ''
    if not header.startswith('if (') or not rest.startswith(RESET_BRANCH):
        errors.append(label + '出口圆环判定必须是 if 分支，且其后紧跟离圈归零分支：' + RESET_BRANCH)
        return errors
    lines = [line.strip() for line in ring.split('\n') if line.strip()]
    if HELD_ADVANCE not in lines:
        errors.append(label + '撤离读条必须在官方界面关闭时按游戏时间累加：圆环分支缺整行 ' + HELD_ADVANCE)
    if HELD_INIT not in lines:
        errors.append(label + '进圈必须从 0 开始计：圆环分支缺整行 ' + HELD_INIT)
    elif HELD_ADVANCE in lines and lines.index(HELD_INIT) > lines.index(HELD_ADVANCE):
        errors.append(label + '进圈置 0 必须排在累加之前')
    if HELD_REMAINING not in lines:
        errors.append(label + '剩余秒数必须由已停留的游戏时间算出：圆环分支缺整行 ' + HELD_REMAINING)
    if not any(OUTPOST_EXTRACT.match(line) for line in lines):
        errors.append(label + '前哨撤离必须由 if (remaining <= 0) 触发 Close(true, "outpost_extract")')
    for line in lines:
        if HELD_WRITE.search(line) and line not in (HELD_INIT, HELD_ADVANCE):
            errors.append(label + '圆环分支里只许进圈置 0 与累加（官方界面打开时读条冻结，不清零也不改写）：' + line)
        if 'unscaled' in line.lower() or 'realtimesincestartup' in line.lower():
            errors.append(label + '撤离读秒不得使用 unscaled / realtime 时间：' + line)
        if re.search(r'\bClose\s*\(', line) and not (PROTOTYPE_EXIT.match(line) or OUTPOST_EXTRACT.match(line)):
            errors.append(label + '圆环分支里的 Close 只许是试验场直接返回或 remaining <= 0 的前哨撤离：' + line)
    reset, _ = block_after(rest, 0)
    if reset is None or 'extractionHeld = -1;' not in [line.strip() for line in reset.split('\n')]:
        errors.append(label + '离开出口圆环必须真正归零：离圈分支缺整行 extractionHeld = -1;')
    return errors


def main():
    text = {key: (ROOT / value).read_text(encoding='utf-8-sig') for key, value in FILES.items()}
    required = {
        'lease': ['bundle.GetAllScenePaths()', 'LoadSceneMode.Additive', 'operation.completed -= OnLoaded',
                  'if (released) { Unload(); return; }', 'SceneManager.UnloadSceneAsync(scene)',
                  'operation.completed -= OnUnloaded', 'bundle.Unload(true)', 'if (loading == null) Unload()'],
        'session': ['SceneRuntimeGate.IsBaseHubSceneName', 'yield return sceneLease.BeginLoad(modDir)',
                    'while (!sceneLease.LoadResolved', 'sceneLease.LoadError != null',
                    'lighting.Apply(arena)', 'lighting.Dispose()', 'sceneLease.Release(',
                    'searchedPoints++', '"outpost_extract"'],
        'lighting': ['sunEnabled = sun.enabled', 'sun.enabled = sunEnabled', 'profile.Add<LightControl>(false)',
                     'volumeObject.SetActive(false)', 'Destroy(profile)', 'Destroy(component)',
                     'renderer.SetPropertyBlock(block)', 'RenderSettings.ambientSkyColor = sky'],
        'search': ['BossRushBuildingInteractableBase', 'if (collected) return', 'collected = true'],
        'host': ['if (StoneOutpostSceneLease.IsResourceScene(scene)) return;'],
    }
    errors = []
    for key, tokens in required.items():
        for token in tokens:
            if token not in text[key]:
                errors.append(f'{FILES[key]}: 缺少 {token}')
    close = text['session'].split('internal void Close(', 1)[1]
    if close.index('player.SetPosition(returnPosition)') > close.index('sceneLease.Release('):
        errors.append('必须在卸载前哨前返还玩家')
    host_load = text['host'].split('private void OnSceneLoaded(', 1)[1]
    if host_load.index('StoneOutpostSceneLease.IsResourceScene(scene)') > host_load.index('PrepareSceneRuntimeForLoad()'):
        errors.append('资源场景不得触发正式关卡清理')
    for forbidden in ('SceneInfoCollection.Entries.Add', 'SaveFile(', 'SavesSystem.Save', 'PlayerPrefs.Set', 'LoadSceneMode.Single'):
        if any(forbidden in value for key, value in text.items() if key != 'host'):
            errors.append('实验场景不得使用 ' + forbidden)
    errors.extend(extraction_errors(clean_source(text['session'])))
    compile_list = (ROOT / 'compile_official.bat').read_text(encoding='utf-8-sig')
    for key in ('lease', 'lighting', 'search'):
        if FILES[key].replace('/', '\\') not in compile_list:
            errors.append('编译清单缺少 ' + FILES[key])
    print('StoneOutpostSceneOwnershipGuard: ' + ('FAIL\n' + '\n'.join(errors) if errors else 'PASS'))
    return bool(errors)


if __name__ == '__main__':
    raise SystemExit(main())
