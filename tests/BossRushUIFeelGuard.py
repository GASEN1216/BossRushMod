# -*- coding: utf-8 -*-
"""Guard: 共享 UI「质感层」必须接在共享入口上（2026-09-23 审美审查）。

owner 验收要求 UI「不要塑料感」。根因在共享层：所有面板 / 卡片 / 按钮都是平涂圆角块，
按钮没有声音、按下没有任何回馈。修法全部挂在已有的共享入口上，调用方不用改——
所以最怕的就是某次重构把挂载点删掉，全 Mod 一起退回塑料感，而编译、其它守卫全绿。

钉住：
  1. `ZombieModeUIHelper.ApplyButtonColors`（CreateButton / SetButtonBaseColor 都经过它）挂按钮手感与按钮投影 / 斜面，
     按下色走共享 `GetPressedColor`，原地改色时鼠标在上就落到悬停色。
  2. `BossRushUI.ApplyPanelStroke`（ApplyFramedPanelSkin / CreateCard / CreateModalSurface 都经过它）挂面的投影 / 斜面，
     并让描边随层级变化挪回最上层。
  3. 两个遮罩工厂（`BossRushUI.CreateBackdrop`、`ZombieModeUIHelper.CreateModalSurface`）套暗角与淡入。
  4. 新增的程序化贴图 / 材质 / 反射缓存都挂在 `BossRushUI.ResetStaticCaches` 这条卸载路径上。
  5. 常态零开销：`BossRushButtonFeel` 不定义 Update；动效组件播完自己 `enabled = false`，走 unscaled 时间并过暂停门。
  6. 不可点的按钮不出声。
  7. 共享粒子材质工厂只用游戏里确认存在的着色器（UnityPy 直读 resources.assets，2026-09-23）。

每条断言都在内存里做反向检查：删掉对应调用后必须转红。
"""

from pathlib import Path
import re
import sys

ROOT = Path(__file__).resolve().parents[1]

UI = "Common/UI/BossRushUI.cs"
FEEL = "Common/UI/BossRushUIFeel.cs"
HELPER = "ZombieMode/ZombieModeUIHelper.cs"
FX = "Common/Effects/BossRushFxMaterials.cs"
PATHS = [UI, FEEL, HELPER, FX]

# 游戏里 Shader.Find 找不到的着色器（UnityPy 直读本机游戏，2026-09-23 特效审查 VB 文首）。
ABSENT_SHADERS = (
    "Legacy Shaders/Particles/Additive",
    "Particles/Additive",
    "Particles/Alpha Blended",
    "Mobile/Particles/Additive",
    "Mobile/Particles/Alpha Blended",
    "Unlit/Color",
    "Unlit/Transparent",
)


def strip_comments(text):
    text = re.sub(r"/\*.*?\*/", "", text, flags=re.S)
    return re.sub(r"//[^\n]*", "", text)


def method_body(text, signature_regex):
    match = re.search(signature_regex, text)
    if match is None:
        return ""
    start = text.find("{", match.end() - 1)
    if start < 0:
        return ""
    depth = 0
    for index in range(start, len(text)):
        ch = text[index]
        if ch == "{":
            depth += 1
        elif ch == "}":
            depth -= 1
            if depth == 0:
                return text[start + 1:index]
    return ""


def class_body(text, name):
    return method_body(text, r"class\s+" + re.escape(name) + r"\b[^{]*\{")


