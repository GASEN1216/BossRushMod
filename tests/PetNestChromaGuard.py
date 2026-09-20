"""遗种巢炫彩 / 异色：调色板不变式 + 文字可读性 + 接线。

守的三件事：
1. 调色板 id 是存档面（写进 PetNestPetRecord.chromaA/B），必须唯一且只增不改；
2. 文字色在深色面板（BossRushUIColors.SurfaceRaised 卡片底）上正文对比度 >= 4.5:1
   （AGENTS 4.14）。owner 明确要求「黑白弄成黑白渐变色，但要确保字能够看见」——
   本色的黑在深色面板上是看不见的，所以 TextHex 必须是**可读的那一档**，不是本色；
3. 炫彩必须真的接到玩家看得见的三处：名字装饰、孵化演出、崽身上的光环。

计算口径与 AGENTS 4.14 一致：游戏跑在 Linear 色彩空间，半透明底在线性光里混合，
因此先把 sRGB 转线性、按 alpha 与 Surface 合成出**实际底色**，再算 WCAG 相对亮度。
"""
from pathlib import Path
import re
import sys

ROOT = Path(__file__).resolve().parents[1]
CHROMA = ROOT / "PetNest/PetNestChroma.cs"
SPAWNER = ROOT / "PetNest/PetNestCompanionSpawner.cs"
REVEAL = ROOT / "PetNest/PetNestHatchRevealView.cs"
SERVICE = ROOT / "PetNest/PetNestService.cs"
TUNING = ROOT / "PetNest/PetNestTuning.cs"
CODEC = ROOT / "PetNest/PetNestPersistenceCodec.cs"
MODELS = ROOT / "PetNest/PetNestModels.cs"

# BossRushUI.cs 里的面板底色（卡片底走 SurfaceRaised，比 Surface 亮一点，是更严的那个？
# 不是：更亮的底对浅色文字更不利，所以两个都算，取最差的一档。）
PANELS = [
    ("Surface", (0.045, 0.055, 0.065), 0.92),
    ("SurfaceRaised", (0.075, 0.09, 0.105), 0.95),
]
# 面板后面是游戏画面 + Backdrop 遮罩，这里按最亮的合理底（中灰）估，结论更保守。
BEHIND = (0.35, 0.35, 0.35)


def srgb_to_linear(c):
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


def relative_luminance(rgb):
    r, g, b = (srgb_to_linear(c) for c in rgb)
    return 0.2126 * r + 0.7152 * g + 0.0722 * b


def composite(front, alpha, back):
    """线性光里按 alpha 合成，再转回相对亮度所需的 sRGB 分量。"""
    out = []
    for f, b in zip(front, back):
        lin = srgb_to_linear(f) * alpha + srgb_to_linear(b) * (1 - alpha)
        # 转回 sRGB，供统一的 relative_luminance 使用
        out.append(1.055 * (lin ** (1 / 2.4)) - 0.055 if lin > 0.0031308 else lin * 12.92)
    return tuple(max(0.0, min(1.0, c)) for c in out)


def contrast(fg, bg):
    l1, l2 = relative_luminance(fg), relative_luminance(bg)
    if l1 < l2:
        l1, l2 = l2, l1
    return (l1 + 0.05) / (l2 + 0.05)


def hex_rgb(text):
    text = text.lstrip("#")
    return tuple(int(text[i:i + 2], 16) / 255.0 for i in (0, 2, 4))


