"""Execute the production cash / item bet ledger arithmetic against an in-memory wallet and store."""
from pathlib import Path
import re
import subprocess
import sys
from xml.sax.saxutils import escape

ROOT = Path(__file__).resolve().parents[3]
HERE = Path(__file__).resolve().parent
OUT = ROOT / 'Build/modeh-item-bet-ledger'
sys.path.insert(0, str(ROOT / 'tests'))
from cs_source_util import clean_source


def member(source, signature):
    assert source.count(signature) == 1, signature
    start = source.index(signature)
    opening = source.index('{', start)
    depth = 0
    for index in range(opening, len(source)):
        depth += (source[index] == '{') - (source[index] == '}')
        if depth == 0:
            return source[start:index + 1]
    raise AssertionError(signature)


def const_line(source, pattern):
    match = re.search(pattern, source)
    assert match, pattern
    return match.group(0)


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    service = clean_source((ROOT / 'ModeH/ModeHCashBetService.cs').read_text(encoding='utf-8-sig'))
    config = clean_source((ROOT / 'ModeH/ModeHConfig.cs').read_text(encoding='utf-8-sig'))

    # 生产常量逐行照抄（数值改了夹具跟着变，不另写一份）
    constants = [
        const_line(config, r'public const int MinOdds = \d+;'),
        const_line(config, r'public const int MaxOdds = \d+;'),
        const_line(config, r'public const int CashBetHouseCutPermille = \d+;'),
        const_line(config, r'public static readonly int\[\] CashBetAssumedWinPermilleByOdds = \{[^}]*\};'),
        const_line(config, r'public const int CashBetCalibrationMinSamples = \d+;'),
        const_line(config, r'public const int MinGameQuality = \d+;'),
        const_line(config, r'public const int MaxGameQuality = \d+;'),
        const_line(config, r'public const int ItemBetMaxPrizeItems = \d+;'),
    ]
    code = ('using System; using System.Collections.Generic; using System.Globalization; using System.Text;\n'
            'namespace BossRush {\n'
            'internal static class ModeHConfig {\n' + '\n'.join(constants) + '\n}\n')
    code += member(service, 'internal sealed class ModeHCashBetRecord') + '\n'
    code += member(service, 'internal sealed class ModeHItemBetEntry') + '\n'
    code += 'internal static partial class ModeHCashBetService {\n'
    for line in (r'internal const int StatusNone = \d+;', r'internal const int StatusReserved = \d+;',
                 r'internal const int StatusSettled = \d+;', r'internal const int StatusRefunded = \d+;',
                 r'internal const int KindCash = \d+;', r'internal const int KindItems = \d+;'):
        code += const_line(service, line) + '\n'
    code += member(service, 'internal static long ComputePayout(long stake, int odds)') + '\n'
    code += member(service, 'internal static int ResolveAssumedWinPermille(int tier)') + '\n'
    # 生产里是 private 嵌套类；夹具里放成 internal 以便直接驱动（成员逐字照抄）
    code += 'internal sealed partial class CashBetJournal {\n'
    for signature in (
            'internal bool TryReserveItems(string runId, int matchIndex, int odds, long value, string items, out string failureReasonId)',
            'internal bool TrySettle(string runId, int matchIndex, bool won, long lossCharge, long winCash, string prizes, out long payout)',
            'internal bool TryRefund(string context, out long refunded)',
            'private static int ReadSchema(string json)',
            'private static int ReadCompatibleSchema(string json)',
            'private static ModeHCashBetRecord Decode(string json)',
            'private static bool TryGetLong(BossRushJsonValue root, string key, out long value)',
            'private static void ReadTier(BossRushJsonValue root, string key, long[] target)',
            'private static string Encode(ModeHCashBetRecord record)',
            'private static string JoinTier(long[] values)'):
        code += member(service, signature) + '\n'
    code += '}\n}\n}\n'
    extracted = OUT / 'Production.cs'
    extracted.write_text(code, encoding='utf-8')

    files = [extracted, HERE / 'Program.cs', HERE / 'Stubs.cs',
             ROOT / 'Common/Data/BossRushJsonValue.cs', ROOT / 'Utilities/SimpleJsonHelper.cs']
    project = ('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType>'
               '<TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion>'
               '<EnableDefaultCompileItems>false</EnableDefaultCompileItems><Nullable>disable</Nullable>'
               '<NoWarn>CS0649;CS0169;CS0414</NoWarn></PropertyGroup><ItemGroup>')
    project += ''.join('<Compile Include="' + escape(str(p), {'"': '&quot;'}) + '" />' for p in files)
    project += '</ItemGroup></Project>'
    path = OUT / 'Regression.csproj'
    path.write_text(project, encoding='utf-8')
    return subprocess.call(['dotnet', 'run', '--project', str(path), '--configuration', 'Release'], cwd=ROOT)


if __name__ == '__main__':
    raise SystemExit(main())
