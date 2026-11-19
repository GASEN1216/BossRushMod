"""ModPathAndAudioGuard：Mod 根目录、官方音频反射、天空岛巡逻表读取各只有一个入口；2026-11-19 删掉的死代码不再出现。

不变式（2026-11-19 统一）：
1. Mod 根目录只经 ModBehaviour.GetModPath() 取。编译清单里的生产代码（含 #if BOSSRUSH_DEV 区块）不再自己拼程序集位置
   （`typeof(X).Assembly.Location`、`GetType().Assembly.Location`、`GetExecutingAssembly().Location`、`assembly.Location`）。
   白名单（写明理由）：
   - DebugAndTools/DebugAndTools.cs：GetModPath 自身，恰好一处且在 GetModPath 方法体内；
   - ModeH/ModeHCanonicalDigest.cs：TryComputeAssemblyFileDigest 要对已加载的程序集文件本身算摘要，取的是 DLL 文件，不是 Mod 根目录。
   静态字段初始化器里不得取 Mod 根目录：抛出会变成 TypeInitializationException 永久毒化类型（NewWeaponSfx 的注释）。
   套装与龙王的音效路径改成用时才拼的属性。
2. 官方音频的反射（AudioManager.PostCustomSFX / Post、AudioObject.StopAll、FMOD EventInstance 的 setVolume / setPitch / stop）
   只在 Audio/BossRushOfficialAudio.cs：首次用到时解析一次，失败返回 null / false，整个文件不抛异常。
   各调用点改调它（下表逐处钉住调用），没有例外。龙王武器射击音原来用 void 委托绑官方 Post，绑不上它的非 void
   返回值，这条音从未响过；2026-11-19 owner 拍板修好，改走统一入口，旧委托与解析标志不得再出现。
3. SkyIslandPatrolRules 是纯规则：不读文件。SkyIslandPatrols 经 JsonDataRegistry.TryReadDataFile 读出 Patrols.json，
   把文本交给 SkyIslandPatrolRules.FromText；缺文件 / 读失败 / 解析失败都回退 CreateFallback()。
4. 删掉的死代码不再出现（生产代码与夹具）：MapSpawnPointRegistry 的导出 / 序列化 / 原文本搜键，SimpleJsonHelper 的
   ExtractLong / ExtractFloat / ExtractBool / FindArrayBounds，BossRushAudioManager 的单参 PlayBossBGM / StopBossBGM 与
   NPC 交互音注册口，音频服务的 void 委托，成就弹窗的 default.mp3 兜底，许愿台版本号的程序集特性分支与 GetModDirectory，
   DailyReportLayoutTable.LoadedFromJson，SkyIslandPatrolRules.Load。

文本守卫防不住「保留 token、杀掉执行路径」：统一入口与各调用点的行为由 AudioRuntime、SkyIslandInteraction 执行回归覆盖，
刷新点读取格式由 AuditCoreParsing 执行回归与 MapSpawnRegistryPropertyTest 覆盖。
"""

from pathlib import Path
import re
import sys

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))
sys.path.insert(0, str(ROOT / "tests"))

from compile_list import parse_compile_sources  # noqa: E402
from cs_source_util import strip_comments  # noqa: E402

AUDIO = "Audio/BossRushOfficialAudio.cs"
DRAGON_KING_AUDIO = "Integration/DragonKing/DragonKingAbilityController_SpecialAttacks.cs"

ASSEMBLY_LOCATION = re.compile(r"(?i)assembly\s*(?:\(\s*\))?\s*\.\s*location\b")
# (文件, 方法签名)：白名单里的程序集位置只能在这个方法体里出现恰好一次。
ASSEMBLY_LOCATION_ALLOWED = {
    "DebugAndTools/DebugAndTools.cs": "public static string GetModPath()",
    "ModeH/ModeHCanonicalDigest.cs": "private static bool TryComputeAssemblyFileDigest(Assembly assembly, out string hex, out string error)",
}
STATIC_INITIALIZER_MOD_PATH = re.compile(
    r"\bstatic\s+(?:readonly\s+)?[\w<>\[\],.?]+\s+\w+\s*=[^;{}]*?(?:\bGetModPath\s*\(|(?i:assembly)\s*\.\s*Location\b)")

