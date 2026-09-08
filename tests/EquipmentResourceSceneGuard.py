"""装备切图保护必须跳过精确的附加资源 Scene；共享层不反向依赖调试会话。"""
from pathlib import Path
from cs_source_util import clean_source

ROOT = Path(__file__).resolve().parents[1]


def main():
    manager = clean_source((ROOT / 'Common/Equipment/EquipmentEffectManager.cs').read_text(encoding='utf-8-sig'))
    gate = clean_source((ROOT / 'Utilities/SceneRuntimeGate.cs').read_text(encoding='utf-8-sig'))
    errors = []
    for method, mutation in [('OnSceneUnloaded', 'sceneTransitionProtection = true;'),
                             ('OnSceneLoaded', 'sceneTransitionProtection = false;')]:
        body = manager.split('protected virtual void ' + method + '(', 1)[1].split('\n        }', 1)[0]
        guard = 'if (SceneRuntimeGate.IsModResourceScene(scene)) return;'
        if guard not in body or mutation not in body or body.index(guard) > body.index(mutation):
            errors.append(method + ' 必须在更改切图保护之前排除附加资源 Scene')
    for forbidden in ('SkyIslandSession', 'SkyIslandSceneLease', 'StoneOutpostSceneLease', 'DebugAndTools'):
        if forbidden in manager or forbidden in gate:
            errors.append('共享场景/装备层不得反向依赖调试类型：' + forbidden)
    # 天空岛正式包是完整独立 Raid 关卡，必须走正常切图保护，不能再登记成附加资源 Scene。
    if 'SkyIslandResourceScenePath' in gate or 'SkyIslandWorld.unity' in gate:
        errors.append('天空岛不是附加资源 Scene，不得重新登记到切图保护排除表')
    for key, path, file in [
        ('StoneOutpostResourceScenePath', 'Assets/StoneOutpost/StoneOutpost.unity', 'DebugAndTools/ArenaPrototype/StoneOutpostSceneLease.cs'),
    ]:
        if f'{key} = "{path}";' not in gate:
            errors.append('共享资源路径变化：' + key)
        if f'string.Equals(scene.path, {key}, StringComparison.OrdinalIgnoreCase)' not in gate:
            errors.append('资源 Scene 必须精确忽略大小写比较：' + key)
        lease = clean_source((ROOT / file).read_text(encoding='utf-8-sig'))
        if f'const string ScenePath = SceneRuntimeGate.{key};' not in lease:
            errors.append('租约未使用共享资源路径事实源：' + file)
    project = (ROOT / 'tests/fixtures/EquipmentResourceScene/Regression.csproj').read_text(encoding='utf-8-sig')
    for source in ('Common/Equipment/EquipmentEffectManager.cs', 'Common/Equipment/EquipmentAbilityManager.cs',
                   'Integration/FlightTotem/FlightTotemEffectManager.cs', 'Integration/FlightTotem/FlightAbilityManager.cs',
                   'Utilities/SceneRuntimeGate.cs'):
        if source not in project:
            errors.append('回归必须执行生产源码：' + source)
    if '"EquipmentResourceScene"' not in (ROOT / 'tools/run_runtime_regressions.py').read_text(encoding='utf-8-sig'):
        errors.append('回归未登记统一入口')
    print('EquipmentResourceSceneGuard: ' + ('FAIL\n' + '\n'.join(errors) if errors else 'PASS'))
    return bool(errors)


if __name__ == '__main__':
    raise SystemExit(main())
