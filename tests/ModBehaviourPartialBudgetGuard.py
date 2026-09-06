"""D-1: pin the whole production ModBehaviour partial, not just individual files."""
from pathlib import Path
import json
import re

ROOT = Path(__file__).resolve().parent.parent
BUDGET = ROOT / "tests/modbehaviour_partial_budget.json"
DECLARATION = re.compile(r"^\s*(?:(?:public|internal|abstract|sealed)\s+)*partial\s+class\s+ModBehaviour\b", re.M)


def collect_partial_sizes():
    # The official list is the production boundary; its separate bidirectional guard
    # rejects omitted sources. Never count Build snapshots, official sources or stubs.
    source_list = (ROOT / "compile_official.bat").read_text(encoding="utf-8-sig")
    sizes = {}
    for raw in re.findall(r"^echo\(([^\r\n]+\.cs)\s*$", source_list, re.M):
        relative = raw.replace("\\", "/")
        path = ROOT / relative
        if not path.is_file():
            raise ValueError("Missing production source: " + relative)
        source = path.read_text(encoding="utf-8-sig")
        if DECLARATION.search(source):
            sizes[relative] = len(source.splitlines())
    return sizes


def violations(sizes, budget):
    errors = []
    new_files = sorted(set(sizes) - set(budget["allowed_files"]))
    if new_files:
        errors.append("New ModBehaviour partial files: " + ", ".join(new_files)
                      + "; put subsystem state in its own runtime/service type.")
    if len(sizes) > budget["max_files"]:
        errors.append(f"Partial file count {len(sizes)} > {budget['max_files']}")
    total = sum(sizes.values())
    if total > budget["max_total_lines"]:
        errors.append(f"Whole partial file size {total} > {budget['max_total_lines']}; "
                      "splitting the same host into more files does not reduce its responsibilities.")
    return errors


def main():
    budget = json.loads(BUDGET.read_text(encoding="utf-8"))
    sizes = collect_partial_sizes()
    errors = violations(sizes, budget)
    for error in errors:
        print("FAIL " + error)
    print(f"ModBehaviourPartialBudgetGuard: {len(sizes)} files / {sum(sizes.values())} lines; "
          + ("FAIL" if errors else "PASS"))
    return 1 if errors else 0


if __name__ == "__main__":
    raise SystemExit(main())
