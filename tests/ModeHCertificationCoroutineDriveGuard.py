#!/usr/bin/env python3
"""
ModeHCertificationCoroutineDriveGuard — Mode H 生产认证协程驱动守卫。

背景（真实事故，2026-09-01 实机验收报告 BossRushValidation_20260901_012335_057.log）：

  ModeHRuntimeModule_SceneFlow.DriveCertification 手工 MoveNext 驱动
  ModeHProductionCertification.Run，但循环体里写死 `yield return null`，
  把 inner.Current 丢掉了。而 Run 内部是 `yield return CertifyKey(...)`——
  它 yield 出一个子 IEnumerator，指望调用方（Unity 协程调度器）递归驱动。

  结果：子协程被创建但一次都没 MoveNext。逐 key 的生成、阵营核对、受控击杀、
  RecordPassed/RecordRejected 全部没执行；keyResult.Passed 恒 false、
  FailureReasonId 恒 null，日志里每个 key 打出一条**空原因**的「认证拒绝」；
  _records 恒空 → passedStableKeys.Count = 0 → 撞 MinProductionCandidateCount
  门槛失败 → Mode H 完全无法开局（不只是验收，正式入场同一路径）。

不变式：
- DriveCertification 必须把 inner.Current 透传（`yield return inner.Current;`），
  不得在驱动循环里写死 `yield return null;`；
- 同仓库正确参照：ModeHRuntimeModule_MatchFlow.DriveMatchSpawning
  的 `while (inner.MoveNext()) yield return inner.Current;`；
- Run 仍必须以 `yield return CertifyKey(` 的形式逐 key 驱动（若改为内联展开，
  本 guard 的前提消失，需连同本文件一起重写而不是删断言）。
"""
import os
import sys

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(REPO_ROOT, "tests"))

from modeh_guard_util import read_text, strip_cs_comments  # noqa: E402

SCENE_FLOW = os.path.join(REPO_ROOT, "ModeH", "ModeHRuntimeModule_SceneFlow.cs")
CERTIFICATION = os.path.join(REPO_ROOT, "ModeH", "ModeHProductionCertification.cs")


def extract_block(source, header):
    """粗粒度取出方法体：从 header 起按花括号配平截断。"""
    start = source.find(header)
    if start < 0:
        return None
    brace = source.find("{", start)
    if brace < 0:
        return None
    depth = 0
    for index in range(brace, len(source)):
        char = source[index]
        if char == "{":
            depth += 1
        elif char == "}":
            depth -= 1
            if depth == 0:
                return source[brace:index + 1]
    return None


def main():
    errors = []

    flow = strip_cs_comments(read_text(SCENE_FLOW))
    drive = extract_block(flow, "private IEnumerator DriveCertification(")
    if drive is None:
        errors.append("找不到 DriveCertification 方法体")
    else:
        if "inner.MoveNext()" not in drive:
            errors.append("DriveCertification 不再手工驱动 inner；本 guard 前提已变，请同步重写")
        if "yield return inner.Current;" not in drive:
            errors.append(
                "DriveCertification 必须 `yield return inner.Current;` 透传子协程，"
                "否则 CertifyKey 永不推进、认证恒失败"
            )
        if "yield return null;" in drive:
            errors.append(
                "DriveCertification 驱动循环里不得写死 `yield return null;`（会丢弃 inner.Current）"
            )

    cert = strip_cs_comments(read_text(CERTIFICATION))
    run = extract_block(cert, "internal IEnumerator Run(")
    if run is None:
        errors.append("找不到 ModeHProductionCertification.Run 方法体")
    elif "yield return CertifyKey(" not in run:
        errors.append(
            "Run 不再以 `yield return CertifyKey(` 逐 key 驱动；"
            "本 guard 的前提消失，请连同本文件一起重写"
        )

    if errors:
        for error in errors:
            print("  - " + error)
        print("ModeHCertificationCoroutineDriveGuard: FAIL")
        return 1
    print("ModeHCertificationCoroutineDriveGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
