"""Extract the complete production player declaration without rewriting its predicate."""
import hashlib
import re
import sys
from pathlib import Path

root = Path(__file__).resolve().parents[3]
sys.path.insert(0, str(root / "tests"))
from cs_source_util import clean_source

source = root / "Common/Coop/BossRushCoop.cs"
raw = source.read_bytes()
text = raw.decode("utf-8-sig")
anchor = "internal sealed class BossRushCoopPlayer"
assert text.count(anchor) == 1, "Production player declaration must be unique"
start = text.index(anchor)
brace = text.index("{", start)
# The declaration currently has no brace-containing literals. Fail on that change
# rather than silently extracting only part of a future implementation.
clean = clean_source(text[start:])
assert clean.count("{") >= 1
depth = 0
end = None
for index in range(brace, len(text)):
    if text[index] == "{":
        depth += 1
    elif text[index] == "}":
        depth -= 1
        if depth == 0:
            end = index + 1
            break
assert end is not None
declaration = text[start:end]
assert not re.search(r'"[^"\n]*[{}][^"\n]*"', clean_source(declaration)), "Use a lexical extractor for brace-containing literals"
out = Path(sys.argv[1]).resolve()
out.parent.mkdir(parents=True, exist_ok=True)
facade = []
for name in ("IsConnected", "IsHost", "IsClient", "HasWorldAuthority", "SessionReady", "HasBoundScene"):
    matches = re.findall(r"^\s*(?:public|internal) static bool " + name + r"\s*\{[^\r\n]+\r?$", text, re.M)
    assert len(matches) == 1, name
    facade.append(matches[0])
out.write_text("// Production declaration SHA256 " + hashlib.sha256(raw).hexdigest() +
               "\nusing System;\nnamespace BossRush {\n" + declaration + "\ninternal static partial class BossRushCoop {\n" + "\n".join(facade) + "\n}\n}\n", encoding="utf-8")
print("ProductionPlayer source=" + str(source) + " SHA256=" + hashlib.sha256(raw).hexdigest())
