"""Guard: UI 图集换皮加载器的 fail-open 契约与两个时序陷阱。

规格见 docs/制作教程/BossRushUI_图集规格.md。守三件事：

  1. **fail-open**。bundle 缺失/加载失败一律不注入，程序化圆角皮肤继续工作。
     这与 Mode G 展示资源的 fail-closed 完全相反：那里缺资源就该拒绝进入，
     而 UI 底图缺失只是观感降级，不该挡住玩法。加载器里不许出现 fail-closed 味道的早退。

  2. **注入必须早于任何面板创建**。ApplyPanelSkin 只在创建 Image 时赋一次 sprite，
     已建好的面板不会追溯换图。因此注入点挂在 InitializeAlwaysOnRuntime（Awake 阶段），
     不能挪到某个界面第一次打开时。

  3. **Cleanup 必须先复位注入点再 Unload(true)**。顺序反了的话，BossRushUISkin
     里留着的是已销毁的 Sprite 引用，之后每个面板都会贴一张空图（白板）。

另外不提供 raw PNG fallback 是刻意的：运行时 LoadImage 出来的散图没有九宫格 border，
拉伸会糊，却让人误以为素材已生效。
"""

from pathlib import Path
import re
import sys

LOADER = Path("Common/UI/BossRushUISkinLoader.cs")
SKIN = Path("Common/UI/BossRushUI.cs")
ALWAYS_ON = Path("Utilities/AlwaysOnRuntimeHooks.cs")
MOD_BEHAVIOUR = Path("ModBehaviour.cs")


def fail(message):
    print("BossRushUISkinLoaderGuard: FAIL - " + message)
    return 1


def strip_comments(text):
    text = re.sub(r"/\*.*?\*/", "", text, flags=re.S)
    return re.sub(r"//[^\n]*", "", text)


def extract_method(text, signature):
    start = text.find(signature)
    if start < 0:
        return ""
    brace = text.find("{", start)
    if brace < 0:
        return ""
    depth = 0
    for i in range(brace, len(text)):
        if text[i] == "{":
            depth += 1
        elif text[i] == "}":
            depth -= 1
            if depth == 0:
                return text[start:i + 1]
    return ""


def main():
    for path in (LOADER, SKIN, ALWAYS_ON, MOD_BEHAVIOUR):
        if not path.is_file():
            return fail("找不到 " + path.as_posix())

    loader = strip_comments(LOADER.read_text(encoding="utf-8", errors="ignore"))
    skin = strip_comments(SKIN.read_text(encoding="utf-8", errors="ignore"))
    always_on = strip_comments(ALWAYS_ON.read_text(encoding="utf-8", errors="ignore"))
    mod_behaviour = strip_comments(MOD_BEHAVIOUR.read_text(encoding="utf-8", errors="ignore"))

    # ---- 1) 注入点仍然存在且 ApplyPanelSkin 每次现查（调用方零改动的前提）----
    for token in ("InjectPanelSprite", "InjectButtonSprite", "GetPanelSprite", "GetButtonSprite"):
        if token not in skin:
            return fail(SKIN.as_posix() + " 缺少皮肤注入点 " + token)

    apply_skin = extract_method(skin, "internal static void ApplyPanelSkin(")
    if not apply_skin:
        apply_skin = extract_method(skin, "public static void ApplyPanelSkin(")
    if not apply_skin:
        return fail(SKIN.as_posix() + " 找不到 ApplyPanelSkin 方法体")
    if "BossRushUISkin.Get" not in apply_skin:
        return fail(
            SKIN.as_posix() + " 的 ApplyPanelSkin 不再现查 BossRushUISkin。"
            "一旦改成缓存 Sprite，注入就只对之后创建的面板生效，"
            "「调用方零改动」的承诺就断了。")

    # ---- 2) fail-open ----
    ensure = extract_method(loader, "internal static void EnsureInjected()")
    if not ensure:
        return fail(LOADER.as_posix() + " 找不到 EnsureInjected 方法体")
    if "throw" in ensure:
        return fail(
            LOADER.as_posix() + " 的 EnsureInjected 会抛异常。"
            "UI 底图缺失只是观感降级，必须 fail-open 让程序化皮肤继续工作。")

    # 不许引入 raw PNG fallback：散图没有九宫格 border，拉伸会糊却看似生效
    if "LoadImage" in loader:
        return fail(
            LOADER.as_posix() + " 出现了 raw PNG fallback（LoadImage）。"
            "运行时加载的散图没有九宫格 border，拉伸会变形，"
            "却让人误以为素材已生效；九宫格必须在 Unity Sprite Editor 里预设后打 bundle。")

    # ---- 3) 注入时机：必须挂在 always-on 初始化，早于任何面板创建 ----
    init = extract_method(always_on, "internal void InitializeAlwaysOnRuntime()")
    if not init:
        return fail(ALWAYS_ON.as_posix() + " 找不到 InitializeAlwaysOnRuntime 方法体")
    if "BossRushUISkinLoader.EnsureInjected" not in init:
        return fail(
            ALWAYS_ON.as_posix() + " 的 InitializeAlwaysOnRuntime 没有注入 UI 皮肤。"
            "ApplyPanelSkin 只在面板创建时赋一次 sprite，注入晚于建面板就换不了皮。")

    # ---- 4) Cleanup 顺序：先复位注入点，再 Unload ----
    cleanup = extract_method(loader, "internal static void Cleanup()")
    if not cleanup:
        return fail(LOADER.as_posix() + " 找不到 Cleanup 方法体")
    inject_at = cleanup.find("InjectPanelSprite(null)")
    unload_at = cleanup.find("Unload(")
    if inject_at < 0:
        return fail(LOADER.as_posix() + " 的 Cleanup 没有把注入点复位为 null")
    if unload_at >= 0 and inject_at > unload_at:
        return fail(
            LOADER.as_posix() + " 的 Cleanup 先 Unload 再复位注入点，顺序反了。"
            "Unload(true) 之后注入点里留的是已销毁的 Sprite，"
            "之后每个面板都会贴一张空图（白板）。")

    if "BossRushUISkinLoader.Cleanup" not in mod_behaviour:
        return fail(
            MOD_BEHAVIOUR.as_posix() + " 宿主销毁时没有调用 BossRushUISkinLoader.Cleanup，"
            "bundle 会泄漏到下一次 runtime。")

    print("BossRushUISkinLoaderGuard: PASS（fail-open + 注入时机 + 清理顺序）")
    return 0


if __name__ == "__main__":
    sys.exit(main())
