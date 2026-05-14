// ============================================================================
// ZombieModeRewardEffectsAndNpc.cs - 丧尸模式奖励效果与真实 NPC
// ============================================================================

using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Duckov.Buffs;
using Duckov.UI;
using ItemStatsSystem;
using ItemStatsSystem.Items;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using BossRush.Utils;

namespace BossRush
{
    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        private bool ApplyZombieModeReward(ZombieModeRewardType rewardType)
        {
            bool bossNode = zombieModeRunState.CurrentRewardNode != null && zombieModeRunState.CurrentRewardNode.BossNode;
            switch (rewardType)
            {
                case ZombieModeRewardType.PurificationPoints:
                {
                    int points = CalculateZombieModePurificationRewardPoints(bossNode);
                    zombieModeRunState.PurificationPoints += points;
                    NotificationText.Push(string.Format(L10n.T("BossRush_ZombieMode_Notify_RewardGranted"), points));
                    return true;
                }

                case ZombieModeRewardType.Heal:
                {
                    CharacterMainControl player = CharacterMainControl.Main;
                    if (player != null && player.Health != null)
                    {
                        player.Health.SetHealth(player.Health.MaxHealth);
                    }
                    NotificationText.Push(L10n.T("BossRush_ZombieMode_Reward_Heal"));
                    return true;
                }

                case ZombieModeRewardType.AttributeMaxHealth:
                    ApplyZombieModeAttributeReward(ZombieModeAttributeMaxHealthKey, 0.10f, 1.00f);
                    return true;
                case ZombieModeRewardType.AttributeMoveSpeed:
                    ApplyZombieModeAttributeReward(ZombieModeAttributeMoveSpeedKey, 0.05f, 0.30f);
                    return true;
                case ZombieModeRewardType.AttributeMeleeDamage:
                    ApplyZombieModeAttributeReward(ZombieModeAttributeMeleeDamageKey, 0.12f, 1.20f);
                    return true;
                case ZombieModeRewardType.AttributeRangedDamage:
                    ApplyZombieModeAttributeReward(ZombieModeAttributeRangedDamageKey, 0.10f, 1.00f);
                    return true;
                case ZombieModeRewardType.AttributeReloadSpeed:
                    ApplyZombieModeAttributeReward(ZombieModeAttributeReloadSpeedKey, 0.10f, 0.80f);
                    return true;
                case ZombieModeRewardType.AttributeDamageReduction:
                    ApplyZombieModeAttributeReward(ZombieModeAttributeDamageReductionKey, -0.05f, -0.40f);
                    return true;

                case ZombieModeRewardType.TempMerchant:
                    GrantZombieModeMerchantPurchaseGuarantee();
                    return true;
                case ZombieModeRewardType.TempNurse:
                case ZombieModeRewardType.TempGoblinNpc:
                case ZombieModeRewardType.TempNurseNpc:
                case ZombieModeRewardType.TempCourierNpc:
                    return true;

                case ZombieModeRewardType.RandomMeleeWeapon:
                    return GrantZombieModeRandomMeleeReward(bossNode);
                case ZombieModeRewardType.RandomGunWithAmmo:
                    return GrantZombieModeRandomGunWithAmmoReward(bossNode);
                case ZombieModeRewardType.AmmoSupply:
                    return GrantZombieModeAmmoSupplyReward();
                case ZombieModeRewardType.MedicalSupply:
                    return GrantZombieModeMedicalSupplyReward();
                case ZombieModeRewardType.ArmorOrHelmet:
                    return GrantZombieModeArmorOrHelmetReward(bossNode);
                case ZombieModeRewardType.FortificationPack:
                    return GrantZombieModeFortificationPack(bossNode);

                case ZombieModeRewardType.ContractPollutionDeal:
                    return ApplyZombieModeContractPollutionDeal(bossNode);
                case ZombieModeRewardType.ContractGearDeal:
                    return ApplyZombieModeContractGearDeal(bossNode);
                case ZombieModeRewardType.ContractHugePurification:
                    return ApplyZombieModeContractHugePurification();
                case ZombieModeRewardType.ContractInsurance:
                    return ApplyZombieModeContractInsurance();
                case ZombieModeRewardType.ContractDevilBargain:
                case ZombieModeRewardType.ContractCursedReload:
                case ZombieModeRewardType.ContractBloodPrice:
                case ZombieModeRewardType.ContractCursePool:
                    return ApplyZombieModePhase2ContractReward(rewardType, bossNode);

                case ZombieModeRewardType.InsuranceKeepOne:
                    ApplyZombieModeInsuranceReward(0.10f, true);
                    return true;
                case ZombieModeRewardType.InsuranceRandom10:
                    ApplyZombieModeInsuranceReward(0.10f, false);
                    return true;
                case ZombieModeRewardType.InsuranceRandom20:
                    ApplyZombieModeInsuranceReward(0.20f, false);
                    return true;
                case ZombieModeRewardType.InsuranceNearFull:
                    zombieModeRunState.PollutionFromContracts += 5;
                    ApplyZombieModeInsuranceReward(0.80f, false);
                    return true;

                case ZombieModeRewardType.MapEventHighValueAirdrop:
                case ZombieModeRewardType.MapEventEliteSquad:
                    ApplyZombieModeMapEventReward(rewardType);
                    return true;

                case ZombieModeRewardType.ProjectilePenetration:
                case ZombieModeRewardType.ProjectileBurn:
                case ZombieModeRewardType.ProjectileCold:
                case ZombieModeRewardType.ProjectilePoison:
                case ZombieModeRewardType.ProjectileArmorBreak:
                case ZombieModeRewardType.ProjectileTrident:
                case ZombieModeRewardType.ProjectileShotgunSpray:
                case ZombieModeRewardType.ProjectileStasis:
                case ZombieModeRewardType.ProjectileRicochet:
                case ZombieModeRewardType.ProjectileFork:
                case ZombieModeRewardType.ProjectileReturn:
                case ZombieModeRewardType.ProjectileHelix:
                case ZombieModeRewardType.ProjectileTrail:
                case ZombieModeRewardType.TriggerLifesteal:
                case ZombieModeRewardType.TriggerLifestealMedium:
                case ZombieModeRewardType.TriggerLifestealLarge:
                case ZombieModeRewardType.TriggerCritBurst:
                case ZombieModeRewardType.TriggerPurificationSiphon:
                case ZombieModeRewardType.TriggerSecondWind:
                case ZombieModeRewardType.TriggerDoomPulse:
                case ZombieModeRewardType.MutatorCritFocus:
                case ZombieModeRewardType.MutatorBulletTime:
                case ZombieModeRewardType.MutatorGuardianShield:
                case ZombieModeRewardType.MutatorQuickReload:
                case ZombieModeRewardType.MutatorDashBoost:
                case ZombieModeRewardType.BattlefieldAmmoRain:
                case ZombieModeRewardType.BattlefieldPurgeAura:
                case ZombieModeRewardType.BattlefieldCurseTrap:
                case ZombieModeRewardType.BattlefieldBlackHole:
                case ZombieModeRewardType.BattlefieldGravityDrag:
                    return ApplyZombieModeOptionReward(rewardType);

                case ZombieModeRewardType.NextNodeFreeRefresh:
                    zombieModeRunState.PendingFreeRefreshNextNode = Mathf.Clamp(
                        zombieModeRunState.PendingFreeRefreshNextNode + 1,
                        0,
                        ZombieModeTuning.FreeRefreshCapPerNode);
                    NotificationText.Push(L10n.T("BossRush_ZombieMode_Reward_NextNodeFreeRefresh"));
                    return true;
                case ZombieModeRewardType.HalfPricePaidRefresh:
                    zombieModeRunState.HalfPriceNextPaidRefresh = true;
                    NotificationText.Push(L10n.T("BossRush_ZombieMode_Reward_HalfPricePaidRefresh"));
                    return true;
                case ZombieModeRewardType.CurrentNodeFreeRefresh:
                {
                    if (zombieModeRunState.FreeRefreshesRemainingCurrentNode >= ZombieModeTuning.FreeRefreshCapPerNode)
                    {
                        zombieModeRunState.PurificationPoints += 30;
                        NotificationText.Push(string.Format(L10n.T("BossRush_ZombieMode_Notify_RewardGranted"), 30));
                    }
                    else
                    {
                        zombieModeRunState.FreeRefreshesRemainingCurrentNode = Mathf.Clamp(
                            zombieModeRunState.FreeRefreshesRemainingCurrentNode + 1,
                            0,
                            ZombieModeTuning.FreeRefreshCapPerNode);
                        NotificationText.Push(L10n.T("BossRush_ZombieMode_Reward_CurrentNodeFreeRefresh"));
                    }
                    return true;
                }

                case ZombieModeRewardType.RandomHighQualityItem:
                {
                    int typeId = FindRandomItemTypeByTags(null, 4, ZombieModeTuning.StarterMaxQuality + 1);
                    if (TryGiveZombieModeItemToPlayerOrDrop(typeId))
                    {
                        NotificationText.Push(L10n.T("BossRush_ZombieMode_Reward_RandomHighQualityItem"));
                        return true;
                    }
                    GrantZombieModeFallbackPurificationReward("RandomHighQualityItem", 120);
                    return true;
                }

                case ZombieModeRewardType.StarterReroll:
                {
                    bool granted = false;
                    if (zombieModeRunState.StarterLoadout == ZombieModeStarterLoadout.Gunner)
                    {
                        granted = TryGiveRandomItemByTags(ZombieModeRewardTagGun, 2, ZombieModeTuning.StarterMaxQuality);
                    }
                    else if (zombieModeRunState.StarterLoadout == ZombieModeStarterLoadout.Melee)
                    {
                        granted = TryGiveRandomItemByTags(ZombieModeRewardTagMeleeWeapon, 2, ZombieModeTuning.StarterMaxQuality);
                    }

                    granted |= TryGiveRandomItemByTags(ZombieModeRewardTagWeapon, 2, ZombieModeTuning.StarterMaxQuality);
                    if (granted)
                    {
                        NotificationText.Push(L10n.T("BossRush_ZombieMode_Reward_StarterReroll"));
                        return true;
                    }
                    GrantZombieModeFallbackPurificationReward("StarterReroll", 80);
                    return true;
                }

                case ZombieModeRewardType.RandomSupply:
                default:
                    bool supplyGranted = false;
                    supplyGranted |= TryGiveRandomItemByTags(ZombieModeRewardTagMedic, 1, 4);
                    supplyGranted |= TryGiveRandomItemByTags(ZombieModeRewardTagMedical, 1, 4);
                    supplyGranted |= TryGiveRandomItemByTags(ZombieModeRewardTagHealing, 1, 4);
                    supplyGranted |= TryGiveRandomItemByTags(ZombieModeRewardTagAmmo, 1, 4);
                    supplyGranted |= TryGiveRandomItemByTags(ZombieModeRewardTagBullet, 1, 4);
                    if (supplyGranted)
                    {
                        NotificationText.Push(L10n.T("BossRush_ZombieMode_Reward_RandomSupply"));
                    }
                    else
                    {
                        GrantZombieModeFallbackPurificationReward("RandomSupply", 60);
                    }
                    return true;
            }
        }

