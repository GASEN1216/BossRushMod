"""终章报名石复用实际路牌场地，不另维护竞技场就绪或出生点状态。"""
from pathlib import Path
import re
from cs_source_util import clean_source

ROOT = Path(__file__).resolve().parents[1]


def body(path, signature):
    source = clean_source((ROOT / path).read_text(encoding="utf-8-sig"))
    start = source.index(signature)
    start = source.index("{", start) + 1
    end, depth = start, 1
    while depth:
        depth += (source[end] == "{") - (source[end] == "}")
        end += 1
    return re.sub(r"\s+", " ", source[start:end-1]).strip()


def main():
    final = "Campaign/CampaignFinalBoss.cs"
    gate = body(final, "private bool ShouldCampaignFinalBossAltarExist()")
    assert "if (!_owner.CampaignArenaActiveForRuntime) return false;" in gate, "报名石须处于真实 BossRush 接管场地"
    assert "BossRushSignInteractable sign = _owner.CampaignArenaSignForRuntime;" in gate
    assert "if (sign == null || sign.gameObject == null || !sign.gameObject.activeInHierarchy || sign.gameObject.scene.handle != UnityEngine.SceneManagement.SceneManager.GetActiveScene().handle) return false;" in gate, "实际路牌须存活、激活并属于当前场景"
    assert "SpawnPositionHelper.TryFindAroundPlayer(_owner.CampaignArenaSignForRuntime.transform.position, 8, 3f," in body(final, "internal void TickCampaignFinalBossAltar()"), "报名石须围绕实际路牌找地面"
    assert body("Campaign/CampaignRuntimeModuleHostBridge.cs", "internal BossRushSignInteractable CampaignArenaSignForRuntime") == "get { return uiAndSignsRuntime.SignInteract; }", "桥须只读同一路牌 owner"
    for signature in ("internal void TryCreateArenaDifficultyEntryPoint_UIAndSigns(Vector3? customPosition)", "private void CreateInvisibleEntryPointForChallengeSnow(Vector3? customPosition)"):
        assert "bossRushSignInteract = signInteract;" in body("UIAndSigns/UIAndSigns.cs", signature), "普通牌与雪图隐形入口均须发布实际组件"
    print("CampaignArenaSignGuard: PASS")


if __name__ == "__main__":
    main()
