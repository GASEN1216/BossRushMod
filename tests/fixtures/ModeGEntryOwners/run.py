"""编译 Mode G 入口服务、旧宿主桥和真实状态/生成契约；不启动游戏。"""
from pathlib import Path
import hashlib
import json
import re
import subprocess
import sys
from xml.sax.saxutils import escape

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
OUT = ROOT / "Build/runtime-regressions/ModeGEntryOwners"


def member(source, signature):
    assert source.count(signature) == 1, signature
    start = source.index(signature)
    opening = source.index("{", start)
    depth = 0
    for i in range(opening, len(source)):
        depth += (source[i] == "{") - (source[i] == "}")
        if depth == 0:
            return source[start:i + 1]
    raise AssertionError(signature)


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    production = [ROOT / "ModeG" / name for name in (
        "ModeGEntry.cs", "ModeGEntryRuntimeServices.cs", "ModeGEntryHostBridge.cs",
        "ModeGRuntimeBridge.cs", "ModeGSpawnTransaction.cs",
        "ModeGRunState.cs", "ModeGStateModel.cs",
    )] + [ROOT / "Utilities/ManagedBossSpawnContracts.cs"]
    network_path = ROOT / "tests/fixtures/CoopProtocol/Stubs.cs"
    network = network_path.read_text(encoding="utf-8-sig")
    network = "using System; using System.Collections.Generic;\n" + network[network.index("namespace BossRush"):]
    network = network.replace("internal sealed class TestPeer", "internal sealed partial class TestPeer")
    network = network.replace("internal static class BossRushCoop", "internal static partial class BossRushCoop")
    network = network.replace("string json = BossRushCoopJson.ToJson(value);", "string json = BossRushCoopJson.ToJson(value); AuditWire.Add(new AuditPacket { Sender = source.Id, Target = id, Topic = topic, Json = json });")
    network = network.replace("Wire.Enqueue(() => Deliver", "if (AuditShouldDrop(value)) return true; Wire.Enqueue(() => Deliver")
    network = network.replace("Current = target;", "Current = target; AuditSelectNativeOwner(target);")
    network = network.replace("Current = saved;", "Current = saved; AuditSelectNativeOwner(saved);")
    network = network.replace("IDisposable Register(string topic", "IDisposable RegisterNetwork(string topic")
    generated_network = OUT / "Network.cs"
    generated_network.write_text(network, encoding="utf-8")
    files = [generated_network] + [ROOT / name for name in ("Utilities/CoopGameplayChannel.cs", "Utilities/CoopPersonalDelivery.cs",
        "Common/Coop/BossRushCoopJson.cs", "Common/Data/BossRushJsonValue.cs", "Utilities/SimpleJsonHelper.cs")]
    for path in production:
        generated = OUT / path.name
        # 所有生产方法体保留，只替换异步载体，不复制入口决策或回收算法。
        generated.write_text(path.read_text(encoding="utf-8-sig")
                             .replace("using Cysharp.Threading.Tasks;", "using System.Threading.Tasks;")
                             .replace("UniTask<", "Task<"), encoding="utf-8")
        files.append(generated)
    runtime_path = ROOT / "ModeG/ModeGRuntimeModule.cs"
    runtime = runtime_path.read_text(encoding="utf-8-sig")
    extracted = OUT / "RuntimeProduction.cs"
    extracted.write_text("using System; using System.Collections.Generic; namespace BossRush {\n"
                         + member(runtime, "internal sealed class ModeGBossSnapshot")
                         + "\ninternal sealed partial class ModeGRuntimeModule {\n"
                         + member(runtime, "public void Update(float deltaTime)")
                         + "\n" + member(runtime, "public override void OnUpdate(float deltaTime, float unscaledDeltaTime)")
                         + "\n}}", encoding="utf-8")
    production.append(runtime_path)
    coop_path = ROOT / "ModeG/ModeGCoopRuntime.cs"
    coop_source = coop_path.read_text(encoding="utf-8-sig")
    coop_generated = OUT / "CoopProduction.cs"
    coop_generated.write_text("using System; using System.Collections.Generic; using UnityEngine; namespace BossRush {\n"
        + "\n".join(member(coop_source, "internal sealed class " + name) for name in ("ModeGCoopActor", "ModeGCoopParticipant", "ModeGCoopWorld", "ModeGCoopCombat"))
        + "\ninternal sealed partial class ModeGEntryRuntime {\n"
        + coop_source[coop_source.index("        private static ModeGEntryRuntime coopOwner;"):coop_source.index("        internal static void PublishCoopCombat(")]
        + "\n" + "\n".join(member(coop_source, signature) for signature in (
            "private void DeliverOrdered(", "private bool ApplyCoopCombat(", "internal static bool PendingCoopCombat",
            "internal static void FinishCoopRun(", "internal static bool HasLeft(", "internal static bool CanApplyPersonalCombat"))
        + "\n}\ninternal sealed partial class ModeGRuntimeModule {\n"
        + "\n".join(member(coop_source, signature) for signature in (
            "private void FreezeCoopAsset()", "internal string GetCoopParticipantAsset(", "internal string CoopRunAsset", "internal string CoopRunRoom",
            "internal bool IsCoopAssetCurrent", "internal bool CheckCoopAsset()", "internal bool CoopAdmissionReady", "internal bool ApplyCoopCombat(",
            "internal long CoopRequestedAdmissionBaseline", "internal bool CoopRunWasClient", "internal bool CoopRunHasWorldAuthority", "internal void FreezeCoopAdmission(", "private void DriveCoopGuest()"))
        + "\n" + member((ROOT / "ModeG/ModeGRuntimeModule_PublicApiAndShutdown.cs").read_text(encoding="utf-8-sig"), "private void RefundStartupPaymentOnTechnicalFailure(")
        + "\n" + member((ROOT / "ModeG/ModeGRuntimeModule_PublicApiAndShutdown.cs").read_text(encoding="utf-8-sig"), "public void DisarmStartupRefund()")
        + "\n}}", encoding="utf-8")
    files.append(coop_generated)
    production.extend([coop_path, network_path])
    lifecycle_path = ROOT / "ModeG/ModeGRuntimeModule_PublicApiAndShutdown.cs"
    facade_path = ROOT / "Common/Coop/BossRushCoop.cs"
    coop_runtime_path = ROOT / "Common/Coop/BossRushCoopRuntimeModule.cs"
    registration_path = ROOT / "ModBehaviourRuntimeModules.cs"
    registration_source = registration_path.read_text(encoding="utf-8-sig")
    registration = member(registration_source, "private void RegisterRuntimeModules()")
    field_types = dict((name, kind) for kind, name in re.findall(r"(?:readonly\s+)?(\w+RuntimeModule)\s+(\w+)", registration_source))
    field_types.update(dict((name, kind) for name, kind in re.findall(r"(\w+)\s*=\s*new\s+(\w+RuntimeModule)\(", registration)))
    calls = []
    for line in registration.splitlines():
        call = re.search(r"runtimeModuleHost\.Register\((.+)\);", line)
        if call:
            argument = call.group(1)
            kind = re.search(r"new\s+(\w+)\(", argument).group(1) if argument.startswith("new ") else field_types[argument]
            replacement = "new ModeGRuntimeModule()" if kind == "ModeGRuntimeModule" else 'new RecordingModule("' + kind + '")'
            calls.append("runtimeModuleHost.Register(" + replacement + ");")
        elif "BossRushCoop.RegisterModules(runtimeModuleHost);" in line:
            calls.append("BossRushCoop.RegisterModules(runtimeModuleHost);")
        elif "GetModeGEntryRuntimeForHost().EnsureCoop();" in line:
            # 保存生产回归：若再次在收集列表时初始化，真实 facade.Register 会当场拒绝。
            calls.append("owner.GetModeGEntryRuntimeForHost().EnsureCoop();")
    facade = facade_path.read_text(encoding="utf-8-sig")
    coop_registrations = member(facade, "internal static void RegisterModules(")
    coop_kinds = re.findall(r"host.Register\(new (\w+)\(\)\);", coop_registrations)
    for kind in coop_kinds:
        if kind != "BossRushCoopRuntimeModule":
            coop_registrations = coop_registrations.replace("new " + kind + "()", 'new RecordingModule("' + kind + '")')
    assembly = OUT / "AssemblyProduction.cs"
    assembly.write_text("using System; namespace BossRush {\ninternal static partial class BossRushCoop {\n"
        + coop_registrations + "\n" + member(facade, "public static IDisposable Register(")
        + "\n}\ninternal sealed partial class BossRushCoopRuntimeModule {\n"
        + member(coop_runtime_path.read_text(encoding="utf-8-sig"), "public override void OnAwake(ModBehaviour owner)")
        + "\n}\ninternal sealed partial class ModeGRuntimeModule {\n"
        + member(lifecycle_path.read_text(encoding="utf-8-sig"), "public override void OnAwake(ModBehaviour owner)")
        + "\n}\ninternal static partial class AssemblyOrder {\ninternal const int ProductionModuleCount = "
        + str(len(calls) - 1 + len(coop_kinds)) + ";\n"
        + "private static void RegisterProductionModuleList(BossRushRuntimeModuleHost runtimeModuleHost, ModBehaviour owner) {\n"
        + "\n".join(calls) + "\n}\n}}", encoding="utf-8")
    files += [assembly, HERE / "AssemblyOrder.cs"]
    for name in ("BossRushRuntimeModuleBase.cs", "IBossRushRuntimeModule.cs", "BossRushRuntimeModuleHost.cs"):
        files.append(ROOT / "Common/Lifecycle" / name)
    production += [lifecycle_path, facade_path, coop_runtime_path, registration_path]
    files += [extracted, HERE / "Stubs.cs", HERE / "Program.cs"]
    (OUT / "production-source-sha256.json").write_text(json.dumps({
        str(p.relative_to(ROOT)): hashlib.sha256(p.read_bytes()).hexdigest() for p in production
    }, indent=2), encoding="utf-8")
    project = '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType>'
    project += '<TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion>'
    project += '<EnableDefaultCompileItems>false</EnableDefaultCompileItems><NoWarn>0649</NoWarn>'
    project += '</PropertyGroup><ItemGroup>'
    project += ''.join('<Compile Include="' + escape(str(p), {'"': '&quot;'}) + '" />' for p in files)
    project += '</ItemGroup></Project>'
    path = OUT / "ModeGEntryOwners.csproj"
    path.write_text(project, encoding="utf-8")
    sys.path.insert(0, str(ROOT / "tools"))
    from run_runtime_regressions import run_project_fixture
    code, output = run_project_fixture(path, OUT / "runs", ROOT)
    print(output)
    return code


if __name__ == "__main__":
    raise SystemExit(main())