        private string GetZombieModePendingTemporaryNpcServiceType(ZombieModeRewardType rewardType)
        {
            if (rewardType == ZombieModeRewardType.TempMerchant)
            {
                return "Merchant";
            }

            if (rewardType == ZombieModeRewardType.TempNurse)
            {
                return "Nurse";
            }

            return string.Empty;
        }

        private void SpawnZombieModeTemporaryRealNpc(int runId, string npcType)
        {
            if (!IsZombieModeRunValid(runId) || string.IsNullOrEmpty(npcType))
            {
                return;
            }

            if (FindZombieModeTemporaryRealNpc(npcType) != null)
            {
                return;
            }

            GameObject npc = CreateZombieModeTemporaryRealNpc(npcType);
            if (npc == null)
            {
                GrantZombieModeFallbackPurificationReward("TempRealNpcSpawnFail_" + npcType, 120);
                return;
            }

            AttachZombieModeTemporaryRealNpcMarker(npc, runId, npcType);
            ApplyZombieModeTemporaryNpcProtection(npc, runId, npcType);

            ZombieModeTemporaryRealNpcRecord record = new ZombieModeTemporaryRealNpcRecord();
            record.GameObject = npc;
            record.NpcType = npcType;
            record.SpawnWave = zombieModeRunState.CurrentWave;
            record.SafeZoneBound = zombieModeRunState.ActiveSafeZoneActive;
            zombieModeRunState.TemporaryRealNpcs.Add(record);
            RegisterZombieModeRunOnlyObject(runId, ZombieModeRunOnlyObjectKind.TemporaryNpc, npc, npc, () => CloseZombieModeTemporaryRealNpcServices(npc));

            string key = "BossRush_ZombieMode_Npc_TempGoblinNpc";
            if (string.Equals(npcType, "NurseNpc", System.StringComparison.Ordinal))
            {
                key = "BossRush_ZombieMode_Npc_TempNurseNpcReal";
            }
            else if (string.Equals(npcType, "Courier", System.StringComparison.Ordinal))
            {
                key = "BossRush_ZombieMode_Npc_TempCourierNpc";
            }

            NotificationText.Push(L10n.T(key));
        }

