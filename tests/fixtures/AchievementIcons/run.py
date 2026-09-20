"""Execute the complete production achievement icon loader with a Unity resource double."""
from pathlib import Path
import subprocess
from xml.sax.saxutils import escape

ROOT = Path(__file__).resolve().parents[3]
HERE = Path(__file__).resolve().parent
OUT = ROOT / "Build/achievement-icons"


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    source = (ROOT / 'Integration/ItemFactory.cs').read_text(encoding='utf-8')
    def method(signature):
        start = source.index(signature)
        opening = source.index('{', start)
        depth = 1
        end = opening + 1
        while depth:
            depth += (source[end] == '{') - (source[end] == '}')
            end += 1
        return source[start:end]
    extracted = OUT / 'ItemFactory.cs'
    extracted.write_text('''using System; using System.IO; using System.Collections.Generic; using UnityEngine;
namespace BossRush { public static class ItemFactory {
private static Dictionary<string,Sprite> loadedSprites = new Dictionary<string,Sprite>();
private static HashSet<Sprite> ownedSprites = new HashSet<Sprite>();
private static HashSet<Texture2D> ownedTextures = new HashSet<Texture2D>();
private static Dictionary<string,AssetBundle> loadedAssetBundles = new Dictionary<string,AssetBundle>();
private static Dictionary<int,object> loadedItems = new Dictionary<int,object>();
private static HashSet<string> loadedBundles = new HashSet<string>();
private static Dictionary<int,object> itemConfigurators = new Dictionary<int,object>();
private static string modDirectory;
private static string GetModDirectory() { return ModBehaviour.GetModPath(); }
''' + method('public static Sprite GetSpriteFromFile(') + '\n' + method('public static void Shutdown()') + '\n}}', encoding='utf-8')
    paths = [ROOT / "Achievement/AchievementIconLoader.cs", ROOT / 'Integration/ProductionIconCache.cs', ROOT / 'Integration/RawImageLoader.cs', ROOT / 'MapSelection/MapThumbnailCache.cs', HERE / "Program.cs", HERE / "Stubs.cs", HERE / 'ResourceOwnershipTests.cs', extracted]
    project = '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup><ItemGroup>'
    project += ''.join('<Compile Include="' + escape(str(path), {'"': '&quot;'}) + '" />' for path in paths)
    project += '</ItemGroup></Project>'
    path = OUT / "AchievementIcons.csproj"
    path.write_text(project, encoding="utf-8")
    return subprocess.call(["dotnet", "run", "--project", str(path), "--configuration", "Release", "--", str(OUT / "runtime-data")], cwd=ROOT)


if __name__ == "__main__":
    raise SystemExit(main())
