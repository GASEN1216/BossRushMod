"""Mode G 入口服务归属：保留注册 shell、每局核心与原宿主 tick/清理链。"""
from pathlib import Path
import re
from cs_source_util import clean_source

ROOT = Path(__file__).resolve().parents[1]
COUNT = 0


def require(condition, message):
    global COUNT
    COUNT += 1
    if not condition:
        raise AssertionError(message)


def read(path):
    return clean_source((ROOT / path).read_text(encoding="utf-8-sig"))


def flat(source):
    return re.sub(r"\s+", "", source)


def block(source, marker):
    require(source.count(marker) == 1, "唯一声明缺失或重复：" + marker)
    opening = source.index("{", source.index(marker))
    depth = 1
    end = opening + 1
    while depth:
        depth += (source[end] == "{") - (source[end] == "}")
        end += 1
    return source[opening + 1:end - 1]


def method(source, name):
    matches = list(re.finditer(r"(?:private|internal|public)\s+(?:override\s+)?(?:static\s+)?(?:async\s+)?[\w<>\[\]]+\s+" + name + r"\(", source))
    require(len(matches) == 1, "唯一方法缺失或重复：" + name)
    return block(source, matches[0].group())


def ordered(source, *tokens):
    position = -1
    source = flat(source)
    for token in tokens:
        position = source.find(flat(token), position + 1)
        require(position >= 0, "生产顺序断开：" + token)