        private GameObject CreateZombieModeTemporaryRealNpc(string npcType)
        {
            Vector3 spawnPos = GetZombieModeTemporaryRealNpcAnchorPosition();

            if (string.Equals(npcType, "Goblin", System.StringComparison.Ordinal))
            {
                return CreateZombieModeTemporaryGoblinNpc(spawnPos);
            }

            if (string.Equals(npcType, "NurseNpc", System.StringComparison.Ordinal))
            {
                return CreateZombieModeTemporaryNurseNpc(spawnPos);
            }

            if (string.Equals(npcType, "Courier", System.StringComparison.Ordinal))
            {
                return CreateZombieModeTemporaryCourierNpc(spawnPos);
            }

            return null;
        }

        private Vector3 GetZombieModeTemporaryRealNpcAnchorPosition()
        {
            CharacterMainControl player = CharacterMainControl.Main;
            Vector3 center = zombieModeRunState.ActiveSafeZoneActive
                ? zombieModeRunState.ActiveSafeZoneCenter
                : (player != null ? player.transform.position + player.transform.forward * 3f : Vector3.zero);
            int existingCount = zombieModeRunState.TemporaryNpcs.Count + zombieModeRunState.TemporaryRealNpcs.Count;
            float angle = existingCount * 72f;
            Vector3 offset = Quaternion.Euler(0f, angle, 0f) * Vector3.forward * 2.8f;
            Vector3 spawnPos = center + offset + Vector3.up * 0.05f;

            Vector3 resolved;
            if (SpawnPositionHelper.TrySampleNavMesh(
                    spawnPos,
                    out resolved,
                    ZombieModeTuning.NavMeshLiftOffset,
                    ZombieModeTuning.NavMeshSafeZoneRadius))
            {
                spawnPos = resolved;
            }
            else
            {
                RaycastHit hit;
                if (Physics.Raycast(spawnPos + Vector3.up * 1f, Vector3.down, out hit, 8f))
                {
                    spawnPos = hit.point + new Vector3(0f, 0.1f, 0f);
                }
            }

            return spawnPos;
        }

