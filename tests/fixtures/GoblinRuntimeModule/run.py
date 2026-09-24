"""Execute the production Goblin runtime module and its legacy host bridge."""
from pathlib import Path
import shutil
import subprocess
import sys

here = Path(__file__).resolve().parent
dotnet = shutil.which("dotnet")
if not dotnet:
    print("ERROR: .NET 8 SDK required; fixture was not run.")
    sys.exit(1)
sys.exit(subprocess.run(
    [dotnet, "run", "--project", str(here / "GoblinRuntimeModule.csproj"),
     "--configuration", "Release"],
    cwd=here).returncode)
