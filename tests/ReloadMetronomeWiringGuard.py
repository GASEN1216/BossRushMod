"""钟守节拍器的玩家入口、原版装填边界与兑换接线；行为由执行回归覆盖。"""
from pathlib import Path
import re
import sys
import json
from cs_source_util import clean_source

ROOT = Path(__file__).resolve().parents[1]
BASE = "Integration/NewWeapons/ReloadMetronome/"


def source(path):
    return clean_source((ROOT / path).read_text(encoding="utf-8-sig"))


def body(path, signature):
    text = source(path)
    start = text.index("{", text.index(signature))
    depth = 1
    for end in range(start + 1, len(text)):
        depth += (text[end] == "{") - (text[end] == "}")
        if depth == 0:
            return re.sub(r"\s+", "", text[start:end + 1])
    raise AssertionError(path + ": unclosed " + signature)


def require(path, signature, statement):
    assert re.sub(r"\s+", "", statement) in body(path, signature), path + ": missing " + statement


def main():
    config = source(BASE + "ReloadMetronomeConfig.cs")
    for token in ('VendorNpcId = "sky_bellkeeper";', "ShopUnlockLevel = 5;", "ShellCost = 20;"):
        assert token in config, "vendor / price drift: " + token
    require("Integration/Items/ItemContentRegistry.cs", "void RegisterItemContentConfigurators()",
            "NewWeaponItemConfigurators.RegisterAll();")
    require("Integration/NewWeapons/Common/NewWeaponItemConfigurators.cs", "void RegisterAll()",
            "ItemFactory.RegisterConfigurator(BossRushItemIds.ReloadMetronome, ConfigureReloadMetronome);")
    require("Integration/NewWeapons/Common/NewWeaponItemConfigurators.cs", "void ConfigureReloadMetronome(Item itemPrefab)",
            "ReloadMetronomeWeaponConfig.TryConfigure(itemPrefab, ReloadMetronomeConfig.BaseName);")
    require("Integration/NPCs/DuckNpc/Permanent/PermanentDuckNpcInteractable.cs", "void EnsureGroupedInteractionOptions()",
            '(ReloadMetronomeShopInteractable component) => component.NpcId = npcId);')
    assert 'npcId == ReloadMetronomeConfig.VendorNpcId' in source("Integration/NPCs/DuckNpc/Permanent/PermanentDuckNpcInteractable.cs")
    group = body("Integration/NPCs/DuckNpc/Permanent/PermanentDuckNpcInteractable.cs", "void EnsureGroupedInteractionOptions()")
    assert group.index("(ReloadMetronomeShopInteractablecomponent)") < group.index("(NPCShopInteractablecomponent)"), "shell exchange must take ownership before the cash shop"
    require("SkyIsland/SkyIslandWorldStoryServices.cs", "void AttachDockShop()",
            'AttachDeviceShop<ReloadMetronomeShopInteractable>("Search_H", "BellKeeperShopOption", ReloadMetronomeConfig.VendorNpcId, ref bellShop, ref bellShopGroup);')
    require("SkyIsland/SkyIslandWorldStoryServices.cs", "void DisposeDockShop()",
            "DisposeDeviceShop(ref bellShop, ref bellShopGroup);")
    ids = source("Config/ConfigItemIds.cs")
    assert "public const int ReloadMetronome = 500107;" in ids, "metronome must keep its distinct TypeID"
    assert "public const int RollCallLedger = 500106;" in ids, "published ledger TypeID must stay unchanged"
    rows = json.loads((ROOT / "Assets/Data/DuckNpcs.json").read_text(encoding="utf-8-sig"))["npcs"]
    shop = next(row for row in rows if row["id"] == "sky_bellkeeper")["permanent"]["shop"]
    # 2026-11-24 钟守的第二只匣子：止语钟罩（500220）与节拍器同为 5 级、各一件。
    assert shop["unlockLevel"] == 5 and shop["items"] == [{"typeId": 500107, "requiredLevel": 5, "maxStock": 1},
                                                         {"typeId": 500220, "requiredLevel": 5, "maxStock": 1}], \
        "bell keeper blueprint must expose exactly the metronome and the bell ward at level 5"
    assert shop["nameCn"] == "钟守的旧物匣" and shop["nameEn"] == "Bell Keeper's Keepsakes", "shop labels must be bilingual"
    require("Integration/NPCs/DuckNpc/Permanent/PermanentDuckNpcAffinityConfig.cs", "List<ShopItemEntry> GetShopItems()",
            "return ShopEnabled ? new List<ShopItemEntry>(_data.shop.items) : new List<ShopItemEntry>();")
    assert "BossRushItemIds.ReloadMetronome" not in source("Integration/Affinity/NPCs/GoblinAffinityConfig.cs"), "new item must not enter Dingdang's shop"
    # 节拍器入口只转发到两只匣子共用的贝壳事务，事务本身的顺序在 TryExchangeForShells 里钉。
    require(BASE + "ReloadMetronomeShop.cs", "bool TryExchange(",
            "return TryExchangeForShells(player, valid, BossRushItemIds.ReloadMetronome, ReloadMetronomeConfig.ShellCost,")
    trade = body(BASE + "ReloadMetronomeShop.cs", "bool TryExchangeForShells(")
    assert "InstantiateSync(typeId)" in trade and "newSkyIslandIngredient(shellId,shellCost)" in trade, "trade must use the requested goods and price"
    assert trade.index("TryReserve(") < trade.index("TryDeliverToBackpack(") < trade.index("payment.Commit();"), "reserve, deliver, commit order"
    assert "payment.Dispose();" in trade, "failed trades must release reserved shells"
    require(BASE + "ReloadMetronomeShop.cs", "bool TryDeliverToBackpack(", "pack.AddItem(output);")
    require(BASE + "ReloadMetronomeShop.cs", "bool TryDeliverToBackpack(",
            "return pack.Content.Contains(output) || SkyIslandInventoryTransaction.HasOwner(output);")
    runtime = source(BASE + "ReloadMetronomeRuntime.cs")
    assert "BulletCount =" not in runtime and "LoadBullets(" not in runtime, "native ammo loading must remain the owner"
    require(BASE + "ReloadMetronomeRuntime.cs", "void BeforeGunUpdate(", "stateTimer = Math.Max(stateTimer, total);")
    patches = source(BASE + "ReloadMetronomePatches.cs")
    assert "ref float ___stateTimer" in patches, "Harmony must bind the original gun timer"
    assert "return false;" not in patches, "metronome must not skip vanilla methods"
    # 物品图标与其他 Mod 物品同一条路（ModItemIcons + Publish，ModItemIconGuard 守卫）；
    # 独立小包只剩换弹 HUD 的反馈资源，不再取图标。
    assert not (ROOT / (BASE + "ReloadMetronomeIcon.cs")).exists(), "no per-item icon owner component"
    assets = source(BASE + "ReloadMetronomeAssets.cs")
    assert "metronome_icon" not in assets and "Icon" not in re.sub(r"ModItemIcons", "", assets), "presentation bundle no longer supplies the item icon"
    assert ".Icon" not in source(BASE + "ReloadMetronomeWeaponConfig.cs"), "configurator must not write the item icon"
    require(BASE + "ReloadMetronomeAssets.cs", "void EnsureFeedback()", 'trail = bundle.LoadAsset<Sprite>("metronome_trail");')
    require("Integration/NewWeapons/Common/NewWeaponRuntime.cs", "void CleanupOnDestroy()",
            "ReloadMetronomeAssets.ResetStaticCaches();")
    require(BASE + "ReloadMetronomeRuntime.cs", "void FinishReload()", "if (showFinish) hud.FinishReload();")
    require(BASE + "ReloadMetronomeRuntime.cs", "void Present(AimMarker marker)",
            "hud = ReloadMetronomeHud.Create(marker.aimMarkerUI);")
    require(BASE + "ReloadMetronomeHud.cs", "void SetResult(ReloadMetronomeResult next)",
            'var master = Duckov.AudioManager.GetBus("Master");')
    require(BASE + "ReloadMetronomeHud.cs", "void SetResult(ReloadMetronomeResult next)",
            'var sfx = Duckov.AudioManager.GetBus("Master/SFX");')
    require(BASE + "ReloadMetronomeHud.cs", "void SetResult(ReloadMetronomeResult next)",
            'audioSource.volume = master != null && sfx != null ? 0.28f * Mathf.Clamp01(master.Volume) * Mathf.Clamp01(sfx.Volume) : 0f;')
    manifest = json.loads((ROOT / "tools/resource_release_manifest.json").read_text(encoding="utf-8"))
    assert "Assets/ui/reload_metronome" in manifest["bundles"], "metronome presentation bundle must be deployed"
    print("ReloadMetronomeWiringGuard: PASS")


if __name__ == "__main__":
    try:
        main()
    except (AssertionError, ValueError) as error:
        print("ReloadMetronomeWiringGuard: FAIL - " + str(error))
        sys.exit(1)
