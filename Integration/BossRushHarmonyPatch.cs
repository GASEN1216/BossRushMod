// ============================================================================
// BossRushHarmonyPatch.cs - Base hub injection patches
// ============================================================================
// 说明：
//   1. Patch StockShop.Awake：在目标售货机实例完成原版初始化后，直接向该实例注入 BossRush 商品
//   2. Patch InteractableBase.Start：在目标船点实例完成原版初始化后，直接向该实例注入 BossRush 交互
//   3. Patch CharacterMainControl.OnDead：在角色死亡前缀中处理额外追加掉落
//   4. 保留现有场景扫描逻辑作为兜底，处理热加载和已存在对象
// ============================================================================

using HarmonyLib;
using ItemStatsSystem;
using UnityEngine;
using Duckov;
using Duckov.Economy;

namespace BossRush
{
    [HarmonyPatch(typeof(StockShop), "Awake")]
    public static class BossRushBaseHubShopAwakePatch
    {
        [HarmonyPostfix]
        public static void Postfix(StockShop __instance)
        {
            ModBehaviour inst = ModBehaviour.Instance;
            if (inst == null || __instance == null || !inst.IsBaseHubNormalMerchantShop(__instance))
            {
                return;
            }

            int injectedCount = inst.TryInjectAllBossRushItemsIntoShop(__instance);
            if (injectedCount > 0)
            {
                ModBehaviour.DevLog("[BossRush] HarmonyPatch: 商店实例注入完成，新增条目数=" + injectedCount + ", merchantID=" + __instance.MerchantID);
            }
        }
    }

    [HarmonyPatch(typeof(InteractableBase), "Start")]
    public static class BossRushBaseHubInteractableStartPatch
    {
        [HarmonyPostfix]
        public static void Postfix(InteractableBase __instance)
        {
            ModBehaviour inst = ModBehaviour.Instance;
            if (inst == null || __instance == null)
            {
                return;
            }

            if (inst.TryInjectBaseHubBoatInteractable(__instance))
            {
                string sceneName = string.Empty;
                try { sceneName = __instance.gameObject.scene.name; } catch { }
                ModBehaviour.DevLog("[BossRush] HarmonyPatch: 已向船点交互实例注入 BossRush 选项, scene=" + sceneName + ", name=" + __instance.gameObject.name);
            }
        }
    }

    [HarmonyPatch(typeof(CharacterMainControl), "OnDead")]
    public static class BossRushCharacterOnDeadPatch
    {
        [HarmonyPrefix]
        public static void Prefix(CharacterMainControl __instance)
        {
            FrostmourneBlueBossDropHandler.TryHandleBlueBossDeath(__instance);
            PhantomWitchScytheBossDropHandler.TryHandlePhantomWitchDeath(__instance);
        }
    }

    // Projectile 有两个 Init 重载；必须显式绑定带 ProjectileContext 的版本，
    // 否则 PatchAll() 会因为重载歧义直接失败，枪械半掩体修复也不会生效。
    [HarmonyPatch(typeof(Projectile), "Init", new System.Type[] { typeof(ProjectileContext) })]
    public static class BossRushProjectileInitPatch
    {
        [HarmonyPostfix]
        public static void Postfix(Projectile __instance)
        {
            if (__instance == null || __instance.context.fromCharacter == null || !__instance.context.fromCharacter.IsMainCharacter)
            {
                return;
            }

            if (!__instance.context.ignoreHalfObsticle || __instance.damagedObjects == null)
            {
                return;
            }

            UnityEngine.GameObject[] nearByHalfObstacles;
            try
            {
                nearByHalfObstacles = __instance.context.fromCharacter.GetNearByHalfObsticles();
            }
            catch
            {
                return;
            }

            if (nearByHalfObstacles == null || nearByHalfObstacles.Length == 0)
            {
                return;
            }

            for (int i = 0; i < nearByHalfObstacles.Length; i++)
            {
                UnityEngine.GameObject obstacle = nearByHalfObstacles[i];
                if (obstacle != null && !__instance.damagedObjects.Contains(obstacle))
                {
                    __instance.damagedObjects.Add(obstacle);
                }
            }
        }
    }

    [HarmonyPatch(typeof(DeadBodyManager), "SpawnDeadBody")]
    public static class BossRushDeathWraithDeadBodySpawnPatch
    {
        [HarmonyPrefix]
        public static void Prefix(DeadBodyManager.DeathInfo info)
        {
            ModBehaviour inst = ModBehaviour.Instance;
            if (inst == null || info == null)
            {
                return;
            }

            inst.NotifyOriginalDeadBodySpawnRequested_DeathWraith(info);
        }
    }

    [HarmonyPatch(typeof(InteractableLootbox), "CreateFromItem")]
    public static class BossRushDeathWraithTombLootboxPatch
    {
        [HarmonyPostfix]
        public static void Postfix(
            Item item,
            Vector3 position,
            Quaternion rotation,
            bool moveToMainScene,
            InteractableLootbox prefab,
            bool filterDontDropOnDead,
            InteractableLootbox __result)
        {
            ModBehaviour inst = ModBehaviour.Instance;
            if (inst == null || __result == null)
            {
                return;
            }

            inst.NotifyOriginalDeadBodyLootboxCreated_DeathWraith(
                __result,
                item,
                position,
                prefab);
        }
    }

    [HarmonyPatch(typeof(DeadBodyManager), "NotifyDeadbodyTouched")]
    public static class BossRushDeathWraithDeadBodyTouchedPatch
    {
        [HarmonyPostfix]
        public static void Postfix(DeadBodyManager.DeathInfo info)
        {
            ModBehaviour inst = ModBehaviour.Instance;
            if (inst == null || info == null)
            {
                return;
            }

            inst.NotifyOriginalDeadBodyTouched_DeathWraith(info);
        }
    }
}
