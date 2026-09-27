"""Exercise the actual aggregate build/execute implementation with stale outputs."""
from pathlib import Path
import sys
import tempfile

ROOT = Path(__file__).resolve().parents[3]
sys.path.insert(0, str(ROOT / "tools"))
from run_runtime_regressions import run_command, run_project_fixture


def main():
    output = ROOT / "Build/runtime-runner-self-test"
    output.mkdir(parents=True, exist_ok=True)
    here = Path(tempfile.mkdtemp(prefix="probe-", dir=output))
    project = here / "DifferentProjectName.csproj"
    project.write_text('''<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net8.0</TargetFramework><OutputType>Exe</OutputType>
    <AssemblyName>ActualFixtureAssembly</AssemblyName><EnableDefaultCompileItems>false</EnableDefaultCompileItems>
  </PropertyGroup><ItemGroup><Compile Include="Program.cs" /></ItemGroup>
</Project>''', encoding="utf-8")
    source = here / "Program.cs"
    source.write_text('System.Console.WriteLine("stale-success"); return 0;', encoding="utf-8")
    code, log = run_command(["dotnet", "build", str(project), "-c", "Release", "--nologo"], ROOT)
    assert code == 0, log
    default_dll = here / "bin/Release/net8.0/ActualFixtureAssembly.dll"
    stale = default_dll.read_bytes()
    source.write_text('System.Console.WriteLine("fresh-success"); return 0;', encoding="utf-8")
    code, log = run_project_fixture(project, here / "isolated", ROOT)
    assert code == 0 and "fresh-success" in log and "stale-success" not in log, log
    print("PASS execute current build with non-default AssemblyName and existing default bin/obj")
    source.write_text('System.Console.WriteLine("fresh-failure"); return 23;', encoding="utf-8")
    code, log = run_project_fixture(project, here / "isolated", ROOT)
    assert code == 23 and "fresh-failure" in log and "stale-success" not in log, log
    assert default_dll.read_bytes() == stale, "default outputs must be left untouched"
    print("PASS stale default and previous isolated successes cannot hide new failure")
    source.write_text("deliberately invalid C#", encoding="utf-8")
    code, log = run_project_fixture(project, here / "isolated", ROOT)
    assert code != 0 and "Execute fresh TargetPath:" not in log, log
    print("PASS failed build never executes a previous binary")
    print("RuntimeRegressionRunner: PASS (3 execution cases)")


if __name__ == "__main__":
    main()
