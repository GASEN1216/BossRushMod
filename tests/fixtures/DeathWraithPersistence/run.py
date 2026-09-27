"""Execute verbatim production persistence members against an isolated typed store."""
from pathlib import Path
import hashlib
import json
import subprocess
import xml.sax.saxutils as xml

ROOT = Path(__file__).resolve().parents[3]
HERE = Path(__file__).resolve().parent
OUT = ROOT / "Build/runtime-regressions/DeathWraithPersistence"


def method(text, signature):
    start = text.index(signature)
    end = text.index("{", start) + 1
    depth = 1
    while depth:
        depth += (text[end] == "{") - (text[end] == "}")
        end += 1
    return text[start:end]


def main():
    paths = ["Integration/DeathWraith/DeathWraithLifecycleAndPersistence.cs", "Integration/DeathWraith/DeathWraithSystem.cs"]
    texts = [(ROOT / p).read_text(encoding="utf-8-sig") for p in paths]
    start = texts[1].index("private List<WraithInfo> _deathWraithListCache;")
    end = texts[1].index(";", texts[1].index("private const float DEATH_WRAITH_SAVE_DELAY", start)) + 1
    code = "using System; using System.Collections.Generic; using UnityEngine; using Saves; namespace BossRush { partial class DeathWraithRuntimeModule {\n"
    key_start = texts[1].index("private const string DEATH_WRAITH_LIST_SAVE_KEY")
    code += texts[1][key_start:texts[1].index(";", key_start) + 1] + "\n"
    code += texts[1][start:end] + "\n"
    for signature in (
        "private List<WraithInfo> LoadStoredDeathWraithInfos_DeathWraith(",
        "private void SaveStoredDeathWraithInfos_DeathWraith(",
        "private void MarkDeathWraithListDirty_DeathWraith(",
        "internal void FlushDeathWraithListIfDirty_DeathWraith(",
        "internal void UpdateDeferredDeathWraithSave_DeathWraith(",
        "private int GetStoredDeathWraithLimit_DeathWraith(",
        "private void AppendStoredDeathWraithInfo_DeathWraith(",
        "private void MergeStoredDeathWraithInfo_DeathWraith(",
        "private WraithInfo FindStoredDeathWraithInfoByRaidId_DeathWraith(",
        "private bool RemoveStoredDeathWraithInfoByRaidId_DeathWraith(",
        "internal void InvalidateStoredDeathWraithRecords_DeathWraith(",
    ):
        code += method(texts[0], signature) + "\n"
    code += method(texts[1], "internal void OnSetFile_DeathWraith(") + "\n}}"
    OUT.mkdir(parents=True, exist_ok=True)
    (OUT / "Production.cs").write_text(code, encoding="utf-8")
    includes = [HERE / "Program.cs", OUT / "Production.cs"]
    project = '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup><ItemGroup>'
    project += ''.join('<Compile Include="' + xml.escape(str(p), {'"': '&quot;'}) + '" />' for p in includes)
    project += '</ItemGroup></Project>'
    (OUT / "Regression.csproj").write_text(project, encoding="utf-8")
    hashes = {p: hashlib.sha256((ROOT / p).read_bytes()).hexdigest() for p in paths}
    hashes["extracted"] = hashlib.sha256(code.encode()).hexdigest()
    (OUT / "source-hashes.json").write_text(json.dumps(hashes, indent=2), encoding="utf-8")
    return subprocess.call(["dotnet", "run", "--project", str(OUT / "Regression.csproj"), "-c", "Release"], cwd=ROOT)


if __name__ == "__main__":
    raise SystemExit(main())
