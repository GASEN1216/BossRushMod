using System;
using Duckov;
using Duckov.UI.DialogueBubbles;
using UnityEngine;

namespace BossRush
{
    public static class PeaceCharmRuntime
    {
        private const float InventoryCacheInterval = 0.2f;

        private static bool hurtEventRegistered;
        private static bool triggeredThisScene;
        private static float nextInventoryCheckTime = -1f;
        private static bool cachedHasCharm;

        public static void InitializeRuntime()
        {
            if (hurtEventRegistered)
            {
                return;
            }

            try
            {
                Health.OnHurt += OnPlayerHurt;
                hurtEventRegistered = true;
                ModBehaviour.DevLog("[PeaceCharmRuntime] Registered hurt event");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PeaceCharmRuntime] InitializeRuntime failed: " + e.Message);
            }
        }

        public static void ShutdownRuntime()
        {
            if (!hurtEventRegistered)
            {
                return;
            }

            try
            {
                Health.OnHurt -= OnPlayerHurt;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PeaceCharmRuntime] ShutdownRuntime failed: " + e.Message);
            }
            finally
            {
                hurtEventRegistered = false;
                triggeredThisScene = false;
                nextInventoryCheckTime = -1f;
                cachedHasCharm = false;
            }
        }

        public static void ResetSceneTrigger()
        {
            triggeredThisScene = false;
            nextInventoryCheckTime = -1f;
            cachedHasCharm = false;
        }

        private static void OnPlayerHurt(Health health, DamageInfo damageInfo)
        {
            try
            {
                if (triggeredThisScene || health == null || !health.IsMainCharacterHealth)
                {
                    return;
                }

                CharacterMainControl player = CharacterMainControl.Main;
                if (player == null || player.Health == null || player.Health != health)
                {
                    return;
                }

                if (!HasCharmInInventory() || health.MaxHealth <= 0.01f)
                {
                    return;
                }

                float healthRatio = health.CurrentHealth / health.MaxHealth;
                if (healthRatio >= PeaceCharmConfig.TRIGGER_HEALTH_RATIO)
                {
                    return;
                }

                if (UnityEngine.Random.value > PeaceCharmConfig.TRIGGER_CHANCE)
                {
                    return;
                }

                triggeredThisScene = true;
                if (health.CurrentHealth <= 0f)
                {
                    health.SetHealth(1f);
                }

                health.SetHealth(health.MaxHealth);
                ShowWarmthBubble(player);
                ModBehaviour.DevLog("[PeaceCharmRuntime] Triggered emergency heal");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PeaceCharmRuntime] OnPlayerHurt failed: " + e.Message);
            }
        }

        private static bool HasCharmInInventory()
        {
            float now = Time.realtimeSinceStartup;
            if (now < nextInventoryCheckTime)
            {
                return cachedHasCharm;
            }

            cachedHasCharm = ItemFactory.GetItemCountInInventory(PeaceCharmConfig.TYPE_ID) > 0;
            nextInventoryCheckTime = now + InventoryCacheInterval;
            return cachedHasCharm;
        }

        private static void ShowWarmthBubble(CharacterMainControl player)
        {
            try
            {
                if (player == null)
                {
                    return;
                }

                DialogueBubblesManager.Show(PeaceCharmConfig.GetWarmthBubbleText(), player.transform, 2.5f, false, false, -1f, 2.5f);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PeaceCharmRuntime] ShowWarmthBubble failed: " + e.Message);
            }
        }
    }
}
