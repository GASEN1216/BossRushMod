# -*- coding: utf-8 -*-
"""潮闸镇 Dev 演练与 F3 潮闸镇区：只在 Dev 构建、不写存档、改过的东西一定还原（2026-11-06）。

演练（`DebugAndTools/F3GameplayValidationTidegateDrill*.cs`，钩子 `Tidegate/TidegateDrillHooks.cs`）在玩家这一趟潮闸镇出击里
拨潮相、驱动阀门 / 大闸 / 灯塔、把主角搬来搬去、强制轻装 / 超重；F3「NPC/剧情」页的潮闸镇区
（`DebugAndTools/TidegateProgressDebugControls.cs`）能清空剧情进度、手动拨潮相。它们能留在仓库里，靠的是下面这些约束：

1. **只在 Dev 构建里存在**：四份文件整份包在 `#if BOSSRUSH_DEV` 里并登记编译清单；F3 面板按钮与处理函数在 `#if` 里；
   生产文件里的钩子点（潮汐偏移、手轮旁路、游泳覆盖、刷怪距离、字幕与音效计数）都在 `#if BOSSRUSH_DEV` 里，
   手轮旁路在正式构建恒为 false，且旁路时不读也不消耗背包里真有的手轮。
2. **演练不写存档、不收录、不开箱、不发物品、不写官方时钟、不切图、不收会话**（禁用清单）；清空进度只在 F3 手动按钮里、先过专用测试档与基地门。
3. **入口先过 CheckTidegateStartGate，再置 _tidegateDrill，最后开会话**；标志只在 CompleteSession 复位，复位前先记下给收尾用。
4. **会话外壳按 _tidegateDrill 不写运行标记、不抑制提示条、收尾不跑主套件清理**；报告头如实写 read_only=false / drill=true。
5. **还原**：潮相偏移回到开跑前、游泳覆盖清掉、机关复位、全图显示结束、增援移走、主角送回开跑处、退订换相；
   CompleteSession 里另有同步兜底；游泳两条的覆盖在 finally 里清；撤离读条那条在 finally 里离开撤离环。
6. **站位离出口 5 m 外、深水点离出口 6 m 外**：站进开着的撤离环读满会真的返航。
7. 只读套件不引用演练；纯判据有执行回归（`TidegateDrillJudges`）。

守卫钉结构，证明不了演练跑起来是对的——那要 owner 在 Dev 构建里按一次演练按钮，AI 读报告。
"""
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tests"))
from cs_source_util import clean_source  # noqa: E402

DRILL = "DebugAndTools/F3GameplayValidationTidegateDrill.cs"
CASES = "DebugAndTools/F3GameplayValidationTidegateDrillCases.cs"
ABLATION = "DebugAndTools/F3GameplayValidationTidegatePerfAblation.cs"
KEYLEAD = "DebugAndTools/F3GameplayValidationTidegateDrillKeyLead.cs"
HOOKS = "Tidegate/TidegateDrillHooks.cs"
CONTROLS = "DebugAndTools/TidegateProgressDebugControls.cs"
PETNEST = "DebugAndTools/CampaignPetNestDebugControls.cs"
RUNNER = "DebugAndTools/F3GameplayValidationRunner.cs"
EXECUTION = "DebugAndTools/F3GameplayValidationExecution.cs"
TIDE = "Tidegate/TidegateTide.cs"
DEVICES = "Tidegate/TidegateDevices.cs"
WATER = "Tidegate/TidegateWater.cs"
ENEMIES = "Tidegate/TidegateEnemies.cs"
SESSION = "Tidegate/TidegateSession.cs"
SOUNDS = "Tidegate/TidegateSounds.cs"
REGRESSIONS = "tools/run_runtime_regressions.py"
BAT = "compile_official.bat"
READ_ONLY = ("DebugAndTools/F3GameplayValidationSkyIsland.cs", "DebugAndTools/F3GameplayValidationSkyIslandCases.cs",
             "DebugAndTools/F3GameplayValidationSkyIslandRuntimeCases.cs")
DEV_FILES = (DRILL, CASES, ABLATION, KEYLEAD, HOOKS, CONTROLS)
PRODUCTION_HOOKED = (TIDE, DEVICES, WATER, ENEMIES, SESSION, SOUNDS)
BACKSLASH = chr(92)

