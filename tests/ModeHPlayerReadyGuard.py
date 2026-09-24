"""Mode H 普通入口直达选人；动态认证仅经 F3 显式门，保留交战目标/拍铃接线。"""
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tests"))
from cs_source_util import clean_source, strip_comments
from ModeHOneClickFlowGuard import method_body, squeeze

FILES = {
    "scene": "ModeH/ModeHRuntimeModule_SceneFlow.cs",
    "recovery": "ModeH/ModeHRuntimeModule_Recovery.cs",
    "f3": "DebugAndTools/F3GameplayValidationRunner.cs",
    "cert": "ModeH/ModeHProductionCertification.cs",
    "combat": "ModeH/ModeHCombatControl.cs",
    "flow": "ModeH/ModeHRuntimeModule_CombatFlow.cs",
    "lease": "ModeH/ModeHSpectatorLease.cs",
    "registry": "ModeH/ModeHCommandCompatibilityRegistry.cs",
}


def check(sources):
    errors = []

    def body(key, signature):
        result = method_body(strip_comments(sources[key]) if key == "f3" else clean_source(sources[key]), signature)
        if result is None:
            errors.append("Missing method: " + signature)
        return squeeze(result)

    def need(value, fragment, message):
        if fragment not in value:
            errors.append(message)

    start = body("scene", "private void StartCertification()")
    need(start, "if (!_certification.TryUseReleaseCatalog())", "普通入场必须先走发布目录")
    need(start, "CreateDraftingSeason(_certification.Report);", "发布目录成功后必须直接选人")
    resume = body("recovery", "private bool TryPrepareSeasonResume(")
    need(resume, "if (!restoredCertification.TryUseReleaseCatalog())", "续赛同样只使用发布目录")
    for label, entry in (("新赛季", start), ("续赛", resume)):
        for forbidden in ("DevModeEnabled", "TryUseCachedReport", "TryRestoreSeasonReport", "DriveCertification(", "StartCoroutine("):
            if forbidden in entry:
                errors.append(label + "入口不得分构建或运行动态认证: " + forbidden)
    gate = body("f3", "internal static bool CanRunModeHCertification(")
    for required in ("!ModBehaviour.DevModeEnabled", "!IsDedicatedCurrentSlot()", "(_instance != null && _instance._running) || SavesSystem.IsSaving || SceneLoader.IsSceneLoading", "host.ModeHRuntime.CanRunCertificationFromF3(out reason)"):
        need(gate, required, "F3 外层门缺少 " + required)
    runtime_gate = body("scene", "internal bool CanRunCertificationFromF3(")
    for required in ("!ModBehaviour.DevModeEnabled", "_runState.Lifecycle != ModeHLifecycle.Drafting", "_season == null", "!_arenaLease.IsActive", "!_spectatorLease.IsActive", "_certificationFromF3 || _certificationRoutine != null"):
        need(runtime_gate, required, "F3 运行时门缺少 " + required)
    button = body("f3", "private void StartModeHCertificationFromF3()")
    order = [button.find(x) for x in ("CanRunModeHCertification(this, out reason)", "HideF3DebugCheatMenu();", "ModeHRuntime.StartCertificationFromF3(out reason)")]
    if -1 in order or order != sorted(order):
        errors.append("F3 必须先校验、关闭自身，再启动认证释放选人页暂停")
    diagnostic_start = body("scene", "internal bool StartCertificationFromF3(")
    need(diagnostic_start, "if (!F3GameplayValidationRunner.CanRunModeHCertification(_owner, out reason)) return false;", "F3 运行时启动不能绕过测试档门")
    for signature in ("private IEnumerator DriveCertification(", "private void RestorePlayerFlowAfterCertification(", "private void CancelSetupFromDiagnostics()"):
        diagnostic = body("scene", signature)
        for forbidden in ("CreateDraftingSeason(", "TryPersistSeason(", "StageWrite(", "RequestCertificationCacheWrite(", "RequestCertificationCacheInvalidate(", "CancelPendingEntry("):
            if forbidden in diagnostic:
                errors.append("F3 测试完成/取消不得改赛季、缓存或票据: " + signature + " / " + forbidden)
    if "#if BOSSRUSH_DEV\n        private void StartModeHCertificationFromF3()" not in sources["f3"].replace("\r\n", "\n"):
        errors.append("F3 按钮入口必须处在 Dev 编译区块")
    run = body("cert", "internal IEnumerator Run(")
    need(run, "if (!ModBehaviour.DevModeEnabled)", "动态认证必须有 Dev 门")
    if run.find('"certification_dev_only"') > run.find("_running = true;"):
        errors.append("Dev 拒绝必须早于动态诊断")
    release = body("cert", "internal bool TryUseReleaseCatalog()")
    for forbidden in ("CreateCharacter", "StartCoroutine", "RecordPassed(", "VerifiedBehavior", ".Hurt("):
        if forbidden in release:
            errors.append("发布目录不得制造诊断或实测证据: " + forbidden)
    need(release, "PassesStaticAudit(ResolveAuditedPreset(key), out error)", "发布目录仍需实际预设静态资格")
    need(release, "ModeHCommandCompatibilityStatus.ReleaseSupported", "发布资格与实测状态必须分开")
    need(release, 'spawnTimelineDigest = "release_contract_v1"', "报告必须标注发布契约来源")
    need(release, "if (!_report.overallPassed) return false;", "不能绕过候选/原型/口令门槛")
    targets = body("combat", "private void RefreshFireTargets(")
    need(targets, "WakeArenaOpponent(character, origin.mainDamageReceiver);", "复用存活敌人扫描为敌方补目标")
    need(targets, "WakeArenaOpponent(origin, _fireContext.NearestEnemy);", "同一轮为我方补最近对手")
    wake = body("combat", "private static void WakeArenaOpponent(")
    need(wake, "LevelManager.Instance.ControllingCharacter == character", "ERROR 受控选手不得被接管目标")
    need(wake, "!character.gameObject.activeInHierarchy", "隔离阶段不得唤醒")
    need(wake, "Team.IsEnemy(character.Team, current.Team)) return;", "保留仍有效的拍铃目标")
    need(wake, "ai.searchedEnemy = target; ai.noticed = true; ai.SetNoticedToTarget(target);", "补目标必须同步官方警觉")
    spawn = body("flow", "private IEnumerator DriveCompleteMatchSpawning()")
    need(spawn, "_spectatorLease.StartAcceptingBell();", "每场成功开打必须重新开铃门")
    if spawn.find("StartAcceptingBell") < spawn.find('"combat_transition_rejected"'):
        errors.append("开铃门必须晚于成功进入战斗")
    hud = body("flow", "private void TickActiveCombat(")
    need(hud, "&& (_spectatorLease == null || _spectatorLease.IsBellAccepting)", "HUD 与点击使用相同租约门")
    need(body("lease", "public void StartAcceptingBell()"), "_bellAccepting = IsActive;", "释放的租约不能重开")
    restore = body("registry", "internal static void RestoreCertificationEffects(")
    need(restore, "effect.status > (int)ModeHCommandCompatibilityStatus.ReleaseSupported", "缓存接受追加状态但拒绝未来未知状态")
    return errors


