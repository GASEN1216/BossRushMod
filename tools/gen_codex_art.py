# -*- coding: utf-8 -*-
"""美术生成脚本：鸭皇图鉴立绘 + 词缀/事件/物品/成就/建筑图标。

设计要点：
  - 默认断点续跑；--filter 限定范围，--force 在暂存目录重出，整张成功才替换旧资产。
  - --list 只预览规格，不启动外部脚本、不联网、不创建生成目录。
  - 全部走色键出图（#ff00ff）+ remove_chroma_key 抠图 + 正方形归一，
    因为网关的 GPT Image 系列不接受 background=transparent，且返回尺寸不受控
    （2.5-flare 实测：请求 1024x1024 实回 1536x1024）。模型名见 tools/imagegen_model.py。
  - 所有主体都必须反复点名 anthropomorphic DUCK：negative prompt 挡不住「鸭变人」，
    这是 2026-08-29 实测结论，见 docs/制作教程/AI图片生成与Unity自动打包流程.md。

用法（需要网络出口，密钥见 docs/AI生图API和密钥.md）：
    export OPENAI_BASE_URL=... OPENAI_API_KEY=...
    python tools/gen_codex_art.py --list --filter Assets/achievement/
    python tools/gen_codex_art.py --filter Assets/achievement/ --force
"""
import argparse
import os
import shutil
import subprocess
import sys
import tempfile
import time

from PIL import Image
from imagegen_model import IMAGE_MODEL

RAW = "output/codex_raw"

STYLE = (" Painterly stylized game-art illustration, bold readable silhouette, "
         "clean shapes that stay legible when scaled down, dramatic rim lighting, "
         "rich saturated colors. Create the subject on a perfectly flat solid #ff00ff "
         "chroma-key background. Do not use #ff00ff anywhere in the subject. "
         "No text, no watermark, no logo, no border, no frame.")

DUCK = ("The subject is an ANTHROPOMORPHIC DUCK - a duck, not a human: rounded duck head, "
        "flat orange duck bill, feathered body, standing upright on two legs. ")


def portrait(desc):
    return ("Character codex portrait for a game bestiary. " + DUCK +
            "This duck is " + desc +
            " Waist-up three-quarter view, centered, menacing confident pose." + STYLE)


def icon(desc):
    return ("Single game UI icon, centered object on empty background. " + desc +
            " Simple bold shape, high contrast, no scene, no character." + STYLE)