# 生产文件里只许出现在 #if BOSSRUSH_DEV 真分支里的钩子标识。
HOOK_TOKENS = re.compile(r"\b(?:DevOffsetHours|DevNoteSpawn|DevNoteStatus|DevNotePost|DevSwimOverride|devBypassHandwheel|DevSpawning|DevCancelled|DevHoldDeep|DevHoldAt)\b")

# 演练（两份文件）里不许出现的入口（清洗注释后按正则找）。每一条都写明为什么。
FORBIDDEN = (
    (r"\bSavesSystem\.(?:Save|SaveFile|SaveGlobal|SetFile|DeleteCurrentSave|RestoreIndexedBackup|CollectSaveData)\b", "直接写档或换槽"),
    (r"\bTidegateStory\.(?:RecordPage|ReadLetter|TryDeliverWatch|TryDeliverLetter|DevResetCurrentSlot|Bind)\b", "推进或清空剧情存档"),
    (r"\b(?:TidegateNoteBridge|NoteIndex)\.", "改官方笔记图鉴"),
    (r"\b(?:ItemAssetsCollection|ItemUtilities)\.|\b(?:GiveToPlayerOrStorage|SendToPlayer|AddOfficialStack)\(|\bTidegateInventory\.Consume\(",
     "发物品或扣物品"),
    (r"\bOnStartLoot\b|\bStartLoot\(|\.Interact\(", "翻开箱子（日志页会记进剧情存档）"),
    (r"\bGameClock\.|\bclockTimeScale\b|\bTime\.timeScale\s*=", "写官方时钟或时间流速（拨潮相只许走 TidegateTide.DevOffsetHours）"),
    (r"\bLoadScene(?:Async)?\(|\bReturnToBase\(|\.Close\(", "切图、返航或收掉会话"),
    (r"\bRuntimeStatModifierTracker\.", "直接给主角挂或摘增益（游泳只许经 DevSwimOverride 走生产路径）"),
    (r"\b(?:WriteRunMarker|ValidationSafeCleanup|BossRushSaveFileThrottle)\b", "写运行标记或跑主套件清理"),
)

READ_ONLY_FORBIDDEN = re.compile(r"\b(?:TryStartTidegateDrill|RunTidegateDrill\w*|_tidegateDrill\w*|TidegateDrill\w*|DevTurnValve|DevLightLighthouse"
                                 r"|DevSwimOverride|DevOffsetHours|DevJump\w*)\b")


def directive_frames(raw):
    """逐行给出 (行号, 去注释的代码, 这一行是否处在 #if BOSSRUSH_DEV 的真分支里)。"""
    stack = []
    result = []
    for number, line in enumerate(raw.splitlines(), 1):
        code = line.split("//", 1)[0].strip()
        if code.startswith("#if"):
            stack.append("BOSSRUSH_DEV" in code and "!" not in code)
        elif code.startswith("#else") and stack:
            stack[-1] = not stack[-1]
        elif code.startswith("#endif") and stack:
            stack.pop()
        result.append((number, code, any(stack)))
    return result


def wrapped_whole_file(raw):
    lines = [line.strip() for line in raw.splitlines() if line.strip()]
    if not lines or lines[0] != "#if BOSSRUSH_DEV" or lines[-1] != "#endif":
        return False
    depth = 0
    body = [line for line in raw.splitlines() if line.strip()]
    for index, line in enumerate(body):
        code = line.strip()
        if code.startswith("#if"):
            depth += 1
        elif code.startswith("#endif"):
            depth -= 1
            if depth == 0 and index != len(body) - 1:
                return False
    return depth == 0


def body_of(source, signature, start=0):
    at = source.find(signature, start)
    if at < 0:
        return None
    brace = source.find("{", at)
    depth = 0
    for i in range(brace, len(source)):
        if source[i] == "{":
            depth += 1
        elif source[i] == "}":
            depth -= 1
            if depth == 0:
                return source[brace + 1:i]
    return None


