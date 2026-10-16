"""Build an extra actual-mode + complete CoopPersonalDelivery chain after the main fixture."""
from pathlib import Path
from xml.sax.saxutils import escape
import hashlib
import json


def run(root, here, out, generated, zombie=False):
    from run_runtime_regressions import run_project_fixture
    folder = out / "real-delivery-chain"
    folder.mkdir(parents=True, exist_ok=True)
    original = (here / ("Program.cs" if zombie else "Stubs.cs")).read_text(encoding="utf-8-sig")
    text = original.replace("internal static class BossRushCoop\n", "internal static partial class BossRushCoop\n")
    if zombie:
        assert text != original
        text = text.replace("internal readonly CharacterMainControl Character", "internal bool IsLocal {get{return Id==BossRushCoop.LocalPlayerId;}}\n        internal readonly CharacterMainControl Character")
        text = text.replace("public static int Max(int a, int b)", "public static float Max(float a,float b){return Math.Max(a,b);} public static int Max(int a, int b)")
        text = text.replace("private readonly FakeChannel coopZombieWorld=new FakeChannel(),coopZombiePersonal=new FakeChannel();", "private readonly FakeChannel coopZombieWorld=new FakeChannel(); private readonly RealPersonal coopZombiePersonal=new RealPersonal();")
        text = text.replace("private void TryLoadBaseSceneAfterZombieModeExtraction() { }", "private void TryLoadBaseSceneAfterZombieModeExtraction() { NativeReturns++; }")
        text = text.replace("ZombieModeRuntimeModule.Run();", "ZombieModeRuntimeModule.RunDeliveryChain();")
        kind, field, dto = "RealPersonal", "Receipt", "CoopZombiePersonal"
    else:
        # F facade lives on one line, so the declaration has a different anchor.
        text = text.replace("internal static class BossRushCoop {", "internal static partial class BossRushCoop {")
        start = text.index("    internal sealed class FakePersonal {")
        end = text.index("    internal sealed class FakeChannel", start)
        text = text[:start] + text[end:]
        kind, field, dto = "FakePersonal", "Payload", "CoopModeFPersonal"
    wrapper = '''
namespace BossRush {
internal sealed class KIND {
 internal sealed class Entry {internal string Player,Id,Asset;internal DTO FIELD;}
 internal readonly System.Collections.Generic.List<Entry> Pending=new System.Collections.Generic.List<Entry>();
 internal CoopPersonalDelivery<DTO> Actual;
 internal void Bind(System.Func<DTO,bool> apply) {Actual=new CoopPersonalDelivery<DTO>("Chain",apply);}
 internal bool HasPendingForIdPrefix(string prefix) {return Actual.HasPendingForIdPrefix(prefix);}
 internal void Tick(float dt) {if(Actual!=null)Actual.Tick(dt);}
 internal bool Deliver(string player,string id,DTO receipt,string asset=null) {
   if(Actual==null)throw new System.Exception("personal production delivery must be bound");
   Pending.Add(new Entry {Player=player,Id=id,Asset=asset,FIELD=receipt});return Actual.Deliver(player,id,receipt,asset);
 }
}}
'''.replace("KIND", kind).replace("DTO", dto).replace("FIELD", field)
    (folder / "Adapters.cs").write_text(text + wrapper, encoding="utf-8")
    common = root / "tests/fixtures/ModeFCoop"
    sources = [generated, folder / "Adapters.cs", common / "DeliveryWire.cs", here / "DeliveryChain.cs", root / "Utilities/CoopPersonalDelivery.cs", root / "Common/Coop/BossRushCoopJson.cs", root / "Common/Data/BossRushJsonValue.cs", root / "Utilities/SimpleJsonHelper.cs"]
    if zombie:
        sources += [out / "Extraction.cs", out / "WaitingUi.cs", out / "Localization.cs", here / "WaitingUiBoundaries.cs"]
    includes = "".join('<Compile Include="' + escape(str(p), {'"': '&quot;'}) + '" />' for p in sources)
    project = folder / "Regression.csproj"
    defines = "<DefineConstants>ZOMBIE_DELIVERY</DefineConstants>" if zombie else ""
    project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion><EnableDefaultCompileItems>false</EnableDefaultCompileItems><NoWarn>0649;0414</NoWarn>' + defines + '</PropertyGroup><ItemGroup>' + includes + '</ItemGroup></Project>', encoding="utf-8")
    code, output = run_project_fixture(project, folder / "runs", root)
    (folder / "execution.log").write_text(output, encoding="utf-8")
    (folder / "manifest.json").write_text(json.dumps({"exit": code, "sources": {str(p): hashlib.sha256(p.read_bytes()).hexdigest() for p in sources}, "boundary": "Real mode Complete/Tick/Apply and complete PersonalDelivery; in-memory wire and native load counter, no real scene load."}, ensure_ascii=False, indent=2), encoding="utf-8")
    return code, output