        private GameObject CreateZombieModeTemporaryGoblinNpc(Vector3 spawnPos)
        {
            if (!LoadGoblinAssetBundle() || goblinPrefab == null)
            {
                return null;
            }

            GameObject npc = UnityEngine.Object.Instantiate(goblinPrefab, spawnPos, Quaternion.identity);
            npc.name = "ZombieMode_TemporaryRealNpc_Goblin";
            npc.SetActive(true);
            foreach (Transform child in npc.GetComponentsInChildren<Transform>(true))
            {
                child.gameObject.SetActive(true);
            }

            NPCCommonUtils.FixShaders(npc, "[ZombieModeTempGoblin]");
            NPCCommonUtils.SetLayerRecursively(npc, LayerMask.NameToLayer("Default"));

            GoblinNPCController controller = npc.GetComponent<GoblinNPCController>();
            if (controller == null)
            {
                controller = npc.AddComponent<GoblinNPCController>();
            }

            GoblinMovement movement = npc.GetComponent<GoblinMovement>();
            if (movement == null)
            {
                movement = npc.AddComponent<GoblinMovement>();
            }

            movement.StopMove();
            movement.enabled = false;
            controller.EnterStationaryIdleState();

            GoblinInteractable interactable = npc.GetComponent<GoblinInteractable>();
            if (interactable == null)
            {
                interactable = npc.AddComponent<GoblinInteractable>();
            }

            return npc;
        }

        private GameObject CreateZombieModeTemporaryNurseNpc(Vector3 spawnPos)
        {
            if (!LoadNurseAssetBundle() || nursePrefab == null)
            {
                return null;
            }

            GameObject npc = UnityEngine.Object.Instantiate(nursePrefab, spawnPos, Quaternion.identity);
            npc.name = "ZombieMode_TemporaryRealNpc_Nurse";
            npc.SetActive(true);
            foreach (Transform child in npc.GetComponentsInChildren<Transform>(true))
            {
                child.gameObject.SetActive(true);
            }

            NPCCommonUtils.FixShaders(npc, "[ZombieModeTempNurse]");
            NPCCommonUtils.SetLayerRecursively(npc, LayerMask.NameToLayer("Default"));

            NurseNPCController controller = npc.GetComponent<NurseNPCController>();
            if (controller == null)
            {
                controller = npc.AddComponent<NurseNPCController>();
            }

            NurseMovement movement = npc.GetComponent<NurseMovement>();
            if (movement == null)
            {
                movement = npc.AddComponent<NurseMovement>();
            }

            movement.StopMove();
            movement.enabled = false;

            NurseInteractable interactable = npc.GetComponent<NurseInteractable>();
            if (interactable == null)
            {
                interactable = npc.AddComponent<NurseInteractable>();
            }

            return npc;
        }

        private GameObject CreateZombieModeTemporaryCourierNpc(Vector3 spawnPos)
        {
            if (!LoadCourierAssetBundle() || courierPrefab == null)
            {
                return null;
            }

            GameObject npc = UnityEngine.Object.Instantiate(courierPrefab, spawnPos, Quaternion.identity);
            npc.name = "ZombieMode_TemporaryRealNpc_Courier";
            npc.SetActive(true);
            foreach (Transform child in npc.GetComponentsInChildren<Transform>(true))
            {
                child.gameObject.SetActive(true);
            }

            NPCCommonUtils.FixShaders(npc, "[ZombieModeTempCourier]");
            NPCCommonUtils.SetLayerRecursively(npc, LayerMask.NameToLayer("Default"));

            CourierNPCController controller = npc.GetComponent<CourierNPCController>();
            if (controller == null)
            {
                controller = npc.AddComponent<CourierNPCController>();
            }

            CourierMovement movement = npc.GetComponent<CourierMovement>();
            if (movement == null)
            {
                movement = npc.AddComponent<CourierMovement>();
            }

            movement.SetStationary(true);
            controller.SetStationary(true);
            controller.StartTalking(false);
            AddCourierInteraction(npc);
            return npc;
        }

        private void AttachZombieModeTemporaryRealNpcMarker(GameObject npc, int runId, string npcType)
        {
            if (npc == null)
            {
                return;
            }

            ZombieModeTemporaryRealNpcMarker marker = npc.GetComponent<ZombieModeTemporaryRealNpcMarker>();
            if (marker == null)
            {
                marker = npc.AddComponent<ZombieModeTemporaryRealNpcMarker>();
            }

            marker.RunId = runId;
            marker.NpcType = npcType ?? string.Empty;
            marker.UsesPurificationPayment = true;
        }

