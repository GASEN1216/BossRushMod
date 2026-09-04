#!/usr/bin/env python3
"""
ModeHDataStampGuard — Mode H 七个数据文件的 contentSignature 必须是新鲜的。

为什么需要这条守卫（它补的是一个真空）：
  ModeHContentCatalog.LoadInternal 逐文件校验 contentSignature，不匹配即
  `content_signature_failed` -> IsLoaded=false -> EnsureValidated 失败 ->
  认证直接中止、IsModeHContentReady 恒 false，**整个 Mode H 不可进入**。

  而 `python tools/modeh_stamp_data.py --check` 不在 tools/run_guards.py 的采集范围
  （它只收 tests/*Guard.py / *PropertyTest.py / *Tests.py）。
  于是「改了 JSON 忘了重新盖章」这件事：编译绿、515 个 guard 全绿，
  只有真机进模式那一刻才炸。这条守卫把它拉回静态可查。

不变式：
  - ModeHConfig.RequiredDataFileNames 里登记的每个文件都存在；
  - 每个文件都有 64 位十六进制 contentSignature；
  - 声明值 == 按规范 JSON 重算的值（规范化实现与 C# 侧 ModeHCanonicalDigest 同源，
    镜像在 tests/modeh_canonical_json.py）。

修法：在仓库根跑 `python tools/modeh_stamp_data.py`，然后 `--check` 复核。
"""
import io
import json
import os
import re
import sys

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(REPO_ROOT, "tests"))

from modeh_canonical_json import content_signature  # noqa: E402

DATA_DIR = os.path.join(REPO_ROOT, "Assets", "Data", "ModeH")
CONFIG = os.path.join(REPO_ROOT, "ModeH", "ModeHConfig.cs")

HEX64 = re.compile(r"^[0-9a-f]{64}$")


def fail(message):
    print("ModeHDataStampGuard: FAIL - " + message)
    return 1


def read_required_file_names():
    """
    从 ModeHConfig.RequiredDataFileNames 反查清单，而不是在守卫里再抄一份。
    两处硬编码同一组文件名，改动时必然漏掉一处。
    """
    with io.open(CONFIG, encoding="utf-8", errors="ignore") as handle:
        code = handle.read()
    block = re.search(
        r"RequiredDataFileNames\s*=\s*new\s+string\[\]\s*\{(.*?)\}", code, re.S)
    if block is None:
        return None

    # 清单里写的是常量引用（BossProfilesFileName 等），不是字面量，先解析回字符串值。
    constants = dict(re.findall(
        r'public\s+const\s+string\s+(\w+FileName)\s*=\s*"([^"]+\.json)"\s*;', code))
    names = []
    for token in re.findall(r"[A-Za-z_]\w*", block.group(1)):
        if token in constants and constants[token] not in names:
            names.append(constants[token])
    return names


def main():
    names = read_required_file_names()
    if not names:
        return fail("在 ModeH/ModeHConfig.cs 里找不到 RequiredDataFileNames 清单")

    for name in names:
        path = os.path.join(DATA_DIR, name)
        if not os.path.isfile(path):
            return fail("缺少数据文件: Assets/Data/ModeH/" + name)

        with io.open(path, encoding="utf-8") as handle:
            document = json.load(handle)

        declared = document.get("contentSignature")
        if not isinstance(declared, str) or not HEX64.match(declared):
            return fail(name + " 的 contentSignature 不是 64 位十六进制摘要")

        actual = content_signature(document)
        if declared != actual:
            return fail(
                name + " 的 contentSignature 已过期（声明 " + declared[:12]
                + "…，实算 " + actual[:12] + "…）。"
                "运行 `python tools/modeh_stamp_data.py` 重新盖章——"
                "不盖章的话游戏内会 content_signature_failed，整个 Mode H 进不去。")

    print("ModeHDataStampGuard: PASS（" + str(len(names)) + " 个数据文件盖章新鲜）")
    return 0


if __name__ == "__main__":
    sys.exit(main())
