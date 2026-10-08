"""三个自定义 Boss 的 OnAwake 在 Boss 池已先行初始化时必须补注册。

背景（2026-10-08 实机日志锁定）：图鉴 / 遗种巢运行模块的 OnAwake 基地预热会先跑
`InitializeEnemyPresets`，而三个自定义 Boss 模块在注册顺序上排在预热模块之后——
预热时它们内部 owner 尚未绑定，注册被跳过，池缺 3 个自定义 Boss（日志：44/44 +
`[DragonDescendant] enemyPresets 为空，无法注册`）。修复：每个模块在自己的 OnAwake 里
检查「池已初始化且自己不在池里」时补一次幂等注册，并让筛选状态与图鉴 / 血脉目录重建。

本守卫检查绑定、条件与调用顺序；生产方法执行时序及禁用名单保留由
WavesArenaPresetWeight 执行回归验证，结构守卫不替代行为与实机验证。
"""
from pathlib import Path
import re
import sys

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tests"))
from cs_source_util import clean_source

CASES = [
    ("Integration/DragonKing/DragonKingRuntimeModuleHostBridge.cs",
     "DragonKingConfig.BossNameKey", "RegisterDragonKingPreset();"),
    ("Integration/DragonDescendant/DragonDescendantRuntimeModuleHostBridge.cs",
     "DragonDescendantConfig.BOSS_NAME_KEY", "RegisterDragonDescendantPreset();"),
    ("Integration/PhantomWitch/PhantomWitchRuntimeModuleHostBridge.cs",
     "PhantomWitchConfig.BossNameKey", "RegisterPhantomWitchPreset();"),
]


def body(source, signature):
    start = source.index("{", source.index(signature))
    depth = 1
    for end in range(start + 1, len(source)):
        depth += (source[end] == "{") - (source[end] == "}")
        if depth == 0:
            return source[start + 1:end]
    raise AssertionError("unclosed method: " + signature)


def main():
    for path, key, register in CASES:
        source = clean_source((ROOT / path).read_text(encoding="utf-8-sig"))
        awake = re.sub(r"\s+", "", body(source, "public override void OnAwake("))
        module = Path(path).stem.removesuffix("HostBridge")
        prefix = "this.owner=owner;if(owner==null)return;owner.Attach" + module + "(this);"
        assert awake.startswith(prefix), "[AttachOrder] " + path + " 必须先绑定并判空，再 attach"
        condition = ("if(WavesArenaRuntimeModule.EnemyPresetsInitialized&&"
                     "FindArenaEnemyPreset(" + key + ")==null)")
        assert awake[len(prefix):].startswith(condition + "{"), \
            "[RecoveryGate] " + path + " 补注册必须同时满足已初始化与当前 Boss 缺席"
        recovery = awake[len(prefix) + len(condition):]
        expected = "{" + register + "owner.ResetBossPoolFilterStateForArena();}"
        assert recovery == expected, \
            "[RecoveryOrder] " + path + " 必须先注册、再作废筛选配置与目录"
    filter_source = clean_source((ROOT / "BossFilter/BossFilter.cs").read_text(encoding="utf-8-sig"))
    start = re.sub(r"\s+", "", body(filter_source, "public override void OnStart()"))
    expected_start = ("if(owner.BossFilterEnemyPresets==null||owner.BossFilterEnemyPresets.Count==0)return;"
                      "ResetBossPoolFilterStateForEnemyPresetRefresh();InitializeBossPoolFilter();")
    assert start == expected_start, \
        "[ConfiguredStart] BossFilter/BossFilter.cs 必须在 Start 重建预热过的池，恢复真实配置"
    host_source = clean_source((ROOT / "ModBehaviour.cs").read_text(encoding="utf-8-sig"))
    host_start = re.sub(r"\s+", "", body(host_source, "void Start()"))
    assert host_start == "StartIntegrationRuntime();runtimeModuleHost.OnStart();", \
        "[HostStartOrder] ModBehaviour.Start 必须先完成配置装载，再分发模块 OnStart"
    print("CustomBossArenaPresetAwakeRegistrationGuard: PASS")
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except (AssertionError, ValueError) as error:
        print("CustomBossArenaPresetAwakeRegistrationGuard: FAIL - " + str(error))
        sys.exit(1)
