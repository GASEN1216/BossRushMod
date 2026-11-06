"""链接完整生产水体、世界索引和水域合并代码，使用真实发布遮罩做离线物理查询回归。"""
from pathlib import Path
import hashlib
import json
import os
import struct
import sys
from xml.sax.saxutils import escape

HERE = Path(__file__).resolve().parent
ROOT = Path(os.environ.get("BOSSRUSH_FIXTURE_SOURCE_ROOT", HERE.parents[2]))
OUT = Path(os.environ.get("BOSSRUSH_FIXTURE_OUT", ROOT / "Build/runtime-regressions/TidegateWater"))
sys.path.insert(0, str(ROOT / "tools"))
from run_runtime_regressions import run_project_fixture


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    export = ROOT / "ArtSource/Tidegate/export"
    manifest = json.loads((export / "manifest.json").read_text(encoding="utf-8"))["terrain"]
    width, height = manifest["width"], manifest["height"]
    pixels = (export / manifest["lagoonFile"]).read_bytes()
    if len(pixels) != width * height:
        raise SystemExit("发布遮罩长度不匹配")
    # 与 TidegateBuilder.BuildWater 相同：three.js 行翻转成 Unity z 由小到大；G 写入 RGHalf。
    # 源遮罩含抗锯齿中间值，先按 half 精度往返，再以 GetPixels 返回的 float 口径提供给生产代码。
    data = OUT / "lagoon-mask.bin"
    with data.open("wb") as stream:
        stream.write(struct.pack("<ii4f", width, height, manifest["x0"], -(manifest["z0"] + height - 1), width - 1, height - 1))
        for row in range(height - 1, -1, -1):
            for value in pixels[row * width:(row + 1) * width]:
                half_value = struct.unpack("<e", struct.pack("<e", value / 255.0))[0]
                stream.write(struct.pack("<f", half_value))
    production = [ROOT / "Tidegate/TidegateWater.cs", ROOT / "Tidegate/TidegateWorld.cs", ROOT / "Tidegate/TidegateWaterRegions.cs"]
    paths = production + [HERE / "Stubs.cs", HERE / "Program.cs"]
    project = OUT / "Regression.csproj"
    project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType>'
                       '<TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion>'
                       '<EnableDefaultCompileItems>false</EnableDefaultCompileItems><NoWarn>0649;0414</NoWarn>'
                       '</PropertyGroup><ItemGroup>'
                       + ''.join('<Compile Include="' + escape(str(p), {'"': '&quot;'}) + '" />' for p in paths)
                       + '</ItemGroup></Project>', encoding="utf-8")
    (OUT / "production-sha256.json").write_text(json.dumps({str(p.relative_to(ROOT)): hashlib.sha256(p.read_bytes()).hexdigest()
                                                            for p in production}, indent=2), encoding="utf-8")
    code, output = run_project_fixture(project, OUT, ROOT)
    print(output)
    return code


if __name__ == "__main__":
    raise SystemExit(main())
