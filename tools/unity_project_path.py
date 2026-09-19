# -*- coding: utf-8 -*-
"""作者工程（Unity）路径的单一解析口。

为什么需要：仓库里曾经有六七处各写各的 `D:/code/ykf/duckov_modding-main/...`。
工程搬到 Steam 库下之后这些路径全部失效，而**它们大多是「找不到就跳过」**，
于是守卫静默跳过、脚本静默什么都没干——2026-09-19 实测才发现
`SkyIslandMiniMapGuard` 的构建器检查从来没真正跑过。

解析顺序：
1. 环境变量 `BOSSRUSH_UNITY_PROJECT`；
2. 仓库同级目录 `../duckov_modding-main/UnityFiles/BossRush`（当前实际布局）；
3. 历史位置 `D:/code/ykf/duckov_modding-main/UnityFiles/BossRush`。

一处都不存在时返回 None，调用方自己决定是跳过还是报错。
"""
from __future__ import annotations

import os

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

#: 按优先级排列的候选位置。新增布局往中间插，不要删历史位置。
CANDIDATES = (
    os.path.join(os.path.dirname(REPO_ROOT), "duckov_modding-main", "UnityFiles", "BossRush"),
    os.path.join("D:", os.sep, "code", "ykf", "duckov_modding-main", "UnityFiles", "BossRush"),
)


def find_unity_project():
    """返回作者工程根目录（含 `Assets/`），找不到返回 None。"""
    override = os.environ.get("BOSSRUSH_UNITY_PROJECT")
    if override and os.path.isdir(os.path.join(override, "Assets")):
        return override
    for candidate in CANDIDATES:
        if os.path.isdir(os.path.join(candidate, "Assets")):
            return candidate
    return None


def describe_missing():
    """守卫跳过时打印的说明，避免「静默跳过」被当成「检查通过」。"""
    return ("找不到 Unity 作者工程；设置 BOSSRUSH_UNITY_PROJECT 或把工程放到 "
            + CANDIDATES[0])


#: Unity Editor 可执行文件的候选位置。版本必须与工程 ProjectVersion.txt 一致，
#: 换版本会触发整工程重新导入，而且 Hub 会弹版本升级确认。
EDITOR_CANDIDATES = (
    os.path.join("E:", os.sep, "Unity", "2022.3.62f3", "Editor", "Unity.exe"),
    os.path.join("C:", os.sep, "Program Files", "Unity", "Hub", "Editor",
                 "2022.3.62f3", "Editor", "Unity.exe"),
)


def find_unity_editor():
    """返回 Unity.exe 路径，找不到返回 None。环境变量 `BOSSRUSH_UNITY_EDITOR` 优先。"""
    override = os.environ.get("BOSSRUSH_UNITY_EDITOR")
    if override and os.path.isfile(override):
        return override
    for candidate in EDITOR_CANDIDATES:
        if os.path.isfile(candidate):
            return candidate
    return None
