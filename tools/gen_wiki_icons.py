# -*- coding: utf-8 -*-
"""gen_wiki_icons.py - 生成在线 Wiki 导航用的小图标，并直接产出可提交的 WebP。

为什么和 tools/gen_codex_art.py 分开：
    那个脚本产出的是**游戏内**要用的美术（图鉴立绘、词缀图标），源图落在
    Assets/ 下由 Unity 打包。本脚本产出的图标**只服务在线站**——侧栏类目、
    门户宫格、速查框、页尾导航上的那些小图，游戏里根本不显示它们。

    共用的是同一套出图管线（image_gen.py 色键出图 + remove_chroma_key 抠图 +
    正方形归一），风格因此和图鉴立绘同源，摆在一个页面上不会打架。

产物三件套（全部要提交）：
    Assets/wiki_icons/<key>.png             源图，local-only（Assets/ 被 .gitignore 挡着）
    wiki-site/docs/public/images/ui/<key>.webp   站点实际加载的产物
    wiki-site/scripts/wiki-icons.json        key -> 中英标题的边车清单

    边车清单存在的理由：build_wiki_images.py 把 Assets/ 的源图转成 WebP 并重建
    image-manifest.json，而本脚本的源图在别人机器上不存在。若不留这份清单，
    别人重跑 build_wiki_images.py 就会把 ui 组整个抹掉，
    tests/WikiImageAssetGuard.py 立刻红（产物在、清单没有 = 孤儿产物）。
    有了它，build_wiki_images.py 直接读清单登记 ui 组，任何机器都能重建。

用法（需要网络出口，密钥见 docs/AI生图API和密钥.md）：
    set OPENAI_BASE_URL=... && set OPENAI_API_KEY=...
    python tools/gen_wiki_icons.py               # 断点续跑：已有 PNG 的跳过
    python tools/gen_wiki_icons.py --webp-only   # 不生图，只把现有 PNG 转 WebP + 写清单

网关限流约 1 次/分钟，间隔低于 20 秒会大面积撞 APIConnectionError
（2026-08-30 实测 58 张里失败 25 张）。间隔用 ART_GEN_DELAY 控制，默认 20 秒。
"""
import argparse
import json
import os
import subprocess
import sys
import time

try:
    from PIL import Image
except ImportError:
    print("gen_wiki_icons: FAIL - 需要 Pillow：python -m pip install Pillow")
    sys.exit(2)

HOME = os.path.expanduser("~")
IMAGEGEN = os.path.join(HOME, ".codex", "skills", ".system", "imagegen", "scripts", "image_gen.py")
CHROMA = os.path.join(HOME, ".codex", "skills", ".system", "imagegen", "scripts", "remove_chroma_key.py")

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC_DIR = os.path.join(REPO, "Assets", "wiki_icons")
RAW_DIR = os.path.join(REPO, "output", "wiki_icons_raw")
WEBP_DIR = os.path.join(REPO, "wiki-site", "docs", "public", "images", "ui")
SIDECAR = os.path.join(REPO, "wiki-site", "scripts", "wiki-icons.json")

PNG_SIZE = 256       # 源图边长；站上最大用到 52px，256 给足 4x
WEBP_MAX = 128       # 与 build_wiki_images.py 的 ICON_MAX 对齐
WEBP_QUALITY = 82

# 与 gen_codex_art.py 逐字同源：换风格串会让新图标和已有图鉴立绘对不上。
STYLE = (" Painterly stylized game-art illustration, bold readable silhouette, "
         "clean shapes that stay legible when scaled down, dramatic rim lighting, "
         "rich saturated colors. Create the subject on a perfectly flat solid #ff00ff "
         "chroma-key background. Do not use #ff00ff anywhere in the subject. "
         "No text, no watermark, no logo, no border, no frame.")

DUCK = ("The subject is an ANTHROPOMORPHIC DUCK - a duck, not a human: rounded duck head, "
        "flat orange duck bill, feathered body, standing upright on two legs. ")