BOSSES = {
    "Cname_Boss_Shot": "a burly shotgun-wielding duck enforcer in scrappy riveted steel plate, holding a giant double-barrel shotgun, smoke curling from the muzzle, ochre and gunmetal palette.",
    "Cname_Boss_3Shot": "a three-barrel burst-gunner duck in layered tactical webbing, wielding an oversized triple-barrel rifle, three glowing muzzle lights, olive and brass palette.",
    "Cname_Boss_Sniper": "a lean marksman duck in a ghillie cloak with a glinting scope monocle over one eye, cradling a very long anti-materiel rifle, muted moss and slate palette.",
    "Cname_Boss_Red": "an elite crimson-armored duck champion in polished red lacquer plate with gold trim, twin heavy pistols crossed, blazing red aura.",
    "Cname_Boss_Blue": "an elite azure-armored duck champion in frost-blue crystalline plate, wielding a humming energy blade, cold blue aura.",
    "Cname_Boss_Arcade": "a neon arcade-themed duck boss built of glowing pixel blocks and CRT screens, wearing a joystick crown, vivid magenta and cyan synthwave palette.",
    "Cname_Boss_Fly": "an airborne duck boss with a roaring rocket jetpack and swept metal wings, hovering above scorched exhaust plumes, orange and steel palette.",
    # Ghost 在 mod 旧版构建里被从 Boss 选取中显式排除（!(name == "Cname_Ghost")），
    # 说明它本来能通过 Boss 池筛选、会出现在图鉴目录里，因此必须有立绘。
    "Cname_Ghost": "a translucent phantom duck revenant, faintly glowing and semi-transparent, trailing wisps of pale mist where its legs should be, hollow glowing eyes, tattered spectral shroud, desaturated blue-white and ghostly teal palette.",
    "Cname_Grenade": "a grenadier duck festooned with bandoliers of round bombs, holding a stubby grenade launcher, a lit fuse sparking, army green palette.",
    "Cname_Hunter": "a feral tracker duck in stitched hide and bone trophies, drawing a heavy crossbow, an animal-skull pauldron, earthy brown palette.",
    "Cname_LabTestObjective": "a mutated laboratory test-subject duck with translucent bioluminescent green flesh, tubes and restraint straps, unstable glowing growths, sickly acid-green palette.",
    "Cname_PMCLeader": "a hardened private-military commander duck in a modern plate carrier and headset, one arm raised giving a hand signal, tan and black tactical palette.",
    "Cname_Prison_Boss": "a hulking prison warden duck in a torn orange jumpsuit with broken shackles on both wrists, swinging a heavy chain, rust and orange palette.",
    "Cname_Roadblock": "an immense fortified duck juggernaut hunkered behind a welded road-barrier shield covered in hazard stripes, yellow and black caution palette.",
    "Cname_SchoolBully": "a smug oversized schoolyard bully duck in a varsity jacket and backwards cap, cracking its knuckles, holding a dented baseball bat, red and cream palette.",
    "Cname_ShortEagle": "a short stocky duck brawler wearing an eagle-crest helmet and a feathered war cape, fists raised in a boxing guard, bronze and white palette.",
    "Cname_SnowMan": "a lumbering snowman-shaped duck boss built of packed snow with a carrot-shaped bill guard, coal-black eyes, twig arms holding an icicle club, white and pale blue palette.",
    "Cname_Snow_BigIce": "a colossal ice-brute duck encased in thick jagged glacier armor, one arm a massive ice hammer, glowing pale cyan cracks, deep blue palette.",
    "Cname_Snow_Fleeze": "a frost-caster duck in a hoarfrost mantle, conjuring a swirling blizzard between its webbed hands, frozen breath, icy white and cyan palette.",
    "Cname_Speedy": "a hyper-fast sprinter duck in a slick aerodynamic runner suit, leaning into a dash with motion streaks trailing behind, lime and white palette.",
    "Cname_Speedy_Ice": "a hyper-fast duck skating on blades of ice, leaving a frozen contrail, sleek frost-blue racing suit, cyan and silver palette.",
    "Cname_StormBoss1": "the first of five storm-lord duck champions, wielding forked lightning in one hand, a storm-cloud cloak, violent purple and white electric palette.",
    "Cname_StormBoss2": "the second of five storm-lord duck champions, armored in wind-swept dark metal, spinning a cyclone glaive, slate grey and teal gale palette.",
    "Cname_StormBoss3": "the third of five storm-lord duck champions, wreathed in torrential rain and carrying a heavy thunder-drum shield, deep indigo and rain-silver palette.",
    "Cname_StormBoss4": "the fourth of five storm-lord duck champions, crackling with ball lightning orbiting its head, holding a jagged tesla staff, acid yellow and black palette.",
    "Cname_StormBoss5": "the fifth and mightiest storm-lord duck champion, crowned with a thunderhead, twin storm sabers raised, blinding white-gold and stormcloud palette.",
    "Cname_UltraMan": "a tokusatsu-style giant hero duck in a chrome red-and-silver suit with a glowing chest timer light and a fin crest, striking a classic hero pose, bright red and silver palette.",
    "Cname_Vida": "a serene life-warden duck healer in flowing white-and-jade robes, cradling a softly glowing orb of life energy, vines curling around its arms, jade and ivory palette.",
    "Cname_XING": "a celestial star-themed duck boss in midnight-blue robes scattered with constellations, a glowing star sigil hovering over its brow, wielding a comet-tipped staff, indigo and gold palette.",
    "DragonDescendant": "the Dragon Descendant: a mid-tier duck warrior in crimson dragon-scale armor with a horned red helm, dragon-breath fire licking from its bill, crimson and bronze palette.",
    "boss_dragonking": "the Skyburner Dragon Lord: a fearsome duck warlord in crimson-and-gold dragon-scale plate, a horned dragon crown, holding a massive flaming halberd, ember particles, crimson and gold palette.",
    "boss_phantomwitch": "the Phantom Witch: a spectral duck sorceress, half translucent and glowing, in a tattered violet hood, wielding a curved soul scythe, ghostly wisps and a cursed rune circle, violet and pale green palette.",
    "zombie_boss_Titan": "an enormous undead zombie duck titan, bloated and hulking, rotting grey-green flesh, exposed ribs, massive swinging arms, sickly green and grey palette.",
    "zombie_boss_Hunter": "a lean feral zombie duck hunter crouched to pounce, elongated claws, milky white eyes, torn sinew, sickly green and dried-blood palette.",
    "zombie_boss_Splitter": "a zombie duck whose body is splitting apart into smaller writhing halves, a vertical seam down the torso, spilling ichor, bile yellow and green palette.",
    "zombie_boss_Shielder": "a zombie duck bulwark with a huge slab of fused bone and scrap welded to one arm as a shield, hunched behind it, bone white and rot green palette.",
    "zombie_boss_Corruptor": "a zombie duck corruptor spewing a cloud of purple spores from swollen sacs on its back, dripping corrosive ichor, toxic purple and green palette.",
}

