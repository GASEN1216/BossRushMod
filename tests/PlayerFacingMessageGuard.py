#!/usr/bin/env python3
"""PlayerFacingMessageGuard — ShowMessage 必须真的能被玩家看到。

`ModBehaviour.ShowMessage` 是全仓最常用的玩家提示入口（一百余处调用），多个守卫
（例如 ModeHStructureGuard 的锁盘反馈检查）直接拿「有没有调 ShowMessage」当
「有没有给玩家可见反馈」的判据。

历史缺陷：它曾经反射 `NotificationText.ShowNext` 并以 `Public | Static` 绑定，
而官方那个方法是**私有实例零参**的（公有静态的是 `Push(string)`）。绑定恒为 null，
整段调用永不执行；`statusMessage` 字段没有任何渲染方，`DevLog` 又被
`[Conditional("BOSSRUSH_DEV")]` 在正式构建里整个剥离。结果是全部提示对玩家静默，
而所有依赖 ShowMessage 的「不再静默失败」修复也跟着一起哑掉。

因此钉住：必须直接调用官方公有静态 `NotificationText.Push`，不得退回反射 ShowNext。
"""
import io
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
UI = os.path.join(ROOT, "UIAndSigns", "UIAndSigns.cs")
CACHE = os.path.join(ROOT, "Common", "Infrastructure", "BossRushEagerReflectionCache.cs")


def read(path):
    if not os.path.isfile(path):
        return None
    with io.open(path, "r", encoding="utf-8", errors="ignore") as fh:
        return fh.read()


def main():
    errors = []

    ui = read(UI)
    if ui is None:
        errors.append("[File] 缺少 UIAndSigns.cs")
    else:
        m = re.search(r"private void ShowMessage_UIAndSigns\(string msg\)[\s\S]*?\n        \}", ui)
        if m is None:
            errors.append("[Message] 找不到 ShowMessage_UIAndSigns 方法体")
        else:
            body = m.group(0)
            if "NotificationText.Push(" not in body:
                errors.append(
                    "[Message] ShowMessage 必须调用官方公有静态 NotificationText.Push，"
                    "否则玩家在正式构建里看不到任何提示")
            if "NotificationText_ShowNext" in body:
                errors.append(
                    "[Message] 不得再反射 ShowNext：官方该方法是私有实例零参，"
                    "用 Public|Static 绑定恒为 null")

    cache = read(CACHE)
    if cache is None:
        errors.append("[File] 缺少 BossRushEagerReflectionCache.cs")
    else:
        if "NotificationText_ShowNext" in cache:
            errors.append(
                "[Cache] 反射缓存不得保留 NotificationText_ShowNext 绑定（签名不存在，恒 null）")
        if re.search(r'GetMethod\(\s*"ShowNext"', cache):
            errors.append('[Cache] 不得以任何 BindingFlags 绑定 "ShowNext"')

    if errors:
        print("PlayerFacingMessageGuard: FAIL ({0} errors)".format(len(errors)))
        for e in errors:
            print("  - " + e)
        return 1

    print("PlayerFacingMessageGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
