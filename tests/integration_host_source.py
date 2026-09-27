"""Read exact production host regions after Integration compatibility consolidation."""
from pathlib import Path
import re


def host_region(root, name, carrier='Integration/IntegrationHostCompatibility.cs'):
    source = (Path(root) / carrier).read_text(encoding='utf-8-sig')
    matches = list(re.finditer(r'^[ \t]*#region ' + re.escape(name) + r'[ \t]*$', source, re.M))
    assert len(matches) == 1, 'missing/duplicate production host region: ' + name
    start = source.index('\n', matches[0].end()) + 1
    depth = 1
    for match in re.finditer(r'^[ \t]*#(region|endregion)\b[^\n]*', source[start:], re.M):
        depth += 1 if match.group(1) == 'region' else -1
        if depth == 0:
            return source[start:start + match.start()]
    raise AssertionError('unclosed production host region: ' + name)


def materialize_host(root, output, name, usings='', carrier='Integration/IntegrationHostCompatibility.cs'):
    output = Path(output)
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(usings + '\nnamespace BossRush { public partial class ModBehaviour {\n'
                      + host_region(root, name, carrier) + '\n}}\n', encoding='utf-8')
    return output
