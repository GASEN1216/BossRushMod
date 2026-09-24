"""Mode F 悬赏闩由模块拥有，战役只在死亡采集点消费。"""
from pathlib import Path
import re
import sys

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tests"))
from cs_source_util import clean_source


def source(path):
    return clean_source((ROOT / path).read_text(encoding="utf-8-sig"))


def body(text, signature):
    start = text.find(signature)
    if start < 0:
        raise AssertionError("missing method: " + signature)
    opening = text.find("{", start + len(signature))
    depth = 1
    end = opening + 1
    while depth and end < len(text):
        depth += (text[end] == "{") - (text[end] == "}")
        end += 1
    if depth:
        raise AssertionError("unclosed method: " + signature)
    return text[opening + 1:end - 1]


def main():
    try:
        module = source("ModeF/ModeFRuntimeModule.cs")
        bounty = source("ModeF/ModeFBounty.cs")
        campaign = source("Campaign/CampaignModeBridge.cs")
        collector = source("Campaign/CampaignObjectiveCollector.cs")
        registration = source("ModBehaviourRuntimeModules.cs")
        assert "private int lastPlayerBountyKillVictimId;" in module
        assert "private bool lastPlayerBountyKillWasBounty;" in module
        assert "modeFLastPlayerBountyKillVictimId" not in bounty
        assert "modeFLastPlayerBountyKillWasBounty" not in bounty
        assert "modeFRuntime = new ModeFRuntimeModule();" in registration
        assert "runtimeModuleHost.Register(modeFRuntime);" in registration
        query = body(campaign, "internal bool HasCampaignBountyMark(CharacterMainControl boss)")
        assert "HasModeFPlayerBountyKillLatch(boss.GetInstanceID())" in query
        assert "ConsumeModeFPlayerBountyKillLatch" not in query
        consume = body(campaign, "internal bool ConsumeCampaignBountyMark(CharacterMainControl boss)")
        first = consume.index("ConsumeModeFPlayerBountyKillLatch(boss.GetInstanceID())")
        second = consume.index("HasCampaignBountyMark(boss)")
        assert first < second
        collection = body(collector, "private static bool HasBountyMark(CharacterMainControl victim)")
        assert "owner.ConsumeCampaignBountyMark(victim)" in collection
        assert "owner.HasCampaignBountyMark(victim)" not in collection
        consume_latch = body(module, "internal bool ConsumePlayerBountyKillLatch(int victimId)")
        assert "lastPlayerBountyKillVictimId != victimId" in consume_latch
        assert "ResetPlayerBountyKillLatch();" in consume_latch
        assert re.search(r"if\s*\(modeFRuntime\s*!=\s*null\)\s*modeFRuntime\.ResetPlayerBountyKillLatch\(\);", bounty)
    except (AssertionError, ValueError) as error:
        print("CampaignBountyLatchOwnershipGuard: FAIL - " + str(error))
        return 1
    print("CampaignBountyLatchOwnershipGuard: PASS")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
