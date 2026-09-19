"""Run production-linked content regressions; requires .NET 8 SDK, no game process."""
from pathlib import Path
import hashlib
import json
import re
import shutil
import subprocess
import sys

HERE = Path(__file__).resolve().parent


def extract_presentation():
    root = HERE.parents[2]
    out = root / "Build/content-third-review-fixes/DailyReport"
    out.mkdir(parents=True, exist_ok=True)
    paths = {
        "DailyReportView": ("Integration/DailyReport/DailyReportUI.cs", (
            "private sealed class PaperRow", "private void ReflowPaper()",
            "private void PinSignInContentToTop()",
            "private static float MeasureRowPart(", "private static void PlaceRowPart(",
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
            for constant in ("Margin", "PanelWidth", "PanelHeight"):
                declaration = re.search(r"private const float " + constant + r"\s*=\s*[0-9.]+f;", source)
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
    failures = []
    for name in ("DailyReport", "Codex"):
        result = subprocess.run(
            [dotnet, "run", "--project", str(HERE / name / (name + ".csproj")), "--configuration", "Release"],
            cwd=HERE,
        )
        if result.returncode:
            failures.append(name)
    if failures:
        print("FAIL: " + ", ".join(failures))
        return 1
    print("ContentThirdReviewFixes: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
