"""Guard: 共享 UI 库的设计 token、层级表与皮肤注入点必须保持单一事实来源。

盘点背景：改造前全 Mod 有 20 个独立 Canvas、两套遮罩色、零九宫格底图，
sortingOrder 分成 10~1001 与 28000~32000 两个孤岛。本 guard 锁住收口结果，
防止后续界面又各写各的。
"""

from pathlib import Path
import re
import sys


UI_LIB = Path("Common/UI/BossRushUI.cs")
COMPILE_LIST = Path("compile_official.bat")
MOD_BEHAVIOUR = Path("ModBehaviour.cs")

# 这些文件已收口到共享库，不允许再退回裸数值
MIGRATED = [
    Path("BossFilter/BossFilterUi.cs"),
    Path("Achievement/AchievementView.cs"),
    Path("Integration/NPCs/Courier/OriginalConfirmDialogueAdapter.cs"),
    Path("ModeF/ModeFUI.cs"),
    Path("Achievement/SteamAchievementPopup.cs"),
    Path("Integration/Wedding/NPCMarriageSystem.cs"),
    Path("Integration/WishFountain/WishFountainRewardAnimationView.cs"),
    # 只登记会**创建 canvas** 的 Mode H 界面文件；ModeHUIPages.cs 只在既有
    # surface 内摆放内容，不碰 sortingOrder，它的裸数字禁令由 ModeHStructureGuard 覆盖。
    Path("ModeH/ModeHUI.cs"),
    Path("ModeH/ModeHRecoveryPanel.cs"),
]

# CanvasScaler 必须走 ZombieModeUIHelper.ConfigureCanvasScaler（AGENTS 4.14）。
# 只 AddComponent 不配置会退化成 ConstantPixelSize，4K 屏上面板缩成一小块；
# 各写各的参数则会在不同界面之间产生不一致的缩放。
# 唯一允许出现 uiScaleMode 赋值的地方就是 helper 自身的实现。
CANVAS_SCALER_HELPER = Path("ZombieMode/ZombieModeUIHelper.cs")
SCAN_EXCLUDE_DIRS = {"Build", "tests", ".git", ".kiro", ".codex_tmp", "鸭科夫源码", "wiki-site", ".qoder", "obj", "bin"}

# legacy UI.Text + 内置 Arial 渲染不了中文，这些文件已转 TMP 或改用字体解析器
NO_ARIAL = [
    Path("Achievement/SteamAchievementPopup.cs"),
    Path("DebugAndTools/NPCTeleportUI.cs"),
    Path("DebugAndTools/F3DebugCheatMenuUi.cs"),
]


def fail(message):
    print("BossRushUISharedLibraryGuard: FAIL - " + message)
    return 1


def main():
    if not UI_LIB.exists():
        return fail("缺少共享 UI 库 Common/UI/BossRushUI.cs")

    lib = UI_LIB.read_text(encoding="utf-8")

    # 1) 层级表必须完整且严格递增，否则模态互压的老问题会回来
    layers = re.findall(r"internal const int (\w+) = (\d+);", lib)
    expected = ["WorldOverlay", "Hud", "HudOverlay", "Panel", "Modal", "ModalConfirm", "Toast", "Cutscene"]
    names = [name for name, _ in layers]
    for want in expected:
        if want not in names:
            return fail("层级表缺少 " + want)
    values = [int(v) for _, v in layers]
    if values != sorted(values):
        return fail("BossRushUILayers 的值必须严格递增，当前顺序: " + str(values))

    # 2) 皮肤注入点必须存在——图集是两步走的第二步，接口不能被删掉
    for needle, message in (
        ("internal static void InjectPanelSprite(Sprite sprite)", "缺少面板图集注入点"),
        ("internal static void InjectButtonSprite(Sprite sprite)", "缺少按钮图集注入点"),
        ("internal static Sprite GetRoundedSprite(int radius)", "缺少程序化圆角九宫格"),
        ("internal static void ApplyPanelSkin(Image image, int radius)", "缺少皮肤应用入口"),
        ("image.type = Image.Type.Sliced;", "圆角底图必须按九宫格拉伸，否则边角会变形"),
        ("new Vector4(radius, radius, radius, radius)", "Sprite.Create 必须带 border 才是九宫格"),
    ):
        if needle not in lib:
            return fail(message + " -> " + needle)

    # 3) 程序化贴图带 DontSave，必须显式销毁，否则切场景泄漏
    if "public static void ResetStaticCaches()" not in lib:
        return fail("共享 UI 库必须提供 ResetStaticCaches")
    reset_index = lib.find("public static void ResetStaticCaches()")
    reset_body = lib[reset_index:reset_index + 900]
    for needle in ("Object.Destroy(sprite);", "roundedSpriteCache.Clear();"):
        if needle not in reset_body:
            return fail("ResetStaticCaches 必须销毁程序化 Sprite/Texture -> " + needle)
    if "BossRushUI.ResetStaticCaches()" not in MOD_BEHAVIOUR.read_text(encoding="utf-8"):
        return fail("ResetStaticCaches 必须挂到 ModBehaviour 的 OnDestroy 路径")

    # 4) 字体解析不得另起一套，必须转发 ZombieModeUIHelper 的四级回退
    if "ZombieModeUIHelper.GetGameFont()" not in lib:
        return fail("字体必须走 ZombieModeUIHelper.GetGameFont() 的四级回退")

    # 5) 新增 .cs 必须进编译清单（AGENTS 4.1）
    if "Common\\UI\\BossRushUI.cs" not in COMPILE_LIST.read_text(encoding="utf-8", errors="ignore"):
        return fail("Common\\UI\\BossRushUI.cs 未登记进 compile_official.bat")

    # 6) 已迁移界面不得回退成裸 sortingOrder / 第二套遮罩色
    for path in MIGRATED:
        if not path.exists():
            return fail("缺少已迁移文件 " + path.as_posix())
        text = path.read_text(encoding="utf-8", errors="ignore")
        if "BossRushUILayers." not in text:
            return fail(path.as_posix() + " 必须使用 BossRushUILayers 常量而不是魔法数字")
        if "new Color(0f, 0f, 0f, 0.7f)" in text:
            return fail(path.as_posix() + " 仍在用第二套遮罩色，应统一为 BossRushUIColors.Backdrop")

    # 7) 源码里不得再出现内置 Arial：它渲染不了中文
    for path in NO_ARIAL:
        if not path.exists():
            continue
        if 'GetBuiltinResource<Font>("Arial.ttf")' in path.read_text(encoding="utf-8", errors="ignore"):
            return fail(path.as_posix() + " 仍在用内置 Arial，中文会显示为方块")

    # 8) CanvasScaler 不得再手写参数，必须走 ZombieModeUIHelper.ConfigureCanvasScaler
    offenders = []
    for path in sorted(Path(".").rglob("*.cs")):
        if any(part in SCAN_EXCLUDE_DIRS for part in path.parts):
            continue
        if path == CANVAS_SCALER_HELPER:
            continue
        try:
            text = path.read_text(encoding="utf-8", errors="ignore")
        except Exception:
            continue
        if re.search(r"\.uiScaleMode\s*=", text):
            offenders.append(path.as_posix())

    if offenders:
        return fail(
            "CanvasScaler 必须调 ZombieModeUIHelper.ConfigureCanvasScaler，"
            "不要各写各的参数 -> " + ", ".join(offenders))

    print("BossRushUISharedLibraryGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