AFFIX = {
    "lifesteal": "a crimson droplet of blood being drawn upward into a fanged crescent, life-drain motif.",
    "slaughter": "a cleaver crossed with a rising red heart, execution-and-recovery motif.",
    "bulwark": "a solid stone-grey tower shield with a glowing blue impact ring, guard motif.",
    "swifthand": "a golden ammunition magazine wrapped in a speed swoosh, fast-reload motif.",
    "thorns": "a spiked iron collar with red barbs radiating outward, reflected-damage motif.",
    "deathburst": "a skull at the center of an orange concussive explosion ring, death-explosion motif.",
    "frenzy": "three stacked orange chevrons inside a whirling ring of motion lines, rising-fury motif.",
    "hawkeye": "a sharp golden eye inside a crosshair reticle, critical-strike motif.",
    "overcharge": "a blue lightning bolt striking through a bullet, electric-infusion motif.",
    "bloodrage": "a clenched fist wrapped in dark red chains with a cracked heart behind it, power-at-a-cost motif.",
    "glasscannon": "a cracked glass cannon barrel with a bright muzzle flare, fragile-power motif.",
    "deathpact": "an hourglass whose falling sand is red blood droplets, a pact-with-death motif.",
}

EVENTS = {
    1: "a supply crate descending under an open parachute, airdrop motif.",
    2: "a blood-red full moon behind torn dark clouds, ominous blood-moon motif.",
    3: "a horned skull silhouette inside a red warning triangle, boss-intrusion alert motif.",
    4: "a hooded merchant lantern above a small pile of coins and wares, traveling-trader motif.",
    5: "a sound-wave ripple radiating from a question mark, decoy-noise motif.",
    6: "a bright festive firework bursting in gold and red sparks, celebration motif.",
    7: "gold coins and banknotes raining down, money-rain motif.",
    8: "a row of small cheerful cartoon ducks waddling in a line, duck-parade motif.",
}

ACHIEVEMENTS = {
    "petnest_first_hatch": "a cracked ancient duck egg revealing one tiny duckling silhouette, warm amber light, first-hatching motif.",
    "petnest_lineage_10": "a sturdy nest supporting a young branching golden family tree with duck-shaped leaves, growing-lineage motif.",
    "petnest_lineage_30": "an ancient majestic golden family tree rising from a relic nest, three large branching tiers of duck-shaped leaves, enduring-lineage motif.",
    "petnest_shiny": "one luminous iridescent duck feather beside a sparkling relic egg, rare-shiny-offspring motif.",
    "petnest_memorial": "a small weathered stone memorial with a carved duck feather and one glowing soul mote, remembrance motif.",
    "codex_first_entry": "an open crimson bestiary with one golden duck silhouette on a page, first-discovery motif.",
    "codex_10": "a crimson bestiary with a bronze duck-shaped seal and a small fan of collected pages, early-collection motif.",
    "codex_20": "two ornate crimson bestiary volumes beneath a silver duck-shaped seal, expanded-collection motif.",
    "codex_all": "a complete open crimson bestiary crowned with a golden duck crown and a radiant laurel wreath, complete-collection motif.",
    "codex_fast_kill": "a golden stopwatch crossed by one decisive silver blade, fast-boss-defeat motif.",
}

BUILDINGS = {
    "petnest_relic_nest": "an ancient circular twig nest cradling one cracked egg, a small duck feather and two relic stones",
    "bossrush_campaign_board": "a rustic wooden notice board with pinned campaign scrolls and one small duck-crown crest",
    # 2026-09-19 实测补充：这张原本是不透明彩色渲染图，与报箱/婚礼堂的纯白手绘不是一路。
    "bossrush_backmountain_showcase": "a glass-domed trophy display counter on a short plinth, one helmet and one rifle displayed inside, a small plaque on the front",
}


