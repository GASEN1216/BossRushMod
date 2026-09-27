"""Run the production shared modal lease with two consumer entry points."""
from pathlib import Path
import subprocess

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]


def generate():
    shared = (ROOT / "Common/UI/BossRushUIFoundation.cs").read_text(encoding="utf-8-sig")
    zombie = (ROOT / "ZombieMode/ZombieModeUIHelper.cs").read_text(encoding="utf-8-sig")
    shared_body = shared[shared.index("        private static int _modalInputLeaseCount;"):
                         shared.index("        /// <summary>\n        /// 获取游戏 TMP 字体资产")]
    wrapper_body = zombie[zombie.index("        // 旧调用方保留 API"):
                          zombie.index("        internal static TMP_FontAsset GetGameFont()")]
    production = ("using System; using UnityEngine; using Duckov.UI;\n"
                  "internal static class BossRushUIKit {\n" + shared_body + "\n}\n"
                  "internal static class ZombieModeUIHelper {\n" + wrapper_body + "\n}\n")
    target = ROOT / "Build/shared-modal-input-fixture"
    target.mkdir(parents=True, exist_ok=True)
    (target / "Production.cs").write_text(production, encoding="utf-8")
    return target


if __name__ == "__main__":
    target = generate()
    result = subprocess.call([
        "dotnet", "build", str(HERE / "SharedModalInput.csproj"), "--configuration", "Release", "--nologo",
        "-p:BaseIntermediateOutputPath=" + str(target / "obj") + "/",
        "-p:BaseOutputPath=" + str(target / "bin") + "/",
    ], cwd=ROOT)
    if result:
        raise SystemExit(result)
    raise SystemExit(subprocess.call(["dotnet", str(target / "bin/Release/net8.0/SharedModalInput.dll")], cwd=ROOT))
