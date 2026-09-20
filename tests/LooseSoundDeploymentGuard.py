# -*- coding: utf-8 -*-
"""散装音效必须真的被部署脚本拷出去。

2026-09-20 第三轮发现的一类供应链缺口：`compile_official.bat` 里原本是一串**逐文件夹**
的 xcopy，只列了 BGM / SkyIsland / SetBonus / NewWeapons 四个；而代码实际会读
Achievement、DragonKing、Goblin、Nurse、items、lottery 另外六个。
那六个文件夹在 owner 的游戏目录里存在，只是早年手工拷进去的——于是：

  - 本机跑起来有声音，**干净安装没有**；
  - 失败是静默的（文件不在就跳过播放），编译、守卫、部署全绿，玩家只是听不见。

许愿台大奖音乐与遗种巢异色揭晓复用的就是 `Assets/Sounds/lottery/special.mp3`，
正好落在没被部署的那六个里。

本守卫的判据：
  1. 从 C# 里抽出所有被读取的 `Assets/Sounds/<Folder>`；
  2. 部署脚本要么整棵树拷（推荐，新增文件夹不用改脚本），要么逐个都列上；
  3. 脚本里的「缺失告警」清单必须覆盖代码读到的每一个文件夹——
     fail-loud 比 fail-silent 重要，缺了要在构建输出里看得见。

散图 / 散音是 local-only（AGENTS §13），所以这里**不**断言文件真的在仓库里，
只断言「脚本会去拷」与「缺了会告警」。
"""
from pathlib import Path
import re
import sys

ROOT = Path(__file__).resolve().parents[1]
BAT = ROOT / "compile_official.bat"

# Path.Combine(..., "Assets", "Sounds", "Folder", ...)
COMBINE = re.compile(r'"Assets"\s*,\s*"Sounds"\s*,\s*"([A-Za-z0-9_]+)"')
# "Assets/Sounds/Folder/file" 形式的字面量路径
SLASHED = re.compile(r'Assets/Sounds/([A-Za-z0-9_]+)/')

SKIP_DIRS = {"Build", "鸭科夫源码", "tests", ".git", "node_modules", "wiki-site", "docs"}


def collect_folders():
    folders = {}
    for path in ROOT.rglob("*.cs"):
        if any(part in SKIP_DIRS for part in path.relative_to(ROOT).parts[:-1]):
            continue
        text = path.read_text(encoding="utf-8-sig", errors="replace")
        for match in list(COMBINE.finditer(text)) + list(SLASHED.finditer(text)):
            folders.setdefault(match.group(1), path.relative_to(ROOT).as_posix())
    return folders


def fail(message):
    print("LooseSoundDeploymentGuard: FAIL - " + message)
    return 1


def main():
    if not BAT.exists():
        return fail("missing compile_official.bat")

    bat = BAT.read_text(encoding="utf-8", errors="replace")
    folders = collect_folders()
    if not folders:
        return fail("no Assets/Sounds reader found in C# - the scanner regex has drifted")

    tree_copy = re.search(r'xcopy\s+/E\s+/Y\s+/I\s+"Assets\\Sounds"', bat)
    for folder, origin in sorted(folders.items()):
        if tree_copy:
            break
        if ('"Assets\\Sounds\\' + folder) not in bat:
            return fail("sound folder read by " + origin
                        + " is never deployed by compile_official.bat -> Assets/Sounds/" + folder)

    # 缺失告警清单：整棵树拷贝掩盖不了「源目录里本来就没有」，必须逐个点名
    warn = re.search(r"for %%S in \(([^)]*)\) do \(", bat)
    if not warn:
        return fail("compile_official.bat lost the per-folder sound audit loop")
    audited = set(warn.group(1).split())
    for folder, origin in sorted(folders.items()):
        if folder not in audited:
            return fail("sound folder read by " + origin
                        + " is missing from the deploy audit list -> " + folder)

    print("LooseSoundDeploymentGuard: PASS (" + str(len(folders))
          + " sound folders deployed and audited)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