def building_icon(desc):
    return ("Single game construction-menu icon of " + desc +
            ". Pure white hand-drawn chalk linework and white silhouette accents only. "
            "Simple bold readable strokes, centered, no scene, no shading, no grey, no colored subject. "
            "Place the white drawing on a perfectly flat solid #ff00ff chroma-key background "
            "for removal to full transparency. No text, no watermark, no border, no frame.")


SPECS = []
for _key, _desc in BOSSES.items():
    SPECS.append(("Assets/ui/Codex/codex_portrait_%s.png" % _key.lower(), 512, portrait(_desc)))
SPECS.append(("Assets/Items/affix_forge_stone.png", 512,
              icon("A glowing molten forge stone: a rough dark rune-carved rock with cracks of "
                   "orange-hot lava light spilling out, a few embers rising.")))
SPECS.append(("Assets/Items/codex_book.png", 512,
              icon("An ornate closed tome bound in deep red leather with gold corner fittings and a "
                   "golden duck-crown emblem embossed on the cover, a bookmark ribbon hanging out.")))
for _aid, _desc in AFFIX.items():
    SPECS.append(("Assets/ui/AffixForge/affix_%s.png" % _aid, 256, icon(_desc)))
for _eid, _desc in EVENTS.items():
    SPECS.append(("Assets/ui/random_events/evt_%d.png" % _eid, 128, icon(_desc)))
for _aid, _desc in ACHIEVEMENTS.items():
    SPECS.append(("Assets/achievement/%s.png" % _aid, 256, icon(_desc)))
for _bid, _desc in BUILDINGS.items():
    SPECS.append(("Assets/buildings/%s.png" % _bid, 256, building_icon(_desc)))
WHITE_ICONS = {"Assets/buildings/%s.png" % key for key in BUILDINGS}


