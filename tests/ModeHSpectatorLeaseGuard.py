#!/usr/bin/env python3
"""
ModeHSpectatorLeaseGuard — Mode H 观战租约守卫（设计提案 §17.1、§26.1）。

不变式：
- 快照字段完整：team / position / invincible / cursor / 玩家引用 / scene generation；
- 不得复用会把 Time.timeScale 设为 0 的暂停型 modal lease；
- 获取顺序固定：DisableInput -> 无敌 -> Teams.middle -> 移动 -> 光标；
  失败严格逆序回滚；
- 释放顺序固定：停止拍铃 -> 位置 -> team -> invincible -> ActiveInput -> 光标 -> 销毁 token；
- 输入 token 获取/释放对称，释放幂等；
- 拍铃不得恢复角色输入；
- 五类退出路径（正常结束/技术中止/场景切换/Mod 销毁/ERROR 恢复）调用同一释放入口。
"""
import os
import re
import sys

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(REPO_ROOT, "tests"))

from modeh_guard_util import read_text, strip_cs_comments  # noqa: E402

LEASE = os.path.join(REPO_ROOT, "ModeH", "ModeHSpectatorLease.cs")

SNAPSHOT_FIELDS = [
    "_originalTeam",
    "_originalPosition",
    "_originalInvincible",
    "_originalCursorVisible",
    "_originalCursorLock",
    "_sceneGeneration",
]


def main():
    errors = []

    lease = read_text(LEASE)
    if lease is None:
        print("ModeHSpectatorLeaseGuard: FAIL (1 errors)")
        print("  - [File] 缺少 ModeH/ModeHSpectatorLease.cs")
        return 1
    code = strip_cs_comments(lease)

    for field in SNAPSHOT_FIELDS:
        if field not in code:
            errors.append("[Snapshot] 缺少快照字段: " + field)

    checks = [
        (r"public bool TryAcquire\(Vector3 spectatorPos, int sceneGeneration, long ownerToken, out string failureReasonId\)",
         "获取入口签名"),
        (r"InputManager\.DisableInput\(_inputToken\);", "创建专用 token 并阻断角色输入"),
        (r"_player\.Health\.SetInvincible\(true\);", "设置无敌"),
        (r"_player\.SetTeam\(Teams\.middle\);", "设为中立阵营"),
        (r"_player\.SetPosition\(spectatorPos\);", "移动到看台点"),
        (r"private void RollbackTo\(int completedStep\)", "失败逆序回滚"),
        (r"public void Release\(int currentSceneGeneration\)", "释放入口带 generation"),
        (r"if \(_released\) return;", "释放幂等守卫"),
        (r"public void StopAcceptingBell\(\)", "停止接收拍铃入口"),
        (r"InputManager\.ActiveInput\(_inputToken\);", "对称恢复输入"),
        (r"private void DestroyToken\(\)", "销毁 token"),
    ]
    for pattern, desc in checks:
        if not re.search(pattern, code):
            errors.append("[Lease] 不满足: " + desc)

    # 禁止复用暂停型 modal lease
    for forbidden in ["ClaimModalInput", "ModalInputLease", "Time.timeScale"]:
        if forbidden in code:
            errors.append("[Lease] 不得复用会暂停时间的模态输入租约: " + forbidden)

    # 获取顺序：DisableInput -> 无敌 -> team -> 位置
    acquire = re.search(r"public bool TryAcquire\([\s\S]*?\n        \}", code)
    if acquire:
        body = acquire.group(0)
        order = [
            ("InputManager.DisableInput", "输入阻断"),
            ("SetInvincible(true)", "无敌"),
            ("SetTeam(Teams.middle)", "中立"),
            ("SetPosition(spectatorPos)", "移动"),
        ]
        positions = []
        for token, label in order:
            pos = body.find(token)
            if pos < 0:
                errors.append("[Order] 获取流程缺少步骤: " + label)
                positions.append(-1)
            else:
                positions.append(pos)
        if all(p >= 0 for p in positions) and positions != sorted(positions):
            errors.append("[Order] 获取顺序必须是 输入阻断 -> 无敌 -> 中立 -> 移动")

    # 释放顺序：停止拍铃 -> 位置 -> team -> invincible -> ActiveInput -> 光标 -> 销毁
    release = re.search(r"public void Release\(int currentSceneGeneration\)[\s\S]*?\n        \}\n", code)
    if release:
        body = release.group(0)
        order = [
            ("_bellAccepting = false;", "停止拍铃"),
            ("SetPosition(_originalPosition)", "恢复位置"),
            ("SetTeam(_originalTeam)", "恢复阵营"),
            ("SetInvincible(_originalInvincible)", "恢复无敌"),
            ("InputManager.ActiveInput", "恢复输入"),
            ("Cursor.visible = _originalCursorVisible", "恢复光标"),
            ("DestroyToken()", "销毁 token"),
        ]
        positions = []
        for token, label in order:
            pos = body.find(token)
            if pos < 0:
                errors.append("[Order] 释放流程缺少步骤: " + label)
                positions.append(-1)
            else:
                positions.append(pos)
        if all(p >= 0 for p in positions) and positions != sorted(positions):
            errors.append("[Order] 释放顺序不符合冻结次序")

    # 拍铃不得恢复角色输入
    if re.search(r"StopAcceptingBell\(\)[\s\S]{0,200}?ActiveInput", code):
        errors.append("[Bell] 拍铃门控不得恢复角色输入")

    if errors:
        print("ModeHSpectatorLeaseGuard: FAIL ({} errors)".format(len(errors)))
        for e in errors:
            print("  - " + e)
        return 1

    print("ModeHSpectatorLeaseGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
