"""独立天空岛使用官方角色、死亡与墓碑流程；禁止回退为基地外置场地。"""
from pathlib import Path
from cs_source_util import clean_source

ROOT = Path(__file__).resolve().parents[1]
SOURCE = clean_source((ROOT / 'DebugAndTools/SkyIsland/SkyIslandOfficialContract.cs').read_text(encoding='utf-8-sig'))
SESSION = clean_source((ROOT / 'DebugAndTools/SkyIsland/SkyIslandSession.cs').read_text(encoding='utf-8-sig'))
errors = []
for token in ['level.gameObject.scene.handle != scene.handle', 'config.gameObject.scene.handle != scene.handle',
              'core.gameObject.scene.handle != scene.handle', '!level.IsRaidMap || level.IsBaseLevel',
              '!LevelConfig.SaveCharacter || !LevelConfig.SpawnTomb', 'config.timeOfDayConfig == null',
              'config.startBuffPrefabs == null', 'player.gameObject.scene.handle != scene.handle',
              'MultiSceneCore.ActiveSubSceneID', 'SceneLocationsProvider.GetProviderOfScene(scene)',
              'DeadBodyManager.Instance == null']:
    if token not in SOURCE:
        errors.append('独立场景合同缺少必要验证：' + token)
if 'SkyIslandOfficialContract.Verify(' not in SESSION:
    errors.append('正式天空岛会话未接官方独立场景验证')
# CR-2026-09-08-005：激活前合同必须真的被调用，而且必须早于官方服务激活。
# 只断言方法定义存在是无效断言 —— 把调用点注释掉照样全绿。
LEASE = clean_source((ROOT / 'DebugAndTools/SkyIsland/SkyIslandRaidLease.cs').read_text(encoding='utf-8-sig'))
for token in ['VerifyBeforeActivation(scene, services, world, out contractError)',
              'SkyIslandSceneReferenceBridge.BindInitializationScene(this, scene)',
              'SkyIslandSceneReferenceBridge.AbortInitialization(this, e.Message)']:
    if token not in LEASE:
        errors.append('独立租约缺少激活前验证接线：' + token)
if 'VerifyBeforeActivation' in LEASE and 'services.SetActive(true)' in LEASE:
    if LEASE.index('VerifyBeforeActivation') > LEASE.index('services.SetActive(true)'):
        errors.append('激活前合同必须早于 services.SetActive(true)')
# CR-2026-09-10-002：合同要求 timeOfDayConfig / startBuffPrefabs 非空，而这两项包里没有、
# 只能由租约注入。注入排到验证之后 = 每次进岛都被自己的合同判死，且日志看不出是代码顺序问题。
if 'VerifyBeforeActivation' in LEASE and 'config.timeOfDayConfig = timeOfDay' in LEASE:
    if LEASE.index('config.timeOfDayConfig = timeOfDay') > LEASE.index('VerifyBeforeActivation'):
        errors.append('官方天气与起始 Buff 必须在激活前合同之前注入，否则合同的 null 判据必红')
if 'internal static bool VerifyBeforeActivation(' not in SOURCE:
    errors.append('缺少激活前合同实现')
for token in ['providers.Length != 1', 'entry.cachedLocations', 'vertexCount > 4095']:
    if token not in SOURCE:
        errors.append('激活前合同缺少必要验证：' + token)
for name in ['DuckNpcModule.cs', 'Permanent/PermanentDuckNpcModule.cs']:
    source = clean_source((ROOT / 'Integration/NPCs/DuckNpc' / name).read_text(encoding='utf-8-sig'))
    if 'string.Equals(sceneName, "SkyIslandRaid", StringComparison.Ordinal)' not in source:
        errors.append('通用 NPC 刷新未排除地图居民 owner：' + name)
if list((ROOT / 'DebugAndTools/SkyIsland').glob('SkyIslandDeath*.cs')):
    errors.append('独立 Raid 场景应沿用官方死亡，不得再引入基地死亡适配')
runner = (ROOT / 'tools/run_runtime_regressions.py').read_text(encoding='utf-8-sig')
if '"SkyIslandOfficialContract"' not in runner:
    errors.append('真实合同与官方 DLL 回归未登记统一入口')
if errors:
    for error in errors:
        print('SkyIslandOfficialContractGuard: FAIL ' + error)
    raise SystemExit(1)
print('SkyIslandOfficialContractGuard: PASS')
