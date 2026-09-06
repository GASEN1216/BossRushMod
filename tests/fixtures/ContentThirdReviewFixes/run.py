"""Run production-linked content regressions; requires .NET 8 SDK, no game process."""
from pathlib import Path
import shutil
import subprocess
import sys

HERE = Path(__file__).resolve().parent


def main():
    dotnet = shutil.which("dotnet")
    if not dotnet:
        print("ERROR: .NET 8 SDK is required; these regressions were not run.")
        return 1
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