def icon(desc):
    return ("Single game UI icon, centered object on empty background. " + desc +
            " Simple bold shape, high contrast, no scene, no character." + STYLE)


def duck_icon(desc):
    """带角色的图标（NPC / 模式）——必须反复点名鸭，否则网关会画成人。"""
    return ("Single game UI icon, one centered character bust on empty background. " + DUCK +
            "This duck is " + desc +
            " Head-and-shoulders, front view, filling the frame." + STYLE)


# ── 图标清单 ────────────────────────────────────────────────────────
# key 必须与 wiki-site/docs/.vitepress/data/structure.mts 里写的 icon 一致，
# 由 tests/WikiSiteStructureGuard.py 双向校验。

ICONS = [
    # ── 类目 ──
    ("cat-start", "入门", "Getting Started",
     icon("A brass ship ticket stub with a punched hole and a small compass rose printed on it, "
          "aged paper texture, warm brass and cream palette.")),
    ("cat-modes", "游戏模式", "Game Modes",
     icon("A round arena colosseum seen from a low angle with two crossed banners planted on its "
          "rim, sand and stone tones with a brass rim light.")),
    ("cat-bosses", "Boss", "Bosses",
     icon("A horned dragon skull trophy mounted on a small dark plinth, empty eye sockets glowing "
          "faint ember orange, bone white and charcoal palette.")),
    ("cat-equipment", "装备", "Equipment",
     icon("A sword and a round shield crossed behind a horned helmet, heraldic arrangement, "
          "polished steel with brass trim.")),
    ("cat-items", "物品", "Items",
     icon("An open wooden supply crate with brass corner fittings, a few small parcels and a "
          "glass vial spilling out over the rim, warm wood and brass palette.")),
    ("cat-npcs", "NPC", "NPCs",
     icon("Two overlapping speech bubbles with a small brass bell hanging between them, "
          "friendly conversation motif, cream and brass palette.")),
    ("cat-maps", "地图", "Maps",
     icon("A partially unrolled parchment map with a dotted route and a red location pin planted "
          "in it, a brass compass rose in the corner, aged paper palette.")),
    ("cat-systems", "系统", "Systems",
     icon("Three interlocking brass gears of different sizes with a faint blueprint grid behind "
          "them, mechanical systems motif, brass and slate palette.")),
    ("cat-achievements", "成就", "Achievements",
     icon("A gold medal with a five-pointed star struck into it, hanging from a short ribbon, "
          "gold and deep red palette.")),
    ("cat-guides", "攻略", "Guides",
     icon("An open field manual with a hand-drawn tactical diagram of arrows and circles on the "
          "spread pages, a pencil resting in the gutter, cream paper and graphite palette.")),
    ("cat-easter", "彩蛋", "Easter Eggs",
     icon("A cracked speckled egg with a warm golden glow escaping through the crack, "
          "playful secret motif, cream shell and gold light.")),
    ("cat-changelog", "更新日志", "Changelog",
     icon("A rolled parchment scroll with a wax seal and a small brass pocket watch resting "
          "against it, version-history motif, parchment and brass palette.")),

    # ── 入门 ──
    ("start-install", "安装与启用", "Installation",
     icon("A download arrow pointing down into an open cardboard box with a checkmark stamped on "
          "the flap, installation motif, slate blue and cream palette.")),
    ("start-firstrun", "新手上路", "First Steps",
     icon("A pair of webbed duck footprints walking forward across sand toward a small flag, "
          "first-steps motif, warm sand and brass palette.")),

    # ── 游戏模式 ──
    ("mode-standard", "标准 BossRush", "Standard BossRush",
     icon("A torn arena admission ticket beside a small round shield, classic-run motif, "
          "cream paper and steel palette.")),
    ("mode-hell", "无间炼狱", "Infinite Hell",
     icon("An infinity symbol forged from molten metal, wreathed in orange flame, "
          "endless-inferno motif, blazing orange and black palette.")),
    ("mode-scratch", "白手起家", "From Scratch",
     icon("An empty upturned backpack with a single rusty pipe wrench lying beside it, "
          "starting-with-nothing motif, faded canvas and rust palette.")),
    ("mode-faction", "划地为营", "Faction War",
     icon("Three small territory banners in red, blue and green planted close together in "
          "cracked ground, faction-claim motif, bold primary colors.")),
    ("mode-bloodhunt", "血猎追击", "Blood Hunt",
     icon("A crosshair reticle overlaid on a dripping crimson blood droplet, "
          "hunted-and-bleeding motif, deep crimson and gunmetal palette.")),
    ("mode-fate", "宿命回响", "Fate Echo",
     icon("A cracked circular mirror reflecting a faint duplicate silhouette, concentric echo "
          "rings radiating outward, destiny-echo motif, violet and silver palette.")),
    ("mode-zombie", "末日丧尸模式", "Zombie Mode",
     icon("A biohazard trefoil symbol half-buried in sickly green ooze with a skeletal hand "
          "reaching out of it, outbreak motif, toxic green and grey palette.")),
    ("mode-cup", "百战留痕", "Black Market Duck Cup",
     icon("A betting slip and a stack of chips in front of a small tournament trophy cup, "
          "underground-bookmaking motif, deep green felt and gold palette.")),

    # ── 装备 ──
    ("eq-phantom-scythe", "噬魂挽歌", "Soulreaper Requiem",
     icon("A curved scythe with a ghostly translucent violet blade and a bone-wrapped haft, "
          "wisps of soul mist trailing off the edge, violet and pale green palette.")),
    ("eq-dragon-set", "龙裔套装", "Dragon Set",
     icon("A horned crimson dragon-scale helmet beside a matching scaled breastplate, "
          "crimson and bronze palette.")),
    ("eq-dragon-king-set", "龙王套装", "Dragon King Set",
     icon("A golden horned dragon crown above an ornate gold-trimmed scale cuirass, "
          "molten seams glowing between the scales, crimson and gold palette.")),
    ("eq-flight-totem", "腾云驾雾图腾", "Cloud Rider Totem",
     icon("A carved jade totem disc with feathered wings spread on either side, small clouds "
          "curling beneath it, jade green and white palette.")),
    ("eq-reverse-scale", "逆鳞", "Reverse Scale",
     icon("A single large dragon scale set backwards against the grain, cracked down the middle "
          "with red light bleeding from the fracture, crimson and obsidian palette.")),
    ("eq-halberd", "焚皇断界戟", "Skyburner Halberd",
     icon("A heavy polearm halberd with a broad flaming blade and a gold dragon-head collar, "
          "ember sparks rising, crimson gold and blackened steel palette.")),
    ("eq-dragon-breath", "龙息", "Dragon Breath",
     icon("A stubby flamethrower rifle with a dragon-jaw muzzle breathing a curl of fire, "
          "orange flame and dark iron palette.")),
    ("eq-dragon-cannon", "焚天龙铳", "Dragon Cannon",
     icon("An ornate heavy hand-cannon with gold dragon filigree along the barrel and a glowing "
          "ember chamber, crimson gold and gunmetal palette.")),
    ("eq-frostmourne", "霜之哀伤", "Frostmourne",
     icon("A large two-handed greatsword of pale blue ice-veined steel, frost creeping along the "
          "fuller, cold vapour spilling off the blade, icy cyan and steel palette.")),
    ("eq-viper-dagger", "毒蛇匕首", "Viper Dagger",
     icon("A curved dagger whose pommel is a snake head, green venom beading along the edge, "
          "toxic green and dark bronze palette.")),
    ("eq-summon-staff", "召唤法杖", "Summoning Staff",
     icon("A gnarled wooden staff topped with a floating pale-blue soul orb, two small spirit "
          "wisps orbiting it, spectral blue and dark wood palette.")),
    ("eq-energy-shield", "能量盾", "Energy Shield",
     icon("A hexagonal translucent energy barrier panel glowing cyan at its edges, mounted on a "
          "small brass emitter, cyan and brass palette.")),
    ("eq-frost-spear", "冰霜长矛", "Frost Spear",
     icon("A long slender spear with a faceted crystal-ice tip, frost rings forming around the "
          "point, pale cyan and silver palette.")),
    ("eq-thunder-ring", "雷电戒指", "Thunder Ring",
     icon("A heavy metal signet ring with a crackling yellow lightning bolt arcing across its "
          "setting, electric yellow and dark steel palette.")),
    ("eq-frost-set", "霜冠套装", "Frost Set",
     icon("A crown of jagged clear ice above a frost-rimed chestplate, pale cyan and white "
          "palette.")),
    ("eq-thunder-set", "雷神套装", "Thunder Set",
     icon("A horned helm with two lightning-bolt horns above a storm-plated cuirass crackling "
          "with arcs, electric yellow and dark blue palette.")),

    # ── 物品 ──
    ("item-key", "入场与功能物品", "Entry & Utility Items",
     icon("A brass key crossed over a folded boarding ticket, gateway-item motif, "
          "brass and cream palette.")),
    ("item-npc", "NPC 相关物品", "NPC Items",
     icon("A small wrapped gift box tied with ribbon beside a sparkling cut diamond, "
          "gifting motif, rose red and icy white palette.")),
    ("item-consumable", "消耗品", "Consumables",
     icon("A small glass vial of glowing amber liquid with a cork stopper and a dropper resting "
          "beside it, consumable motif, amber and glass palette.")),
    ("item-mode", "模式专属物品", "Mode-Exclusive Items",
     icon("A coil of barbed wire on top of a stacked sandbag barricade, field-fortification "
          "motif, olive canvas and rusted steel palette.")),

    # ── NPC ──
    ("npc-goblin", "叮当", "Dingdang",
     duck_icon("a clever goblin-eared tinker in a leather apron and welding goggles pushed up on "
               "its forehead, a smudge of soot on one cheek, holding a small smith hammer, "
               "green skin tone with warm leather browns.")),
    ("npc-nurse", "羽织", "Yuori",
     duck_icon("a calm field nurse in a white medical coat over a soft pink blouse, a red cross "
               "armband and a stethoscope around its neck, white and rose palette.")),
    ("npc-courier", "阿稳", "Awen",
     duck_icon("a cheerful courier in a brown delivery uniform and flat cap, a bulging parcel "
               "satchel strap across its chest, holding up a small package, brown and tan "
               "palette.")),

    # ── 系统 ──
    ("sys-loot", "掉落与奖励", "Loot & Rewards",
     icon("An open treasure chest overflowing with gold coins and a glowing gem, "
          "reward motif, dark wood gold and warm light.")),
    ("sys-wraith", "死亡亡魂", "Death Wraith",
     icon("A hooded spectral figure of pale smoke with two hollow glowing eyes, dissolving into "
          "wisps at the bottom, ghostly blue-white and charcoal palette.")),
    ("sys-fountain", "星愿许愿台", "StarWish Fountain",
     icon("A small dusty stone wishing fountain basin with a single golden star hovering above "
          "the water and a few coins at the bottom, weathered stone and gold palette.")),
    ("sys-reforge", "重铸系统", "Reforge System",
     icon("A blacksmith hammer striking a glowing orange sword blank on an anvil, sparks flying, "
          "reforge motif, hot orange and dark iron palette.")),
    ("sys-filter", "Boss 筛选器", "Boss Filter",
     icon("A funnel filter with small skull tokens falling into it and one skull deflected away "
          "to the side, filtering motif, slate and bone palette.")),
    ("sys-mutator", "变异词条系统", "Mutator System",
     icon("A pair of dice mid-tumble surrounded by three floating rune tiles, "
          "random-modifier motif, ivory dice and violet rune glow.")),
    ("sys-events", "局内随机事件", "Random Events",
     icon("A question-mark sigil inside a warning diamond with a small parachute crate drifting "
          "behind it, surprise-event motif, amber and slate palette.")),
    ("sys-marriage", "好感度与婚姻", "Affinity & Marriage",
     icon("Two interlocking gold wedding rings with a small red heart resting where they cross, "
          "gold and rose red palette.")),
    ("sys-config", "配置选项", "Configuration",
     icon("Three vertical slider controls at different heights beside a small toggle switch, "
          "settings-panel motif, slate blue and brass palette.")),

    # ── 替掉四张不适合当导航图标的游戏内美术 ──
    # 它们本身没问题，是**尺寸不匹配**：那些图是给游戏里几百像素的建筑/物品位准备的，
    # 缩到导航栏的 40px 就废了——
    #   petnest        最大 alpha 只有 180，是张半透明线稿，压在纸底上几乎看不见；
    #   daily-mailbox  白色线稿，同样在浅色底上没对比度；
    #   campaign-board / showcase  自带不透明方底（bbox 铺满 128×128），
    #                  在一排抠figure图标里就是两个突兀的色块，深色模式下更刺眼。
    # 图鉴书（codex-book）和词缀熔石（forge-stone）缩下来依然立得住，继续复用，不重画。
    ("sys-petnest", "遗种巢", "PetNest",
     icon("A woven nest of dark twigs cradling a single speckled egg with a faint golden glow "
          "seeping through its shell, deep brown nest and warm gold light, strong dark outline.")),
    ("sys-daily", "鸭科夫日报", "The Duckov Daily",
     icon("A tightly rolled newspaper tied with twine, one end showing dense grey column text, "
          "resting at a slight angle, aged newsprint cream against a dark ink outline.")),
    ("sys-campaign", "鸭王征程", "Duck King Campaign",
     icon("A weathered contract scroll stamped with a red wax seal and a small gold crown above "
          "it, quest-contract motif, parchment cream gold and deep red.")),
    ("sys-backyard", "竞技场后山", "Arena Backyard",
     icon("A small vegetable plot with three green sprouts in tilled soil, a wooden trophy shelf "
          "standing behind it, backyard-homestead motif, earthy brown and leaf green.")),
]