def check(sources):
    errors = []
    ui = strip_comments(sources[UI])
    feel = strip_comments(sources[FEEL])
    helper = strip_comments(sources[HELPER])
    fx = strip_comments(sources[FX])

    buttons = method_body(helper, r"internal\s+static\s+void\s+ApplyButtonColors\s*\([^)]*\)\s*\{")
    if not buttons:
        errors.append("找不到 ZombieModeUIHelper.ApplyButtonColors")
    else:
        for needle, why in (
            ("BossRushButtonFeel.Attach(button)", "共享按钮必须挂按钮手感（官方悬停 / 点击音效、按下回弹）"),
            ("BossRushUIDepth.ApplyButtonDepth(button,", "共享按钮必须挂投影与斜面"),
            ("colors.pressedColor = BossRushUI.GetPressedColor(normalColor)", "按下色必须走共享 GetPressedColor（全 Mod 一套口径）"),
            ("feel.IsHovered", "原地改色时鼠标在按钮上要落到悬停色"),
        ):
            if needle not in buttons:
                errors.append("ApplyButtonColors：" + why + " -> " + needle)

    stroke = method_body(ui, r"internal\s+static\s+Image\s+ApplyPanelStroke\s*\([^)]*\)\s*\{")
    if not stroke:
        errors.append("找不到 BossRushUI.ApplyPanelStroke")
    else:
        for needle, why in (
            ("BossRushUIDepth.ApplySurfaceDepth(surface, radius, part)", "有边的面必须挂外投影与顶边高光"),
            ("BossRushStrokeOnTop.Track(surface, stroke)", "描边必须随层级变化挪回最上层，否则后加的全宽标题栏会盖掉框线"),
        ):
            if needle not in stroke:
                errors.append("ApplyPanelStroke：" + why + " -> " + needle)

    backdrop = method_body(ui, r"internal\s+static\s+Image\s+CreateBackdrop\s*\([^)]*\)\s*\{")
    if "BossRushUIKit.StyleBackdrop(image)" not in backdrop:
        errors.append("CreateBackdrop 必须套暗角与淡入（BossRushUIKit.StyleBackdrop）")
    modal = method_body(helper, r"internal\s+static\s+GameObject\s+CreateModalSurface\s*\(")
    if "BossRushUIKit.StyleBackdrop(backdropImage)" not in modal:
        errors.append("CreateModalSurface 的遮罩必须与 CreateBackdrop 同口径（BossRushUIKit.StyleBackdrop）")

    reset = method_body(ui, r"public\s+static\s+void\s+ResetStaticCaches\s*\(\s*\)\s*\{")
    for owner in ("BossRushUIDepth", "BossRushUISound", "BossRushUIKit", "BossRushFxMaterials", "BossRushFxKit"):
        if owner + ".ResetStaticCaches()" not in reset:
            errors.append("BossRushUI.ResetStaticCaches 必须清理 " + owner + "（程序化贴图 / 材质带 DontSave，切场景不会自动回收）")

    feel_class = class_body(feel, "BossRushButtonFeel")
    if not feel_class:
        errors.append("找不到 BossRushButtonFeel")
    else:
        if re.search(r"void\s+Update\s*\(", feel_class):
            errors.append("BossRushButtonFeel 不能有 Update：每颗按钮常态零开销，动效交给播完即停的 BossRushButtonMotion")
        down = method_body(feel_class, r"public\s+void\s+OnPointerDown\s*\([^)]*\)\s*\{")
        enter = method_body(feel_class, r"public\s+void\s+OnPointerEnter\s*\([^)]*\)\s*\{")
        for name, body, sound in (("OnPointerDown", down, "PlayClick"), ("OnPointerEnter", enter, "PlayHover")):
            if sound not in body:
                errors.append("BossRushButtonFeel." + name + " 必须播官方 UI 音效 " + sound)
            elif "Interactable" not in body.split(sound)[0]:
                errors.append("BossRushButtonFeel." + name + " 必须先判可点再出声：禁用按钮点了没反应却响一声")

    for animated in ("BossRushButtonMotion", "BossRushUICloseAnimation"):
        body = class_body(feel, animated)
        update = method_body(body, r"void\s+Update\s*\(\s*\)\s*\{")
        if not update:
            errors.append("找不到 " + animated + ".Update")
            continue
        if "BossRushUI.IsGamePaused()" not in update:
            errors.append(animated + " 走 unscaled 时间，必须过暂停门 BossRushUI.IsGamePaused()")
        if "Time.unscaledDeltaTime" not in update:
            errors.append(animated + " 必须走 unscaled 时间（模态会把 timeScale 置 0）")
        if animated == "BossRushButtonMotion" and "enabled = false" not in update:
            errors.append("BossRushButtonMotion 播完必须 enabled = false，常态不跑 Update")

    for name in ABSENT_SHADERS:
        if '"' + name + '"' in fx:
            errors.append("BossRushFxMaterials 引用了游戏里不存在的着色器 " + name)
    if '"Universal Render Pipeline/Particles/Unlit"' not in fx:
        errors.append("BossRushFxMaterials 必须首选游戏里存在且保留透明变体的 URP/Particles/Unlit")
    if "_SURFACE_TYPE_TRANSPARENT" not in fx:
        errors.append("BossRushFxMaterials 必须开 _SURFACE_TYPE_TRANSPARENT（否则落到不透明变体）")
    if 'SetVector("_TintColor", new Vector4(0.5f, 0.5f, 0.5f, 0.5f))' not in fx:
        errors.append("Legacy Alpha Blended 兜底的 _TintColor 必须按原值写中性 0.5（SetVector；SetColor 在 Linear 下会换成 0.214）")
    return errors


