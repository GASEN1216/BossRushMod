# -*- coding: utf-8 -*-
"""一次性美术生成脚本：鸭皇图鉴立绘 + 词缀/事件/物品图标。

设计要点：
  - 可断点续跑：目标文件已存在就跳过，网关抽风或中途中断都能重跑补齐。
  - 全部走色键出图（#ff00ff）+ remove_chroma_key 抠图 + 正方形归一，
    因为网关的 gpt-image-2 不接受 background=transparent，且返回尺寸不受控。
  - 所有主体都必须反复点名 anthropomorphic DUCK：negative prompt 挡不住「鸭变人」，
    这是 2026-08-29 实测结论，见 docs/制作教程/AI图片生成与Unity自动打包流程.md。

用法（需要网络出口，密钥见 docs/AI生图API和密钥.md）：
    export OPENAI_BASE_URL=... OPENAI_API_KEY=...
    python tools/gen_codex_art.py
"""
import os
import subprocess
import sys
import time

from PIL import Image

HOME = os.path.expanduser("~")
IMAGEGEN = os.path.join(HOME, ".codex", "skills", ".system", "imagegen", "scripts", "image_gen.py")
CHROMA = os.path.join(HOME, ".codex", "skills", ".system", "imagegen", "scripts", "remove_chroma_key.py")
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


def normalize(src, dst, size):
    """裁到内容包围盒 -> 等比缩放 -> 居中贴进透明正方形画布。"""
    im = Image.open(src).convert("RGBA")
    bbox = im.getbbox()
    if bbox:
        im = im.crop(bbox)
    im.thumbnail((size, size), Image.LANCZOS)
    canvas = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    canvas.paste(im, ((size - im.size[0]) // 2, (size - im.size[1]) // 2), im)
    os.makedirs(os.path.dirname(dst), exist_ok=True)
    canvas.save(dst, "PNG", optimize=True)


def main():
    os.makedirs(RAW, exist_ok=True)
    todo = [s for s in SPECS if not os.path.exists(s[0])]
    print("总计 %d 项，待生成 %d 项" % (len(SPECS), len(todo)), flush=True)
    ok = 0
    fail = 0
    for i, (dst, size, prompt) in enumerate(todo, 1):
        stem = os.path.splitext(os.path.basename(dst))[0]
        raw = os.path.join(RAW, stem + "_raw.png")
        cut = os.path.join(RAW, stem + "_cut.png")
        print("[%d/%d] %s" % (i, len(todo), dst), flush=True)
        try:
            if not os.path.exists(raw):
                # 网关会间歇性抛 APIConnectionError，单次失败不代表这张图不能出，
                # 因此带指数退避重试；仍然失败就跳过，靠断点续跑在收尾轮补齐。
                last_err = ""
                for attempt in range(1, 4):
                    r = subprocess.run([sys.executable, IMAGEGEN, "generate", "--model", "gpt-image-2",
                                        "--size", "1024x1024", "--n", "1", "--no-augment",
                                        "--out", raw, "--prompt", prompt],
                                       capture_output=True, text=True, timeout=300)
                    if r.returncode == 0 and os.path.exists(raw):
                        break
                    last_err = (r.stderr or "")[-160:]
                    print("   [retry %d/3] %s" % (attempt, last_err.replace(chr(10), " ")[-90:]),
                          flush=True)
                    time.sleep(8 * attempt)
                if not os.path.exists(raw):
                    print("   [FAIL] 生成失败: " + last_err, flush=True)
                    fail += 1
                    continue
            im = Image.open(raw)
            if im.mode == "RGBA" and im.split()[-1].getextrema()[0] < 255:
                src = raw  # 网关直接回了透明图，跳过抠图
            else:
                subprocess.run([sys.executable, CHROMA, "--input", raw, "--out", cut,
                                "--auto-key", "border", "--soft-matte",
                                "--transparent-threshold", "12", "--opaque-threshold", "220",
                                "--despill"], capture_output=True, text=True, timeout=180)
                src = cut if os.path.exists(cut) else raw
            normalize(src, dst, size)
            ok += 1
            print("   [OK] -> %s (%dpx)" % (dst, size), flush=True)
        except Exception as e:  # noqa: BLE001 - 单张失败不该中断整批
            fail += 1
            print("   [ERR] %s" % e, flush=True)
        # 网关限流约 1 次/分钟（docs/制作教程 与实测一致）。间隔太短会大面积撞
        # APIConnectionError：首轮用 3 秒跑出过约 50% 失败率，补齐轮拉到 20 秒。
        time.sleep(int(os.environ.get('ART_GEN_DELAY', '20')))
    print("完成：成功 %d，失败 %d" % (ok, fail), flush=True)
    return 0 if fail == 0 else 1


if __name__ == "__main__":
    sys.exit(main())
