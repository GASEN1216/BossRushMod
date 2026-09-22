"""诅咒视觉回调只观察已有 Buff，不为无关伤害加载诅咒资源。"""
from pathlib import Path
import re
from cs_source_util import clean_source

def main():
    source = clean_source(Path("Integration/PhantomWitch/PhantomWitchCurseSweatVfx.cs").read_text(encoding="utf-8-sig"))
    start = source.index("private static void OnGlobalHurt(")
    end = source.index("private static CharacterMainControl TryGetTargetCharacter(", start)
    body = source[start:end]
    observed = r"bool\s+hasCurse\s*=\s*buffMgr\s*!=\s*null\s*&&\s*buffMgr\.HasBuff\(PhantomWitchConfig.CurseBuffID\);"
    attach = r"if\s*\(hasCurse\s*&&\s*character\s*!=\s*null\)\s*\{\s*TryAttach\(character.gameObject\);\s*\}"
    if not re.search(observed, body) or not re.search(attach, body) or "GetCurseBuff(" in body:
        print("PhantomWitchCurseSweatVfxPrefilterGuard: FAIL - OnGlobalHurt must attach only after observing existing curse")
        return 1
    print("PhantomWitchCurseSweatVfxPrefilterGuard: PASS")
    return 0

if __name__ == "__main__":
    raise SystemExit(main())