PROBES = [
    ("scene", "CreateDraftingSeason(_certification.Report);", ""),
    ("f3", "(_instance != null && _instance._running) || SavesSystem.IsSaving", "IsRunning || SavesSystem.IsSaving"),
    ("recovery", "if (!restoredCertification.TryUseReleaseCatalog())", "if (!restoredCertification.TryUseCachedReport())"),
    ("f3", "if (!IsDedicatedCurrentSlot())\n            { reason = L10n.T(\"请先在基地将当前槽标记为专用测试档\"", "if (false)\n            { reason = L10n.T(\"请先在基地将当前槽标记为专用测试档\""),
    ("scene", "_runState.Lifecycle != ModeHLifecycle.Drafting || _season == null", "_season == null"),
    ("scene", "if (!F3GameplayValidationRunner.CanRunModeHCertification(_owner, out reason)) return false;", ""),
    ("cert", "if (!ModBehaviour.DevModeEnabled)", "if (false)"),
    ("cert", 'spawnTimelineDigest = "release_contract_v1"', 'spawnTimelineDigest = ""'),
    ("combat", "WakeArenaOpponent(character, origin.mainDamageReceiver);", ""),
    ("combat", "WakeArenaOpponent(origin, _fireContext.NearestEnemy);", ""),
    ("combat", "Team.IsEnemy(character.Team, current.Team)) return;", "false) return;"),
    ("flow", "_spectatorLease.StartAcceptingBell();", "_spectatorLease.StopAcceptingBell();"),
    ("lease", "_bellAccepting = IsActive;", "_bellAccepting = true;"),
    ("registry", "effect.status > (int)ModeHCommandCompatibilityStatus.ReleaseSupported", "effect.status > 99"),
]


def main():
    sources = {key: (ROOT / path).read_text(encoding="utf-8-sig") for key, path in FILES.items()}
    errors = check(sources)
    for key, old, new in PROBES:
        if sources[key].count(old) != 1:
            errors.append("Mutation anchor must be unique: " + old)
            continue
        mutant = dict(sources)
        mutant[key] = mutant[key].replace(old, new, 1)
        if not check(mutant):
            errors.append("Mutation survived: " + old)
    for error in errors:
        print("FAIL " + error)
    print("ModeHPlayerReadyGuard: " + ("FAIL" if errors else "PASS") + f" ({len(PROBES)} mutation probes)")
    return int(bool(errors))


if __name__ == "__main__":
    sys.exit(main())
