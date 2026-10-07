"""执行完整生产调度器，并在按字节快照的隔离副本上做反向变异。"""
from pathlib import Path
import hashlib
import sys
import tempfile
from xml.sax.saxutils import quoteattr

sys.dont_write_bytecode = True
HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
sys.path.insert(0, str(ROOT / "tools"))
from run_runtime_regressions import run_project_fixture

PRODUCTION = ROOT / "SkyIsland/SkyIslandPatrolSchedule.cs"
NAVIGATION = ROOT / "SkyIsland/SkyIslandPatrols.cs"
OUT = ROOT / "Build/runtime-regressions/SkyIslandPatrols"


def digest(data):
    return hashlib.sha256(data).hexdigest()


def navigation_method():
    text = NAVIGATION.read_text(encoding="utf-8-sig")
    signature = "private void ConfigureNavigation(CharacterMainControl character)"
    if text.count(signature) != 1:
        raise AssertionError("Navigation production method anchor must be unique")
    start = text.index(signature)
    end = text.index("{", start) + 1
    depth = 1
    while depth:
        depth += (text[end] == "{") - (text[end] == "}")
        end += 1
    return text[start:end]


def project(path, source, navigation):
    sources = (source, navigation, HERE / "Host.cs", HERE / "Program.cs", HERE / "NavigationRegression.cs")
    path.write_text(
        '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType>'
        '<TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion><RollForward>Major</RollForward>'
        '<AssemblyName>SkyIslandPatrolRegression</AssemblyName>'
        '<EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup><ItemGroup>'
        + ''.join('<Compile Include=' + quoteattr(str(item)) + ' />' for item in sources)
        + '</ItemGroup></Project>', encoding="utf-8")


# 每个锚点恰好一次；断言标识来自执行结果，不能以文本替换成功充当反向验证通过。
MUTATIONS = (
    ("reserve_budget", "activeCount + pendingCount < activeLimit;",
     "activeCount + pendingCount <= activeLimit;", "ASSERT[capacity_reservation]"),
    ("activate_budget", "activeCount + pendingCount >= activeLimit) return false;",
     "activeCount + pendingCount > activeLimit) return false;", "ASSERT[capacity_reactivation]"),
    ("one_pending", "pendingCount == 0 &&", "pendingCount <= 1 &&", "ASSERT[single_pending]"),
    ("stale_generation", "generation == this.generation && states[i] == SlotState.Pending;",
     "true && states[i] == SlotState.Pending;", "ASSERT[stale_token]"),
    ("duplicate_completion", "generation == this.generation && states[i] == SlotState.Pending;",
     "generation == this.generation && (states[i] == SlotState.Pending || states[i] == SlotState.Active);", "ASSERT[duplicate_completion]"),
    ("closed_reservation", "return !closed && Valid(i) && states[i] == SlotState.Unused",
     "return Valid(i) && states[i] == SlotState.Unused", "ASSERT[closed_rejects_work]"),
    ("terminal_failure", "retryable ? SlotState.Unused : SlotState.Defeated;",
     "SlotState.Unused;", "ASSERT[terminal_failure]"),
)


def main():
    original = PRODUCTION.read_bytes()
    fingerprint = digest(original)
    OUT.mkdir(parents=True, exist_ok=True)
    isolation = Path(tempfile.mkdtemp(prefix="snapshot-", dir=OUT)).resolve()
    source = isolation / "SkyIslandPatrolSchedule.cs"
    source.write_bytes(original)
    method = navigation_method()
    navigation = isolation / "Navigation.Extracted.cs"
    navigation.write_text("using System; using Pathfinding; namespace BossRush { internal sealed partial class SkyIslandPatrols {\n"
                          + method + "\n} }\ninternal static partial class Program { static partial void CheckNavigation() { NavigationRegression.Run(); } }",
                          encoding="utf-8")
    csproj = isolation / "Regression.csproj"
    project(csproj, source, navigation)
    print("Production snapshot SHA-256: " + fingerprint, flush=True)
    try:
        code, log = run_project_fixture(csproj, isolation / "baseline", ROOT)
        (isolation / "baseline.log").write_text(log, encoding="utf-8")
        if code != 0 or "SkyIslandPatrols: PASS" not in log:
            raise AssertionError("Production baseline failed:\n" + log)
        print(log, flush=True)
        anchor = "GetComponentInChildren<AICharacterController>(true)"
        if method.count(anchor) != 1:
            raise AssertionError("Inactive AI navigation mutation anchor must be unique")
        before_navigation = navigation.read_bytes()
        navigation.write_bytes(before_navigation.replace(anchor.encode(), b"GetComponentInChildren<AICharacterController>()", 1))
        try:
            code, log = run_project_fixture(csproj, isolation / "inactive_ai", ROOT)
            (isolation / "inactive_ai.log").write_text(log, encoding="utf-8")
            if code == 0 or "ASSERT[inactive_child_navigation]" not in log or "Execute fresh TargetPath:" not in log:
                raise AssertionError("Inactive AI mutation did not fail at production navigation: \n" + log)
            print("MUTATION inactive_ai: rejected by ASSERT[inactive_child_navigation]", flush=True)
        finally:
            navigation.write_bytes(before_navigation)
            if navigation.read_bytes() != before_navigation:
                raise AssertionError("Extracted navigation was not restored byte-for-byte")
        for name, anchor, replacement, expected in MUTATIONS:
            before = anchor.encode("utf-8")
            if original.count(before) != 1:
                raise AssertionError("Mutation anchor is not unique: " + name)
            source.write_bytes(original.replace(before, replacement.encode("utf-8"), 1))
            try:
                code, log = run_project_fixture(csproj, isolation / name, ROOT)
                (isolation / (name + ".log")).write_text(log, encoding="utf-8")
                if code == 0 or expected not in log or "Execute fresh TargetPath:" not in log:
                    raise AssertionError("Mutation did not fail in the intended behavior: " + name + "\n" + log)
                print("MUTATION " + name + ": rejected by " + expected, flush=True)
            finally:
                source.write_bytes(original)
                if source.read_bytes() != original or digest(source.read_bytes()) != fingerprint:
                    raise AssertionError("Isolated source was not restored byte-for-byte: " + name)
        print("SkyIslandPatrols: PASS (8 non-equivalent reverse mutations; isolated source restored SHA-256="
              + fingerprint + ")", flush=True)
        print("Isolated execution logs: " + str(isolation), flush=True)
    finally:
        source.write_bytes(original)
        if PRODUCTION.read_bytes() != original:
            raise AssertionError("Production source changed during isolated verification")


if __name__ == "__main__":
    main()
