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
           ("private bool EnsureStory()", "private void CloseStory()", "internal void Schedule()",
            "private bool ShouldRunObjective()")]
generated = OUT / "PreludeGenerated.cs"
# 导航只抽取真正决定地图/罗盘目标的生产迭代器，不在替身里另写任务状态树。
marker_path = ROOT / "DebugAndTools/SkyIsland/SkyIslandMapMarkers.cs"
marker_source = marker_path.read_text(encoding="utf-8-sig")
marker_methods = [method(marker_source, signature) for signature in
                  ("internal static IEnumerable<string> ObjectiveTargets(", "internal static IEnumerable<string> SideTargets(")]
world_path = ROOT / "DebugAndTools/SkyIsland/SkyIslandWorldStory.cs"
world_source = world_path.read_text(encoding="utf-8-sig")
# 掩码和初始化值也来自生产文件；否则生产删掉一项，测试还在用自己的正确常量会假绿。
import re
feedback_fields = []
for name in ("feedbackChinese", "feedbackFlags", "FeedbackFlags"):
    found = re.findall(r"^        private (?:const )?\w+ " + name + r"\b[^;]*;", world_source, re.M)
    assert len(found) == 1, name
    feedback_fields.extend(found)
generated.write_text("using System; using System.Collections.Generic; using UnityEngine; using UnityEngine.SceneManagement;\n"
                     "namespace BossRush { internal sealed partial class SkyIslandPreludeFlow {\n"
                     + "\n".join(methods) + "\n}\n"
                     + "internal static class SkyIslandMapMarkers {\n" + "\n".join(marker_methods) + "\n}\n"
                     + "internal sealed partial class SkyIslandFeedbackHarness {\n" + "\n".join(feedback_fields)
                     + "\n" + method(world_source, "private void RebuildFeedback()") + "\n} }\n", encoding="utf-8")
(OUT / "navigation-feedback-source-sha256.txt").write_text("\n".join(
    str(path.relative_to(ROOT)) + " " + hashlib.sha256(path.read_bytes()).hexdigest()
    for path in (marker_path, world_path)), encoding="utf-8")
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
