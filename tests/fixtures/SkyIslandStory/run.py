"""编译真实剧情、编解码与共享存档逻辑，宿主和磁盘由内存替身隔离。"""
from pathlib import Path
import subprocess
import hashlib

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
OUT = ROOT / 'Build' / 'runtime-regressions' / 'SkyIslandStory'
# 与 SkyIslandDelivery 同口径逐字抽取生产方法，不在替身里重写初始化算法。
def method(source, signature):
    if source.count(signature) != 1:
        raise ValueError("Non-unique production method: " + signature)
    start = source.index(signature)
    opening = source.index("{", start)
    depth = 0
    for i in range(opening, len(source)):
        if source[i] == "{": depth += 1
        elif source[i] == "}":
            depth -= 1
            if depth == 0: return source[start:i + 1]
    raise ValueError(signature)

OUT.mkdir(parents=True, exist_ok=True)
source_path = ROOT / "DebugAndTools/SkyIsland/SkyIslandPreludeFlow.cs"
source = source_path.read_text(encoding="utf-8-sig")
methods = [method(source, signature) for signature in
           ("private bool EnsureStory()", "private void CloseStory()", "internal void Schedule()")]
generated = OUT / "PreludeGenerated.cs"
generated.write_text("using System; using UnityEngine; using UnityEngine.SceneManagement;\n"
                     "namespace BossRush { internal sealed partial class SkyIslandPreludeFlow {\n"
                     + "\n".join(methods) + "\n} }\n", encoding="utf-8")
(OUT / "prelude-source-sha256.txt").write_text(hashlib.sha256(source_path.read_bytes()).hexdigest() + "\n", encoding="utf-8")
build = subprocess.run([
    'dotnet', 'build', str(HERE / 'Regression.csproj'), '--configuration', 'Release',
    '--output', str(OUT / 'bin'),
    '-p:BaseIntermediateOutputPath=' + str(OUT / 'obj') + '/',
    '-p:SkyQuestGeneratedSource=' + str(generated),
], cwd=ROOT)
if build.returncode:
    raise SystemExit(build.returncode)
raise SystemExit(subprocess.run(['dotnet', str(OUT / 'bin' / 'Regression.dll')], cwd=ROOT).returncode)
