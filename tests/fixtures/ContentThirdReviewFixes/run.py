"""Run production-linked content regressions; requires .NET 8 SDK, no game process."""
from pathlib import Path
import hashlib
import json
import re
import shutil
import sys

HERE = Path(__file__).resolve().parent


def extract_presentation():
    root = HERE.parents[2]
    out = root / "Build/content-third-review-fixes/DailyReport"
    out.mkdir(parents=True, exist_ok=True)
    paths = {
        "DailyReportView": ("Integration/DailyReport/DailyReportUI.cs", (
            "private void FitPaper()", "private static string BuildBountyBlock(")),
        "DailyReportMailboxBuilder": ("Integration/DailyReport/DailyReportMailboxRuntime.cs", (
            "private bool InjectDailyReportBuildingData()", "private static void SetDailyReportBuildingInfoField(",
            "private void SetDailyReportBuildingCost(")),
    }
    code = "using System; using System.Collections; using System.Collections.Generic; using System.Reflection; using UnityEngine; using TMPro; namespace BossRush {\n"
    hashes = {}
    for cls, (path, signatures) in paths.items():
        raw = (root / path).read_bytes()
        hashes[path] = hashlib.sha256(raw).hexdigest()
        source = raw.decode("utf-8-sig")
        code += "partial class " + cls + " {\n"
        if cls == "DailyReportView":
            # 2026-09-20：面板尺寸改为引用版面表常量，不再是字面量
            for constant in ("PanelWidth", "PanelHeight"):
                declaration = re.search(
                    r"private const float " + constant + r"\s*=\s*DailyReportLayoutTable\." + constant + r";",
                    source)
                assert declaration, constant
                code += declaration.group(0) + "\n"
        for signature in signatures:
            start = source.index(signature)
            opening = source.index("{", start)
            depth, end = 1, opening + 1
            while depth:
                depth += (source[end] == "{") - (source[end] == "}")
                end += 1
            code += source[start:end] + "\n"
        code += "}\n"
    code += "}\n"
    (out / "ExtractedPresentation.cs").write_text(code, encoding="utf-8")
    (out / "presentation-source-hashes.json").write_text(json.dumps(hashes, indent=2), encoding="utf-8")


def main():
    dotnet = shutil.which("dotnet")
    if not dotnet:
        print("ERROR: .NET 8 SDK is required; these regressions were not run.")
        return 1
    extract_presentation()
    root = HERE.parents[2]
    sys.path.insert(0, str(root / 'tools'))
    from run_runtime_regressions import run_project_fixture
    failures = []
    for name in ("DailyReport", "Codex"):
        code, output = run_project_fixture(HERE / name / (name + ".csproj"),
            root / 'Build/content-third-review-fixes' / name / 'runs', HERE)
        print(output)
        if code:
            failures.append(name)
    if failures:
        print("FAIL: " + ", ".join(failures))
        return 1
    print("ContentThirdReviewFixes: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