        private ZombieModeTemporaryRealNpcRecord FindZombieModeTemporaryRealNpc(string npcType)
        {
            for (int i = 0; i < zombieModeRunState.TemporaryRealNpcs.Count; i++)
            {
                ZombieModeTemporaryRealNpcRecord npc = zombieModeRunState.TemporaryRealNpcs[i];
                if (npc != null &&
                    npc.GameObject != null &&
                    string.Equals(npc.NpcType, npcType, System.StringComparison.Ordinal))
                {
                    return npc;
                }
            }

            return null;
        }

        public bool IsZombieModeTemporaryRealNpc(Component component)
        {
            if (component == null)
            {
                return false;
            }

            ZombieModeTemporaryRealNpcMarker marker = component.GetComponentInParent<ZombieModeTemporaryRealNpcMarker>();
            return marker != null &&
                   marker.UsesPurificationPayment &&
                   IsZombieModeRunValid(marker.RunId);
        }

        public bool CanAffordZombieModePurificationPointsForRealNpc(Component component, int cost)
        {
            if (!IsZombieModeTemporaryRealNpc(component))
            {
                return false;
            }

            return cost <= 0 || zombieModeRunState.PurificationPoints >= cost;
        }

        public bool TrySpendZombieModePurificationPointsForRealNpc(Component component, int cost, string reason)
        {
            if (!IsZombieModeTemporaryRealNpc(component))
            {
                return false;
            }

            return SpendZombieModePurificationPoints(cost, reason);
        }

        public void RefundZombieModePurificationPointsForRealNpc(Component component, int cost, bool shouldRefund)
        {
            if (!shouldRefund || cost <= 0 || !IsZombieModeTemporaryRealNpc(component))
            {
                return;
            }

            zombieModeRunState.PurificationPoints += cost;
        }

        public int GetZombieModePurificationPointsForRealNpcUi(Component component)
        {
            return IsZombieModeTemporaryRealNpc(component)
                ? zombieModeRunState.PurificationPoints
                : 0;
        }

        public string GetZombieModeNpcHealCurrencyLabel(Component component, int cost)
        {
            return IsZombieModeTemporaryRealNpc(component)
                ? L10n.T("治疗（净化点 " + cost + "）", "Heal (Purification " + cost + ")")
                : L10n.T("治疗（￥" + cost + "）", "Heal ($" + cost + ")");
        }

        private void ApplyZombieModeAttributeReward(string key, float increment, float cap)
        {
            if (string.IsNullOrEmpty(key))
            {
                return;
            }

            float current = 0f;
            zombieModeRunState.AttributeBonuses.TryGetValue(key, out current);
            float next = increment < 0f
                ? Mathf.Max(cap, current + increment)
                : Mathf.Min(cap, current + increment);
            zombieModeRunState.AttributeBonuses[key] = next;
            ApplyZombieModePlayerAttributeModifiers();

            NotificationText.Push(string.Format(
                L10n.T("BossRush_ZombieMode_Notify_AttributeBonus"),
                GetZombieModeAttributeDisplayName(key),
                Mathf.RoundToInt(Mathf.Abs(next) * 100f)));
        }

        private string GetZombieModeAttributeDisplayName(string key)
        {
            if (key == ZombieModeAttributeMaxHealthKey)
            {
                return L10n.T("BossRush_ZombieMode_Reward_AttributeName_MaxHealth");
            }
            if (key == ZombieModeAttributeMoveSpeedKey ||
                key == ZombieModeAttributeWalkSpeedKey ||
                key == ZombieModeAttributeRunSpeedKey)
            {
                return L10n.T("BossRush_ZombieMode_Reward_AttributeName_MoveSpeed");
            }
            if (key == ZombieModeAttributeMeleeDamageKey)
            {
                return L10n.T("BossRush_ZombieMode_Reward_AttributeName_MeleeDamage");
            }
            if (key == ZombieModeAttributeRangedDamageKey)
            {
                return L10n.T("BossRush_ZombieMode_Reward_AttributeName_RangedDamage");
            }
            if (key == ZombieModeAttributeReloadSpeedKey)
            {
                return L10n.T("BossRush_ZombieMode_Reward_AttributeName_ReloadSpeed");
            }
            if (key == ZombieModeAttributeDamageReductionKey)
            {
                return L10n.T("BossRush_ZombieMode_Reward_AttributeName_DamageReduction");
            }
            return key;
        }