# 官方音频成员的反射：按名字取方法，或按名字比对方法。
REFLECTED_AUDIO = re.compile(
    r"\bGetMethods?\s*\(\s*(?:[^,()\"]+,\s*)?\"(PostCustomSFX|Post|StopAll|setVolume|setPitch|stop)\"")
NAME_COMPARED_AUDIO = re.compile(r"\.Name\s*==\s*\"(stop|setVolume|setPitch|PostCustomSFX|StopAll)\"")
AUDIO_LITERAL = re.compile(r"\"(PostCustomSFX|StopAll)\"")
TYPEOF_AUDIO_REFLECTION = re.compile(
    r"typeof\s*\(\s*(?:global::)?Duckov\s*\.\s*(AudioManager|AudioObject)\s*\)\s*\.\s*GetMethods?\s*\(")
AUDIO_DELEGATE = re.compile(r"CreateDelegate\s*\(\s*typeof\s*\(\s*\w*(?:Post|Sfx)\w*Delegate\s*\)")

# (文件, 调用, 次数)：各调用点改调统一入口。
AUDIO_CALLERS = [
    ("Audio/BossRushAudioRuntimeService.cs", "BossRushOfficialAudio.PostCustomSfx(filePath, target, false);", 1),
    ("Audio/BossRushAudioRuntimeService.cs", "BossRushOfficialAudio.TryPostCustomSfx(filePath, target, false, out posted, out error)", 1),
    ("SkyIsland/SkyIslandAmbience.cs", "BossRushOfficialAudio.TryPostCustomSfx(path, device.Root, loop, out instance, out error)", 1),
    ("SkyIsland/SkyIslandAmbience.cs", "BossRushOfficialAudio.TryStopAll(audio, out error)", 1),
    ("SkyIsland/SkyIslandGnats.cs", "BossRushOfficialAudio.TryPostCustomSfx(path, buzzEmitter, true, out instance, out error)", 1),
    ("SkyIsland/SkyIslandGnats.cs", "BossRushOfficialAudio.TryStopAll(audio, out error)", 1),
    ("SkyIsland/SkyIslandFxAssets.cs", "BossRushOfficialAudio.TryPostCustomSfx(Path.Combine(directory, Files[index]), emitter, false, out instance, out error)", 1),
    ("Tidegate/TidegateSounds.cs", "BossRushOfficialAudio.TryPostCustomSfx(path, emitter, loop, out instance, out error)", 1),
    ("Tidegate/TidegateSounds.cs", "BossRushOfficialAudio.TryStopAll(audio, out error)", 1),
    ("LastShiftYard/LastShiftSounds.cs", "BossRushOfficialAudio.TryPostCustomSfx(path, emitter, false, out instance, out error)", 1),
    ("Achievement/SteamAchievementPopup.cs", "BossRushOfficialAudio.PostCustomSfx(soundPath, null, false);", 1),
    ("Integration/DragonDescendant/DragonDescendantAbilities_CollisionAndIce.cs", "BossRushOfficialAudio.TryPostCustomSfx(filePath, target, false, out instance, out error)", 1),
    ("Integration/DragonDescendant/DragonDescendantAbilities_Phase2Combat.cs", "BossRushOfficialAudio.TryPost(eventName, bossCharacter.gameObject, out instance, out error)", 1),
    ("Integration/Wedding/NPCMarriageSystem.cs", "BossRushOfficialAudio.TryPostCustomSfx(filePath, target, false, out instance, out error)", 1),
    ("Integration/Wedding/NPCMarriageSystem.cs", "BossRushOfficialAudio.TrySetEventVolume(handle, Mathf.Clamp01(volume), out error);", 1),
    ("Integration/Wedding/NPCMarriageSystem.cs", "BossRushOfficialAudio.TryStopEvent(handle, out error);", 1),
    ("Common/UI/BossRushUIFeel.cs", "BossRushOfficialAudio.Post(HoverEvent);", 1),
    ("Common/UI/BossRushUIFeel.cs", "BossRushOfficialAudio.Post(ClickEvent);", 1),
    ("Integration/UI/IntegrationUIFeedback.cs", "BossRushOfficialAudio.Post(eventName);", 1),
    ("Integration/NewWeapons/AstralStaff/AstralStaffController.cs", "BossRushOfficialAudio.Post(eventName, at.gameObject);", 1),
    ("Integration/NewWeapons/AstralStaff/AstralStaffController.cs", "BossRushOfficialAudio.TrySetEventPitch(handle, pitch, out error)", 1),
    ("Integration/NewWeapons/AstralStaff/AstralStaffController.cs", "BossRushOfficialAudio.TrySetEventVolume(handle, volume, out error);", 1),
    ("Integration/NewWeapons/EmptyMagazineMine/EmptyMagazineMineFx.cs", 'BossRushOfficialAudio.Post("SFX/Special/Radar/Beep", gameObject);', 1),
    ("Integration/NPCs/Courier/CourierService.cs", 'BossRushOfficialAudio.TryPost("Char/Voice/PlayerQuak", player.gameObject, out instance, out error)', 1),
    ("Integration/NPCs/Courier/StorageDepositBulkActions.cs", 'BossRushOfficialAudio.TryPost("Char/Voice/PlayerQuak", player.gameObject, out instance, out error)', 1),
    ("Common/Equipment/EquipmentAbilityAction.cs", "BossRushOfficialAudio.Post(soundKey, gameObject);", 1),
    (DRAGON_KING_AUDIO, "BossRushOfficialAudio.Post(cachedWeaponShootEventName, bossCharacter.gameObject);", 1),
]

