#!/usr/bin/env python3
"""CompanionCleanupExemptionGuard — 每一条会 Destroy 角色的全局扫描都必须豁免遗种巢随从。

随从的本体是官方 `CharacterMainControl`，clone preset 只中性化了队伍/经验/掉落，
**不改 nameKey**，因此 `DisplayName` 仍是官方 Boss 的名字。任何按名字或按「非本 Mod 生成」
清场的扫描都会命中它。

历史缺陷：四条扫描里有三条调了 `PetNestCompanionAgent.IsCompanionCharacter` 跳过随从，
唯独 `ModBehaviour.TryCleanNonBossRushDaXingXing` 漏了。后果是大兴兴血脉的崽入场即被
自家逻辑销毁；又因为不是「受伤致死」，pet.state 不会转 Downed，重试窗口内每秒重生一次
再被杀一次，每轮还泄漏一个 clone preset。

本守卫按文件钉住豁免调用的存在，新增 Destroy 扫描时请一并登记到 SCANS。
"""
import io
import os
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

# 文件 -> 该文件里必须出现豁免调用的次数下限
SCANS = {
    os.path.join(ROOT, "ModBehaviour.cs"): 2,
    os.path.join(ROOT, "WavesArena", "WavesArenaEnemyMaintenance.cs"): 2,
}

EXEMPTION = "PetNestCompanionAgent.IsCompanionCharacter"


def read(path):
    if not os.path.isfile(path):
        return None
    with io.open(path, "r", encoding="utf-8", errors="ignore") as fh:
        return fh.read()


def main():
    errors = []

    for path, minimum in SCANS.items():
        src = read(path)
        name = os.path.basename(path)
        if src is None:
            errors.append("[File] 缺少 " + name)
            continue
        count = src.count(EXEMPTION)
        if count < minimum:
            errors.append(
                "[Exempt] {0} 至少要有 {1} 处随从豁免，当前 {2} 处".format(name, minimum, count))

    # 大兴兴清理是历史上漏掉的那一条，单独钉住
    mb = read(os.path.join(ROOT, "ModBehaviour.cs"))
    if mb is not None:
        start = mb.find("private void TryCleanNonBossRushDaXingXing()")
        if start < 0:
            errors.append("[Exempt] 找不到 TryCleanNonBossRushDaXingXing")
        else:
            # 方法体到下一个 4 空格缩进的方法声明为止
            end = mb.find("\n        private ", start + 10)
            if end < 0:
                end = len(mb)
            body = mb[start:end]
            if EXEMPTION not in body:
                errors.append(
                    "[Exempt] TryCleanNonBossRushDaXingXing 必须豁免遗种巢随从："
                    "随从沿用官方 preset 身份，DisplayName 会命中名字匹配")
            if "Destroy(" not in body:
                errors.append("[Exempt] TryCleanNonBossRushDaXingXing 应仍是一条 Destroy 扫描（守卫锚点失效）")

    if errors:
        print("CompanionCleanupExemptionGuard: FAIL ({0} errors)".format(len(errors)))
        for e in errors:
            print("  - " + e)
        return 1

    print("CompanionCleanupExemptionGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
