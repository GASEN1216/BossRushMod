"""Execute production quest projection, Jeff bindings and guide observation with isolated hosts."""
from pathlib import Path
import hashlib
import os
import re
import subprocess
import sys
from xml.sax.saxutils import escape

ROOT = Path(__file__).resolve().parents[3]
HERE = Path(__file__).resolve().parent
OUT = ROOT / 'Build/jeff-quest-flow'


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    sys.path.insert(0, str(ROOT / 'tests'))
    from cs_source_util import clean_source
    source = clean_source((ROOT / 'ModeH/ModeHRuntimeModule_PreparedLoadouts.cs').read_text(encoding='utf-8-sig'))
    signature = 'internal bool HasCompletedMatch'
    assert source.count(signature) == 1
    start = source.index(signature)
    end = source.index('{', start) + 1
    depth = 1
    while depth:
        depth += (source[end] == '{') - (source[end] == '}')
        end += 1
    extracted = OUT / 'ModeHCompletion.cs'
    extracted.write_text('namespace BossRush { internal sealed partial class ModeHRuntimeModule {\n'
                         + source[start:end] + '\n}}', encoding='utf-8')
    paths = [ROOT / name for name in (
        'Utilities/OfficialQuests/OfficialQuestProjection.cs',
        'Utilities/OfficialQuests/OfficialQuestBinding.cs',
        'Utilities/OfficialQuests/OfficialQuestComponents.cs',
        'Utilities/OfficialQuests/OfficialQuestItemRules.cs', 'Campaign/CampaignRewardTable.cs',
        'Campaign/CampaignOfficialQuestClient.cs', 'Campaign/CampaignQuestTable.cs',
        'Campaign/CampaignGuideTable.cs', 'Campaign/CampaignGuideFacts.cs',
        'Campaign/CampaignContentCatalog.cs', 'Campaign/CampaignBaseObjectives.cs',
        'Campaign/CampaignModels.cs', 'Campaign/CampaignTuning.cs',
        'Common/Data/BossRushJsonValue.cs', 'Utilities/SimpleJsonHelper.cs',
        'ModeH/ModeHCanonicalDigest.cs', 'ModeH/ModeHSeedStream.cs',
    )] + [HERE / 'Program.cs', HERE / 'Stubs.cs', HERE / 'OfficialAssemblyContract.cs', extracted]
    project = '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion><EnableDefaultCompileItems>false</EnableDefaultCompileItems><NoWarn>0649;0067;0414</NoWarn></PropertyGroup><ItemGroup>'
    project += ''.join('<Compile Include="' + escape(str(p), {'"': '&quot;'}) + '" />' for p in paths)
    project += '</ItemGroup></Project>'
    target = OUT / 'Regression.csproj'
    target.write_text(project, encoding='utf-8')
    (OUT / 'source-hashes.txt').write_text('\n'.join(hashlib.sha256(p.read_bytes()).hexdigest() + ' ' + str(p.relative_to(ROOT)) for p in paths), encoding='utf-8')
    game = os.environ.get('GAME_PATH')
    if game:
        managed = Path(game) / 'Duckov_Data/Managed'
    else:
        response = (ROOT / 'Build/BossRush.rsp').read_text(encoding='utf-8-sig')
        managed = Path(re.search(r'/lib:"([^"]+)"', response).group(1))
    if not (managed / 'TeamSoda.Duckov.Core.dll').is_file():
        raise SystemExit('Set GAME_PATH to the installed game for read-only DLL contract checks')
    return subprocess.call(['dotnet', 'run', '--project', str(target), '--configuration', 'Release', '--', str(managed)], cwd=ROOT)


if __name__ == '__main__':
    raise SystemExit(main())
