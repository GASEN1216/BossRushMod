"""编译 Mode G 入口服务、旧宿主桥和真实状态/生成契约；不启动游戏。"""
from pathlib import Path
import hashlib
import json
import subprocess
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
    files = []
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
    return subprocess.call(["dotnet", "run", "--project", str(path), "--configuration", "Release"], cwd=ROOT)


if __name__ == "__main__":
    raise SystemExit(main())
