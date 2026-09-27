"""Keep the automatically imported root rules small and preserve parsed section anchors."""

from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
AGENTS = ROOT / "AGENTS.md"
MAX_BYTES = 20_000
MAX_LINES = 180


def main():
    raw = AGENTS.read_bytes()
    text = raw.decode("utf-8-sig")
    errors = []
    if len(raw) > MAX_BYTES:
        errors.append("AGENTS.md 超过 20,000 字节：{}".format(len(raw)))
    if len(text.splitlines()) > MAX_LINES:
        errors.append("AGENTS.md 超过 180 行：{}".format(len(text.splitlines())))
    headings = [line for line in text.splitlines() if line.startswith("## ")]
    for number in range(1, 15):
        if not any(line.startswith("## {}. ".format(number)) for line in headings):
            errors.append("AGENTS.md 缺少 §{} 标题".format(number))
    for heading in ("### 4.1 新增 `.cs` 必须登记编译清单",
                    "### 4.3 TypeID 严格递增、不复用"):
        if heading not in text.splitlines():
            errors.append("AGENTS.md 标题改名或缺失：" + heading)
    for anchor in ("MODULES.md", "Common/UI/AGENTS.md", "DebugAndTools/AGENTS.md",
                   "Integration/AGENTS.md", "tests/AGENTS.md"):
        if anchor not in text:
            errors.append("AGENTS.md 缺少导航入口：" + anchor)
    if errors:
        for error in errors:
            print("RootAgentsBudgetGuard: " + error)
        return 1
    print("RootAgentsBudgetGuard: PASS ({} lines / {} bytes)".format(
        len(text.splitlines()), len(raw)))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