def check(raw):
    errors = []
    clean = {rel: clean_source(text) for rel, text in raw.items() if rel.endswith(".cs")}

    # ---- 1. 只在 Dev 构建里存在 ----
    for rel in DEV_FILES:
        if not wrapped_whole_file(raw[rel]):
            errors.append(rel + " 没有整份包在 #if BOSSRUSH_DEV … #endif 里：正式构建里会出现会改状态的演练或调试入口")
        if "echo(" + rel.replace("/", BACKSLASH) not in raw[BAT]:
            errors.append("编译清单缺少 " + rel + "（无通配符，漏了不报错，Dev 构建里按钮点了会编不过）")
    for number, code, in_dev in directive_frames(raw[RUNNER]):
        if ("StartTidegateDrillFromF3" in code or "TryStartTidegateDrill" in code) and not in_dev:
            errors.append(RUNNER + ":%d 在 #if BOSSRUSH_DEV 之外引用了潮闸镇演练入口（正式构建的 F3 面板不许有这个按钮）" % number)
    if "StartTidegateDrillFromF3" not in raw[RUNNER]:
        errors.append("F3 验收页上没有潮闸镇演练按钮（独立入口）")
    if "TidegateProgressDebugControls.Build(parent, host, report);" not in clean[PETNEST]:
        errors.append("F3「NPC/剧情」页没有挂上潮闸镇区（清空进度、拨潮相）")
    for rel in PRODUCTION_HOOKED:
        for number, code, in_dev in directive_frames(raw[rel]):
            if HOOK_TOKENS.search(code) and not in_dev:
                errors.append(rel + ":%d 的 Dev 钩子在 #if BOSSRUSH_DEV 之外：%s" % (number, code[:80]))
    clock = body_of(clean[TIDE], "internal static double ClockHours()") or ""
    if "TidegateTideRules.ElapsedHours(GameClock.Day, GameClock.TimeOfDay.TotalSeconds)" not in clock or "hours += DevOffsetHours;" not in clock:
        errors.append("拨潮相只许加在 TidegateTide.ClockHours 上（跨天连续时钟 + DevOffsetHours）")
    if "DevOffsetHours = 0.0;" not in (body_of(clean[TIDE], "public void Dispose()") or ""):
        errors.append("拨过的潮相要在会话收尾（TidegateTide.Dispose）清零：不然下一趟还带着偏移")
    bypass = body_of(raw[DEVICES], "private bool DevBypassHandwheel()") or ""
    if not re.search(r"#if BOSSRUSH_DEV\s+return devBypassHandwheel;\s+#else\s+return false;\s+#endif", bypass):
        errors.append("手轮旁路在正式构建必须恒为 false")
    turn = body_of(clean[DEVICES], "private void TurnValve(int index)") or ""
    if "Item wheel = DevBypassHandwheel() ? null : TidegateInventory.FindCarried(" not in turn:
        errors.append("手轮旁路不得读或消耗背包里真有的手轮（Dev 旁路时 wheel 必须是 null）")

    # ---- 2. 演练不写存档、不收录、不开箱、不发物品、不写时钟、不切图 ----
    for rel in (DRILL, CASES, ABLATION, KEYLEAD):
        for pattern, why in FORBIDDEN:
            if re.search(pattern, clean[rel]):
                errors.append(rel + " 不得" + why + "：/" + pattern + "/")
    for rel in READ_ONLY:
        if READ_ONLY_FORBIDDEN.search(clean[rel]):
            errors.append(rel + " 是只读套件，不得引用潮闸镇演练或它的钩子")
    controls_reset = body_of(clean[CONTROLS], "private static void Reset(ModBehaviour host, Action<string, bool> report)") or ""
    gate_at = controls_reset.find("F3GameplayValidationRunner.CanRunManualProgression(host, out reason)")
    reset_at = controls_reset.find("TidegateStory.DevResetCurrentSlot(out detail)")
    if gate_at < 0 or reset_at < 0 or gate_at > reset_at:
        errors.append("清空潮闸镇进度写存档：执行前必须先过 CanRunManualProgression（Dev + 专用测试档 + 基地）")
    for rel, text in clean.items():
        if rel not in (HOOKS, CONTROLS) and re.search(r"\bDevResetCurrentSlot\(", text):
            errors.append(rel + " 不得调用 DevResetCurrentSlot：清空进度只许在 F3 手动按钮里")

    # ---- 3. 入口顺序与标志 ----
    start = body_of(clean[DRILL], "internal static bool TryStartTidegateDrill(ModBehaviour host, out string reason)") or ""
    gate = start.find("if (!_instance.CheckTidegateStartGate(out reason)) return false;")
    flag = start.find("_instance._tidegateDrill = true;")
    begin = start.find("_instance.BeginSession(out reason)")
    if not (0 <= gate < flag < begin):
        errors.append("演练入口必须先过 CheckTidegateStartGate（Dev + 专用测试档 + 在潮闸镇 + 没开模态），再置 _tidegateDrill，最后开会话")
    tidegate_gate = body_of(clean[DRILL], "private bool CheckTidegateStartGate(out string reason)") or ""
    for token in ("IsDedicatedCurrentSlot()", "ModBehaviour.DevModeEnabled", "GetComponent<TidegateSession>()", "TidegateRaidScene.IsScene("):
        if token not in tidegate_gate:
            errors.append("潮闸镇演练开跑门缺少 " + token)
    sets = sum(len(re.findall(r"\b_tidegateDrill\s*=\s*true\s*;", clean[rel])) for rel in (DRILL, CASES, EXECUTION, RUNNER))
    if sets != 1:
        errors.append("_tidegateDrill 只许在演练入口置位一次（现在 %d 处）" % sets)
    execution = clean[EXECUTION]
    complete = body_of(execution, "private void CompleteSession()") or ""
    capture, reset = complete.find("bool tidegate = _tidegateDrill;"), complete.find("_tidegateDrill = false;")
    if not (0 <= capture < reset) or len(re.findall(r"\b_tidegateDrill\s*=\s*false\s*;", execution)) != 1:
        errors.append("_tidegateDrill 必须且只能在 CompleteSession 里先记下、再复位")
    fallback_frames = [in_dev for _, code, in_dev in directive_frames(raw[EXECUTION]) if "FinishTidegateDrillRestoreSynchronously" in code]
    if fallback_frames != [True] or "if (tidegate) FinishTidegateDrillRestoreSynchronously();" not in complete:
        errors.append("CompleteSession 要在 #if BOSSRUSH_DEV 里按 tidegate 同步兜底还原一次（取消、异常、超时也走到）")
    if not re.search(r"if \(!skyIsland && !tidegate && !_slotChanged && SavesSystem\.CurrentSlot == _sessionSlot\)", complete):
        errors.append("潮闸镇演练收尾不得跑面向主套件的全宿主清理（ValidationSafeCleanup 按 !tidegate 门控）")

    # ---- 4. 会话外壳 ----
    begin_body = body_of(execution, "private bool BeginSession(out string reason)") or ""
    marker = body_of(begin_body, "if (!_skyIslandMode && !_tidegateDrill)")
    if marker is None or "WriteRunMarker()" not in marker:
        errors.append("潮闸镇演练不得写运行标记：WriteRunMarker 必须包在 if (!_skyIslandMode && !_tidegateDrill) 块里")
    if not re.search(r'_tidegateDrill\s*\?\s*"SESSION \| slot=" \+ _sessionSlot \+ " \| assisted=true \| read_only=false \| drill=true \| map=tidegate \| save_writes=false',
                     execution):
        errors.append("会话报告头没有按 _tidegateDrill 如实写 read_only=false / drill=true / save_writes=false")
    if not re.search(r"if \(_host != null && !_skyIslandMode && !_tidegateDrill\) _host\.GameplayValidationSuppressNotifications = true;", execution):
        errors.append("潮闸镇演练不得吞掉这一趟的正常提示（抑制提示条按 !_tidegateDrill 门控）")

    # ---- 5. 还原 ----
    restore = body_of(clean[DRILL], "private List<string> RestoreTidegateDrillState(out string metrics)") or ""
    for token in ("PhaseChanged -= _tidegateDrillPhaseHandler", "TidegateTide.DevOffsetHours = _tidegateDrillOffsetBefore;",
                  "DevSwimOverride = 0;", "DevReset();", "DevEndReveal();", "DevRemoveExtras(",
                  "TidegateDrillSafePoint(session, _tidegateDrillStart, out back)", "DevTeleport(back, false",
                  "_tidegateDrillRestored = true;"):
        if token not in restore:
            errors.append("还原缺少 " + token)
    suite = body_of(clean[DRILL], "private IEnumerator RunTidegateDrillSuite()") or ""
    if "RecordTidegateDrillRestore();" not in suite or suite.rfind("RecordTidegateDrillRestore();") < suite.rfind("RunTidegateDrillCase("):
        errors.append("套件最后一步必须是还原（RecordTidegateDrillRestore）")
    fallback = body_of(clean[DRILL], "private void FinishTidegateDrillRestoreSynchronously()") or ""
    if "if (_tidegateDrillRestored) return;" not in fallback or "RestoreTidegateDrillState(" not in fallback:
        errors.append("同步兜底要先看还原跑过没有，没跑过才还原")
    for case in ("private IEnumerator RunTidegateDrillSwim()", "private IEnumerator RunTidegateDrillFloodHint()"):
        body = body_of(clean[CASES], case) or ""
        tail = body_of(body, "finally") if body else None
        if tail is None or "DevSwimOverride = 0;" not in tail:
            errors.append(case.split()[-1] + " 的游泳覆盖必须在 finally 里清掉：中途异常会让这一趟一直「当轻装 / 当超重」")
    hold = body_of(clean[CASES], "private IEnumerator RunTidegateDrillExtractionHold()") or ""
    hold_finally = body_of(hold, "finally") if hold else None
    if hold_finally is None or "TidegateDrillStepAway(" not in hold_finally:
        errors.append("撤离读条那条必须在 finally 里离开撤离环：读满会真的返航")

    # ---- 6. 站位离出口 ----
    safe = body_of(clean[DRILL], "private static bool TidegateDrillSafePoint(TidegateSession session, Vector3 near, out Vector3 point)") or ""
    if "TidegateDrillExitWithin(session.DevWorld, snapped, 5f)" not in safe:
        errors.append("安全站位要离任何出口 5 m 以上（撤离环最大 2.6 m）")
    stand = body_of(clean[DRILL], "private IEnumerator TidegateDrillStand(") or ""
    if "TidegateDrillSafePoint(" not in stand:
        errors.append("演练站位必须走 TidegateDrillSafePoint")
    deep = body_of(clean[DRILL], "private static bool TidegateDrillFindDeepWater(") or ""
    if "TidegateDrillExitWithin(world, probe, 6f) != null" not in deep:
        errors.append("找深水点要避开出口 6 m（栈桥渡口高潮时开着）")
    snaps = len(re.findall(r"\bDevTeleport\(", clean[CASES]))
    # 深水点（游泳、深水箱子提示）、公路检查站读条、离开撤离环（TidegateDrillStepAway 里的 point 来自 TidegateDrillSafePoint）。
    allowed = len(re.findall(r"\bDevTeleport\((?:deep|near|road\.position|point), ", clean[CASES]))
    step_away = body_of(clean[CASES], "private static bool TidegateDrillStepAway(TidegateSession session, Vector3 anchor)") or ""
    if "TidegateDrillSafePoint(" not in step_away:
        errors.append("离开撤离环也要走 TidegateDrillSafePoint")
    if snaps != allowed:
        errors.append("用例里直接搬主角只许去深水点、公路检查站读条或离开撤离环（其余一律 TidegateDrillStand）：%d 处里有 %d 处不合规"
                      % (snaps, snaps - allowed))

    # ---- 6b. 2026-11-06 独立审查修的几件事 ----
    # 开跑门拒绝「这一趟已经动过」：机关动过或箱子翻过，好几条会假红、还原也回不去。
    for token in ("session.DevDevices.DevTouched", "session.DevLoot.OpenedCount > 0"):
        if token not in tidegate_gate:
            errors.append("潮闸镇演练开跑门要拒绝这一趟已经动过的情况：缺 " + token)
    # 拨到高潮只许经 TidegateDrillJumpToHigh：先让淘沙帮按「涨潮前 20 秒撤」回去，直接跳会把他们泡在深水里卷走。
    if re.search(r"DevJumpInto\(TidegateTidePhase\.High", clean[CASES]):
        errors.append("用例里不得直接拨到高潮（要经 TidegateDrillJumpToHigh 先让淘沙帮撤回）")
    jump_high = body_of(clean[DRILL], "private IEnumerator TidegateDrillJumpToHigh(") or ""
    retreat = body_of(clean[DRILL], "private IEnumerator TidegateDrillWaitForRetreat()") or ""
    if not re.search(r"if\s*\(phase != TidegateTidePhase\.High\)\s*yield return TidegateDrillWaitForRetreat\(\);", jump_high):
        errors.append("TidegateDrillJumpToHigh 在非高潮时要先走淘沙帮撤回窗口")
    if not re.search(r"TidegateTide\.DevJumpBeforeEnd\(TidegateTidePhase\.Low, 19\.0\);\s*yield return WaitSeconds\(14f\);", retreat):
        errors.append("淘沙帮撤回窗口要先拨到低潮还剩 19 秒，再等 14 秒")
    phases = body_of(clean[CASES], "private IEnumerator RunTidegateDrillTidePhases()") or ""
    if not re.search(r"if\s*\(phase == TidegateTidePhase\.Flood\)\s*yield return TidegateDrillWaitForRetreat\(\);", phases):
        errors.append("四相循环的变量拨潮同样要在涨潮前留淘沙帮撤回窗口")
    notices = body_of(clean[CASES], "private IEnumerator RunTidegateDrillTideNotices()") or ""
    if notices.find("yield return TidegateDrillWaitForRetreat();") < 0 or notices.find("yield return TidegateDrillWaitForRetreat();") > notices.find("TidegateTide.DevJumpBeforeEnd(TidegateTidePhase.High, 15.0);"):
        errors.append("换相前提示检查跳到高潮之前也要留淘沙帮撤回窗口")
    # 潮相偏移只在这一趟还在时复位：出击已收尾时 Dispose 已经清零，写回旧值会带进下一趟。
    alive_block = body_of(restore, "if (alive)") or ""
    if "TidegateTide.DevOffsetHours = _tidegateDrillOffsetBefore;" not in alive_block:
        errors.append("潮相偏移只许在出击还在（if (alive)）时复位")
    # 取消还在异步创建的增援：删槽前标取消，await 返回后在保留角色之前拒绝，原 finally 销毁迟到角色与 preset。
    extras = body_of(clean[HOOKS], "internal int DevRemoveExtras(out int pending)") or ""
    if not re.search(r"slot\.DevCancelled = true;\s*if\s*\(slot\.DevSpawning\)\s*pending\+\+;", extras):
        errors.append("DevRemoveExtras 必须取消所有增援，并按槽记录仍在创建的迟到结果")
    if re.search(r"\bcontinue\s*;", extras[extras.find("slot.DevCancelled = true;"):]):
        errors.append("已取消的增援不能跳过删槽，迟到回调由 Spawn finally 负责")
    spawn = body_of(clean[ENEMIES], "private async void Spawn(Slot slot)") or ""
    awaited = spawn.find("created = await clone.CreateCharacterAsync(")
    reject = spawn.find("if (slot.DevCancelled) return;")
    retain = spawn.find("slot.Character = created;")
    if not (0 <= awaited < reject < retain):
        errors.append("增援的 await 返回后必须先按 DevCancelled 拒留，再挂 slot.Character")
    spawn_finally = body_of(spawn, "finally") or ""
    if not re.search(r"slot\.DevSpawning = false;", spawn_finally) or "if (!retained && created != null)" not in spawn_finally or "if (clone != null) UnityEngine.Object.Destroy(clone," not in spawn_finally:
        errors.append("Spawn finally 要清在建标志，并销毁未保留的迟到角色与 preset")
    enemies_case = body_of(clean[CASES], "private IEnumerator RunTidegateDrillEnemies()") or ""
    incomplete = body_of(enemies_case, "if (errors.Count == 0 && !TidegateDrillEnemyCoverageOk(") or ""
    if not re.search(r'Record\(id, "WARN",', incomplete) or not re.search(r"yield break;", incomplete):
        errors.append("敌人检查没有逐槽刷出 / 距离 / 活守卫 / 活 Boss 的观测时必须 WARN，不能 PASS")
    assembly = body_of(clean[CASES], "private IEnumerator RunTidegateDrillAssembly()") or ""
    tide_check = body_of(clean[DRILL], "private static void TidegateDrillCheckTide(") or ""
    for token in ("TidegateDrillWaterTopsOk(session.DevWater.DevSeaTops, session.Tide.Level, 0.03f, out waterReason)",
                  "TidegateDrillWaterTopsOk(session.DevWater.DevLagoonTops, session.Tide.LagoonLevel, 0.03f, out waterReason)"):
        if token not in assembly:
            errors.append("装配核对要检查海水和泻湖全部盒的顶面有效与同步：缺 " + token)
    if not re.search(r"List<float> seaTops = water\.DevSeaTops, lagoonTops = water\.DevLagoonTops;", tide_check):
        errors.append("潮相核对必须取海水与泻湖全部水盒，不能只测首盒或绕过缺失泻湖")
    for token in ("TidegateDrillWaterTopsOk(seaTops, tide.Level, 0.03f, out why)",
                  "TidegateDrillWaterTopsOk(lagoonTops, tide.LagoonLevel, 0.03f, out why)",
                  "TidegateDrillLevelOk(shaderTide, tide.Level, 0.01f)", "TidegateDrillLevelOk(shaderLagoon, tide.LagoonLevel, 0.01f)"):
        if token not in tide_check:
            errors.append("潮相的水盒和着色器读数必须有限且同步：缺 " + token)

    # ---- 7. 纯判据执行回归 ----
    if "#region 纯判据" not in raw[DRILL]:
        errors.append("演练纯判据区缺失（tests/fixtures/TidegateDrillJudges 抽它执行）")
    if '"TidegateDrillJudges"' not in raw[REGRESSIONS]:
        errors.append("执行回归聚合器没有登记 TidegateDrillJudges")
    return errors


