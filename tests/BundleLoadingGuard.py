"""BundleLoadingGuard：AssetBundle 的打开与「找已加载的包」各只有一个入口，且不会对同一文件重复打开。

不变式（2026-11-19 统一，规则写在 Utilities/ResourceBundleLoader.cs）：
1. 生产代码里 AssetBundle.LoadFromFile / LoadFromFileAsync / LoadFromMemory(Async) / LoadFromStream(Async)
   只出现在 ResourceBundleLoader.cs（含 #if BOSSRUSH_DEV 区块；也不许用字符串反射这些方法）。
2. AssetBundle.GetAllLoadedAssetBundles 全仓库只有 ResourceBundleLoader.FindLoaded 一处；
   匹配规则（IsNamed）是「包内名按 / 与 \\ 取最后一段，与文件名 OrdinalIgnoreCase 全等」，不用子串。
   七处调用点都改调 FindLoaded，传入的名字与它旁边的包路径最后一段相同。
3. 基地建筑（含公告板、展示柜）的 RunSpecial 都带 alreadyLoaded；RunSpecial 换场景重试前重新判一次。
4. ItemFactory / EquipmentFactory 在 LoadFromFile 前先复用本工厂已持有的同名包；
   遗种巢 / 日报箱（与婚礼、许愿台一样）重载前先卸掉旧句柄。
5. 已删的死代码不再出现：同步 LoadAllItems / LoadAllEquipment、DebugListBundleAssets、FindAlreadyLoadedAssetBundle、
   EntityModelFactory 的 HasPrefab / GetLoadedPrefabNames / LoadAllBundles / IsInitialized / _modPath、
   NPCBubbleAnimator.CreateFromBundle、龙王的异步 LoadAssetBundle / UnloadAssetBundle / ClearCache 空分支、
   PhantomWitchScytheIds.AssetBundlePath、三个建筑的「占位 bundle」判断。

文本守卫防不住「保留 token、杀掉执行路径」；FindLoaded 的匹配与 RunSpecial 的重试由 ResourceProduction 执行回归覆盖，
宿主判据由 ContentBuildingOwnership 执行回归覆盖。
"""

from pathlib import Path
import re
import sys

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))
sys.path.insert(0, str(ROOT / "tests"))

from compile_list import parse_compile_sources  # noqa: E402
from cs_source_util import strip_comments  # noqa: E402

LOADER = "Utilities/ResourceBundleLoader.cs"
NATIVE_LOAD = re.compile(r"\bAssetBundle\s*\.\s*(LoadFromFile|LoadFromFileAsync|LoadFromMemory|LoadFromMemoryAsync|LoadFromStream|LoadFromStreamAsync)\s*\(")
REFLECTED = re.compile(r"\"(LoadFromFile|LoadFromFileAsync|LoadFromMemory|LoadFromMemoryAsync|LoadFromStream|LoadFromStreamAsync|GetAllLoadedAssetBundles)\"")
FIND_ALL = re.compile(r"\bGetAllLoadedAssetBundles\s*\(")

# (调用文件, 调用语句, 名字常量所在文件, 名字常量, 路径常量)；ItemFactory / ImageViewerUI 传的是 Assets/Items 下的文件名本身。
FIND_CALLERS = [
    ("Integration/Codex/CodexPortraitCache.cs", "ResourceBundleLoader.FindLoaded(CodexTuning.PortraitBundleName)", 1,
     "Integration/Codex/CodexTuning.cs", "PortraitBundleName", "PortraitBundleRelativePath"),
    ("Common/UI/BossRushUISkinLoader.cs", "ResourceBundleLoader.FindLoaded(SkinBundleName)", 1,
     "Common/UI/BossRushUISkinLoader.cs", "SkinBundleName", "BundleRelativePath"),
    ("Campaign/CampaignAssetCache.cs", "ResourceBundleLoader.FindLoaded(PresentationBundleName)", 1,
     "Campaign/CampaignAssetCache.cs", "PresentationBundleName", "BundleRelativePath"),
    ("ModeG/ModeGPresentationAssetCache.cs", "ResourceBundleLoader.FindLoaded(PresentationBundleName)", 1,
     "ModeG/ModeGPresentationAssetCache.cs", "PresentationBundleName", "BundleRelativePath"),
    ("ModeH/ModeHPresentationAssetCache.cs", "ResourceBundleLoader.FindLoaded(PresentationBundleName)", 1,
     "ModeH/ModeHPresentationAssetCache.cs", "PresentationBundleName", "BundleRelativePath"),
    ("Achievement/AchievementIconLoader.cs", "ResourceBundleLoader.FindLoaded(BUNDLE_NAME)", 2,
     "Achievement/AchievementIconLoader.cs", "BUNDLE_NAME", "BUNDLE_RELATIVE_PATH"),
    ("Integration/ItemFactory.cs", "ResourceBundleLoader.FindLoaded(bundleName)", 2, None, None, None),
    ("Integration/UI/ImageViewerUI.cs", "ResourceBundleLoader.FindLoaded(bundleName)", 1, None, None, None),
]