        private void ApplyZombieModePlayerAttributeModifiers()
        {
            CharacterMainControl player = CharacterMainControl.Main;
            if (player == null || player.CharacterItem == null)
            {
                return;
            }

            float oldMaxHealth = player.Health != null ? player.Health.MaxHealth : -1f;
            RemoveZombieModeAttributeModifiers();
            foreach (KeyValuePair<string, float> pair in zombieModeRunState.AttributeBonuses)
            {
                if (Mathf.Approximately(pair.Value, 0f))
                {
                    continue;
                }

                if (pair.Key == ZombieModeAttributeMoveSpeedKey)
                {
                    AddZombieModeAttributeModifier(player, ZombieModeAttributeMoveSpeedKey, pair.Value);
                    AddZombieModeAttributeModifier(player, ZombieModeAttributeWalkSpeedKey, pair.Value);
                    AddZombieModeAttributeModifier(player, ZombieModeAttributeRunSpeedKey, pair.Value);
                    continue;
                }

                AddZombieModeAttributeModifier(player, pair.Key, pair.Value);
            }

            if (player.Health != null && oldMaxHealth > 0f)
            {
                float delta = player.Health.MaxHealth - oldMaxHealth;
                if (delta > 0f)
                {
                    player.Health.SetHealth(Mathf.Min(player.Health.MaxHealth, player.Health.CurrentHealth + delta));
                }
            }
        }

        private void AddZombieModeAttributeModifier(CharacterMainControl player, string statName, float percent)
        {
            if (player == null || player.CharacterItem == null || string.IsNullOrEmpty(statName) || Mathf.Approximately(percent, 0f))
            {
                return;
            }

            try
            {
                bool added = RuntimeStatModifierTracker.TryAdd(
                    player,
                    statName,
                    percent,
                    this,
                    zombieModeRunState.AttributeModifierRecords,
                    "ZombieMode Reward Attribute");
                if (!added)
                {
                    return;
                }

                if (!zombieModeRunState.AttributeModifierCleanupRegistered)
                {
                    zombieModeRunState.AttributeModifierCleanupRegistered = true;
                    RegisterZombieModeRunOnlyObject(zombieModeRunState.RunId, ZombieModeRunOnlyObjectKind.Buff, null, player.CharacterItem, RemoveZombieModeAttributeModifiers);
                }
            }
            catch (System.Exception e)
            {
                DevLog("[ZombieMode] [WARNING] Attribute Modifier 注册失败: " + e.Message);
            }
        }

        private void RemoveZombieModeAttributeModifiers()
        {
            RuntimeStatModifierTracker.RemoveAll(
                zombieModeRunState.AttributeModifierRecords,
                "ZombieMode Reward Attribute");
            zombieModeRunState.AttributeModifierCleanupRegistered = false;
        }

        private void GrantZombieModeMerchantPurchaseGuarantee()
        {
            zombieModeRunState.GuaranteedMerchantPurchasePending = true;
            zombieModeRunState.GuaranteedMerchantPurchaseMinQuality = 6;
            NotificationText.Push(L10n.T("BossRush_ZombieMode_Notify_TempMerchantGuarantee"));
        }

        private string[] GetZombieModeMerchantGrantTagAliases(string grantTag)
        {
            if (string.IsNullOrEmpty(grantTag))
            {
                return new string[0];
            }

            if (string.Equals(grantTag, "Medical", System.StringComparison.Ordinal) ||
                string.Equals(grantTag, "Medic", System.StringComparison.Ordinal) ||
                string.Equals(grantTag, "Healing", System.StringComparison.Ordinal))
            {
                return new string[] { "Medic", "Medical", "Consumable", "Healing", "Injector" };
            }

            if (string.Equals(grantTag, "Armor", System.StringComparison.Ordinal) ||
                string.Equals(grantTag, "Helmet", System.StringComparison.Ordinal))
            {
                return new string[] { "Armor", "Helmat", "Helmet" };
            }

            if (string.Equals(grantTag, "Mask", System.StringComparison.Ordinal) ||
                string.Equals(grantTag, "FaceMask", System.StringComparison.Ordinal) ||
                string.Equals(grantTag, "Headset", System.StringComparison.Ordinal))
            {
                return new string[] { "Headset", "Mask", "FaceMask" };
            }

            return new string[] { grantTag };
        }

        private string GetZombieModeMerchantModeECategorySuffix(string grantTag)
        {
            if (string.IsNullOrEmpty(grantTag))
            {
                return string.Empty;
            }

            if (string.Equals(grantTag, "Gun", System.StringComparison.Ordinal))
            {
                return "Gun";
            }

            if (string.Equals(grantTag, "MeleeWeapon", System.StringComparison.Ordinal))
            {
                return "Melee";
            }

            if (string.Equals(grantTag, "Ammo", System.StringComparison.Ordinal) ||
                string.Equals(grantTag, "Bullet", System.StringComparison.Ordinal))
            {
                return "Bullet";
            }

            if (string.Equals(grantTag, "Armor", System.StringComparison.Ordinal))
            {
                return "Armor";
            }

            if (string.Equals(grantTag, "Helmet", System.StringComparison.Ordinal))
            {
                return "Helmat";
            }

            if (string.Equals(grantTag, "Food", System.StringComparison.Ordinal))
            {
                return "Food";
            }

            if (string.Equals(grantTag, "Medic", System.StringComparison.Ordinal) ||
                string.Equals(grantTag, "Medical", System.StringComparison.Ordinal) ||
                string.Equals(grantTag, "Healing", System.StringComparison.Ordinal))
            {
                return "Medical";
            }

            return string.Empty;
        }