def normalize(src, dst, size):
    """裁到内容包围盒 -> 等比缩放 -> 居中贴进透明正方形画布。与 gen_codex_art.py 同法。"""
    im = Image.open(src).convert("RGBA")
    bbox = im.getbbox()
    if bbox:
        im = im.crop(bbox)
    im.thumbnail((size, size), Image.LANCZOS)
    canvas = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    canvas.paste(im, ((size - im.size[0]) // 2, (size - im.size[1]) // 2), im)
    os.makedirs(os.path.dirname(dst), exist_ok=True)
    canvas.save(dst, "PNG", optimize=True)


def generate_one(key, prompt):
    """出一张图并归一成 Assets/wiki_icons/<key>.png。已存在则跳过。返回是否成功。"""
    dst = os.path.join(SRC_DIR, key + ".png")
    if os.path.exists(dst):
        return True

    raw = os.path.join(RAW_DIR, key + "_raw.png")
    cut = os.path.join(RAW_DIR, key + "_cut.png")
    os.makedirs(RAW_DIR, exist_ok=True)

    if not os.path.exists(raw):
        # 网关会间歇性抛 APIConnectionError，单次失败不代表这张图出不来，
        # 因此带指数退避重试；仍失败就跳过，靠断点续跑补齐。
        last_err = ""
        for attempt in range(1, 4):
            r = subprocess.run(
                [sys.executable, IMAGEGEN, "generate", "--model", "gpt-image-2",
                 "--size", "1024x1024", "--n", "1", "--no-augment",
                 "--out", raw, "--prompt", prompt],
                capture_output=True, text=True, timeout=300)
            if r.returncode == 0 and os.path.exists(raw):
                break
            last_err = (r.stderr or "")[-160:]
            print("   [retry %d/3] %s" % (attempt, last_err.replace(chr(10), " ")[-90:]), flush=True)
            time.sleep(8 * attempt)
        if not os.path.exists(raw):
            print("   [FAIL] " + last_err, flush=True)
            return False

    im = Image.open(raw)
    if im.mode == "RGBA" and im.split()[-1].getextrema()[0] < 255:
        src = raw  # 网关直接回了透明图，跳过抠图
    else:
        subprocess.run(
            [sys.executable, CHROMA, "--input", raw, "--out", cut,
             "--auto-key", "border", "--soft-matte",
             "--transparent-threshold", "12", "--opaque-threshold", "220", "--despill"],
            capture_output=True, text=True, timeout=180)
        src = cut if os.path.exists(cut) else raw

    normalize(src, dst, PNG_SIZE)
    return True


def write_products():
    """把 Assets/wiki_icons/*.png 转成站点 WebP，并写出边车清单。

    只登记**真的转出了 WebP** 的条目：源图缺失时清单里也不该有它，
    否则 WikiImageAssetGuard 会因为「清单里有、产物没有」而红。
    """
    os.makedirs(WEBP_DIR, exist_ok=True)
    entries = []
    missing = []
    for key, zh, en, _prompt in ICONS:
        src = os.path.join(SRC_DIR, key + ".png")
        dst = os.path.join(WEBP_DIR, key + ".webp")
        if not os.path.isfile(src):
            # 源图没了但产物还在（别人的机器 / 只 clone 了仓库）——保留产物并照常登记
            if os.path.isfile(dst):
                entries.append({"key": key, "src": "/images/ui/" + key + ".webp", "zh": zh, "en": en})
            else:
                missing.append(key)
            continue
        with Image.open(src) as im:
            im = im.convert("RGBA")
            w, h = im.size
            scale = min(1.0, float(WEBP_MAX) / max(w, h))
            if scale < 1.0:
                im = im.resize((max(1, int(w * scale)), max(1, int(h * scale))), Image.LANCZOS)
            im.save(dst, "WEBP", quality=WEBP_QUALITY, method=6)
        entries.append({"key": key, "src": "/images/ui/" + key + ".webp", "zh": zh, "en": en})

    with open(SIDECAR, "w", encoding="utf-8", newline="\n") as fh:
        json.dump(entries, fh, ensure_ascii=False, indent=2)
        fh.write("\n")

    total = sum(os.path.getsize(os.path.join(WEBP_DIR, e["key"] + ".webp")) for e in entries)
    print("gen_wiki_icons: 写出 %d 个 WebP（%.0f KB），清单 -> %s"
          % (len(entries), total / 1024.0, os.path.relpath(SIDECAR, REPO)), flush=True)
    if missing:
        print("  尚缺 %d 个（源图与产物都没有）: %s" % (len(missing), ", ".join(missing[:10])), flush=True)
    return len(missing)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--webp-only", action="store_true",
                        help="不调生图接口，只把现有 PNG 转 WebP 并重写清单")
    parser.add_argument("--only", default="",
                        help="逗号分隔的 key，只生成这几个（用于试跑）")
    args = parser.parse_args()

    if not args.webp_only:
        wanted = [k.strip() for k in args.only.split(",") if k.strip()]
        specs = [s for s in ICONS if not wanted or s[0] in wanted]
        todo = [s for s in specs if not os.path.exists(os.path.join(SRC_DIR, s[0] + ".png"))]
        print("总计 %d 项，待生成 %d 项" % (len(specs), len(todo)), flush=True)
        ok = fail = 0
        delay = int(os.environ.get("ART_GEN_DELAY", "20"))
        for i, (key, _zh, _en, prompt) in enumerate(todo, 1):
            print("[%d/%d] %s" % (i, len(todo), key), flush=True)
            try:
                if generate_one(key, prompt):
                    ok += 1
                    print("   [OK] -> Assets/wiki_icons/%s.png" % key, flush=True)
                else:
                    fail += 1
            except Exception as e:  # noqa: BLE001 - 单张失败不该中断整批
                fail += 1
                print("   [ERR] %s" % e, flush=True)
            if i < len(todo):
                time.sleep(delay)
        print("生成完成：成功 %d，失败 %d" % (ok, fail), flush=True)

    missing = write_products()
    return 0 if missing == 0 else 1


if __name__ == "__main__":
    sys.exit(main())