REVERSE_PROBES = [
    (HELPER, "BossRushButtonFeel.Attach(button);", "", "按钮手感"),
    (HELPER, "BossRushUIDepth.ApplyButtonDepth(button, normalColor);", "", "按钮投影"),
    (HELPER, "colors.pressedColor = BossRushUI.GetPressedColor(normalColor);",
     "colors.pressedColor = Color.Lerp(normalColor, Color.black, 0.18f);", "按下色两套口径"),
    (UI, "BossRushUIDepth.ApplySurfaceDepth(surface, radius, part);", "", "面的投影"),
    (UI, "BossRushStrokeOnTop.Track(surface, stroke);", "", "描边置顶"),
    (UI, "BossRushUIKit.StyleBackdrop(image);", "", "遮罩暗角"),
    (UI, "BossRushFxMaterials.ResetStaticCaches();", "", "特效材质清理"),
    (FEEL, "            if (!Interactable)\n            {\n                return;\n            }\n            pressed = true;",
     "            pressed = true;", "禁用按钮出声"),
    (FX, '"Universal Render Pipeline/Particles/Unlit"', '"Legacy Shaders/Particles/Additive"', "不存在的着色器"),
    (FX, 'material.SetVector("_TintColor", new Vector4(0.5f, 0.5f, 0.5f, 0.5f));',
     'material.SetColor("_TintColor", new Color(0.5f, 0.5f, 0.5f, 0.5f));', "兜底 Tint 被线性换算"),
]


def main():
    sources = {}
    for rel in PATHS:
        path = ROOT / rel
        if not path.exists():
            print("BossRushUIFeelGuard: FAIL - 缺少 " + rel)
            return 1
        sources[rel] = path.read_text(encoding="utf-8").replace("\r\n", "\n")

    errors = check(sources)
    if errors:
        print("BossRushUIFeelGuard: FAIL")
        for error in errors:
            print("  - " + error)
        return 1

    probes = 0
    for rel, old, new, label in REVERSE_PROBES:
        if old not in sources[rel]:
            print("BossRushUIFeelGuard: FAIL - 反向检查的锚点不存在（" + label + "）：" + old[:60])
            return 1
        mutated = dict(sources)
        mutated[rel] = sources[rel].replace(old, new, 1)
        if not check(mutated):
            print("BossRushUIFeelGuard: FAIL - 反向检查没转红（" + label + "），断言抓不住这类回退")
            return 1
        probes += 1

    print("BossRushUIFeelGuard: PASS（按钮手感 / 面的投影 / 遮罩暗角 / 缓存清理 / 常态零开销 / 着色器可用性；%d 个反向检查）" % probes)
    return 0


if __name__ == "__main__":
    sys.exit(main())