        private void SpawnZombieModeTemporaryNpc(int runId, string serviceType, bool bossNodeStock)
        {
            if (!IsZombieModeRunValid(runId))
            {
                return;
            }

            GameObject npc = CreateZombieModeTemporaryServiceTerminal(runId, serviceType);
            if (npc == null)
            {
                return;
            }

            ZombieModeTemporaryNpcInteractable interactable = npc.GetComponent<ZombieModeTemporaryNpcInteractable>();
            ApplyZombieModeTemporaryNpcProtection(npc, runId, serviceType);

            ZombieModeTemporaryNpc record = CreateZombieModeTemporaryNpcRecord(npc, serviceType, bossNodeStock);
            zombieModeRunState.TemporaryNpcs.Add(record);
            RegisterZombieModeRunOnlyObject(runId, ZombieModeRunOnlyObjectKind.TemporaryNpc, npc, interactable, null);

            string key = string.Equals(serviceType, "Nurse", System.StringComparison.Ordinal)
                ? "BossRush_ZombieMode_Npc_TempNurse"
                : "BossRush_ZombieMode_Npc_TempMerchant";
            NotificationText.Push(L10n.T(key));
        }

        private GameObject CreateZombieModeTemporaryServiceTerminal(int runId, string serviceType)
        {
            CharacterMainControl player = CharacterMainControl.Main;
            Vector3 center = zombieModeRunState.ActiveSafeZoneActive
                ? zombieModeRunState.ActiveSafeZoneCenter
                : (player != null ? player.transform.position + player.transform.forward * 3f : Vector3.zero);
            int existingCount = zombieModeRunState.TemporaryNpcs.Count;
            float angle = ZombieModeNpcCatalog.NpcAngleArrangement[existingCount % ZombieModeNpcCatalog.NpcAngleArrangement.Length];
            Vector3 offset = Quaternion.Euler(0f, angle, 0f) * Vector3.forward * 2.4f;

            GameObject terminal = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            terminal.name = "ZombieMode_TemporaryNpc_" + serviceType;
            terminal.transform.position = center + offset + Vector3.up * 0.05f;
            if (player != null)
            {
                Vector3 look = player.transform.position - terminal.transform.position;
                look.y = 0f;
                if (look.sqrMagnitude > 0.01f)
                {
                    terminal.transform.rotation = Quaternion.LookRotation(look.normalized);
                }
            }

            Renderer renderer = terminal.GetComponent<Renderer>();
            if (renderer != null)
            {
                SetZombieModeRendererColor(
                    renderer,
                    string.Equals(serviceType, "Nurse", System.StringComparison.Ordinal)
                        ? new Color(0.85f, 0.25f, 0.28f, 0.95f)
                        : new Color(0.25f, 0.65f, 0.35f, 0.95f));
            }

            ZombieModeTemporaryNpcInteractable interactable = terminal.GetComponent<ZombieModeTemporaryNpcInteractable>();
            if (interactable == null)
            {
                interactable = terminal.AddComponent<ZombieModeTemporaryNpcInteractable>();
            }
            interactable.Initialize(runId, serviceType);
            return terminal;
        }

        private ZombieModeTemporaryNpc CreateZombieModeTemporaryNpcRecord(GameObject npc, string serviceType, bool bossNodeStock)
        {
            ZombieModeTemporaryNpc record = new ZombieModeTemporaryNpc();
            record.GameObject = npc;
            record.ServiceType = serviceType;
            record.SpawnWave = zombieModeRunState.CurrentWave;
            record.ServiceState = CreateZombieModeNpcServiceState(serviceType, bossNodeStock, zombieModeRunState.ActiveSafeZoneActive);
            return record;
        }

        private void ApplyZombieModeTemporaryNpcProtection(GameObject npc, int runId, string serviceType)
        {
            if (npc == null)
            {
                return;
            }

            ZombieModeTemporaryNpcProtectionMarker marker = npc.GetComponent<ZombieModeTemporaryNpcProtectionMarker>();
            if (marker == null)
            {
                marker = npc.AddComponent<ZombieModeTemporaryNpcProtectionMarker>();
            }

            marker.RunId = runId;
            marker.ServiceType = serviceType ?? string.Empty;
            TrySetZombieModeTemporaryNpcInvincible(npc);
            ClearZombieModeTemporaryNpcThreatTargets();
        }

        private void TrySetZombieModeTemporaryNpcInvincible(GameObject npc)
        {
            if (npc == null)
            {
                return;
            }

            Health[] healths = npc.GetComponentsInChildren<Health>(true);
            for (int i = 0; i < healths.Length; i++)
            {
                Health health = healths[i];
                if (health == null)
                {
                    continue;
                }

                try
                {
                    health.SetInvincible(true);
                    health.SetHealth(health.MaxHealth);
                }
                catch (System.Exception e)
                {
                    DevLog("[ZombieMode] Heal 设置无敌+血量失败: " + e.Message);
                }
            }
        }