PATROL_RULES = "SkyIsland/SkyIslandPatrolRules.cs"
PATROLS = "SkyIsland/SkyIslandPatrols.cs"

DEAD_EVERYWHERE = [
    (re.compile(r"\bExportFromHardcoded\b"), "MapSpawnPointRegistry.ExportFromHardcoded"),
    (re.compile(r"\bMapSpawnPointRegistry\s*\.\s*SerializeToJson\b"), "MapSpawnPointRegistry.SerializeToJson"),
    (re.compile(r"\bSimpleJsonHelper\s*\.\s*(ExtractLong|ExtractFloat|ExtractBool|FindArrayBounds)\b"), "SimpleJsonHelper 的已删取值函数"),
    (re.compile(r"\b(Register|Unregister)NPCInteractSFXHandler\b"), "BossRushAudioManager 的 NPC 交互音注册口"),
    (re.compile(r"\b(PostCustomSfxDelegate|TryGetPostCustomSfxDelegate|_cachedPostCustomSfx|_postCustomSfxResolved)\b"), "音频服务的 void 委托"),
    (re.compile(r"\bSkyIslandPatrolRules\s*\.\s*Load\s*\("), "SkyIslandPatrolRules.Load（改由宿主经 JsonDataRegistry 读）"),
    (re.compile(r"\bDailyReportLayoutTable\s*\.\s*LoadedFromJson\b"), "DailyReportLayoutTable.LoadedFromJson"),
    (re.compile(r"\bAssembly(Informational|File)VersionAttribute\b"), "程序集版本特性（编译清单从不写，读它的分支是死代码）"),
]
DEAD_IN_FILE = [
    ("Common/MapConfig/MapSpawnPointRegistry.cs",
     re.compile(r"\b(SerializeToJson|AppendJsonString|AppendVector3\w*|ExtractStringValue|ExtractNullableStringValue|ExtractIntValue\w*)\b|\bjson\s*\.\s*IndexOf\s*\(")),
    ("Utilities/SimpleJsonHelper.cs", re.compile(r"\b(ExtractLong|ExtractFloat|ExtractBool|FindArrayBounds)\b")),
    ("Audio/BossRushAudioManager.cs", re.compile(r"\b(?:PlayBossBGM|StopBossBGM)\s*\(\s*string\s+\w+\s*\)")),
    ("Integration/DailyReport/DailyReportLayoutTable.cs", re.compile(r"\b_?[lL]oadedFromJson\b")),
    ("Integration/WishFountain/WishFountainConfigAndValidation.cs", re.compile(r"\b(GetModDirectory|NormalizeVersionText)\b|GetName\s*\(\s*\)\s*\.\s*Version")),
    ("Achievement/SteamAchievementPopup.cs", re.compile(r"default\.mp3|\b(cachedPostCustomSFXMethod|audioReflectionCached|CacheAudioManagerReflection)\b")),
    ("Integration/DragonDescendant/DragonDescendantAbilities.cs", re.compile(r"\b(cachedAudioManagerType|cachedAudioPostMethod|cachedAudioPostCustomSFXMethod|audioManagerReflectionCached)\b")),
    ("Integration/Bonus/SetBonusVisuals.cs", re.compile(r"\bstatic\s+readonly\s+string\b|\bBasePath\b")),
    ("Integration/DragonKing/DragonKingConfig.cs", re.compile(r"\bSoundBasePath\b|static\s+readonly\s+string\s+Sound_")),
    (DRAGON_KING_AUDIO, re.compile(r"\b(CacheAudioPostMethod|cachedAudioPostDelegate|cachedAudioPostResolved|AudioPostDelegate)\b")),
    ("Integration/DragonKing/DragonKingAbilityController.cs",
     re.compile(r"\b(CacheAudioPostMethod|cachedAudioPostDelegate|cachedAudioPostResolved|AudioPostDelegate)\b")),
]
FIXTURE_DEAD = ("ExtractLong(", "ExtractFloat(", "ExtractBool(", "FindArrayBounds(", "PostCustomSfxDelegate",
                "_postCustomSfxResolved", "_cachedPostCustomSfx", "SkyIslandPatrolRules.Load(", "ExportFromHardcoded",
                "SerializeToJson")


