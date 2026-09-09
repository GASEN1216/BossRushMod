"""Execute the real independent-raid lease with controlled official-loader substitutes."""
from pathlib import Path
import subprocess
HERE=Path(__file__).resolve().parent
ROOT=HERE.parents[2]
OUT=ROOT/'Build'/'runtime-regressions'/'SkyIslandRaidLease'
result=subprocess.run(['dotnet','build',str(HERE/'Regression.csproj'),'--configuration','Release','--output',str(OUT/'bin'),'-p:BaseIntermediateOutputPath='+str(OUT/'obj')+'/'],cwd=ROOT)
if result.returncode: raise SystemExit(result.returncode)
raise SystemExit(subprocess.run(['dotnet',str(OUT/'bin'/'Regression.dll')],cwd=ROOT).returncode)
