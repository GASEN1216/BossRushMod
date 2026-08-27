"""Guard: TypeID 台账一致性

golden rule 4.3：自定义物品/装备 TypeID 使用 5000xx 区间，严格递增、不复用、不回填已删 ID。
TypeID 会进入存档键、掉落表、Wiki 与调试流程，复用属于存档兼容风险（BREAKING）。

此前这条规则完全靠人工遵守，没有任何自动化——本 guard 补上：

1. 从 `docs/contracts.md` §1 解析台账（已登记范围 + 保留空洞），作为唯一事实源。
2. 交叉核对 `AGENTS.md` §4.3 的范围与空洞与 contracts.md 一致（防止两份文档各说各话）。
3. 扫描全部 Mod 源码（剥离注释）里的 `5000xx` 字面量：
   - 命中保留空洞 = 回填，直接 FAIL；
   - 超出已登记范围 = 台账未更新（或写错了 ID），FAIL；
   - 已知非 TypeID 的同形数字走 tests/typeid_literal_allowlist.txt 豁免。

扫描排除目录：Build/、tests/、.git/、.kiro/、.codex_tmp/、鸭科夫源码/、wiki-site/、.qoder/
"""

from pathlib import Path
import re
import sys

ROOT = Path(__file__).resolve().parents[1]
CONTRACTS = ROOT / "docs" / "contracts.md"
AGENTS = ROOT / "AGENTS.md"
ALLOWLIST_FILE = Path(__file__).parent / "typeid_literal_allowlist.txt"

EXCLUDE_DIRS = {
    "Build", "tests", ".git", ".kiro", ".codex_tmp",
    "鸭科夫源码", "wiki-site", ".qoder", "obj", "bin",
}

RE_TYPEID_LITERAL = re.compile(r"\b(5000\d{2})\b")
RE_RANGE = re.compile(r"(500\d{3})\s*-\s*(500\d{3})")
RE_HOLE = re.compile(r"`(500\d{3})`")


def fail(message):
    print("TypeIdLedgerGuard: FAIL - " + message)
    return 1


def strip_comments(text):
    """去掉 // 行注释与 /* */ 块注释，保留字符串字面量。

    数字写在注释里（例如解释某个 ID 是保留空洞）不应被当成代码用了该 ID。
    """
    out = []
    i = 0
    n = len(text)
    in_line_comment = False
    in_block_comment = False
    in_string = False
    in_char = False
    verbatim = False

    while i < n:
        ch = text[i]
        nxt = text[i + 1] if i + 1 < n else ""

        if in_line_comment:
            if ch == "\n":
                in_line_comment = False
                out.append(ch)
            i += 1
            continue

        if in_block_comment:
            if ch == "*" and nxt == "/":
                in_block_comment = False
                i += 2
            else:
                if ch == "\n":
                    out.append(ch)
                i += 1
            continue

        if in_string:
            out.append(ch)
            if verbatim:
                if ch == '"' and nxt == '"':
                    out.append(nxt)
                    i += 2
                    continue
                if ch == '"':
                    in_string = False
                    verbatim = False
            else:
                if ch == "\\" and nxt:
                    out.append(nxt)
                    i += 2
                    continue
                if ch == '"':
                    in_string = False
            i += 1
            continue

        if in_char:
            out.append(ch)
            if ch == "\\" and nxt:
                out.append(nxt)
                i += 2
                continue
            if ch == "'":
                in_char = False
            i += 1
            continue

        if ch == "/" and nxt == "/":
            in_line_comment = True
            i += 2
            continue
        if ch == "/" and nxt == "*":
            in_block_comment = True
            i += 2
            continue
        if ch == "@" and nxt == '"':
            in_string = True
            verbatim = True
            out.append(ch)
            out.append(nxt)
            i += 2
            continue
        if ch == '"':
            in_string = True
            verbatim = False
            out.append(ch)
            i += 1
            continue
        if ch == "'":
            in_char = True
            out.append(ch)
            i += 1
            continue

        out.append(ch)
        i += 1

    return "".join(out)


def load_allowlist():
    """已知非 TypeID 的 5000xx 数字；格式 `相对路径:数字 | 原因`。"""
    allowed = set()
    if not ALLOWLIST_FILE.exists():
        return allowed
    for raw in ALLOWLIST_FILE.read_text(encoding="utf-8").splitlines():
        line = raw.strip()
        if not line or line.startswith("#"):
            continue
        entry = line.split("|", 1)[0].strip()
        if ":" in entry:
            path, value = entry.rsplit(":", 1)
            allowed.add((path.strip().replace("\\", "/"), value.strip()))
    return allowed


