"""Steam lookup caches belong to a standalone helper; the old entry only forwards."""
from pathlib import Path
import re
from cs_source_util import clean_source
from ArchitectureStructureGuard import extract_method_body

ROOT = Path(__file__).resolve().parents[1]
source = clean_source((ROOT / "Utilities/SteamHelper.cs").read_text(encoding="utf-8-sig"))
bridge = clean_source((ROOT / "Utilities/Utilities.cs").read_text(encoding="utf-8-sig"))
assert "internal static class SteamPlatformInfo" in source and "partial class ModBehaviour" not in source, "Steam helper must own its static state"
for name in ("steam_getPersonaNameMethod", "steam_getSteamDisplayMethod", "steamPersonaLookupLogged", "steamPersonaInvokeLogged", "steamDisplayLookupLogged", "steamDisplayInvokeLogged"):
    assert re.search(r"private static \w+ " + name + r";", source), "Steam cache lifetime must remain static: " + name
body = extract_method_body(bridge, "internal static string TryGetSteamPersonaName()")
assert re.sub(r"\s+", "", body) == "{returnSteamPlatformInfo.TryGetSteamPersonaName();}", "published Steam entry must forward without another lookup/cache"
print("SteamPlatformInfoOwnershipGuard: PASS")
