# -*- coding: utf-8 -*-
"""基地四个自建建筑的 Tripo 输入概念图：报箱 / 公告栏 / 遗种巢 / 展示柜。

owner 2026-09-20：「报箱，公告栏，遗种巢，展示柜请你生成合适的图片然后我拿去生成模型真正接入游戏里」。

这四个建筑目前都是用 Unity 原始几何体（Cube / Cylinder / Sphere）现搭的占位造型
（`Integration/DailyReport/DailyReportMailboxBuilder.cs`、`Campaign/CampaignBoardBuilder.cs`、
`PetNest/PetNestBuilder.cs`、`Integration/BackMountain/ShowcaseBuildingBuilder.cs`），
所以这里出的是**给 Tripo image-to-3D 用的输入概念图**，不是 UI 图标。

规格与 tools/gen_sky_island_boss_gear_art.py 的 concepts 步骤一致，因为那批已经跑通过整条
「概念图 -> Tripo -> GLB -> 导入 -> 打包」流程：
  - 单体居中、四周留足空白；
  - 浅灰无缝纯色底；
  - 全向柔光、**无投影无强高光**（强方向光会被烘进 albedo，拆不掉）；
  - 不带角色、不带展台、不带任何文字。

出图落在 output/building_concepts/，由 owner 上传 Tripo 生成 GLB。
断点续跑：成品已存在就跳过，--force 重出。

用法（需要网络出口；密钥只从环境变量读，来源见 docs/AI生图API和密钥.md，
不要写进脚本、命令行或日志）：
    python -X utf8 tools/gen_building_concepts.py --list
    python -X utf8 tools/gen_building_concepts.py
    python -X utf8 tools/gen_building_concepts.py --only petnest --force
"""
import argparse
import os
import shutil
import sys
import tempfile

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gen_codex_art as base  # noqa: E402

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "output", "building_concepts")

STYLE = (
    " Single stylized 3D game asset render, exactly one object, standing on nothing, centered in frame with "
    "generous empty margin on every side, front three-quarter view. Plain seamless flat light grey background, "
    "soft even diffuse studio lighting from all around, no cast shadow, no contact shadow, no strong specular "
    "highlights, no glow, no bloom, no depth of field. Chunky simplified low-poly shapes with rounded edges, "
    "bold readable silhouette, hand-painted matte texture look, slightly worn and used. "
    "Cozy post-apocalyptic scavenger workshop palette: warm amber, weathered olive green, cream, rust orange and "
    "muted teal. No purple, no magenta, no neon. No character, no duck, no person, no hands, no display stand, "
    "no ground plane, no text, no letters, no numbers, no logo, no watermark, no border, no frame, no UI.")

SCALE = ("Human-scale base furniture for a small cartoon duck: about waist-high to a short round duck, "
         "chunky enough to read clearly from a top-down game camera. ")

PIECES = [
    ("mailbox", SCALE +
     "A scavenged newspaper mailbox on a single sturdy post: a rounded olive-green metal box with a hinged lid and "
     "a wide horizontal letter slot on the front, a small riveted metal flag arm on the side raised upright, a rolled "
     "newspaper sticking out of the slot, a dented tin roof cap on top, chipped paint and a few rust streaks near "
     "the base of the post, a coil of wire wrapped around the post."),
    ("noticeboard", SCALE +
     "A free-standing wooden notice board: two thick weathered timber legs holding a rectangular cork-and-plank "
     "panel at a slight backward tilt, a small sloped corrugated metal rain roof over the top edge, several blank "
     "sheets of paper and blank index cards pinned to the cork with colored push pins and a strip of twine with two "
     "clothes pegs, a small hanging brass bell on one leg, chipped olive paint on the frame. All paper is completely "
     "blank with no writing on it."),
    ("petnest", SCALE +
     "A relic hatchery nest built from scavenged parts: a wide shallow bowl of woven straw and rope resting inside "
     "a cut-open rusty steel drum ring, three smooth speckled eggs of different sizes nestled in the straw, a "
     "salvaged brass heat lamp on a bent gooseneck arm leaning over the nest, a "
     "small cracked ceramic water dish at the front, a folded wool blanket tucked along one side, warm amber straw "
     "against olive-green painted metal. Shot like a high-key studio product photo on a seamless light grey "
     "backdrop: the lamp bulb is dark and switched off, there is no light source in the picture, no vignette, "
     "no dark corners, no moody atmosphere."),
    ("showcase", SCALE +
     "A trophy display cabinet: a narrow upright case with a chunky weathered olive-green painted wooden frame, "
     "clear glass on the front and sides, two empty plain wooden shelves inside, brass corner brackets and a small "
     "brass latch on the door, short stubby feet, a scavenged strip of metal riveted along the top edge as a "
     "cornice. The shelves are completely empty and the glass is clean and plain with no reflections of any scene."),
]


def prompt_for(description):
    return "Concept reference sheet for 3D modelling. " + description + STYLE


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--list", action="store_true", help="只打印规格，不联网、不建目录")
    parser.add_argument("--only", nargs="*", help="只出这些件名")
    parser.add_argument("--force", action="store_true", help="已存在也重出")
    args = parser.parse_args()

    selected = [p for p in PIECES if not args.only or p[0] in args.only]
    if args.list:
        for name, description in selected:
            print(name + " -> output/building_concepts/" + name + ".png")
            print("   " + description[:150] + "...")
        return 0
    if not selected:
        print("没有匹配的件名")
        return 1

    os.makedirs(OUT, exist_ok=True)
    imagegen, _chroma = base.tool_paths()

    failures = []
    for name, description in selected:
        target = os.path.join(OUT, name + ".png")
        if os.path.exists(target) and not args.force:
            print("[skip] " + name)
            continue
        staging = tempfile.mkdtemp(prefix="concept-", dir=OUT)
        try:
            print("[gen ] " + name, flush=True)
            raw = base.generate_raw(prompt_for(description), staging, imagegen)
            # 网关返回尺寸不受 --size 控制，必须自己归一化（见 docs/AI生图API和密钥.md）
            staged = os.path.join(staging, name + ".png")
            base.normalize_square(raw, staged) if hasattr(base, "normalize_square") else shutil.copy2(raw, staged)
            os.replace(staged, target)
            print("[ok  ] " + target)
        except Exception as error:  # noqa: BLE001 - 单件失败不拖垮整批
            print("[FAIL] " + name + ": " + str(error)[-160:])
            failures.append(name)
        finally:
            shutil.rmtree(staging, ignore_errors=True)

    if failures:
        print("失败: " + ", ".join(failures))
        return 1
    print("全部完成，产物在 " + OUT)
    return 0


if __name__ == "__main__":
    sys.exit(main())
