using System;
using System.Collections.Generic;
using Duckov.Scenes;
using HarmonyLib;
using UnityEngine;

namespace BossRush
{
    /// <summary>局内设施按自己的边界和预警裁决；只有玩家本人在冲击发生的那一帧走官方死亡流程。</summary>
    internal sealed class TidegateHazards : IDisposable
    {
        private TidegateSluiceDanger sluice;
        private TidegateCraneDanger crane;
        private readonly Action<string, bool> announce;
        private static Health resolving;
        private static bool impactEntered;

        // 官方把 DamageInfo 按值一路交给死亡结算，元素列表引用随它一起传递；标签只属于这一击。
        private sealed class HazardElements : List<ElementFactor>
        {
            internal readonly bool Sluice;
            internal HazardElements(bool sluice) { Sluice = sluice; }
        }

        [HarmonyPatch(typeof(DamageInfo), nameof(DamageInfo.GenerateDescription))]
        private static class DeathDescription
        {
            private static void Postfix(ref DamageInfo __instance, ref string __result)
            {
                var tag = __instance.elementFactors as HazardElements;
                if (tag != null) __result = tag.Sluice ? L10n.T("卷入大闸泄洪口", "Swept into the sluice intake")
                    : L10n.T("被码头吊货砸中", "Crushed by the dock crane");
            }
        }

        internal TidegateHazards(TidegateWorld world, TidegateTide tide, Action<string, bool> status)
        {
            announce = status;
            try { sluice = new TidegateSluiceDanger(world, tide, status); }
            catch (Exception e) { Report("sluice_setup", e); }
            try { crane = new TidegateCraneDanger(world, tide, status); }
            catch (Exception e) { Report("crane_setup", e); }
        }

        private static void Report(string step, Exception e)
        {
            Debug.LogWarning(TidegateRaidScene.LogTag + " hazard " + step + ": " + e.Message);
        }

        internal static bool IsResolving(Health health) { return !ReferenceEquals(resolving, null) && ReferenceEquals(resolving, health); }

        // Hurt 在设 isDead 前会同步触发 OnHealthChange。只准本体那一次，勿让其回调在同一目标身上再进死亡分支。
        internal static bool TryEnterImpact()
        {
            if (impactEntered) return false;
            impactEntered = true;
            return true;
        }

        internal bool Tick(CharacterMainControl player, float advance)
        {
            if (advance <= 0f || player == null || player != CharacterMainControl.Main || player.Health == null || player.Health.IsDead
                || LevelManager.Instance == null || !LevelManager.Instance.IsRaidMap || (MultiSceneCore.Instance != null && MultiSceneCore.Instance.IsLoading)) return false;
#if BOSSRUSH_DEV
            // 实机演练会直接瞬移到各种观测点，无人驾驶的测试绝不触发真实死亡。
            if (F3GameplayValidationRunner.IsRunning) return false;
#endif
            float dt = Mathf.Min(advance, 0.15f);
            Vector3 at = player.transform.position;
            bool swept = false, crushed = false;
            try { if (sluice != null) swept = sluice.Tick(at, dt); }
            catch (Exception e) { Report("sluice_tick", e); Release(ref sluice); }
            try { if (crane != null) crushed = crane.Tick(at, dt); }
            catch (Exception e) { Report("crane_tick", e); Release(ref crane); }
            if (!swept && !crushed) return false;
            bool dead = ResolveImpact(player.Health, at, swept);
            if (dead && announce != null) announce(swept ? L10n.T("你被卷进了泄洪口。", "You were swept into the open sluice.")
                : L10n.T("吊货落下，你没来得及离开落区。", "You were caught beneath the falling load."), true);
            Debug.Log(TidegateRaidScene.LogTag + " HAZARD_IMPACT kind=" + (swept ? "sluice" : "crane") + " dead=" + dead);
            return dead;
        }

        private static bool ResolveImpact(Health health, Vector3 at, bool swept)
        {
            if (resolving != null || health == null || health.IsDead || LevelManager.Instance == null
                || !LevelManager.Instance.IsRaidMap || (MultiSceneCore.Instance != null && MultiSceneCore.Instance.IsLoading)) return false;
            bool wasInvincible = health.Invincible;
            impactEntered = false;
            resolving = health;
            try
            {
                if (wasInvincible) health.SetInvincible(false);
                health.Hurt(new DamageInfo(null) { damageType = DamageTypes.realDamage, damageValue = health.CurrentHealth + 1f,
                    ignoreArmor = true, ignoreDifficulty = true, damagePoint = at, elementFactors = new HazardElements(swept) });
                return health.IsDead;
            }
            finally
            {
                resolving = null;
                impactEntered = false;
                if (wasInvincible && health != null) health.SetInvincible(true);
            }
        }

        private static void Release<T>(ref T device) where T : class, IDisposable
        {
            T old = device;
            device = null;
            try { if (old != null) old.Dispose(); }
            catch (Exception e) { Report("dispose", e); }
        }

        public void Dispose()
        {
            Release(ref sluice);
            Release(ref crane);
        }
    }
}
