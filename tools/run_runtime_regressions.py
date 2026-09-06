"""Aggregate production-linked regression fixtures without launching the game."""
from pathlib import Path
import argparse
from concurrent.futures import ThreadPoolExecutor, as_completed
import json
import os
import subprocess
import sys

ROOT = Path(__file__).resolve().parent.parent
SCRIPT_FIXTURES = (
    "RuntimeOwnership", "ContentTransactions", "ContentSecondReview", "AirdropSecondReview",
    "HarmonyBindingSecondReview", "ModeHReinforcementSecondReview", "modeh_effects",
    "ModeHThirdReviewFixes", "ContentThirdReviewFixes", "IntegrationThirdReviewFixes",
    "ContentBuildingOwnership",
)
PROJECT_FIXTURES = {
    "ReviewSeptember": "ReviewSeptember.csproj",
    "ModeHReviewFixes": "Review.csproj",
    "ModeHRecoverySecondReview": "Review.csproj",
}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--filter", default="", help="Run names containing this text")
    parser.add_argument("--jobs", type=int, default=3, help="Concurrent fixtures (default 3)")
    parser.add_argument("--list", action="store_true", help="List selected fixtures without running")
    args = parser.parse_args()
    names = sorted(name for name in (*SCRIPT_FIXTURES, *PROJECT_FIXTURES)
                   if args.filter.casefold() in name.casefold())
    if not names:
        parser.error("No matching fixtures")
    if args.list:
        print("\n".join(names))
        return 0
    out = ROOT / "Build/runtime-regressions"
    out.mkdir(parents=True, exist_ok=True)

    def run(name):
        here = ROOT / "tests/fixtures" / name
        if name in PROJECT_FIXTURES:
            command = ["dotnet", "run", "--project", str(here / PROJECT_FIXTURES[name]),
                       "--configuration", "Release",
                       "-p:BaseIntermediateOutputPath=" + str(out / name / "obj") + os.sep,
                       "-p:BaseOutputPath=" + str(out / name / "bin") + os.sep]
        else:
            command = [sys.executable, str(here / "run.py")]
        try:
            result = subprocess.run(command, cwd=ROOT, stdout=subprocess.PIPE,
                                    stderr=subprocess.STDOUT, timeout=600,
                                    env=dict(os.environ, PYTHONIOENCODING="utf-8", PYTHONUTF8="1"))
            output = result.stdout.decode("utf-8", errors="replace")
            code = result.returncode
        except (OSError, subprocess.TimeoutExpired) as error:
            code, output = 1, str(error)
        (out / (name + ".log")).write_text(output, encoding="utf-8")
        return name, code, output

    results = {}
    with ThreadPoolExecutor(max_workers=max(1, min(args.jobs, len(names)))) as pool:
        for future in as_completed([pool.submit(run, name) for name in names]):
            name, code, output = future.result()
            results[name] = code
            print(("PASS " if code == 0 else "FAIL ") + name, flush=True)
            if code:
                print(output[-5000:], flush=True)
    (out / "results.json").write_text(json.dumps(results, indent=2) + "\n", encoding="utf-8")
    failures = sorted(name for name, code in results.items() if code)
    print(f"Runtime regressions: {len(results) - len(failures)} PASS / {len(failures)} FAIL")
    if failures:
        print("Failed: " + ", ".join(failures))
    print("Logs: " + str(out))
    return 1 if failures else 0


if __name__ == "__main__":
    raise SystemExit(main())