def main():
    paths = ("ModeG/ModeGEntry.cs", "ModeG/ModeGRuntimeBridge.cs", "ModeG/ModeGSpawnTransaction.cs")
    parts = [read(path) for path in paths]
    owner = "internal sealed partial class ModeGEntryRuntime"
    for path, source in zip(paths, parts):
        require(source.count(owner) == 1, path + " 业务必须归 ModeGEntryRuntime")
        require("partial class ModBehaviour" not in source, path + " 残留宿主业务")
    entry, runtime_bridge, transaction = parts
    service = read("ModeG/ModeGEntryRuntimeServices.cs")
    host = read("ModeG/ModeGEntryHostBridge.cs")
    require(service.count(owner) == 1 and owner + " :" not in service, "入口服务不得注册为核心模块")
    service_sources = "\n".join(parts + [service])
    require(not re.search(r"\bModBehaviour\s+\w+\s*[;=]", service_sources), "服务不得持有业务宿主字段")
    require(not re.search(r"\bModBehaviour\s*\.\s*Instance", service_sources), "服务不得经单例改绑迟到回调")
    for declaration in (
        "private bool modeGActive = false;", "private ModeGRuntimeModule modeGRuntime;",
        "private ModeGHUD modeGHUD;", "private ModeGEntryPreview modeGEntryPreview;",
        "private int modeGSelectedContractId = -1;", "private static long modeGSessionCounter = 0;",
        "private static string modeGPlayerGuidCache;", "private static MethodInfo modeGGetSteamIdMethod;",
        "private static bool modeGSteamIdLookupFailed;",
    ):
        require(flat(declaration) in flat(entry), "入口状态未归服务：" + declaration)
        require(flat(declaration) not in flat(host), "宿主复制入口状态：" + declaration)
    require(flat("private ModeGEntryRuntime modeGEntryRuntime;") in flat(host), "宿主服务持有字段缺失")
    require(flat("private bool modeGActive { get { return modeGEntryRuntime != null && modeGEntryRuntime.IsRunActiveForHost; } }") in flat(host), "active 旧入口必须只读转发")
    require(flat("private ModeGRuntimeModule modeGRuntime { get { return modeGEntryRuntime != null ? modeGEntryRuntime.CurrentRunForHost : null; } }") in flat(host), "core 旧入口必须只读转发")

    bindings = {
        "BindEntryQueries": "() => IsActive, () => modeDActive, () => modeEActive, () => modeFActive, () => IsZombieModeActive, () => config != null ? config.modeGAbandonHotkey : 0",
        "BindEntryServices": "DetectBossRushTicketItem, DetectFactionFlag, DetectBloodhuntTransponder, TryConsumeModeEntryItem, GetBossRushTicketTypeId, ShowMessage, ShowBigBanner",
        "BindBossPoolQueries": "InitializeEnemyPresets, InitializeBossPoolFilter, EnsureCharacterPresetsCacheReady, GetFilteredEnemyPresets, () => cachedCharacterPresets, IsDragonDescendantPreset, IsDragonKingPreset, IsPhantomWitchPreset, IsManagedBossPreset, BuildGeneralBossLootCandidateIdSet",
        "BindSignatureQueries": "FindQuestionMarkPreset, FindFallbackPreset, FindDragonKingBasePreset, FindPhantomWitchBasePreset",
        "BindSpawnServices": "SpawnEnemyCoreInternalAsync, PrepareManagedDragonDescendantAsync, PrepareManagedDragonKingAsync, PrepareManagedPhantomWitchAsync, ActivateModeGManagedCharacter, CleanupModeGManagedCharacter",
        "BindArenaServices": "GetCurrentSceneSpawnPoints, SetCurrentMapSpawnPoints, InitializeItemValueCacheAsync, TryCreateArenaDifficultyEntryPoint, PreCacheMapSpawnerPositions, DisableAllSpawners, ClearEnemiesForBossRush, () => spawnersDisabled, value => spawnersDisabled = value, value => bossRushArenaActive = value, value => bossRushArenaPlanned = value",
    }
    bind = method(host, "GetModeGEntryRuntimeForHost")
    expected = "if (modeGEntryRuntime != null) return modeGEntryRuntime; ModeGEntryRuntime runtime = new ModeGEntryRuntime();"
    for name, arguments in bindings.items():
        expected += "runtime." + name + "(" + arguments + ");"
        binding_body = method(service, name)
        assignments = re.findall(r"\b(\w+) = (\w+)Callback;", binding_body)
        require(bool(assignments), "绑定方法没有逐项赋值：" + name)
        require(all(left == right for left, right in assignments), "绑定目标错位：" + name)
        require(flat(binding_body) == "".join(left + "=" + right + "Callback;" for left, right in assignments), "绑定方法增加业务：" + name)
    expected += "modeGEntryRuntime = runtime; return runtime;"
    require(flat(bind) == flat(expected), "宿主必须完整绑定原回调后再发布服务")

    bridges = {
        "SetModeGSelectedContractId": (False, "contractId"), "DetectFateEchoRelic": (True, ""),
        "IsModeGLoadoutEligible": (True, ""), "GetOrCreateModeGEntryPreview": (True, ""),
        "IsModeGEntryPreviewValidForCurrentScene": (True, "preview"), "TryStartModeG": (True, ""),
        "TryRefundModeGPendingPrepaidTicket": (True, ""), "RollbackModeGStagedArenaEntry": (False, ""),
        "TryRefundModeGStartupItem": (True, "typeId, displayName"), "GetModeGTicketTypeId": (True, ""),
        "StartModeGRuntime": (True, "preview, refundTicketOnStartupFailure, refundRelicOnStartupFailure, out startupRefundOwnedByRuntime"),
        "CreateModeGBossSnapshot": (True, ""), "SpawnModeGManagedBossAsync": (True, "info, position, waveNumber, ctx"),
        "GetModeGSpawnPositions": (True, "waveIndex, count, variant, temperament, isNemesisWave"),
        "PrepareModeGArenaRuntime": (True, "preview"), "CommitModeGArenaEntry": (True, "preview"),
        "ShowModeGWaveBanner": (False, "waveIndex, wave, axis, temperament"),
        "SpawnModeGOfficialBossAsync": (True, "preset, position, waveNumber, onCommit"),
        "GetModeGOfficialBossPoolKeys": (True, ""), "FindModeGOfficialPresetByKey": (True, "key"),
        "GetModeGRewardCandidates": (True, ""),
        "DispatchModeGManagedBossSpawnAsync": (True, "preset, position, managedContext, deferActivationUntilNextFrame"),
    }
    for name, (returns, arguments) in bridges.items():
        expected = ("return " if returns else "") + "GetModeGEntryRuntimeForHost()." + name + "(" + arguments + ");"
        require(flat(method(host, name)) == flat(expected), "兼容桥必须直接转发参数和返回值：" + name)
    for name, arguments in (("UpdateModeG", "deltaTime"), ("ShutdownModeG", "")):
        expected = "if (modeGEntryRuntime != null) modeGEntryRuntime." + name + "(" + arguments + ");"
        require(flat(method(host, name)) == flat(expected), "空闲生命周期不能创建服务：" + name)
    names = re.findall(r"(?:private|internal|public)\s+(?:static\s+)?(?:async\s+)?[\w<>\[\]]+\s+(\w+)\(", host)
    require(set(names) == set(bridges) | {"GetModeGEntryRuntimeForHost", "UpdateModeG", "ShutdownModeG"}, "宿主新增业务方法")

    start = method(entry, "StartModeGRuntime")
    ordered(start, "modeGRuntime = new ModeGRuntimeModule();", "modeGRuntime.Initialize(state, preview)",
            "state.TryAdvanceLifecycle(ModeGLifecyclePhase.Starting)", "ModeGRunContext.Bind(state, modeGRuntime);",
            "modeGRuntime.ArmStartupRefund(", "startupRefundOwnedByRuntime = true;", "modeGRuntime.StartRun()",
            "modeGHUD = new ModeGHUD(modeGRuntime);", "modeGActive = true;", "ConsumeModeGEntryPreview();")
    ordered(method(entry, "UpdateModeG"), "TryHandleModeGAbandonHotkey();", "modeGRuntime.Update(deltaTime);", "modeGHUD.Update(deltaTime);", "state.IsTerminal", "ShutdownModeG();")
    shutdown = method(entry, "ShutdownModeG")
    ordered(shutdown, "ModeGInteractable.CloseActiveConfirmation();", "ModeGAbandonPresenter.CloseIfOpen();", "modeGHUD.Dispose();", "modeGRuntime.Dispose();", "ModeGRunContext.Unbind(state);", "modeGActive = false;")
    require(not re.search(r"\b(?:get\w+|set\w+|SpawnEnemyCoreInternalAsync|PrepareManaged\w+|ActivateModeGManagedCharacter|CleanupModeGManagedCharacter)\s*=\s*null", service_sources), "单局清理不得清空原宿主回调")
    require("!started && !startupRefundOwnedByRuntime" in method(entry, "TryStartModeG"), "外层退款必须遵守已移交责任")
    ordered(method(transaction, "SpawnModeGOfficialBossAsync"), "ModeGRunState state = ModeGRunContext.Current;", "await SpawnEnemyCoreInternalAsync(", "state.RegisterStagingBoss(")
    dispatcher = method(transaction, "DispatchModeGManagedBossSpawnAsync")
    require(dispatcher.count("ctx.IsOwnerValid()") == 2, "managed 工厂 await 前后必须重验 owner")
    require("prepared.Handle.CleanupOnce(ManagedBossCleanupReason.OwnerInvalid);" in dispatcher, "迟到 managed handle 缺清理")

    registration = read("ModBehaviourRuntimeModules.cs")
    require(registration.count("runtimeModuleHost.Register(new ModeGRuntimeModule());") == 1, "注册 shell 来源必须保留")
    sources = [(str(p.relative_to(ROOT)), clean_source(p.read_text(encoding="utf-8-sig")))
               for p in (ROOT / "ModeG").glob("*.cs")]
    constructors = [(path, source.count("new ModeGRuntimeModule(")) for path, source in sources if "new ModeGRuntimeModule(" in source]
    require(constructors == [(str(Path("ModeG/ModeGEntry.cs")), 1)], "Mode G 每局核心必须只由入口创建")
    require("Register(new ModeGEntryRuntime(" not in registration, "入口服务不得作为注册模块运行")
    core = read("ModeG/ModeGRuntimeModule.cs")
    require(flat(method(core, "OnUpdate")) == flat("if (ModeGRunContext.CurrentModule != this) return; DriveCore(deltaTime);"), "注册 shell 不能驱动另一局核心")
    require("_dispatcherRef = _host.DispatchModeGManagedBossSpawnAsync;" in core, "核心 dispatcher 必须绑定旧宿主桥")
    require("ModBehaviour.ManagedBossSpawnDispatcher = _dispatcherRef;" in core, "dispatcher 引用必须原样发布")
    require("ReferenceEquals(ModBehaviour.ManagedBossSpawnDispatcher, _dispatcherRef)" in read("ModeG/ModeGRuntimeModule_PublicApiAndShutdown.cs"), "清理必须比较 dispatcher owner")
    root_host = read("ModBehaviour.cs")
    ordered(method(root_host.replace("void Update()", "private void Update()"), "Update"), "runtimeModuleHost.OnUpdate(", "TickModeRuntimeGroup(")
    hooks = read("Utilities/ModeRuntimeHooks.cs")
    ordered(method(hooks, "TickModeRuntimeGroup"), "TickModeFRuntime(deltaTime);", "UpdateModeG(deltaTime);", "TickZombieModeRuntime(unscaledDeltaTime);")
    ordered(method(hooks, "CleanupModeRuntimeForSceneLoad"), "modeGRuntime.End(ModeGExitReason.SceneChanged);", "ShutdownModeG();")
    require("ShutdownModeG();" in method(hooks, "CleanupModeRuntimeOnDestroy"), "宿主销毁缺入口清理")
    require("StartModeGRuntime(preview, false, false, out runtimeOwnsRefund)" in read("DebugAndTools/F3GameplayValidationRunner.cs"), "F3 旧 private 入口断开")
    print("PASS ModeGEntryOwnershipGuard: %d assertions" % COUNT)


if __name__ == "__main__":
    try:
        main()
    except AssertionError as error:
        print("FAIL ModeGEntryOwnershipGuard:", error)
        raise SystemExit(1)
