"""宠物照料复用库存事务、资产屏障及现成语言管线，实例与槽位归属不得丢失。"""
from pathlib import Path
import re
from cs_source_util import clean_source

ROOT = Path(__file__).resolve().parents[1]


def source(path):
    return clean_source((ROOT / path).read_text(encoding="utf-8-sig"))


def member(code, signature):
    assert code.count(signature) == 1, signature
    start = code.index(signature)
    opening = code.index("{", start)
    depth = 0
    for end in range(opening, len(code)):
        depth += (code[end] == "{") - (code[end] == "}")
        if depth == 0:
            return code[start:end + 1]
    raise AssertionError(signature)


def main():
    errors = []
    care = source("PetNest/PetNestCompanionCare.cs")
    feed = member(care, "internal static void TryFeed(")
    boundary = feed.find("RequireBackpackAssetSnapshot(out error)")
    reserve = feed.find("SkyIslandInventoryTransaction.TryReserve(")
    stage = feed.find("if (!PetNestService.StageCommit()) return;")
    accept = feed.find("accepted = true;")
    commit = feed.find("meal.Commit();")
    flush = feed.find("RequestAssetFlush(out error)")
    if min(boundary, reserve, stage, accept, commit, flush) < 0 or not boundary < reserve < stage < accept < commit < flush:
        errors.append("[资产事务] 喂食必须先立主角资产义务，预留原物，接纳成长后提交消耗并请求同批保存")
    if "if (!accepted) PetNestPersistenceAccess.AbortTransaction();" not in feed or not re.search(r"finally\s*\{\s*if \(meal != null\) meal.Dispose\(\);\s*\}", feed):
        errors.append("[回滚] 失败必须由库存事务归还原物，接纳后的成长不得撤销")
    if "ItemFactory.ConsumeItem(" in feed or "ReturnFood(" in care:
        errors.append("[原物凭据] 喂食不得用失真消耗布尔值或按 TypeID 新造退款物")
    spawner = source("PetNest/PetNestCompanionSpawner.cs")
    if "PetNestCareService.AttachOptions(backpack, pet.id, handle.Character);" not in spawner:
        errors.append("[正式入口] 照料组必须绑定本次激活的实际宠物角色")
    enabled = member(care, "protected override bool IsInteractable()")
    if "_slot == SavesSystem.CurrentSlot" not in enabled or "Companion != null" not in enabled or "PetNestCompanionRuntime.CompanionCharacter" in enabled:
        errors.append("[实例归属] 照料必须认自身角色与槽位，基地不能依赖局内 owner")
    for statement in ("PetNestCareService.TryFeed(PetId, Companion);", "PetNestCareService.Pat(PetNestService.TryGetPet(PetId), Companion);"):
        if statement not in care:
            errors.append("[实例归属] 正式子操作必须作用于绑定的宠物：" + statement)
    refresh = member(care, "internal static void RefreshOptionLocalization()")
    if "PetNestBaseIdleSpawner.ActiveCount == 0" not in refresh or "Resolve(PetNestService.DeployedPet)" not in refresh:
        errors.append("[语言门控] 只在有场上宠物时刷新当前记录的行为名称")
    inject = member(source("Localization/PetNestLocalization.cs"), "public static void Inject()")
    if "PetNestCareService.RefreshOptionLocalization();" not in inject:
        errors.append("[语言入口] 照料文案必须接进现有语言重注入管线")
    if "OnSetLanguage +=" in care or re.search(r"\bvoid Update\s*\(", care):
        errors.append("[低频更新] 照料不得为换语言新增订阅或每帧刷新")
    if errors:
        print("PetNestCareTransactionGuard: FAIL\n" + "\n".join(errors))
        return 1
    print("PetNestCareTransactionGuard: PASS")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