def main():
    errors = []
    for path in (CHROMA, SPAWNER, REVEAL, SERVICE, TUNING, CODEC, MODELS):
        if not path.exists():
            print("PetNestChromaGuard: FAIL - missing source " + path.as_posix())
            return 1

    chroma = CHROMA.read_text(encoding="utf-8-sig")

    entries = re.findall(
        r'Make\("([a-z]+)",\s*"([^"]+)",\s*"([^"]+)",\s*'
        r'([0-9.]+)f,\s*([0-9.]+)f,\s*([0-9.]+)f,\s*"(#[0-9A-Fa-f]{6})"\)',
        chroma)
    if len(entries) < 6:
        errors.append("调色板条目太少，解析到 %d 条" % len(entries))

    ids = [e[0] for e in entries]
    if len(set(ids)) != len(ids):
        errors.append("调色板 id 重复：" + ",".join(ids))

    for entry in entries:
        color_id, name_cn, _name_en, _r, _g, _b, text_hex = entry
        if len(name_cn) != 1:
            errors.append("中文色名必须是单字（拼「黑白」这种搭配名）：" + color_id)
        fg = hex_rgb(text_hex)
        worst = None
        for _label, panel, alpha in PANELS:
            bg = composite(panel, alpha, BEHIND)
            ratio = contrast(fg, bg)
            worst = ratio if worst is None else min(worst, ratio)
        if worst < 4.5:
            errors.append("文字色对比度不足 4.5:1（%.2f）：%s %s" % (worst, color_id, text_hex))

    shiny = re.search(r'ShinyTextHex\s*=\s*"(#[0-9A-Fa-f]{6})"', chroma)
    if shiny is None:
        errors.append("缺少异色文字色常量 ShinyTextHex")
    else:
        fg = hex_rgb(shiny.group(1))
        worst = min(contrast(fg, composite(panel, alpha, BEHIND)) for _l, panel, alpha in PANELS)
        if worst < 4.5:
            errors.append("异色文字色对比度不足 4.5:1（%.2f）" % worst)

    # 两色必须不同：RollPair 的 offset 偏移是唯一保证
    if "int offset = Clamp(roll1, count - 1);" not in chroma or "offset >= a ? offset + 1 : offset" not in chroma:
        errors.append("RollPair 必须保证两色不同（owner：任意两个颜色搭配）")

    # 存档面
    models = MODELS.read_text(encoding="utf-8-sig")
    if "public string chromaA;" not in models or "public string chromaB;" not in models:
        errors.append("PetNestPetRecord 缺少 chromaA / chromaB 字段")
    if "clone.chromaA = chromaA;" not in models or "clone.chromaB = chromaB;" not in models:
        errors.append("深拷贝必须带上炫彩字段（PetNestModelsGuard 同款纪律）")
    codec = CODEC.read_text(encoding="utf-8-sig")
    for token in ('.Str("chromaA"', '.Str("chromaB"', 'GetString("chromaA"', 'GetString("chromaB"'):
        if token not in codec:
            errors.append("编解码缺少炫彩字段：" + token)

    # 概率：异色必须比炫彩稀有得多（owner：异色极为稀有）
    tuning = TUNING.read_text(encoding="utf-8-sig")
    shiny_chance = re.search(r"ShinyChance\s*=\s*([0-9.]+)f", tuning)
    chroma_chance = re.search(r"ChromaChance\s*=\s*([0-9.]+)f", tuning)
    if shiny_chance is None or chroma_chance is None:
        errors.append("缺少 ShinyChance / ChromaChance")
    elif float(shiny_chance.group(1)) >= float(chroma_chance.group(1)):
        errors.append("异色概率必须明显低于炫彩概率")

    # 接线：名字、演出、崽身上的光环
    if "PetNestChroma.Decorate(pet, raw" not in SERVICE.read_text(encoding="utf-8-sig"):
        errors.append("GetDecoratedPetName 必须经 PetNestChroma.Decorate")
    reveal = REVEAL.read_text(encoding="utf-8-sig")
    if "PetNestChroma.DescribePair" not in reveal:
        errors.append("孵化演出必须显示炫彩搭配名")
    if "PlayJackpotMusic" not in reveal or "lottery" not in reveal:
        errors.append("异色必须复用许愿台的大奖音乐")
    spawner = SPAWNER.read_text(encoding="utf-8-sig")
    if "AttachChromaAura(" not in spawner or "DetachChromaAura(" not in spawner:
        errors.append("崽身上的光环必须成对创建 / 回收")
    if "if (!pet.shiny && !chroma) return;" not in spawner:
        errors.append("普通崽必须零对象（AGENTS 4.12 按实际使用状态门控）")

    if errors:
        print("PetNestChromaGuard: FAIL (%d errors)" % len(errors))
        for error in errors:
            print("  - " + error)
        return 1

    print("PetNestChromaGuard: PASS (%d palette entries)" % len(entries))
    return 0


if __name__ == "__main__":
    sys.exit(main())
