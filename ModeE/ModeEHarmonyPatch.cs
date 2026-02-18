// ============================================================================
// ModeEHarmonyPatch.cs - Mode E Harmony Patches
// ============================================================================
// 包含：
//   1. SetTeam 阵营保护 Patch：阻止原版防作弊逻辑篡改玩家阵营
//   2. HealthBar 友方血条绿色 Patch：同阵营单位血条显示为绿色
//   3. HealthBar 名字追加阵营后缀 Patch：在原版名字右边显示 " - 阵营名"
//   4. InteractableBase.OnTimeOut Patch：拦截商人主交互，执行召唤煤球
// ============================================================================

using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Duckov.UI;

namespace BossRush
{
    /// <summary>
    /// Patch CharacterMainControl.SetTeam：
    /// Mode E 中阻止主角阵营被篡改为 Teams.all
    /// </summary>
    [HarmonyPatch(typeof(CharacterMainControl), "SetTeam")]
    public static class ModeESetTeamPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(CharacterMainControl __instance, Teams _team)
        {
            // 非 Mode E 或非 Teams.all 时放行
            var inst = ModBehaviour.Instance;
            if (inst == null || !inst.IsModeEActive || _team != Teams.all)
                return true;

            // 只保护主角
            if (!__instance.IsMainCharacter)
                return true;

            // 阻止 SetTeam(Teams.all)，保持玩家正确阵营
            return false;
        }
    }

    /// <summary>
    /// Patch InteractableBase.OnTimeOut：
    /// Mode E 中拦截神秘商人主交互，执行召唤煤球逻辑
    /// </summary>
    [HarmonyPatch(typeof(InteractableBase), "OnTimeOut")]
    public static class ModeEMerchantInteractPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(InteractableBase __instance)
        {
            // 非 Mode E 时放行
            var inst = ModBehaviour.Instance;
            if (inst == null || !inst.IsModeEActive)
                return true;

            // 检查是否是商人主交互
            if (inst.ModeEMerchantMainInteract == null || __instance != inst.ModeEMerchantMainInteract)
                return true;

            // 拦截并执行召唤煤球
            ModeEPetSpawner.SpawnPet();

            // 返回 false 阻止原版 OnTimeOut 执行
            return false;
        }
    }

    /// <summary>
    /// Patch HealthBar.Refresh：
    /// Mode E 中将同阵营友方单位的血条颜色覆盖为绿色
    /// [性能优化] 缓存 ModBehaviour.Instance 引用，减少每帧属性访问
    /// </summary>
    [HarmonyPatch(typeof(HealthBar), "Refresh")]
    public static class ModeEHealthBarColorPatch
    {
        /// <summary>友方血条绿色（鲜明的绿色，易于辨识）</summary>
        private static readonly Color AllyHealthBarColor = new Color(0.2f, 0.9f, 0.2f, 1f);

        /// <summary>缓存的 ModBehaviour 实例引用</summary>
        private static ModBehaviour cachedInstance;
        /// <summary>上次刷新缓存的帧号（用帧号代替计数器，避免多 HealthBar 实例竞态导致刷新频率不稳定）</summary>
        private static int lastRefreshFrame = -1;

        [HarmonyPostfix]
        public static void Postfix(HealthBar __instance, Image ___fill)
        {
            // 每 60 帧刷新一次缓存（约每秒一次，不受多实例并发影响）
            int currentFrame = Time.frameCount;
            if (cachedInstance == null || currentFrame - lastRefreshFrame >= 60)
            {
                lastRefreshFrame = currentFrame;
                cachedInstance = ModBehaviour.Instance;
            }

            // 非 Mode E 时跳过（快速路径）
            if (cachedInstance == null || !cachedInstance.IsModeEActive)
                return;

            // 获取血条绑定的 Health 目标
            Health target = __instance.target;
            if (target == null) return;

            // 获取角色
            CharacterMainControl character = target.TryGetCharacter();
            if (character == null || character.IsMainCharacter) return;

            // 判断是否与玩家同阵营
            if (character.Team == cachedInstance.ModeEPlayerFaction)
            {
                // 同阵营友方：覆盖血条颜色为绿色
                if (___fill != null)
                {
                    ___fill.color = AllyHealthBarColor;
                }
            }
        }
    }

    /// <summary>
    /// Patch HealthBar.RefreshCharacterIcon：
    /// Mode E 中在原版名字右边追加 " - 阵营名"，完全交给游戏原版渲染
    /// </summary>
    [HarmonyPatch(typeof(HealthBar), "RefreshCharacterIcon")]
    public static class ModeEHealthBarFactionNamePatch
    {
        /// <summary>缓存的 ModBehaviour 实例引用</summary>
        private static ModBehaviour cachedInstance;
        /// <summary>上次刷新缓存的帧号</summary>
        private static int lastRefreshFrame = -1;

        [HarmonyPostfix]
        public static void Postfix(HealthBar __instance, TextMeshProUGUI ___nameText)
        {
            // 每 60 帧刷新一次缓存
            int currentFrame = Time.frameCount;
            if (cachedInstance == null || currentFrame - lastRefreshFrame >= 60)
            {
                lastRefreshFrame = currentFrame;
                cachedInstance = ModBehaviour.Instance;
            }

            // 非 Mode E 时跳过
            if (cachedInstance == null || !cachedInstance.IsModeEActive)
                return;

            // nameText 不可用或未激活时跳过
            if (___nameText == null || !___nameText.gameObject.activeSelf)
                return;

            // 获取角色阵营
            Health target = __instance.target;
            if (target == null) return;

            CharacterMainControl character = target.TryGetCharacter();
            if (character == null) return;

            // 获取阵营显示名并追加到原版名字后面
            string factionSuffix = cachedInstance.GetModeEFactionSuffix(character.Team);
            if (factionSuffix != null)
            {
                ___nameText.text += factionSuffix;
            }
        }
    }
}