        private void TickZombieModeTemporaryNpcProtection()
        {
            if (!IsZombieModeActive ||
                (zombieModeRunState.TemporaryNpcs.Count <= 0 && zombieModeRunState.TemporaryRealNpcs.Count <= 0))
            {
                return;
            }

            if (Time.unscaledTime - zombieModeRunState.LastTemporaryNpcProtectionTickTime <
                ZombieModeTuning.TemporaryNpcProtectionTickIntervalSeconds)
            {
                return;
            }
            zombieModeRunState.LastTemporaryNpcProtectionTickTime = Time.unscaledTime;

            for (int i = zombieModeRunState.TemporaryNpcs.Count - 1; i >= 0; i--)
            {
                ZombieModeTemporaryNpc npc = zombieModeRunState.TemporaryNpcs[i];
                if (npc == null || npc.GameObject == null)
                {
                    zombieModeRunState.TemporaryNpcs.RemoveAt(i);
                    continue;
                }

                ApplyZombieModeTemporaryNpcProtection(npc.GameObject, zombieModeRunState.RunId, npc.ServiceType);
            }

            for (int i = zombieModeRunState.TemporaryRealNpcs.Count - 1; i >= 0; i--)
            {
                ZombieModeTemporaryRealNpcRecord npc = zombieModeRunState.TemporaryRealNpcs[i];
                if (npc == null || npc.GameObject == null)
                {
                    zombieModeRunState.TemporaryRealNpcs.RemoveAt(i);
                    continue;
                }

                ApplyZombieModeTemporaryNpcProtection(npc.GameObject, zombieModeRunState.RunId, npc.NpcType);
            }

            ClearZombieModeTemporaryNpcThreatTargets();
        }

        private void ClearZombieModeTemporaryNpcThreatTargets()
        {
            for (int i = 0; i < zombieModeRunState.RunOnlyObjects.Count; i++)
            {
                ZombieModeRunOnlyRecord record = zombieModeRunState.RunOnlyObjects[i];
                if (record == null ||
                    (record.Kind != ZombieModeRunOnlyObjectKind.Enemy && record.Kind != ZombieModeRunOnlyObjectKind.Boss) ||
                    record.GameObject == null)
                {
                    continue;
                }

                AICharacterController ai = record.GameObject.GetComponentInChildren<AICharacterController>();
                if (ai == null || !IsZombieModeTemporaryNpcDamageReceiver(ai.searchedEnemy))
                {
                    continue;
                }

                ai.searchedEnemy = null;
                ai.noticed = false;
                if (ShouldZombieModeEnemyAggroPlayerNow())
                {
                    SetZombieModeEnemyTargetToMainPlayer(ai);
                }
            }
        }

        private bool IsZombieModeTemporaryNpcDamageReceiver(DamageReceiver receiver)
        {
            if (receiver == null)
            {
                return false;
            }

            try
            {
                if (receiver.GetComponentInParent<ZombieModeTemporaryNpcProtectionMarker>() != null)
                {
                    return true;
                }

                if (receiver.health != null)
                {
                    CharacterMainControl character = receiver.health.TryGetCharacter();
                    return character != null &&
                           character.GetComponentInParent<ZombieModeTemporaryNpcProtectionMarker>() != null;
                }
            }
            catch (System.Exception e)
            {
                DevLog("[ZombieMode] TemporaryNpcProtection 判定失败: " + e.Message);
            }

            return false;
        }

        private void SetZombieModeEnemyTargetToMainPlayer(AICharacterController ai)
        {
            if (ai == null)
            {
                return;
            }

            CharacterMainControl main = CharacterMainControl.Main;
            if (main == null || main.mainDamageReceiver == null)
            {
                ai.searchedEnemy = null;
                ai.noticed = false;
                return;
            }

            ai.searchedEnemy = main.mainDamageReceiver;
            ai.SetTarget(main.mainDamageReceiver.transform);
            ai.SetNoticedToTarget(main.mainDamageReceiver);
            ai.noticed = true;
        }

        private ZombieModeNpcServiceState CreateZombieModeNpcServiceState(string serviceType, bool bossNodeStock, bool safeZoneBound)
        {
            ZombieModeNpcServiceState state = new ZombieModeNpcServiceState();
            state.BossNodeStock = bossNodeStock;
            state.SafeZoneBound = safeZoneBound;
            if (string.Equals(serviceType, "Nurse", System.StringComparison.Ordinal))
            {
                ZombieModeNpcCatalog.NurseServiceEntry[] services = ZombieModeNpcCatalog.NurseServices;
                for (int i = 0; i < services.Length; i++)
                {
                    state.NurseUsesRemaining.Add(services[i].Uses);
                }
            }
            else
            {
                ZombieModeNpcCatalog.MerchantStockEntry[] stock = bossNodeStock
                    ? ZombieModeNpcCatalog.BossNodeStock
                    : ZombieModeNpcCatalog.NormalWaveStock;
                for (int i = 0; i < stock.Length; i++)
                {
                    state.MerchantStockRemaining.Add(stock[i].StockCount);
                }
            }

            return state;
        }
    }
}