def compact(text):
    return re.sub(r"\s+", "", text)


def mask_literals(code):
    """字符串与字符字面量里的字符换成空格（长度不变），只用来数花括号。"""
    out = list(code)
    i, n = 0, len(code)
    while i < n:
        ch = code[i]
        if ch == "@" and i + 1 < n and code[i + 1] == '"':
            j = i + 2
            while j < n:
                if code[j] == '"':
                    if j + 1 < n and code[j + 1] == '"':
                        j += 2
                        continue
                    break
                j += 1
            for k in range(i + 2, min(j, n)):
                out[k] = " "
            i = j + 1
            continue
        if ch in "\"'":
            j = i + 1
            while j < n and code[j] != ch and code[j] != "\n":
                j += 2 if code[j] == "\\" else 1
            for k in range(i + 1, min(j, n)):
                out[k] = " "
            i = j + 1
            continue
        i += 1
    return "".join(out)


def span(code, signature):
    """方法体在 code 里的 [start, end)（不含外层花括号）；签名不唯一或缺失返回 None。"""
    if code.count(signature) != 1:
        return None
    masked = mask_literals(code)
    start = masked.index("{", code.index(signature)) + 1
    depth, end = 1, start
    while depth and end < len(masked):
        depth += {"{": 1, "}": -1}.get(masked[end], 0)
        end += 1
    return start, end - 1


def body(code, signature):
    s = span(code, signature)
    return None if s is None else code[s[0]:s[1]]


