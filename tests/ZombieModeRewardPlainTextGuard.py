from pathlib import Path
import re
import sys


LOCALIZATION = Path("Localization/LocalizationInjector.cs")


EXPECTED_CN_PHRASES = {
    "BossRush_ZombieMode_Reward_Heal": ["生命回满"],
    "BossRush_ZombieMode_Reward_RandomSupply": ["随机补给物品"],
    "BossRush_ZombieMode_Reward_RandomHighQualityItem": ["随机高品质物品"],
    "BossRush_ZombieMode_Reward_StarterReroll": ["按流派补武器"],
    "BossRush_ZombieMode_Reward_TempMerchant": ["补给终端", "保底高品质"],
    "BossRush_ZombieMode_Reward_TempNurse": ["医疗终端", "净化点治疗"],
    "BossRush_ZombieMode_Reward_FortificationPack": ["掩体", "路障", "铁丝网", "维修喷剂"],
    "BossRush_ZombieMode_Reward_ContractPollutionDeal": ["污染 +1/+2", "净化点 -80/-150"],
    "BossRush_ZombieMode_Reward_ContractGearDeal": ["污染 +2/+3", "净化点 -60/-120", "高阶枪械", "护甲/头盔"],
    "BossRush_ZombieMode_Reward_ContractHugePurification": ["污染 +3", "净化点 -200"],
    "BossRush_ZombieMode_Reward_ContractInsurance": ["污染 +2", "净化点 -80", "指定保留", "随机20%"],
    "BossRush_ZombieMode_Reward_MapEventHighValueAirdrop": ["立即获得", "高品质"],
    "BossRush_ZombieMode_Reward_MapEventEliteSquad": ["下波额外", "3 个精英"],
    "BossRush_ZombieMode_Reward_ProjectilePenetration": ["子弹穿透 +1", "代价：换弹速度"],
    "BossRush_ZombieMode_Reward_ProjectileBurn": ["命中有概率点燃", "燃烧概率 +35%", "最高75%", "代价：枪械伤害"],
    "BossRush_ZombieMode_Reward_ProjectileCold": ["命中有概率减速", "冰霜概率 +25%", "最高60%", "代价：换弹速度"],
    "BossRush_ZombieMode_Reward_ProjectilePoison": ["命中有概率中毒", "毒化概率 +35%", "最高75%", "代价：最大生命"],
    "BossRush_ZombieMode_Reward_ProjectileArmorBreak": ["穿甲 +25%", "破甲 +10%", "代价：承受伤害"],
    "BossRush_ZombieMode_Reward_MutatorCritFocus": ["暴击率 +15%", "最多45%", "代价：换弹速度"],
    "BossRush_ZombieMode_Reward_TriggerCritBurst": ["暴击时爆炸", "本次伤害30%", "代价：承受伤害"],
    "BossRush_ZombieMode_Reward_TriggerPurificationSiphon": ["击杀额外掉", "净化星", "代价：污染"],
    "BossRush_ZombieMode_Reward_TriggerSecondWind": ["击杀回血", "每层 2", "代价：最大生命"],
    "BossRush_ZombieMode_Reward_TriggerDoomPulse": ["累计击杀", "触发 3 次爆炸", "代价：承受伤害"],
    "BossRush_ZombieMode_Reward_MutatorBulletTime": ["生命低于25%", "1 秒子弹时间", "代价：承受伤害"],
    "BossRush_ZombieMode_Reward_MutatorGuardianShield": ["满血时", "物理伤害 -25%", "代价：枪械伤害"],
    "BossRush_ZombieMode_Reward_MutatorQuickReload": ["换弹速度 +25%", "代价：枪械伤害"],
    "BossRush_ZombieMode_Reward_MutatorDashBoost": ["翻滚速度 +25%", "代价：枪械伤害"],
    "BossRush_ZombieMode_Reward_BattlefieldAmmoRain": ["每45秒", "60发弹药", "代价：净化点"],
    "BossRush_ZombieMode_Reward_ContractDevilBargain": ["污染 +3/+4", "净化点 -120/-200"],
    "BossRush_ZombieMode_Reward_ContractCursedReload": ["污染 +2/+3", "净化点 -60/-100", "换弹速度 +35%/+45%"],
    "BossRush_ZombieMode_Reward_ContractBloodPrice": ["污染 +2/+3", "净化点 -50/-80", "立即回血30%/45%"],
    "BossRush_ZombieMode_Reward_ContractCursePool": ["污染 +3/+4", "净化点 -100/-150", "随机获得", "保底/保险"],
    "BossRush_ZombieMode_Reward_ProjectileTrident": ["当前枪至少3发", "单颗伤害分摊", "代价：换弹速度"],
    "BossRush_ZombieMode_Reward_ProjectileShotgunSpray": ["当前枪至少5发", "单颗伤害分摊", "代价：枪械伤害"],
    "BossRush_ZombieMode_Reward_ProjectileStasis": ["命中普通敌人", "减速65%", "1秒", "代价：移动速度"],
    "BossRush_ZombieMode_Reward_ProjectileRicochet": ["命中后弹向附近敌人", "命中后追加", "追向附近敌人", "代价：换弹速度"],
    "BossRush_ZombieMode_Reward_ProjectileFork": ["命中后分出两发支援弹", "命中后分裂", "2发斜向子弹", "代价：枪械伤害"],
    "BossRush_ZombieMode_Reward_ProjectileReturn": ["命中后向你飞回支援弹", "命中后回射", "玩家方向", "代价：最大生命"],
    "BossRush_ZombieMode_Reward_ProjectileHelix": ["子弹螺旋飞行", "代价：移动速度"],
    "BossRush_ZombieMode_Reward_ProjectileTrail": ["沿途造成小范围伤害", "子弹沿途", "范围伤害", "代价：承受伤害"],
    "BossRush_ZombieMode_Reward_BattlefieldPurgeAura": ["身边范围伤害", "每3秒", "代价：污染"],
    "BossRush_ZombieMode_Reward_BattlefieldCurseTrap": ["前方延迟爆炸", "每18秒", "代价：最大生命"],
    "BossRush_ZombieMode_Reward_BattlefieldBlackHole": ["定期在前方生成牵引场", "前方牵引黑洞", "每12秒", "代价：净化点"],
    "BossRush_ZombieMode_Reward_BattlefieldGravityDrag": ["定期把小怪往前方拉", "前方弱牵引区", "每16秒", "代价：移动速度"],
}


def fail(message: str) -> int:
    print("ZombieModeRewardPlainTextGuard: FAIL - " + message)
    return 1


def extract_cn_text(localization: str, key: str) -> str:
    pattern = re.compile(
        r'InjectZombieModeString\("' + re.escape(key) + r'",\s*"([^"]+)"',
        re.MULTILINE,
    )
    match = pattern.search(localization)
    return match.group(1) if match else ""


def main() -> int:
    localization = LOCALIZATION.read_text(encoding="utf-8")
    for key, phrases in EXPECTED_CN_PHRASES.items():
        cn_text = extract_cn_text(localization, key)
        if not cn_text:
            return fail("missing localization key -> " + key)
        missing = [phrase for phrase in phrases if phrase not in cn_text]
        if missing:
            return fail(key + " missing phrase(s) " + ", ".join(missing) + " in: " + cn_text)

    print("ZombieModeRewardPlainTextGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
