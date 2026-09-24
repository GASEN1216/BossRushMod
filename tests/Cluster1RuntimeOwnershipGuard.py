"""Boss filter and entry UI own their state after P3 cluster 1 extraction."""
from pathlib import Path
import re

from cs_source_util import clean_source

ROOT = Path(__file__).resolve().parent.parent


def code(path):
    return clean_source((ROOT / path).read_text(encoding="utf-8-sig"))


def require(condition, message, errors):
    if not condition:
        errors.append(message)


def main():
    errors = []
    registration = code("Common/Lifecycle/BossRushRuntimeModuleRegistration.cs")
    compile_list = (ROOT / "compile_official.bat").read_text(encoding="utf-8-sig")
    boss = code("BossFilter/BossFilter.cs")
    ui = code("UIAndSigns/UIAndSigns.cs")
    scan = code("UIAndSigns/BossRushInteractionScan.cs")
    boss_bridge = code("BossFilter/BossFilterHostBridge.cs")
    ui_bridge = code("UIAndSigns/UIAndSignsRuntimeBridges.cs")
    for path, source, module in (
        ("BossFilter/BossFilter.cs", boss, "BossFilterRuntimeModule"),
        ("BossFilter/BossFilterUi.cs", code("BossFilter/BossFilterUi.cs"), "BossFilterRuntimeModule"),
        ("UIAndSigns/UIAndSigns.cs", ui, "UIAndSignsRuntimeModule"),
        ("UIAndSigns/BossRushInteractionScan.cs", scan, "UIAndSignsRuntimeModule"),
    ):
        require(re.search(r"partial\s+class\s+" + module + r"\b", source) is not None,
                path + " must be module owned", errors)
        require(re.search(r"partial\s+class\s+ModBehaviour\b", source) is None,
                path + " still declares host partial", errors)
    for variable, module in (("bossFilterRuntime", "BossFilterRuntimeModule"),
                             ("uiAndSignsRuntime", "UIAndSignsRuntimeModule")):
        require(registration.count("new " + module + "(this)") == 1,
                module + " must have one instance", errors)
        require(re.search(variable + r"\s*=\s*new\s+" + module
                          + r"\(this\);\s*runtimeModuleHost\.Register\(" + variable + r"\);",
                          registration) is not None,
                module + " registration must use the stored instance", errors)
    require("public override void OnDestroy() { DestroyBossPoolUI(); }" in boss,
            "Boss filter must release its panel during module cleanup", errors)
    for bridge, token in ((boss_bridge, "bossFilterRuntime.GetFilteredEnemyPresets()"),
                          (boss_bridge, "bossFilterRuntime.BossPoolLateUpdate()"),
                          (ui_bridge, "uiAndSignsRuntime.FindInteractionTargets(scanTimes)"),
                          (ui_bridge, "uiAndSignsRuntime.ShowMessage_UIAndSigns(msg)")):
        require(token in bridge, "missing module dispatch: " + token, errors)
    require("owner.PetNestRuntime.NotifyEnemyPresetsRefreshed()" in boss
            and "owner.CodexRuntime.NotifyEnemyPresetsRefreshed()" in boss,
            "filter invalidation must refresh both dependent catalogs", errors)
    require(not (ROOT / "Injection/Injection.cs").exists()
            and "Injection\\Injection.cs" not in compile_list,
            "empty Injection host placeholder remains", errors)
    for error in errors:
        print("Cluster1RuntimeOwnershipGuard: " + error)
    print("Cluster1RuntimeOwnershipGuard: " + ("FAIL" if errors else "PASS"))
    return 1 if errors else 0


if __name__ == "__main__":
    raise SystemExit(main())