def parse_ledger():
    """从 contracts.md §1 解析已登记范围与保留空洞。"""
    if not CONTRACTS.exists():
        return None, None, "docs/contracts.md 不存在（该文件是 TypeID 台账的事实源）"

    text = CONTRACTS.read_text(encoding="utf-8")
    section = text.split("## 2.")[0]

    range_match = RE_RANGE.search(section)
    if not range_match:
        return None, None, "docs/contracts.md §1 里找不到形如 `500001-500058` 的已登记范围"

    low = int(range_match.group(1))
    high = int(range_match.group(2))

    holes = set()
    for line in section.splitlines():
        if "空洞" not in line:  # 空洞
            continue
        for m in RE_HOLE.finditer(line):
            value = int(m.group(1))
            if low <= value <= high:
                holes.add(value)

    return (low, high), holes, None


def cross_check_agents(low, high, holes):
    """AGENTS.md §4.3 必须与 contracts.md 记载同一套数字。"""
    if not AGENTS.exists():
        return "AGENTS.md 不存在"

    text = AGENTS.read_text(encoding="utf-8")
    idx = text.find("4.3")
    section = text[idx:idx + 1500] if idx >= 0 else text

    range_match = RE_RANGE.search(section)
    if not range_match:
        return "AGENTS.md §4.3 里找不到形如 `500001-500058` 的登记范围"

    a_low = int(range_match.group(1))
    a_high = int(range_match.group(2))
    if (a_low, a_high) != (low, high):
        return ("AGENTS.md §4.3 登记范围 {0}-{1} 与 docs/contracts.md §1 的 {2}-{3} 不一致"
                .format(a_low, a_high, low, high))

    agents_holes = set()
    for line in section.splitlines():
        if "空" not in line:  # 空缺 / 空洞
            continue
        for m in RE_HOLE.finditer(line):
            value = int(m.group(1))
            if low <= value <= high:
                agents_holes.add(value)

    if agents_holes != holes:
        return ("AGENTS.md §4.3 的保留空洞 {0} 与 docs/contracts.md §1 的 {1} 不一致"
                .format(sorted(agents_holes), sorted(holes)))

    return None


def iter_source_files():
    for path in sorted(ROOT.rglob("*.cs")):
        try:
            rel = path.relative_to(ROOT)
        except ValueError:
            continue
        if any(part in EXCLUDE_DIRS for part in rel.parts):
            continue
        yield rel, path


def main():
    print("TypeIdLedgerGuard: 开始核对 TypeID 台账...")

    ledger_range, holes, err = parse_ledger()
    if err:
        return fail(err)

    low, high = ledger_range

    drift = cross_check_agents(low, high, holes)
    if drift:
        return fail(drift)

    allowlist = load_allowlist()

    backfilled = []   # 命中保留空洞
    out_of_range = []  # 超出已登记范围
    seen = set()

    for rel, path in iter_source_files():
        try:
            code = strip_comments(path.read_text(encoding="utf-8"))
        except Exception:
            continue

        rel_str = str(rel).replace("\\", "/")
        for line_no, line in enumerate(code.splitlines(), start=1):
            for m in RE_TYPEID_LITERAL.finditer(line):
                value = int(m.group(1))
                if (rel_str, m.group(1)) in allowlist:
                    continue
                if value in holes:
                    backfilled.append((rel_str, line_no, value))
                elif value < low or value > high:
                    out_of_range.append((rel_str, line_no, value))
                else:
                    seen.add(value)

    if backfilled:
        print("\n  === 回填保留空洞（BREAKING，存档兼容风险） ===")
        for rel_str, line_no, value in backfilled:
            print("  [FAIL] {0}:{1} 使用了保留空洞 TypeID {2}".format(rel_str, line_no, value))

    if out_of_range:
        print("\n  === 超出已登记范围 ===")
        for rel_str, line_no, value in out_of_range:
            print("  [FAIL] {0}:{1} 的 {2} 不在已登记范围 {3}-{4} 内".format(
                rel_str, line_no, value, low, high))
        print("  提示: 新增 TypeID 必须先更新 docs/contracts.md §1 与 AGENTS.md §4.3 的台账；")
        print("        若这个数字根本不是 TypeID，加进 tests/typeid_literal_allowlist.txt 并写明原因。")

    if backfilled or out_of_range:
        return fail("发现 {0} 处回填、{1} 处越界".format(len(backfilled), len(out_of_range)))

    print("\n  台账范围: {0}-{1}（来自 docs/contracts.md §1，已与 AGENTS.md §4.3 交叉核对）".format(low, high))
    print("  保留空洞: {0}（源码中零命中）".format(sorted(holes) or "无"))
    print("  源码实际使用的 TypeID: {0} 个".format(len(seen)))
    print("  豁免条目: {0} 条".format(len(allowlist)))
    print("\nTypeIdLedgerGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
