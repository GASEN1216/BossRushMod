"""Guard: zombie-mode temporary terminals must prepare InteractableBase internals before base.Awake."""

from pathlib import Path


SOURCE = Path("ZombieMode/ZombieModeRewards.cs")


def fail(message):
    print("ZombieModeTemporaryNpcAwakeGuard: FAIL - " + message)
    raise SystemExit(1)


def extract_method(text, signature):
    start = text.find(signature)
    if start == -1:
        return ""
    brace_start = text.find("{", start)
    if brace_start == -1:
        return ""

    depth = 0
    for index in range(brace_start, len(text)):
        char = text[index]
        if char == "{":
            depth += 1
        elif char == "}":
            depth -= 1
            if depth == 0:
                return text[start:index + 1]
    return ""


def main():
    text = SOURCE.read_text(encoding="utf-8")
    awake = extract_method(text, "protected override void Awake()")
    if not awake:
        fail("missing ZombieModeTemporaryNpcInteractable.Awake")

    group_index = awake.find("NPCInteractionGroupHelper.GetOrCreateGroupList(this, \"[ZombieMode] TemporaryNpc\");")
    base_index = awake.find("base.Awake();")
    if group_index == -1:
        fail("Awake does not initialize InteractableBase group internals")
    if base_index == -1:
        fail("Awake does not call base.Awake")
    if group_index > base_index:
        fail("group internals must be initialized before base.Awake")

    print("ZombieModeTemporaryNpcAwakeGuard: PASS")


if __name__ == "__main__":
    main()
