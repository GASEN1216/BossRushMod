"""ModeEMerchantHealthGuard: 神秘商人 999999 生命加成必须挂到角色真正的 Item 上。

官方 CharacterMainControl.SetItem 把角色 Item 挂到角色的子物体（_item.transform.SetParent(base.transform)），
角色 GameObject 本身没有 Item 组件：character.GetComponent<Item>() 恒为 null，生命修饰符永远加不上
（2026-10-01 外部审查 P2，划地为营与随机事件商人共用这一入口）。必须走 character.CharacterItem。
"""

from pathlib import Path
import sys

from cs_source_util import clean_source

SOURCE = Path("ModeE/ModeEBattle_ScalingAndRuntime.cs")
SIGNATURE = "internal void SetModeEMerchantHealth(CharacterMainControl character)"


def body(text: str, signature: str) -> str:
    start = text.find(signature)
    if start < 0:
        return ""
    opening = text.find("{", start)
    depth = 0
    for index in range(opening, len(text)):
        if text[index] == "{":
            depth += 1
        elif text[index] == "}":
            depth -= 1
            if depth == 0:
                return text[start:index + 1]
    return ""


def check(text: str) -> list:
    errors = []
    method = body(clean_source(text), SIGNATURE)
    if not method:
        return ["找不到 SetModeEMerchantHealth"]
    if "Item characterItem = character.CharacterItem;" not in method:
        errors.append("商人生命加成必须从 character.CharacterItem 取角色 Item")
    if "GetComponent<Item>()" in method:
        errors.append("角色 GameObject 上没有 Item 组件，不得用 GetComponent<Item>() 取角色 Item")
    if 'characterItem.GetStat("MaxHealth")' not in method or "maxHealthStat.AddModifier(mod);" not in method:
        errors.append("生命加成必须加在角色 Item 的 MaxHealth 属性上")
    return errors


def main() -> int:
    text = SOURCE.read_text(encoding="utf-8-sig")
    errors = check(text)
    # 反向探针：退回旧写法必须转红
    probe = text.replace("Item characterItem = character.CharacterItem;",
                         "Item characterItem = character.GetComponent<Item>();", 1)
    if probe == text or not check(probe):
        errors.append("[探针] 退回 GetComponent<Item>() 没被拦住")
    if errors:
        print("ModeEMerchantHealthGuard: FAIL")
        for error in errors:
            print("  - " + error)
        return 1
    print("ModeEMerchantHealthGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
