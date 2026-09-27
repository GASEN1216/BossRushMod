"""E/F must share the same virtual spawner registry at the original call sites."""
from pathlib import Path
from cs_source_util import clean_source


def read(path):
    return clean_source(Path(path).read_text(encoding="utf-8"))


def main():
    registration = read("ModBehaviourRuntimeModules.cs")
    source = read("Utilities/ModeEFVirtualSpawnerRegistry.cs")
    e = read("ModeE/ModeERuntimeModule.cs")
    f = read("ModeF/ModeFRuntimeModule.cs")
    startup = read("ModeE/ModeEStartup.cs")
    respawn = read("ModeF/ModeFRespawn.cs")
    bridge = read("ModeE/ModeEBattle_ScalingAndRuntime.cs")
    pairs = (
        (registration, "modeERuntime.BindVirtualSpawnerRegistry(modeEFVirtualSpawnerRegistry);"),
        (registration, "modeFRuntime.BindVirtualSpawnerRegistry(modeEFVirtualSpawnerRegistry);"),
        (e, "virtualSpawnerRegistry = registry;"),
        (f, "virtualSpawnerRegistry = registry;"),
        (startup, "virtualSpawnerRegistry.ClearRegisteredEnemies();"),
        (bridge, "virtualSpawnerRegistry.CleanupModeEVirtualSpawnerRoot();"),
        (bridge, "virtualSpawnerRegistry.RegisterModeEEnemyToSpawnerRoot(character);"),
        (bridge, "virtualSpawnerRegistry.UnregisterModeEEnemyFromSpawnerRoot(character);"),
        (respawn, "virtualSpawnerRegistry.RegisterModeEEnemyToSpawnerRoot(boss);"),
        (respawn, "virtualSpawnerRegistry.UnregisterModeEEnemyFromSpawnerRoot(boss);"),
        (source, "private static FieldInfo modeESpawnerRootCreatedCharactersField = null;"),
        (source, "modeEVirtualSpawnerRoot.enabled = false;"),
    )
    for text, statement in pairs:
        if statement not in text:
            print("ModeEFVirtualSpawnerRegistryGuard: FAIL missing " + statement)
            return 1
    if registration.count("new ModeEFVirtualSpawnerRegistry()") != 1:
        print("ModeEFVirtualSpawnerRegistryGuard: FAIL registry must have one shared instance")
        return 1
    if "private CharacterSpawnerRoot modeEVirtualSpawnerRoot" in read("ModeE/ModeEBattle.cs"):
        print("ModeEFVirtualSpawnerRegistryGuard: FAIL Mode E still owns the shared registry")
        return 1
    print("ModeEFVirtualSpawnerRegistryGuard: PASS")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