def normalize(src, dst, size, white=False):
    """裁到 alpha 包围盒，居中合成；保留半透明边缘，建筑线稿只留纯白 RGB。"""
    with Image.open(src) as source:
        im = source.convert("RGBA")
    bbox = im.getchannel("A").getbbox()
    if not bbox:
        raise ValueError("图像没有可见内容")
    im = im.crop(bbox)
    im.thumbnail((size, size), Image.LANCZOS)
    if white:
        silhouette = Image.new("RGBA", im.size, (255, 255, 255, 0))
        silhouette.putalpha(im.getchannel("A"))
        im = silhouette
    canvas = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    canvas.alpha_composite(im, ((size - im.size[0]) // 2, (size - im.size[1]) // 2))
    os.makedirs(os.path.dirname(os.path.abspath(dst)), exist_ok=True)
    canvas.save(dst, "PNG", optimize=True)


def tool_paths():
    """真正生成时才解析外部工具位置，预览和本地像素验证不接触工具。"""
    script_root = os.path.join(os.path.expanduser("~"), ".codex", "skills", ".system", "imagegen", "scripts")
    return os.path.join(script_root, "image_gen.py"), os.path.join(script_root, "remove_chroma_key.py")


def inspect_image(path, require_transparency=False):
    with Image.open(path) as source:
        alpha_min, alpha_max = source.convert("RGBA").getchannel("A").getextrema()
    if alpha_max == 0:
        raise ValueError("图像没有可见内容")
    if require_transparency and alpha_min == 255:
        raise ValueError("抠图结果没有透明背景")
    return alpha_min < 255


def generate_raw(prompt, staging_dir, imagegen):
    # 每次尝试独立输出，失败产生的半成品不能被后一次当作成功，也不能退回旧 raw。
    last_err = ""
    for attempt in range(1, 4):
        attempt_raw = os.path.join(staging_dir, "attempt_%d.png" % attempt)
        try:
            result = subprocess.run(
                [sys.executable, imagegen, "generate", "--model", IMAGE_MODEL,
                 "--size", "1024x1024", "--n", "1", "--no-augment",
                 "--out", attempt_raw, "--prompt", prompt],
                capture_output=True, text=True, timeout=300)
            if result.returncode == 0 and os.path.isfile(attempt_raw):
                inspect_image(attempt_raw)
                return attempt_raw
            last_err = (result.stderr or "外部生成器未输出有效图片")[-160:]
        except (OSError, ValueError, subprocess.TimeoutExpired) as error:
            last_err = str(error)[-160:]
        print("   [retry %d/3] %s" % (attempt, last_err.replace(chr(10), " ")[-90:]), flush=True)
        if attempt < 3:
            time.sleep(8 * attempt)
    raise RuntimeError("生成失败: " + last_err)


def publish_outputs(replacements, staging_dir):
    """全部处理成功后逐文件原子替换；提交中出错则恢复本轮已替换的旧文件。"""
    backups = {}
    for index, (_, target) in enumerate(replacements):
        os.makedirs(os.path.dirname(os.path.abspath(target)), exist_ok=True)
        if os.path.exists(target):
            backup = os.path.join(staging_dir, "previous_%d.png" % index)
            shutil.copy2(target, backup)
            backups[target] = backup
    published = []
    try:
        for staged, target in replacements:
            os.replace(staged, target)
            published.append(target)
    except OSError:
        for target in reversed(published):
            if target in backups:
                os.replace(backups[target], target)
            else:
                os.remove(target)
        raise


def generate_one(spec, force=False):
    dst, size, prompt = spec
    stem = os.path.splitext(os.path.basename(dst))[0]
    raw = os.path.join(RAW, stem + "_raw.png")
    cut = os.path.join(RAW, stem + "_cut.png")
    imagegen, chroma = tool_paths()
    os.makedirs(RAW, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix=stem + "-", dir=RAW) as staging_dir:
        stage_raw = os.path.join(staging_dir, "raw.png")
        stage_cut = os.path.join(staging_dir, "cut.png")
        stage_final = os.path.join(staging_dir, "final.png")
        if not force and os.path.isfile(raw):
            shutil.copy2(raw, stage_raw)
        else:
            fresh_raw = generate_raw(prompt, staging_dir, imagegen)
            os.replace(fresh_raw, stage_raw)
        if inspect_image(stage_raw):
            shutil.copy2(stage_raw, stage_cut)
        else:
            result = subprocess.run(
                [sys.executable, chroma, "--input", stage_raw, "--out", stage_cut,
                 "--auto-key", "border", "--soft-matte", "--transparent-threshold", "12",
                 "--opaque-threshold", "220", "--despill"],
                capture_output=True, text=True, timeout=180)
            if result.returncode != 0 or not os.path.isfile(stage_cut):
                raise RuntimeError("抠图失败，保留原有资产: " + (result.stderr or "未输出抠图文件")[-160:])
        inspect_image(stage_cut, require_transparency=True)
        normalize(stage_cut, stage_final, size, white=dst in WHITE_ICONS)
        publish_outputs([(stage_raw, raw), (stage_cut, cut), (stage_final, dst)], staging_dir)


def select_specs(filters):
    lowered = [value.replace("\\", "/").lower() for value in filters]
    return [spec for spec in SPECS if not lowered or any(value in spec[0].lower() for value in lowered)]


def main(argv=None):
    parser = argparse.ArgumentParser(description="生成游戏美术；--list 可离线预览，不生成文件")
    parser.add_argument("--filter", action="append", default=[], metavar="PATH_OR_ID",
                        help="仅处理路径或 ID 含此子串的条目；可多次指定，取并集")
    parser.add_argument("--force", action="store_true", help="重新生成选中条目的 raw 和最终资产，失败保留旧文件")
    parser.add_argument("--list", action="store_true", help="只列出选中条目，不启动外部工具、不创建目录")
    args = parser.parse_args(argv)
    selected = select_specs(args.filter)
    todo = [spec for spec in selected if args.force or not os.path.exists(spec[0])]
    print("选中 %d 项，待生成 %d 项" % (len(selected), len(todo)), flush=True)
    if args.list:
        for dst, size, _ in selected:
            state = "重出" if args.force and os.path.exists(dst) else "已存在" if os.path.exists(dst) else "缺失"
            print("%s (%dpx, %s)" % (dst, size, state), flush=True)
        return 0
    if not selected:
        print("没有匹配条目；请先使用 --list 核对筛选条件。", flush=True)
        return 2
    ok = 0
    fail = 0
    for index, spec in enumerate(todo, 1):
        print("[%d/%d] %s" % (index, len(todo), spec[0]), flush=True)
        try:
            generate_one(spec, force=args.force)
            ok += 1
            print("   [OK] -> %s (%dpx)" % (spec[0], spec[1]), flush=True)
        except Exception as error:  # noqa: BLE001 - 单张失败不该中断整批
            fail += 1
            print("   [ERR] %s" % error, flush=True)
        # 网关请求之间保留节流；离线预览和空批次不等待。
        if index < len(todo):
            time.sleep(int(os.environ.get("ART_GEN_DELAY", "20")))
    print("完成：成功 %d，失败 %d" % (ok, fail), flush=True)
    return 0 if fail == 0 else 1


if __name__ == "__main__":
    sys.exit(main())