def validate(root=ROOT):
    errors = []

    def raw(path):
        p = root / path
        return p.read_text(encoding="utf-8-sig") if p.is_file() else ""

    bat = (root / "compile_official.bat").read_text(encoding="utf-8", errors="replace")
    sources = [s for s in parse_compile_sources(bat) if (root / s).is_file()]
    if AUDIO not in sources:
        errors.append(AUDIO + " 没有登记进 compile_official.bat")
    codes = {path: strip_comments(raw(path)) for path in sources}

    # ---------- 1：Mod 根目录只经 GetModPath ----------
    for path, code in codes.items():
        hits = [m.start() for m in ASSEMBLY_LOCATION.finditer(code)]
        if not hits:
            continue
        signature = ASSEMBLY_LOCATION_ALLOWED.get(path)
        where = span(code, signature) if signature else None
        if where is None or len(hits) != 1 or not (where[0] <= hits[0] < where[1]):
            for start in hits:
                errors.append("%s:%d: 不要自己拼程序集位置取 Mod 根目录，改调 ModBehaviour.GetModPath()"
                              % (path, code.count("\n", 0, start) + 1))
    for path, code in codes.items():
        for m in STATIC_INITIALIZER_MOD_PATH.finditer(code):
            errors.append("%s:%d: 静态字段初始化器里不得取 Mod 根目录（会抛 TypeInitializationException 毒化类型），改成用时才取"
                          % (path, code.count("\n", 0, m.start()) + 1))
    get_mod_path = compact(body(codes.get("DebugAndTools/DebugAndTools.cs", ""), "public static string GetModPath()") or "")
    for token in ("stringassemblyLocation=typeof(ModBehaviour).Assembly.Location;",
                  "if(!string.IsNullOrEmpty(modDir)&&System.IO.Directory.Exists(modDir))", "returnnull;"):
        if token not in get_mod_path:
            errors.append("DebugAndTools/DebugAndTools.cs: GetModPath 缺少 " + token + "（统一入口本身不改）")
    for path, signature in (("Integration/Bonus/SetBonusVisuals.cs", "private static string Resolve(string fileName)"),
                            ("Integration/DragonKing/DragonKingConfig.cs", "private static string SoundPath(string fileName)")):
        resolver = compact(body(codes.get(path, ""), signature) or "")
        if "stringmodPath=ModBehaviour.GetModPath();if(string.IsNullOrEmpty(modPath))returnnull;" not in resolver:
            errors.append(path + ": " + signature + " 必须用时经 ModBehaviour.GetModPath() 取，取不到返回 null")
    king = codes.get("Integration/DragonKing/DragonKingConfig.cs", "")
    sound_properties = re.findall(r"public static string Sound_\w+ \{ get \{ return SoundPath\(\"[^\"]+\"\); \} \}", king)
    if len(sound_properties) != 13:
        errors.append("Integration/DragonKing/DragonKingConfig.cs: 13 个龙王音效应是用时才拼的属性，实际 %d" % len(sound_properties))
    set_bonus = codes.get("Integration/Bonus/SetBonusVisuals.cs", "")
    if len(re.findall(r"public static string (?:Thunder|Frost)\w+ \{ get \{ return Resolve\(\"[^\"]+\"\); \} \}", set_bonus)) != 4:
        errors.append("Integration/Bonus/SetBonusVisuals.cs: 4 个套装音效应是用时才拼的属性")

    # ---------- 2：官方音频反射只在统一入口 ----------
    for path, code in codes.items():
        if path == AUDIO:
            continue
        for pattern, label in ((REFLECTED_AUDIO, "按名字反射官方音频成员"), (NAME_COMPARED_AUDIO, "按名字比对官方音频成员"),
                               (AUDIO_LITERAL, "官方音频成员名字面量"), (TYPEOF_AUDIO_REFLECTION, "typeof(官方音频类型).GetMethod"),
                               (AUDIO_DELEGATE, "官方音频委托")):
            for m in pattern.finditer(code):
                errors.append("%s:%d: %s %r 只能出现在 %s，改调 BossRushOfficialAudio"
                              % (path, code.count("\n", 0, m.start()) + 1, label, m.group(0), AUDIO))

    audio = codes.get(AUDIO, "")
    audio_c = compact(audio)
    if re.search(r"\bthrow\b", audio):
        errors.append(AUDIO + ": 统一入口不得抛异常（失败返回 null / false）")
    resolve = compact(body(audio, "private static void Resolve()") or "")
    if not resolve.startswith("if(resolved)return;resolved=true;try{ResolveOfficialMembers();}catch(Exceptione){"):
        errors.append(AUDIO + ": Resolve 必须只解析一次，并把官方成员解析包在 try 里（失败视为入口缺失）")
    if "[MethodImpl(MethodImplOptions.NoInlining)]privatestaticvoidResolveOfficialMembers()" not in audio_c:
        errors.append(AUDIO + ": ResolveOfficialMembers 必须禁止内联（官方类型加载失败的异常要落在 Resolve 的 try 里）")
    members = compact(body(audio, "private static void ResolveOfficialMembers()") or "")
    for token in ('manager.GetMethod("PostCustomSFX",BindingFlags.Public|BindingFlags.Static,null,new[]{typeof(string),typeof(GameObject),typeof(bool)},null);',
                  'manager.GetMethod("Post",BindingFlags.Public|BindingFlags.Static,null,new[]{typeof(string),typeof(GameObject)},null);',
                  'manager.GetMethod("Post",BindingFlags.Public|BindingFlags.Static,null,new[]{typeof(string)},null);',
                  'typeof(Duckov.AudioObject).GetMethod("StopAll",BindingFlags.NonPublic|BindingFlags.Instance);',
                  "stopImmediate=Enum.ToObject(stop.GetParameters()[0].ParameterType,1);"):
        if token not in members:
            errors.append(AUDIO + ": ResolveOfficialMembers 缺少 " + token)
    invoke = compact(body(audio, "private static bool Invoke(MethodInfo method, object target, object[] args, out object result, out Exception error)") or "")
    if invoke != "result=null;error=null;if(method==null)returnfalse;try{result=method.Invoke(target,args);returntrue;}catch(Exceptione){error=e;returnfalse;}":
        errors.append(AUDIO + ": Invoke 必须吞掉异常并经 error 交出（不抛给调用方）")
    stop = compact(body(audio, "internal static bool TryStopAll(Duckov.AudioObject audio, out Exception error)") or "")
    if "if(ReferenceEquals(audio,null)||stopAll==null)returnfalse;" not in stop or "new[]{stopImmediate}" not in stop:
        errors.append(AUDIO + ": TryStopAll 必须按 ReferenceEquals 判空（发声体已销毁时也要停得掉）并以 STOP_MODE.IMMEDIATE 调官方")
    for path, call, count in AUDIO_CALLERS:
        code = compact(codes.get(path, ""))
        if code.count(compact(call)) != count:
            errors.append("%s: 应恰好 %d 处 %s，实际 %d" % (path, count, call, code.count(compact(call))))

    # ---------- 3：巡逻表由宿主经 JsonDataRegistry 读，规则只解析 ----------
    rules = codes.get(PATROL_RULES, "")
    for pattern in (r"\bSystem\s*\.\s*IO\b", r"\bFile\s*\.", r"\bDirectory\s*\.", r"\bStreamReader\b", r"\bReadAllText\b"):
        m = re.search(pattern, rules)
        if m:
            errors.append(PATROL_RULES + ": 纯规则不得读文件（" + m.group(0) + "），文本由宿主经 JsonDataRegistry 读出后交给 FromText")
    from_text = compact(body(rules, "internal static SkyIslandPatrolData FromText(string raw)") or "")
    if not from_text.startswith("if(string.IsNullOrEmpty(raw))returnCreateFallback();try{") \
            or not from_text.endswith("returnCreateFallback();") or "if(TryParse(raw,outresult,outerror))returnresult;" not in from_text:
        errors.append(PATROL_RULES + ": FromText 必须在空文本、解析失败与异常时回退 CreateFallback()")
    for const, value in (("DataSubDirectory", "SkyIsland"), ("DataFileName", "Patrols.json")):
        if not re.search(r"internal const string " + const + r" = \"" + re.escape(value) + r"\";", rules):
            errors.append(PATROL_RULES + ": " + const + " 必须是 \"" + value + "\"")
    if not (root / "Assets/Data/SkyIsland/Patrols.json").is_file():
        errors.append("Assets/Data/SkyIsland/Patrols.json 不存在")
    patrols = compact(codes.get(PATROLS, ""))
    if compact("SkyIslandPatrolRules.FromText(JsonDataRegistry.TryReadDataFile(SkyIslandPatrolRules.DataSubDirectory, "
               "SkyIslandPatrolRules.DataFileName, out raw) ? raw : null);") not in patrols:
        errors.append(PATROLS + ": 巡逻表必须经 JsonDataRegistry.TryReadDataFile 读出后交给 SkyIslandPatrolRules.FromText")

    # ---------- 4：死代码 ----------
    for path, code in codes.items():
        for pattern, label in DEAD_EVERYWHERE:
            m = pattern.search(code)
            if m:
                errors.append("%s:%d: 已删除的 %s 又出现了" % (path, code.count("\n", 0, m.start()) + 1, label))
    for path, pattern in DEAD_IN_FILE:
        m = pattern.search(codes.get(path, ""))
        if m:
            errors.append(path + ": 已删除的死代码又出现了：" + m.group(0))
    fixtures = root / "tests" / "fixtures"
    extra = [root / "tests" / "SimpleJsonHelperTests.cs"]
    for path in (sorted(fixtures.rglob("*")) if fixtures.is_dir() else []) + extra:
        if not path.is_file() or path.suffix not in (".cs", ".py") or {"bin", "obj", "__pycache__"} & set(path.parts):
            continue
        text = path.read_text(encoding="utf-8-sig", errors="replace")
        for token in FIXTURE_DEAD:
            if token in text:
                errors.append(path.relative_to(root).as_posix() + ": 夹具仍引用已删除的 " + token)
    return errors


def main():
    errors = validate()
    if errors:
        print("ModPathAndAudioGuard: FAIL")
        for error in errors:
            print("  - " + error)
        return 1
    print("ModPathAndAudioGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