BOOTSTRAP = "Integration/BossRushIntegrationRuntimeModule_DeferredBootstrap.cs"
HOST = "Integration/IntegrationHostCompatibility.cs"
HELD_PROBES = [
    ('FactoryResourceLoading.RunSpecial(_owner, "Assets/buildings/bossrush_campaign_board", _owner.InitCampaignBoardBuilding, () => _owner.HasCampaignBoardBundle);',
     "internal bool HasCampaignBoardBundle { get { return _campaignBoardBuilder != null && _campaignBoardBuilder.HasAssetBundle; } }",
     "Campaign/CampaignBoardBuilder.cs", "campaignBoardModelBundle"),
    ('FactoryResourceLoading.RunSpecial(_owner, "Assets/buildings/bossrush_backmountain_showcase", _owner.InitBackMountainShowcase, () => _owner.HasBackMountainShowcaseBundle);',
     "internal bool HasBackMountainShowcaseBundle { get { return _showcaseBuildingBuilder != null && _showcaseBuildingBuilder.HasAssetBundle; } }",
     "Integration/BackMountain/ShowcaseBuildingBuilder.cs", "showcaseModelBundle"),
]

# 四个自持模型包的建筑：模型加载方法里重载前先卸旧句柄（文件, 方法签名, 卸载语句, 加载语句）。
# 钉在加载方法体内：清理方法里有同一句卸载，按整份文件找会被它骗过（2026-11-19 反向验证实测）。
RELOADERS = [
    ("PetNest/PetNestBuilder.cs", "private void LoadPetNestBuildingModel()",
     'AssetBundleUnloadHelper.TryUnload(petNestAssetBundle, "[PetNest]"); petNestAssetBundle = null;',
     "petNestAssetBundle = ResourceBundleLoader.LoadFromFile(bundlePath);"),
    ("Integration/DailyReport/DailyReportMailboxBuilder.cs", "private void LoadDailyReportBuildingModel()",
     "AssetBundleUnloadHelper.TryUnload(dailyReportAssetBundle, DailyReportTuning.LogPrefix); dailyReportAssetBundle = null;",
     "dailyReportAssetBundle = ResourceBundleLoader.LoadFromFile(bundlePath);"),
    ("Integration/Wedding/WeddingBuildingInjector.cs", "private void LoadWeddingBuildingModel()",
     'AssetBundleUnloadHelper.TryUnload(weddingAssetBundle, "[WeddingBuilding]"); weddingAssetBundle = null;',
     "weddingAssetBundle = ResourceBundleLoader.LoadFromFile(bundlePath);"),
    ("Integration/WishFountain/WishFountainBuilder.cs", "private void LoadStarwishBuildingModel()",
     'AssetBundleUnloadHelper.TryUnload(starwishAssetBundle, "[WishFountain]"); starwishAssetBundle = null;',
     "starwishAssetBundle = ResourceBundleLoader.LoadFromFile(bundlePath);"),
]

DEAD_EVERYWHERE = [
    (re.compile(r"\bLoadAllItems\s*\("), "同步 ItemFactory.LoadAllItems（用 LoadAllItemsAsync）"),
    (re.compile(r"\bLoadAllEquipment\s*\("), "同步 EquipmentFactory.LoadAllEquipment（用 LoadAllEquipmentAsync）"),
    (re.compile(r"\bDebugListBundleAssets\b"), "ItemFactory.DebugListBundleAssets"),
    (re.compile(r"\bFindAlreadyLoadedAssetBundle\b"), "ItemFactory.FindAlreadyLoadedAssetBundle（用 ResourceBundleLoader.FindLoaded）"),
    (re.compile(r"\bFindLoadedBundle\b"), "AchievementIconLoader.FindLoadedBundle（用 ResourceBundleLoader.FindLoaded）"),
    (re.compile(r"\bCreateFromBundle\b"), "NPCBubbleAnimator.CreateFromBundle"),
    (re.compile(r"\bUnloadAssetBundle\b"), "DragonKingAssetManager.UnloadAssetBundle"),
    (re.compile(r"\bLoadAssetBundle\s*\("), "DragonKingAssetManager 的异步 LoadAssetBundle"),
    (re.compile(r"PLACEHOLDER_BUNDLE|PlaceholderBundle\s*\("), "建筑的「占位 bundle」判断"),
    (re.compile(r"\bEntityModelFactory\s*\.\s*(HasPrefab|GetLoadedPrefabNames|LoadAllBundles|IsInitialized)\b"), "EntityModelFactory 查询方法"),
    (re.compile(r"\bPhantomWitchScytheIds\s*\.\s*AssetBundlePath\b"), "PhantomWitchScytheIds.AssetBundlePath"),
]
DEAD_IN_FILE = [
    ("Utilities/EntityModelFactory.cs", re.compile(r"\b(HasPrefab|GetLoadedPrefabNames|LoadAllBundles|IsInitialized|_modPath)\b")),
    ("Integration/PhantomWitch/PhantomWitchScytheIds.cs", re.compile(r"\bAssetBundlePath\b")),
]
# 注释里的用法示例也会误导：这两个旧同步入口连注释都不许再出现。
STALE_IN_COMMENTS = re.compile(r"\b(LoadAllItems|LoadAllEquipment)\s*\(")


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


