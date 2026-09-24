"""天空岛交互提交与失效边界：逐字抽取生产方法，在确定性宿主替身上执行。

真实逻辑：面板 Show / BuildChoice / RunChoice / SetBodyText / Dispose，WorldStory
服务回调、下一步与谜题回执，苔药判据、奖励落点提交、StopBuzz、SetShape；
剧情规则、谜题状态和委托状态直接链接完整生产文件。
替身边界：Unity 对象/布局测量/渲染、经济/角色、物理落点和音频后端均为内存替身；
不证明字体实际排版、游戏帧时间、真实音效或存档 IO。不启动游戏、不接触玩家数据。
运行入口：tools/run_runtime_regressions.py --filter SkyIslandInteraction。
"""
from pathlib import Path
import hashlib
import json
import os
import re
import shutil
import subprocess

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
OUT = ROOT / "Build/runtime-regressions/SkyIslandInteraction"
SKY = "DebugAndTools/SkyIsland/"


def member(source, signature):
    """按成员的真实缩进取到闭括号，内部嵌套块不匹配；保留方法正文原字节内容。"""
    pattern = r"^([ \t]*)" + re.escape(signature)
    hits = list(re.finditer(pattern, source, re.M))
    if len(hits) != 1:
        raise AssertionError("成员锚点必须唯一: " + signature)
    hit = hits[0]
    opening = source.index("{", hit.end())
    end = re.search(r"^" + re.escape(hit.group(1)) + r"\}", source[opening:], re.M)
    if end is None:
        raise AssertionError("成员未闭合: " + signature)
    return source[hit.start():opening + end.end()]