def main():
    raw = {}
    rels = set(DEV_FILES + PRODUCTION_HOOKED + READ_ONLY + (PETNEST, RUNNER, EXECUTION, REGRESSIONS, BAT))
    for rel in rels:
        path = ROOT / rel
        raw[rel] = path.read_text(encoding="utf-8-sig", errors="ignore").replace("\r\n", "\n") if path.is_file() else None
    missing = [rel for rel, value in raw.items() if value is None]
    if missing:
        print("TidegateDrillGuard: FAIL - 缺文件：" + ", ".join(sorted(missing)))
        return 1
    errors = check(raw)

    probes = (
        (DRILL, "#if BOSSRUSH_DEV\n", "", "拆掉演练文件的 #if"),
        (HOOKS, "#if BOSSRUSH_DEV\n", "", "拆掉钩子文件的 #if"),
        (CONTROLS, "#if BOSSRUSH_DEV\n", "", "拆掉潮闸镇区的 #if"),
        (BAT, "echo(DebugAndTools" + BACKSLASH + "F3GameplayValidationTidegateDrillCases.cs\n", "", "编译清单漏登记用例文件"),
        (RUNNER, "        private void StartFullGameplayValidationFromF3()\n        {\n",
         "        private void StartFullGameplayValidationFromF3()\n        {\n            StartTidegateDrillFromF3();\n", "演练入口挪出 #if"),
        (TIDE, "#if BOSSRUSH_DEV\n            // Dev 演练 / F3 拨潮相", "            // Dev 演练 / F3 拨潮相", "潮相偏移挪出 #if"),
        (TIDE, "            DevOffsetHours = 0.0;\n", "", "会话收尾不清潮相偏移"),
        (DEVICES, "Item wheel = DevBypassHandwheel() ? null : TidegateInventory.FindCarried(", "Item wheel = TidegateInventory.FindCarried(", "旁路照样消耗真手轮"),
        (CASES, "devices.DevTurnValve(i, false);", "devices.DevTurnValve(i, false); TidegateStory.RecordPage(0);", "演练写剧情存档"),
        (CASES, "devices.DevLightLighthouse();", "devices.DevLightLighthouse(); SavesSystem.SaveFile(false);", "演练写档"),
        (CASES, "devices.DevLightLighthouse();", "devices.DevLightLighthouse(); GameClock.Instance.clockTimeScale = 600f;", "演练改官方时钟"),
        (CASES, "yield return TidegateDrillStand(session, lighthouse, errors, \"lighthouse\");",
         "string skip; session.DevTeleport(lighthouse, false, out skip);", "绕过安全站位直接搬主角"),
        (DRILL, "if (!_instance.CheckTidegateStartGate(out reason)) return false;", "", "绕过开跑门"),
        (DRILL, "TidegateTide.DevOffsetHours = _tidegateDrillOffsetBefore;", "", "还原不复位潮相偏移"),
        (DRILL, "session.DevEnemies.DevEndReveal();", "", "还原不收灯塔地图标记"),
        (DRILL, "if (_tidegateDrillRestored) return;", "", "兜底不看还原跑过没有"),
        (DRILL, "TidegateDrillExitWithin(session.DevWorld, snapped, 5f)", "TidegateDrillExitWithin(session.DevWorld, snapped, 1f)", "站位贴着撤离环"),
        (CASES, "                TidegateDrillStepAway(session, road.position);\n", "", "撤离读条不离开撤离环"),
        (EXECUTION, "if (tidegate) FinishTidegateDrillRestoreSynchronously();", "", "收尾不兜底还原"),
        (EXECUTION, "read_only=false | drill=true | map=tidegate", "read_only=true | drill=true | map=tidegate", "报告头谎报只读"),
        (EXECUTION, "if (!_skyIslandMode && !_tidegateDrill)\n", "if (!_skyIslandMode)\n", "演练写运行标记"),
        (CONTROLS, "            if (!F3GameplayValidationRunner.CanRunManualProgression(host, out reason)) { report(reason, true); return; }\n            string detail;",
         "            string detail;", "清空进度绕过专用测试档门"),
        (REGRESSIONS, '"TidegateDrillJudges", ', "", "纯判据回归没登记"),
        (DRILL, "session.DevLoot.OpenedCount > 0", "session.DevLoot.OpenedCount > 999", "开跑门不拦翻过的箱子"),
        (CASES, "yield return TidegateDrillJumpToHigh(session, 4.0);", "TidegateTide.DevJumpInto(TidegateTidePhase.High, 4.0);", "直接拨到高潮"),
        (DRILL, "TidegateTide.DevJumpBeforeEnd(TidegateTidePhase.Low, 19.0);", "", "拨高潮前不让淘沙帮撤回"),
        (HOOKS, "slot.DevCancelled = true;", "slot.DevCancelled = false;", "清理没有取消迟到增援"),
        (ENEMIES, "if (slot.DevCancelled) return;", "", "迟到增援仍然保留"),
        (CASES, "if (phase == TidegateTidePhase.Flood) yield return TidegateDrillWaitForRetreat();", "", "四相循环绕过淘沙帮撤回窗口"),
        (CASES, 'Record(id, "WARN", sw.ElapsedMilliseconds, string.Join(" | ", notes.ToArray()), coverageReason + ";未观测项目须人工补测");',
         'Record(id, "PASS", sw.ElapsedMilliseconds, string.Join(" | ", notes.ToArray()), coverageReason);', "缺敌人观测仍然 PASS"),
        (CASES, "TidegateDrillWaterTopsOk(session.DevWater.DevLagoonTops, session.Tide.LagoonLevel, 0.03f, out waterReason)",
         "TidegateDrillWaterTopsOk(session.DevWater.DevSeaTops, session.Tide.Level, 0.03f, out waterReason)", "装配检查漏泻湖"),
        (DRILL, "TidegateDrillWaterTopsOk(lagoonTops, tide.LagoonLevel, 0.03f, out why)",
         "TidegateDrillWaterTopsOk(seaTops, tide.Level, 0.03f, out why)", "四相检查漏泻湖"),
        (DRILL, "TidegateDrillLevelOk(shaderTide, tide.Level, 0.01f)",
         "(Math.Abs(shaderTide - tide.Level) <= 0.01f)", "着色器 NaN 检查丢失"),
    )
    for rel, before, after, label in probes:
        text = raw[rel]
        if before not in text:
            errors.append("反向检查锚点失效（%s）：%s" % (label, before[:60]))
            continue
        altered = dict(raw)
        altered[rel] = text.replace(before, after, 1)
        if not check(altered):
            errors.append("反向检查失效：%s 之后守卫仍然全绿" % label)

    if errors:
        print("TidegateDrillGuard: FAIL")
        for error in errors:
            print("  - " + error)
        return 1
    print("TidegateDrillGuard: PASS（整份 #if BOSSRUSH_DEV 与生产钩子门控 / 不写存档不收录不开箱不发物品不写时钟 / 开跑门与标志 / "
          "会话外壳 / 还原与兜底 / 站位离出口 / 纯判据回归；%d 个反向检查）" % len(probes))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
