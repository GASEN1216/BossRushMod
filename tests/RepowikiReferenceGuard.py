"""Guard: repowiki 引用文件必须存在

golden rule 4.13：`.qoder/repowiki/` 是仓库的详细 Wiki 内容库，代码变更必须同步；
「repowiki 内容过时视为未完成变更」。

但此前**没有任何自动化**校验它跟代码的一致性。最容易发生也最容易发现的一类漂移是
**死链**：内容文档里的 `[Xxx.cs](file://路径)` 引用指向已被删除、改名或移动的文件。

本 guard 只管这一件事——所有 `file://` 引用的目标必须真实存在。
它抓不到「正文描述过时」，但能在删文件/改名时立刻把该同步的文档指出来。
（本 guard 落地时一次性修掉了 13 个死链、103 处引用，其中既有历史路径前缀写错，
也有把 `Common/Infrastructure/ReflectionCache.cs` 改名后遗留的引用。）
"""

from pathlib import Path
import re
import sys

ROOT = Path(__file__).resolve().parents[1]
REPOWIKI = ROOT / ".qoder" / "repowiki"
ALLOWLIST_FILE = Path(__file__).parent / "repowiki_reference_allowlist.txt"

# [标题](file://相对路径#L1-L2) —— 只取路径部分，丢掉 #Lxx-Lyy 行号锚点
RE_FILE_REF = re.compile(r"\(file://([^)#]+)")


def fail(message):
    print("RepowikiReferenceGuard: FAIL - " + message)
    return 1


def load_allowlist():
    allowed = set()
    if not ALLOWLIST_FILE.exists():
        return allowed
    for raw in ALLOWLIST_FILE.read_text(encoding="utf-8").splitlines():
        line = raw.strip()
        if not line or line.startswith("#"):
            continue
        allowed.add(line.split("|", 1)[0].strip().replace("\\", "/"))
    return allowed


def main():
    print("RepowikiReferenceGuard: 开始核对 repowiki 引用文件是否存在...")

    if not REPOWIKI.is_dir():
        print("  .qoder/repowiki 不存在，跳过")
        print("\nRepowikiReferenceGuard: PASS")
        return 0

    allowlist = load_allowlist()

    # 目标路径 -> 引用它的 (文档, 行号) 列表
    missing = {}
    total_refs = 0
    unique_targets = set()
    scanned_docs = 0

    for md in sorted(REPOWIKI.rglob("*.md")):
        try:
            text = md.read_text(encoding="utf-8")
        except Exception:
            continue
        scanned_docs += 1

        for line_no, line in enumerate(text.splitlines(), start=1):
            for m in RE_FILE_REF.finditer(line):
                target = m.group(1).strip().replace("\\", "/")
                if not target:
                    continue
                total_refs += 1
                unique_targets.add(target)

                if target in allowlist:
                    continue
                if (ROOT / target).exists():
                    continue

                doc_rel = str(md.relative_to(ROOT)).replace("\\", "/")
                missing.setdefault(target, []).append((doc_rel, line_no))

    if missing:
        print("\n  === 引用的文件不存在（repowiki 与代码基线已漂移） ===")
        for target in sorted(missing):
            refs = missing[target]
            print("  [FAIL] {0}  （被 {1} 处引用）".format(target, len(refs)))
            for doc_rel, line_no in refs[:3]:
                print("           {0}:{1}".format(doc_rel, line_no))
            if len(refs) > 3:
                print("           ...（还有 {0} 处）".format(len(refs) - 3))
        print("  提示: 删除或改名代码文件时，必须同步 .qoder/repowiki 的引用与正文（AGENTS.md §4.13）。")
        print("        确实不该指向仓库内文件的引用，加进 tests/repowiki_reference_allowlist.txt 并写明原因。")
        return fail("{0} 个引用目标不存在".format(len(missing)))

    print("\n  扫描文档: {0} 篇".format(scanned_docs))
    print("  file:// 引用: {0} 处，去重后 {1} 个目标，全部存在".format(total_refs, len(unique_targets)))
    print("  豁免条目: {0} 条".format(len(allowlist)))
    print("\nRepowikiReferenceGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