def generate():
    sources = {}
    for name in ("SkyIslandStoryPresentation.cs", "SkyIslandWorldStory.cs", "SkyIslandWorldStoryServices.cs",
                 "SkyIslandServices.cs", "SkyIslandGnats.cs", "SkyIslandGroundRing.cs",
                 "SkyIslandPreludeFlow.cs", "SkyIslandResidents.cs"):
        sources[name] = (ROOT / SKY / name).read_text(encoding="utf-8-sig")
    # 2026-09-23：面板类超 1200 行，按 AGENTS §4.15 原样拆出 SkyIslandStoryPresentation_Parts.cs（同一 partial），接在后面照旧抽取。
    sources["SkyIslandStoryPresentation.cs"] += "\n" + (ROOT / SKY / "SkyIslandStoryPresentation_Parts.cs").read_text(encoding="utf-8-sig")
    presentation = sources["SkyIslandStoryPresentation.cs"]
    fields = presentation[presentation.index("        #region 布局常量"):presentation.index("        internal void Show(string title, string text, IList<Choice> choices)\n")]
    panel_methods = (
        "internal void Show(string title, string text, IList<Choice> choices,",
        "private void BuildChoice(", "private void RunChoice(", "private void SetBodyText(",
        "private void RestoreBottom(", "private void Register(", "private void Select(",
        "private void SetFocused(", "private static Color FocusColor(", "private static float FitTitleFont(",
        "private static float ChoiceLabelWidth", "public void Dispose()",
        "private static RectTransform MakeRect(", "private static TextMeshProUGUI MakeText(",
        "private static string KeepCountsTogether(",
        # 2026-09-23 审美审查：选项的纯显示修饰（图标 / 不够的数标红 / 次级导航）、手记正文排版、主动关闭的淡出交接。
        "private sealed class ChoiceLook", "internal static Choice WithItem(", "internal static Choice AsSecondary(",
        "private static ChoiceLook LookOf(", "internal void Close()", "private static void SettleEntrance(",
        "private static string MarkShortfalls(", "private static string StyleJournalLines(")
    counted = re.search(r"        private static readonly Regex CountedRun = new Regex\([\s\S]*?RegexOptions.CultureInvariant\);", presentation)
    if not counted:
        raise AssertionError("CountedRun 缺失")
    look_fields = []
    for pattern in (r"        private static readonly System\.Runtime\.CompilerServices\.ConditionalWeakTable<Choice, ChoiceLook> looks =[\s\S]*?;",
                    r"        private static readonly Regex HaveNeed = [^;]+;",
                    r"        private static readonly string DangerHex = [^;]+;",
                    r"        private const string JournalEntrySpacer = [^;]+;"):
        found = re.findall(pattern, presentation)
        if len(found) != 1:
            raise AssertionError("面板字段锚点必须唯一: " + pattern)
        look_fields.append(found[0])
    world = sources["SkyIslandWorldStory.cs"]
    world_services = sources["SkyIslandWorldStoryServices.cs"]
    service = sources["SkyIslandServices.cs"]
    constants = "\n".join(re.findall(r"        internal const [^;]+;", service))
    parts = ["using System;\nusing System.Collections.Generic;\nusing System.Reflection;\nusing System.Text.RegularExpressions;\nusing UnityEngine;\nusing UnityEngine.UI;\nusing TMPro;\nusing Duckov.Economy;\nusing BossRush.Utils;\nnamespace BossRush {\n"]
    parts.append("internal sealed partial class SkyIslandStoryPresentation {\n" + member(presentation, "internal sealed class Choice") + "\n" + fields)
    parts.extend(member(presentation, s) for s in panel_methods)
    parts.append(counted.group(0) + "\n" + "\n".join(look_fields) + "\n}\ninternal sealed partial class SkyIslandWorldStory {\n")
    parts.extend(member(world, s) for s in (
        "private string Refreshed(", "private void ServiceChoice(", "private string WithNextStep(",
        "private void Hint(", "private string NextStep()", "private void PuzzleChoices(",
        "private string PuzzleBody(", "internal void Hide()", "internal static string ResidentName("))
    parts.extend(member(world_services, s) for s in (
        "private string Repair()", "private string Heal()", "private string Meal()",
        "private void RepairChoice(", "private void HealChoice(", "private void MealChoice(", "private static string ServiceTag("))
    parts.append("}\n" + member(service, "internal enum SkyIslandServiceReadiness") + "\ninternal sealed partial class SkyIslandServices {\n" + constants)
    parts.extend(member(service, s) for s in (
        "internal static int HealPriceFor(", "internal SkyIslandServiceReadiness HealReadiness(",
        "private SkyIslandServiceReadiness EvaluateHeal(", "internal string Heal()", "internal string Meal(",
        "internal bool DropBountyReward("))
    parts.append("}\ninternal sealed partial class SkyIslandGnats {\n" + member(sources["SkyIslandGnats.cs"], "internal void StopBuzz()") + "\n}\n")
    ring = sources["SkyIslandGroundRing.cs"]
    segments = re.search(r"        internal const int Segments = \d+;", ring)
    if not segments:
        raise AssertionError("Segments 缺失")
    parts.append("internal static class SkyIslandGroundRing {\n" + segments.group(0) + "\n" + member(ring, "internal static void SetShape(") + "\n}\n}\n")
    # 语言刷新运行真实方法和字段，Unity UI 写入与语言管理器由显式替身观测。
    prelude, residents = (sources[n] for n in ("SkyIslandPreludeFlow.cs", "SkyIslandResidents.cs"))
    def field(source, name):
        found = re.findall(r"^        private [^;{}]*\b" + name + r"\b[^;{}]*;", source, re.M)
        if len(found) != 1:
            raise AssertionError("字段锚点必须唯一: " + name)
        return found[0]
    parts.append("namespace BossRush { internal sealed partial class SkyIslandResidents {\n" + field(residents, "owned") + "\n"
                 + field(residents, "namesChinese") + "\n" + member(residents, "private void RefreshLocalizedNames()") + "\n}\n")
    parts.append("internal sealed class SkyIslandPreludeFlow {\n"
                 + "\n".join(re.findall(r"^        (?:private|internal) const string [^;]+;", prelude, re.M))
                 + "\n" + member(prelude, "internal static void InjectLocalizations()") + "\n}\n}\n")
    helper_path = ROOT / "Integration/Utils/NPCNameTagHelper.cs"
    helper = helper_path.read_text(encoding="utf-8-sig")
    parts.append("namespace BossRush.Utils { internal static partial class NPCNameTagHelper {\n"
                 + member(helper, "private sealed class OriginalHealthBarEntry") + "\n"
                 + field(helper, "OriginalHealthBarEntriesByTransformId") + "\n"
                 + member(helper, "internal static bool UpdateOriginalHealthBarDisplayName(") + "\n}\n}\n")
    OUT.mkdir(parents=True, exist_ok=True)
    generated = OUT / "Production.cs"
    generated.write_text("\n".join(parts), encoding="utf-8-sig")
    linked = [ROOT / SKY / n for n in ("SkyIslandStoryRules.cs", "SkyIslandOfficialQuestTable.cs", "SkyIslandPuzzles.cs", "SkyIslandBounty.cs")]
    hashes = {SKY + n: hashlib.sha256((ROOT / SKY / n).read_bytes()).hexdigest() for n in sources}
    hashes.update({p.relative_to(ROOT).as_posix(): hashlib.sha256(p.read_bytes()).hexdigest() for p in linked})
    hashes[helper_path.relative_to(ROOT).as_posix()] = hashlib.sha256(helper_path.read_bytes()).hexdigest()
    (OUT / "source-hashes.json").write_text(json.dumps(hashes, indent=2), encoding="utf-8")
    return [generated, HERE / "Stubs.cs", HERE / "Host.cs", HERE / "Program.cs", HERE / "LocalizationRegression.cs"] + linked


def main():
    if os.name != "nt":
        raise SystemExit("SkyIslandInteraction requires Windows .NET Framework (no game process).")
    files = generate()
    dotnet = shutil.which("dotnet")
    if not dotnet:
        raise SystemExit(".NET SDK not found")
    sdk = subprocess.check_output([dotnet, "--list-sdks"], text=True).strip().splitlines()[-1]
    compiler = Path(sdk[sdk.index("[") + 1:sdk.index("]")]) / sdk.split()[0] / "Roslyn/bincore/csc.dll"
    framework = Path(os.environ.get("WINDIR", r"C:\Windows")) / "Microsoft.NET/Framework64/v4.0.30319"
    exe = OUT / "Regression.exe"
    args = ["/nologo", "/target:exe", "/langversion:7.3", "/nostdlib+", "/nowarn:0649,0414",
            '/out:"' + str(exe) + '"']
    args += ['/r:"' + str(framework / n) + '"' for n in ("mscorlib.dll", "System.dll", "System.Core.dll")]
    args += ['"' + str(p) + '"' for p in files]
    response = OUT / "compile.rsp"
    response.write_text("\n".join(args), encoding="utf-8-sig")
    code = subprocess.call([dotnet, str(compiler), "@" + str(response)], cwd=ROOT)
    if code:
        return code
    return subprocess.call([str(exe)], cwd=ROOT)


if __name__ == "__main__":
    raise SystemExit(main())