def body(code, signature):
    """方法体（不含外层花括号）；签名不唯一或缺失返回 None。"""
    if code.count(signature) != 1:
        return None
    masked = mask_literals(code)
    start = masked.index("{", code.index(signature)) + 1
    depth, end = 1, start
    while depth and end < len(masked):
        depth += {"{": 1, "}": -1}.get(masked[end], 0)
        end += 1
    return code[start:end - 1]


def call_arguments(code, start):
    """从 code[start] 处的 "(" 起，按顶层逗号切出实参（字面量里的逗号与括号不算）。"""
    masked = mask_literals(code)
    depth, args, last = 0, [], start + 1
    for i in range(start, len(code)):
        c = masked[i]
        if c in "([{":
            depth += 1
        elif c in ")]}":
            depth -= 1
            if depth == 0:
                args.append(code[last:i].strip())
                return args
        elif c == "," and depth == 1:
            args.append(code[last:i].strip())
            last = i + 1
    return args


def const_value(code, name):
    m = re.search(r"\bconst\s+string\s+" + re.escape(name) + r"\s*=\s*\"([^\"]*)\"\s*;", code)
    return m.group(1) if m else None


def validate(root=ROOT):
    errors = []

    def raw(path):
        p = root / path
        return p.read_text(encoding="utf-8-sig") if p.is_file() else ""

    bat = (root / "compile_official.bat").read_text(encoding="utf-8", errors="replace")
    sources = [s for s in parse_compile_sources(bat) if (root / s).is_file()]
    if LOADER not in sources:
        errors.append(LOADER + " 没有登记进 compile_official.bat")
    codes = {path: strip_comments(raw(path)) for path in sources}

    # ---------- 1：原生打开只在 ResourceBundleLoader ----------
    for path, code in codes.items():
        for m in NATIVE_LOAD.finditer(code):
            if path != LOADER or m.group(1) not in ("LoadFromFile", "LoadFromFileAsync"):
                errors.append(path + ": AssetBundle." + m.group(1) + " 只能出现在 " + LOADER + "，改调 ResourceBundleLoader.LoadFromFile / Prepare")
        for m in REFLECTED.finditer(code):
            errors.append(path + ": 不得用字符串反射 AssetBundle." + m.group(1))
    loader = codes.get(LOADER, "")
    load_sync = compact(body(loader, "internal static AssetBundle LoadFromFile(string path)") or "")
    prepare = compact(body(loader, "internal static IEnumerator Prepare(string path, bool prefabs, Func<bool> cancelled, Action consumer, float timeoutSeconds = 60f)") or "")
    if "AssetBundleresult=AssetBundle.LoadFromFile(path);" not in load_sync:
        errors.append(LOADER + ": 同步 LoadFromFile 必须在 ResourceBundleLoader.LoadFromFile 里调 AssetBundle.LoadFromFile(path)")
    if "operation.Request=AssetBundle.LoadFromFileAsync(path);" not in prepare:
        errors.append(LOADER + ": 异步加载必须在 ResourceBundleLoader.Prepare 里调 AssetBundle.LoadFromFileAsync(path)")
    if len(NATIVE_LOAD.findall(loader)) != 2:
        errors.append(LOADER + ": 原生打开只应有 LoadFromFile 与 Prepare 两处")

    # ---------- 2：找已加载的包只有 FindLoaded ----------
    hits = [(path, len(FIND_ALL.findall(code))) for path, code in codes.items() if FIND_ALL.search(code)]
    find = body(loader, "internal static AssetBundle FindLoaded(string bundleFileName)")
    if hits != [(LOADER, 1)] or find is None or len(FIND_ALL.findall(find)) != 1:
        errors.append("AssetBundle.GetAllLoadedAssetBundles 只能出现在 ResourceBundleLoader.FindLoaded 里一次，实际：%r" % hits)
    find_c = compact(find or "")
    for token in ("if(string.IsNullOrEmpty(bundleFileName))returnnull;",
                  "foreach(AssetBundleloadedinAssetBundle.GetAllLoadedAssetBundles())",
                  "if(IsNamed(loaded,bundleFileName))returnloaded;", "catch{}", "returnnull;"):
        if token not in find_c:
            errors.append("ResourceBundleLoader.FindLoaded 缺少 " + token + "（失败要返回 null，不抛给调用方）")
    named = compact(body(loader, "internal static bool IsNamed(AssetBundle bundle, string bundleFileName)") or "")
    for token in ("if(bundle==null||string.IsNullOrEmpty(bundleFileName))returnfalse;", "intcut=name.LastIndexOfAny(NameSeparators);",
                  "returnstring.Equals(cut<0?name:name.Substring(cut+1),bundleFileName,StringComparison.OrdinalIgnoreCase);"):
        if token not in named:
            errors.append("ResourceBundleLoader.IsNamed 必须是「最后一段 OrdinalIgnoreCase 全等」，缺少 " + token)
    if re.search(r"\.(IndexOf|Contains|StartsWith|EndsWith)\(", named):
        errors.append("ResourceBundleLoader.IsNamed 不得用子串 / 前后缀匹配")
    if "privatestaticreadonlychar[]NameSeparators={'/','\\\\'};" not in compact(loader):
        errors.append("ResourceBundleLoader.NameSeparators 必须是 { '/', '\\\\' }")
    for path, call, count, const_file, name_const, path_const in FIND_CALLERS:
        code = codes.get(path, "")
        if code.count(call) != count:
            errors.append(path + ": 应恰好 %d 处 %s，实际 %d" % (count, call, code.count(call)))
        if const_file:
            consts = codes.get(const_file, "")
            name, rel = const_value(consts, name_const), const_value(consts, path_const)
            if not name or not rel or rel.replace("\\", "/").rsplit("/", 1)[-1] != name:
                errors.append(const_file + ": %s=%r 必须等于 %s=%r 的最后一段（全等匹配不能漏掉原来能命中的包）"
                              % (name_const, name, path_const, rel))

    # ---------- 3：基地建筑 RunSpecial 带 alreadyLoaded，重试前再判 ----------
    bootstrap = compact(codes.get(BOOTSTRAP, ""))
    host = compact(codes.get(HOST, ""))
    for call, forward, builder_file, field in HELD_PROBES:
        if bootstrap.count(compact(call)) != 1:
            errors.append(BOOTSTRAP + ": 缺少 " + call)
        if host.count(compact(forward)) != 1:
            errors.append(HOST + ": 缺少只读已有模块的判据 " + forward)
        builder = compact(codes.get(builder_file, ""))
        if ("internalboolHasAssetBundle{get{return" + field + "!=null;}}") not in builder:
            errors.append(builder_file + ": HasAssetBundle 必须读模型包持有字段 " + field)
        if ("out" + field + "))") not in builder:
            errors.append(builder_file + ": " + field + " 必须是 TryInstantiateBundle 交出的包")
    plain = codes.get(BOOTSTRAP, "")
    for m in re.finditer(r"FactoryResourceLoading\s*\.\s*RunSpecial\s*\(", plain):
        args = call_arguments(plain, m.end() - 1)
        if len(args) > 1 and args[1].startswith('"Assets/buildings/') and len(args) != 4:
            errors.append(BOOTSTRAP + ": 建筑 " + args[1] + " 的 RunSpecial 必须带 alreadyLoaded（建筑自己持有包时不再异步重开）")
    run = compact(body(codes.get("Integration/FactoryResourceLoading.cs", ""),
                       "internal static IEnumerator RunSpecial(ModBehaviour owner, string relativePath, Action consume, Func<bool> alreadyLoaded = null)") or "")
    first = run.find("if(alreadyLoaded!=null&&alreadyLoaded()){guardedConsume();yieldbreak;}")
    loop = run.find("while(owner!=null){")
    wait = run.find("while(owner!=null&&(SceneLoader.IsSceneLoading||LevelManager.LevelInitializing))yieldreturnnull;")
    retry = run.find("if(owner!=null&&alreadyLoaded!=null&&alreadyLoaded()){guardedConsume();yieldbreak;}")
    if not (0 <= first < loop < wait < retry):
        errors.append("FactoryResourceLoading.RunSpecial 必须先判 alreadyLoaded，并在每次换场景重试前（等待场景之后）再判一次：%r"
                      % [first, loop, wait, retry])

    # ---------- 4：工厂先复用自持包；建筑重载先卸旧句柄 ----------
    item = codes.get("Integration/ItemFactory.cs", "")
    held = compact(body(item, "private static AssetBundle FindHeldBundle(string bundleName)") or "")
    if held != "AssetBundleheld;returnloadedAssetBundles.TryGetValue(bundleName,outheld)&&held!=null?held:null;":
        errors.append("ItemFactory.FindHeldBundle 必须只查本工厂 loadedAssetBundles 里仍有效的同名包")
    owned = compact(body(codes.get("Integration/EquipmentFactory.cs", ""), "private static AssetBundle FindOwnedBundle(string bundleName)") or "")
    if owned != "foreach(AssetBundleownedinownedBundles){if(ResourceBundleLoader.IsNamed(owned,bundleName))returnowned;}returnnull;":
        errors.append("EquipmentFactory.FindOwnedBundle 必须只查本工厂 ownedBundles，按 ResourceBundleLoader.IsNamed 匹配")
    for path, finder in (("Integration/ItemFactory.cs", "FindHeldBundle"), ("Integration/EquipmentFactory.cs", "FindOwnedBundle")):
        internal = compact(body(codes.get(path, ""), "private static int LoadBundleInternal(string bundlePath, string bundleName)") or "")
        order = ("boolacquired=false,retained=false;", "bundle=" + finder + "(bundleName);",
                 "if(bundle==null){bundle=ResourceBundleLoader.LoadFromFile(bundlePath);acquired=bundle!=null;}",
                 "if(acquired&&!retained&&bundle!=null)bundle.Unload(true);")
        positions = [internal.find(token) for token in order]
        if -1 in positions or positions != sorted(positions) or internal.count("LoadFromFile(") != 1:
            errors.append(path + ": LoadBundleInternal 必须 先复用 " + finder + " → 没有才 LoadFromFile 并记 acquired → 只卸本次打开的：%r" % positions)
    fx = compact(body(codes.get("SkyIsland/SkyIslandFxAssets.cs", ""), "private static void Load()") or "")
    if "bundle=ResourceBundleLoader.LoadFromFile(path);" not in fx:
        errors.append("SkyIsland/SkyIslandFxAssets.cs: 特效小包必须经 ResourceBundleLoader.LoadFromFile 打开")
    for path, signature, unload, load in RELOADERS:
        code = compact(body(codes.get(path, ""), signature) or "")
        a, b = code.find(compact(unload)), code.find(compact(load))
        if a < 0 or b < 0 or a > b or code.count(compact(load)) != 1 or code.count(compact(unload)) != 1:
            errors.append(path + ": " + signature + " 重载模型包前必须先卸旧句柄：" + unload)

    # ---------- 5：死代码 ----------
    for path, code in codes.items():
        for pattern, label in DEAD_EVERYWHERE:
            if pattern.search(code):
                errors.append(path + ": 已删除的 " + label + " 又出现了")
        if STALE_IN_COMMENTS.search(raw(path)):
            errors.append(path + ": 注释里还在写已删除的同步 LoadAllItems() / LoadAllEquipment()")
    for path, pattern in DEAD_IN_FILE:
        m = pattern.search(codes.get(path, ""))
        if m:
            errors.append(path + ": 已删除的死代码 " + m.group(1 if m.groups() else 0) + " 又出现了")
    clear = compact(body(codes.get("Integration/DragonKing/DragonKingAssetManager.cs", ""), "public static void ClearCache()") or "")
    if not clear or "assetBundleRefCount<=0" in clear:
        errors.append("DragonKingAssetManager.ClearCache 不应再有引用计数归零后只写日志的空分支")
    fixtures = root / "tests" / "fixtures"
    for path in sorted(fixtures.rglob("*")) if fixtures.is_dir() else []:
        if not path.is_file() or path.suffix not in (".cs", ".py") or {"bin", "obj"} & set(path.parts):
            continue
        text = path.read_text(encoding="utf-8-sig", errors="replace")
        for token in ("FindAlreadyLoadedAssetBundle", "DebugListBundleAssets", "CreateFromBundle"):
            if token in text:
                errors.append(path.relative_to(root).as_posix() + ": 夹具仍引用已删除的 " + token)
    return errors


def main():
    errors = validate()
    if errors:
        print("BundleLoadingGuard: FAIL")
        for error in errors:
            print("  - " + error)
        return 1
    print("BundleLoadingGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
