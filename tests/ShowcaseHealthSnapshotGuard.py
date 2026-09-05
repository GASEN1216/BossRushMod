"""Full-health eligibility must be measured before removing the existing max-HP bonus."""
from pathlib import Path
import re
import sys

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "Integration/BackMountain/ShowcaseService.cs"


def validate(source):
    method = source[source.index("internal static void ReapplyBonuses()"):
                    source.index("internal static float CalculateBonus()")]
    markers = ["beforeMax = main.Health.MaxHealth;", "wasFull = main.Health.CurrentHealth >= beforeMax - 0.01f;",
               'RuntimeStatModifierTracker.RemoveAll(_records, "Showcase");', "RuntimeStatModifierTracker.TryAdd(",
               "if (wasFull)", "main.Health.MaxHealth > beforeMax", "main.Health.SetHealth(main.Health.MaxHealth)"]
    offsets = [method.find(marker) for marker in markers]
    return all(offset >= 0 for offset in offsets) and offsets == sorted(offsets)


def main():
    source = re.sub(r"/\*.*?\*/|//[^\n]*", "", SOURCE.read_text(encoding="utf-8-sig"), flags=re.S)
    try:
        valid = validate(source)
        remove = 'RuntimeStatModifierTracker.RemoveAll(_records, "Showcase");'
        old_order = source.replace(remove, "", 1).replace("CharacterMainControl main = CharacterMainControl.Main;",
                                                            remove + "\nCharacterMainControl main = CharacterMainControl.Main;", 1)
        valid = valid and not validate(old_order) and not validate(source.replace("if (wasFull)", "if (true)"))
    except ValueError:
        valid = False
    print("ShowcaseHealthSnapshotGuard: " + ("PASS (2 negative probes)" if valid else
          "FAIL - sample current/max HP before RemoveAll, and heal only previously full players after max increases"))
    return not valid


if __name__ == "__main__":
    sys.exit(main())
