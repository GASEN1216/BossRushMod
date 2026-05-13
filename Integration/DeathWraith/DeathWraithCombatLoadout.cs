// ============================================================================
// DeathWraithSystem partial - extracted from DeathWraithSystem.cs
// ============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;
using Cysharp.Threading.Tasks;
using Duckov;
using Duckov.Economy;
using Duckov.Scenes;
using Duckov.Utilities;
using ItemStatsSystem;
using ItemStatsSystem.Data;
using ItemStatsSystem.Items;
using Saves;

namespace BossRush
{
    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        #region 亡魂系统 — 战斗配置

        private void InitializeWraithAI_DeathWraith(
            CharacterMainControl wraith,
            Vector3 spawnPos,
            WraithInfo info)
        {
            if (wraith == null)
            {
                return;
            }

            try
            {
                wraith.AudioVoiceType = info != null
                    ? info.playerVoiceType
                    : AudioManager.VoiceType.Duck;
                wraith.FootStepMaterialType = info != null
                    ? info.playerFootStepMaterialType
                    : AudioManager.FootStepMaterialType.organic;
            }
            catch { }

            try
            {
                if (wraith.aiCharacterController == null)
                {
                    Item preferredWeapon = SelectPreferredCombatWeaponItem_DeathWraith(wraith);
                    string presetName =
                        ResolveWraithHostPresetNameForWeapon_DeathWraith(preferredWeapon);
                    AICharacterController aiPrefab =
                        GetWraithHostAIPrefab_DeathWraith(presetName);
                    if (aiPrefab != null)
                    {
                        AICharacterController clonedAi = UnityEngine.Object.Instantiate(aiPrefab);
                        wraith.aiCharacterController = clonedAi;
                        DevLog("[DeathWraith] 已为亡魂克隆 AI 控制器: " + aiPrefab.name
                            + " | preset=" + presetName
                            + " | weapon=" + (preferredWeapon != null ? preferredWeapon.DisplayName : "<null>"));
                    }
                    else
                    {
                        DevLog("[DeathWraith] [WARNING] 未找到可用 AI 控制器预设，亡魂可能无法主动战斗");
                    }
                }

                if (wraith.aiCharacterController != null &&
                    wraith.aiCharacterController.CharacterMainControl != wraith)
                {
                    wraith.aiCharacterController.Init(
                        wraith,
                        spawnPos,
                        wraith.AudioVoiceType,
                        wraith.FootStepMaterialType);
                    DevLog("[DeathWraith] 亡魂 AI 初始化完成");
                    try
                    {
                        DevLog("[DeathWraith] AI 参数: reaction=" + wraith.aiCharacterController.baseReactionTime
                            + ", shootDelay=" + wraith.aiCharacterController.shootDelay);
                    }
                    catch { }

                    try
                    {
                        // 与项目中其他“自然感知”AI路径保持一致：不在出生时强制锁定玩家。
                        wraith.aiCharacterController.forceTracePlayerDistance = 0f;
                        wraith.aiCharacterController.searchedEnemy = null;
                        wraith.aiCharacterController.noticed = false;
                        DevLog("[DeathWraith] 亡魂 AI 保持自然感知，不设置初始仇恨目标");
                    }
                    catch (Exception aggroEx)
                    {
                        DevLog("[DeathWraith] 重置初始仇恨状态失败: " + aggroEx.Message);
                    }
                }
                else if (wraith.aiCharacterController != null)
                {
                    DevLog("[DeathWraith] 亡魂 AI 已绑定，无需重复初始化");
                }
            }
            catch (Exception e)
            {
                DevLog("[DeathWraith] 初始化亡魂 AI 失败: " + e.Message);
            }
        }

        private string ResolveWraithHostPresetNameForWeapon_DeathWraith(Item preferredWeapon)
        {
            if (IsGunItem_DeathWraith(preferredWeapon))
            {
                return DEATH_WRAITH_GUN_AI_PRESET_NAME;
            }

            return DEATH_WRAITH_MELEE_AI_PRESET_NAME;
        }

        private AICharacterController GetWraithHostAIPrefab_DeathWraith(string presetName)
        {
            EnsureWraithPresetCache_DeathWraith();

            try
            {
                CharacterRandomPreset forcedPreset;
                bool matchedForcedPreset =
                    TryGetWraithPresetByNameKey_DeathWraith(
                        presetName,
                        out forcedPreset) ||
                    TryGetWraithPresetByRuntimeName_DeathWraith(
                        presetName,
                        out forcedPreset);

                if (!matchedForcedPreset || forcedPreset == null)
                {
                    DevLog("[DeathWraith] [WARNING] 未找到固定 AI 宿主预设: "
                        + presetName);
                    return null;
                }

                AICharacterController forcedAiPrefab =
                    GetAIPrefabFromPreset_DeathWraith(forcedPreset);
                if (forcedAiPrefab == null)
                {
                    DevLog("[DeathWraith] [WARNING] 固定 AI 宿主预设缺少 AI 控制器: "
                        + forcedPreset.name
                        + " (nameKey=" + forcedPreset.nameKey + ")");
                    return null;
                }

                DevLog("[DeathWraith] 使用固定 AI 宿主预设: "
                    + forcedPreset.name
                    + " (nameKey=" + forcedPreset.nameKey
                    + ", ai=" + forcedAiPrefab.name + ")");
                return forcedAiPrefab;
            }
            catch (Exception e)
            {
                DevLog("[DeathWraith] 解析固定 AI 宿主预设失败: " + e.Message);
            }

            return null;
        }

        private static AICharacterController GetAIPrefabFromPreset_DeathWraith(CharacterRandomPreset preset)
        {
            if (preset == null || CharacterRandomPreset_AiControllerField == null)
            {
                return null;
            }

            try
            {
                return CharacterRandomPreset_AiControllerField.GetValue(preset) as AICharacterController;
            }
            catch
            {
                return null;
            }
        }

        private async UniTask PrepareWraithCombatLoadout_DeathWraith(CharacterMainControl wraith)
        {
            if (wraith == null)
            {
                return;
            }

            try
            {
                ForceRefreshWraithEquipmentAgents_DeathWraith(wraith);

                Item selectedWeapon = SelectPreferredCombatWeaponItem_DeathWraith(wraith);
                if (selectedWeapon == null)
                {
                    DevLog("[DeathWraith] [WARNING] 亡魂没有可用武器");
                    return;
                }

                wraith.ChangeHoldItem(selectedWeapon);
                await UniTask.Yield();

                ItemAgent_Gun gun = wraith.GetGun();
                if (gun != null)
                {
                    SyncWraithCombatMode_DeathWraith(wraith, false);
                    await EnsureWraithGunReady_DeathWraith(wraith, gun);
                    DevLog("[DeathWraith] 已切换为枪械作战: " + gun.Item.DisplayName);
                    return;
                }

                ItemAgent_MeleeWeapon melee = wraith.GetMeleeWeapon();
                if (melee == null && IsLikelyMeleeWeaponItem_DeathWraith(wraith, selectedWeapon))
                {
                    melee = EnsureWraithMeleeAgentReady_DeathWraith(wraith, selectedWeapon);
                }

                if (melee != null)
                {
                    SyncWraithCombatMode_DeathWraith(wraith, true);
                    DevLog("[DeathWraith] 已切换为近战作战: " + melee.Item.DisplayName);
                    return;
                }

                DevLog("[DeathWraith] [WARNING] 切换武器后仍未拿到枪械/近战代理");
            }
            catch (Exception e)
            {
                DevLog("[DeathWraith] 准备亡魂战斗装备失败: " + e.Message + "\n" + e.StackTrace);
            }
        }

        private ItemAgent_MeleeWeapon EnsureWraithMeleeAgentReady_DeathWraith(
            CharacterMainControl wraith,
            Item meleeItem)
        {
            if (wraith == null || meleeItem == null)
            {
                return null;
            }

            try
            {
                DuckovItemAgent holdAgent = wraith.CurrentHoldItemAgent;
                if (holdAgent == null)
                {
                    DevLog("[DeathWraith] [WARNING] 近战兜底失败：当前没有手持代理");
                    return null;
                }

                Item holdItem = null;
                try
                {
                    holdItem = holdAgent.Item;
                }
                catch
                {
                }

                if (holdItem != null && holdItem != meleeItem)
                {
                    DevLog("[DeathWraith] [WARNING] 近战兜底中止：当前手持物品与目标近战不一致");
                    return null;
                }

                ItemAgent_MeleeWeapon meleeAgent = holdAgent as ItemAgent_MeleeWeapon;
                if (meleeAgent == null)
                {
                    meleeAgent = holdAgent.GetComponent<ItemAgent_MeleeWeapon>();
                }

                bool addedAtRuntime = false;
                if (meleeAgent == null)
                {
                    meleeAgent = holdAgent.gameObject.AddComponent<ItemAgent_MeleeWeapon>();
                    addedAtRuntime = true;
                }

                CopyMeleeAgentDefaultsFromTemplate_DeathWraith(meleeAgent, meleeItem);

                if (ItemAgent_ItemField_DeathWraith != null)
                {
                    ItemAgent_ItemField_DeathWraith.SetValue(meleeAgent, meleeItem);
                }

                EnsureDuckovItemAgentSocketsInitialized_DeathWraith(holdAgent);
                EnsureDuckovItemAgentSocketsInitialized_DeathWraith(meleeAgent);

                if (ItemAgentMeleeWeapon_OnInitializeMethod_DeathWraith != null)
                {
                    try
                    {
                        ItemAgentMeleeWeapon_OnInitializeMethod_DeathWraith.Invoke(meleeAgent, null);
                    }
                    catch
                    {
                    }
                }

                meleeAgent.SetHolder(wraith);
                holdAgent.handheldSocket = meleeAgent.handheldSocket;
                holdAgent.handAnimationType = meleeAgent.handAnimationType;

                Transform weaponSocket = null;
                if (wraith.characterModel != null)
                {
                    weaponSocket = wraith.characterModel.RightHandSocket;
                    if (weaponSocket == null)
                    {
                        weaponSocket = wraith.characterModel.MeleeWeaponSocket;
                    }
                }

                if (weaponSocket != null)
                {
                    holdAgent.transform.SetParent(weaponSocket, false);
                    holdAgent.transform.localPosition = Vector3.zero;
                    holdAgent.transform.localRotation = Quaternion.identity;
                    if (wraith.agentHolder != null &&
                        ItemAgentHolder_CurrentUsingSocketCacheField_DeathWraith != null)
                    {
                        ItemAgentHolder_CurrentUsingSocketCacheField_DeathWraith.SetValue(
                            wraith.agentHolder,
                            weaponSocket);
                    }
                }

                if (wraith.agentHolder != null)
                {
                    if (ItemAgentHolder_MeleeRefField_DeathWraith != null)
                    {
                        ItemAgentHolder_MeleeRefField_DeathWraith.SetValue(wraith.agentHolder, meleeAgent);
                    }

                    if (ItemAgentHolder_GunRefField_DeathWraith != null)
                    {
                        ItemAgentHolder_GunRefField_DeathWraith.SetValue(wraith.agentHolder, null);
                    }
                }

                ItemAgent_MeleeWeapon resolved = wraith.GetMeleeWeapon();
                if (resolved != null)
                {
                    DevLog("[DeathWraith] 已补齐近战手持代理: "
                        + meleeItem.DisplayName
                        + " | runtimeAdded=" + addedAtRuntime);
                    return resolved;
                }

                DevLog("[DeathWraith] [WARNING] 近战代理兜底执行后仍未拿到 _meleeRef: "
                    + meleeItem.DisplayName);
            }
            catch (Exception e)
            {
                DevLog("[DeathWraith] 近战代理兜底失败: " + e.Message + "\n" + e.StackTrace);
            }

            return null;
        }

        private void CopyMeleeAgentDefaultsFromTemplate_DeathWraith(
            ItemAgent_MeleeWeapon target,
            Item meleeItem)
        {
            if (target == null)
            {
                return;
            }

            ItemAgent_MeleeWeapon template = null;
            try
            {
                if (meleeItem != null)
                {
                    template = meleeItem.GetComponent<ItemAgent_MeleeWeapon>();
                }
            }
            catch
            {
            }

            target.handheldSocket = template != null
                ? template.handheldSocket
                : HandheldSocketTypes.normalHandheld;
            target.handAnimationType = template != null
                ? template.handAnimationType
                : HandheldAnimationType.meleeWeapon;

            if (template != null)
            {
                if (template.hitFx != null)
                {
                    target.hitFx = template.hitFx;
                }

                if (template.slashFx != null)
                {
                    target.slashFx = template.slashFx;
                }

                if (ItemAgentMeleeWeapon_SlashFxDelayTimeField_DeathWraith != null)
                {
                    try
                    {
                        object slashDelay = ItemAgentMeleeWeapon_SlashFxDelayTimeField_DeathWraith.GetValue(template);
                        if (slashDelay != null)
                        {
                            ItemAgentMeleeWeapon_SlashFxDelayTimeField_DeathWraith.SetValue(target, slashDelay);
                        }
                    }
                    catch
                    {
                    }
                }
            }

            if (ItemAgentMeleeWeapon_SoundKeyField_DeathWraith != null)
            {
                try
                {
                    string soundKey = "Default";
                    if (template != null)
                    {
                        object rawKey = ItemAgentMeleeWeapon_SoundKeyField_DeathWraith.GetValue(template);
                        if (rawKey is string templateKey && !string.IsNullOrWhiteSpace(templateKey))
                        {
                            soundKey = templateKey;
                        }
                    }

                    ItemAgentMeleeWeapon_SoundKeyField_DeathWraith.SetValue(target, soundKey);
                }
                catch
                {
                }
            }
        }

        private void EnsureDuckovItemAgentSocketsInitialized_DeathWraith(DuckovItemAgent agent)
        {
            if (agent == null || DuckovItemAgent_SocketsListField_DeathWraith == null)
            {
                return;
            }

            try
            {
                object socketsList = DuckovItemAgent_SocketsListField_DeathWraith.GetValue(agent);
                if (socketsList == null)
                {
                    DuckovItemAgent_SocketsListField_DeathWraith.SetValue(agent, new List<Transform>());
                }
            }
            catch
            {
            }
        }

        private bool IsLikelyMeleeWeaponItem_DeathWraith(CharacterMainControl wraith, Item item)
        {
            if (item == null)
            {
                return false;
            }

            try
            {
                if (item.GetComponent<ItemSetting_MeleeWeapon>() != null)
                {
                    return true;
                }
            }
            catch
            {
            }

            try
            {
                Slot meleeSlot = wraith != null ? wraith.MeleeWeaponSlot() : null;
                return meleeSlot != null && meleeSlot.Content == item;
            }
            catch
            {
                return false;
            }
        }

        private void ForceRefreshWraithEquipmentAgents_DeathWraith(CharacterMainControl wraith)
        {
            if (wraith == null || wraith.CharacterItem == null || wraith.CharacterItem.Slots == null)
            {
                return;
            }

            try
            {
                foreach (Slot slot in wraith.CharacterItem.Slots)
                {
                    if (slot != null && slot.Content != null)
                    {
                        slot.ForceInvokeSlotContentChangedEvent();
                    }
                }
            }
            catch (Exception e)
            {
                DevLog("[DeathWraith] 刷新亡魂装备代理失败: " + e.Message);
            }
        }

        private void SyncWraithCombatMode_DeathWraith(CharacterMainControl wraith, bool meleeMode)
        {
            if (wraith == null)
            {
                return;
            }

            try
            {
                AICharacterController ai = wraith.aiCharacterController;
                if (ai == null)
                {
                    DevLog("[DeathWraith] [WARNING] 无 AI 控制器，无法同步近战/枪战模式");
                    return;
                }

                ai.melee = meleeMode;
                ai.defaultWeaponOut = true;

                if (meleeMode)
                {
                    DevLog("[DeathWraith] AI 已切换为近战态（保留预设近战参数）");
                }
                else
                {
                    DevLog("[DeathWraith] AI 已切换为枪战态");
                }
            }
            catch (Exception e)
            {
                DevLog("[DeathWraith] 同步亡魂战斗模式失败: " + e.Message);
            }
        }

        private Item SelectPreferredCombatWeaponItem_DeathWraith(CharacterMainControl wraith)
        {
            if (wraith == null)
            {
                return null;
            }

            Slot primary = wraith.PrimWeaponSlot();
            if (primary != null && IsGunItem_DeathWraith(primary.Content))
            {
                return primary.Content;
            }

            Slot secondary = wraith.SecWeaponSlot();
            if (secondary != null && IsGunItem_DeathWraith(secondary.Content))
            {
                return secondary.Content;
            }

            Slot melee = wraith.MeleeWeaponSlot();
            if (melee != null && melee.Content != null)
            {
                return melee.Content;
            }

            if (primary != null && primary.Content != null)
            {
                return primary.Content;
            }

            if (secondary != null && secondary.Content != null)
            {
                return secondary.Content;
            }

            return null;
        }

        private static bool IsGunItem_DeathWraith(Item item)
        {
            if (item == null)
            {
                return false;
            }

            try
            {
                return item.GetComponent<ItemSetting_Gun>() != null;
            }
            catch
            {
                return false;
            }
        }

        private async UniTask EnsureWraithGunReady_DeathWraith(CharacterMainControl wraith, ItemAgent_Gun gun)
        {
            if (wraith == null || gun == null || gun.Item == null)
            {
                return;
            }

            try
            {
                ItemSetting_Gun gunSetting = gun.GunItemSetting;
                Inventory inventory = wraith.CharacterItem != null ? wraith.CharacterItem.Inventory : null;
                if (gunSetting == null || inventory == null)
                {
                    DevLog("[DeathWraith] [WARNING] 枪械缺少 GunSetting 或库存，无法补弹");
                    return;
                }

                Item ammoPrototype = ResolveWraithAmmoPrototype_DeathWraith(gunSetting, inventory);
                if (ammoPrototype == null)
                {
                    DevLog("[DeathWraith] [WARNING] 未能解析亡魂枪械的对应子弹: " + gun.Item.DisplayName);
                    return;
                }

                int targetBulletId = ammoPrototype.TypeID;
                gunSetting.SetTargetBulletType(targetBulletId);

                int existingAmmo = CountAmmoInInventory_DeathWraith(inventory, targetBulletId);
                int desiredAmmo = Math.Max(gunSetting.Capacity * 3, Math.Max(30, ammoPrototype.MaxStackCount));
                if (existingAmmo < desiredAmmo)
                {
                    int added = desiredAmmo - existingAmmo;
                    AddAmmoToInventory_DeathWraith(inventory, ammoPrototype, added);
                    DevLog("[DeathWraith] 已为枪械补充子弹: typeID=" + targetBulletId
                        + ", added=" + added);
                }

                try
                {
                    gun.Item.Variables.SetInt("BulletCount", gunSetting.Capacity, true);
                }
                catch
                {
                    try
                    {
                        gun.Item.Variables.SetInt("BulletCount".GetHashCode(), gunSetting.Capacity);
                    }
                    catch { }
                }

                gunSetting.AutoSetTypeInInventory(inventory);
                gunSetting.LoadBulletsFromInventory(inventory);
                await UniTask.Yield();
            }
            catch (Exception e)
            {
                DevLog("[DeathWraith] 补充亡魂枪械子弹失败: " + e.Message);
            }
        }

        private Item ResolveWraithAmmoPrototype_DeathWraith(ItemSetting_Gun gunSetting, Inventory inventory)
        {
            if (gunSetting == null)
            {
                return null;
            }

            try
            {
                if (gunSetting.TargetBulletID > 0)
                {
                    Item exactBullet = ItemAssetsCollection.InstantiateSync(gunSetting.TargetBulletID);
                    if (exactBullet != null)
                    {
                        return exactBullet;
                    }
                }
            }
            catch { }

            if (inventory == null)
            {
                return null;
            }

            try
            {
                string weaponCaliber = gunSetting.Item != null
                    ? gunSetting.Item.Constants.GetString("Caliber".GetHashCode(), null)
                    : null;

                foreach (Item item in inventory)
                {
                    if (item == null || !item.GetBool("IsBullet", false))
                    {
                        continue;
                    }

                    if (!string.IsNullOrEmpty(weaponCaliber))
                    {
                        string ammoCaliber = item.Constants.GetString("Caliber".GetHashCode(), null);
                        if (!string.Equals(ammoCaliber, weaponCaliber, StringComparison.Ordinal))
                        {
                            continue;
                        }
                    }

                    return item;
                }
            }
            catch { }

            return null;
        }

        private int CountAmmoInInventory_DeathWraith(Inventory inventory, int ammoTypeId)
        {
            if (inventory == null || ammoTypeId <= 0)
            {
                return 0;
            }

            int count = 0;
            try
            {
                foreach (Item item in inventory)
                {
                    if (item != null && item.TypeID == ammoTypeId)
                    {
                        count += Math.Max(1, item.StackCount);
                    }
                }
            }
            catch { }

            return count;
        }

        private void AddAmmoToInventory_DeathWraith(Inventory inventory, Item ammoPrototype, int amountToAdd)
        {
            if (inventory == null || ammoPrototype == null || amountToAdd <= 0)
            {
                return;
            }

            int remaining = amountToAdd;
            int maxStack = Math.Max(1, ammoPrototype.MaxStackCount);
            while (remaining > 0)
            {
                Item ammo = ammoPrototype.CreateInstance();
                if (ammo == null)
                {
                    break;
                }

                int stack = Math.Min(remaining, maxStack);
                ammo.StackCount = Math.Max(1, stack);
                inventory.AddItem(ammo);
                remaining -= ammo.StackCount;
            }
        }

        private void ApplyWraithRuntimePreset_DeathWraith(
            CharacterMainControl wraith,
            WraithInfo info,
            string displayNameKey,
            string displayName)
        {
            if (wraith == null)
            {
                return;
            }

            try
            {
                CharacterRandomPreset sourcePreset = wraith.characterPreset;
                CharacterRandomPreset runtimePreset = sourcePreset != null
                    ? UnityEngine.Object.Instantiate(sourcePreset)
                    : ScriptableObject.CreateInstance<CharacterRandomPreset>();
                DestroyOwnedWraithPresetClone_DeathWraith(wraith);

                runtimePreset.name = "BossRush_DeathWraithPreset(Clone)";
                runtimePreset.aiCombatFactor = 1f;
                runtimePreset.showName = true;
                runtimePreset.showHealthBar = true;
                runtimePreset.dropBoxOnDead = false;
                runtimePreset.team = Teams.scav;
                runtimePreset.hasSoul = false;
                runtimePreset.voiceType = info != null
                    ? info.playerVoiceType
                    : wraith.AudioVoiceType;
                runtimePreset.footstepMaterialType = info != null
                    ? info.playerFootStepMaterialType
                    : wraith.FootStepMaterialType;
                runtimePreset.nameKey = !string.IsNullOrEmpty(displayNameKey)
                    ? displayNameKey
                    : displayName;

                if (ReflectionCache.CharacterRandomPreset_CharacterIconType != null)
                {
                    ReflectionCache.CharacterRandomPreset_CharacterIconType.SetValue(
                        runtimePreset,
                        CharacterIconTypes.pmc);
                }

                wraith.characterPreset = runtimePreset;
            }
            catch (Exception e)
            {
                DevLog("[DeathWraith] 创建亡魂运行时预设失败: " + e.Message);
            }
        }



        private void RestoreWraithItemRuntimeStateRecursive_DeathWraith(Item item, string reason, int depth = 0)
        {
            if (item == null || depth > 16)
            {
                return;
            }

            try
            {
                CustomItemRuntimeStateHelper.RestoreRuntimeState(item, reason);
            }
            catch (Exception e)
            {
                DevLog("[DeathWraith] 恢复物品运行时状态异常: " + e.Message);
            }

            try
            {
                if (item.Slots != null)
                {
                    foreach (Slot slot in item.Slots)
                    {
                        if (slot == null || slot.Content == null)
                        {
                            continue;
                        }

                        RestoreWraithItemRuntimeStateRecursive_DeathWraith(
                            slot.Content,
                            reason + ":slot:" + slot.Key,
                            depth + 1);
                    }
                }
            }
            catch (Exception e)
            {
                DevLog("[DeathWraith] 遍历装备槽恢复异常: " + e.Message);
            }

            try
            {
                if (item.Inventory != null)
                {
                    foreach (Item invItem in item.Inventory)
                    {
                        if (invItem == null)
                        {
                            continue;
                        }

                        RestoreWraithItemRuntimeStateRecursive_DeathWraith(
                            invItem,
                            reason + ":inv",
                            depth + 1);
                    }
                }
            }
            catch (Exception e)
            {
                DevLog("[DeathWraith] 遍历背包恢复异常: " + e.Message);
            }
        }

        private void DestroyDetachedItem_DeathWraith(Item item, string reason)
        {
            if (item == null)
            {
                return;
            }

            string itemName = null;
            try
            {
                itemName = item.DisplayName;
            }
            catch { }

            try
            {
                item.Detach();
            }
            catch { }

            try
            {
                if (item.gameObject != null)
                {
                    UnityEngine.Object.Destroy(item.gameObject);
                }
            }
            catch (Exception e)
            {
                DevLog("[DeathWraith] 销毁未转移物品异常: " + e.Message);
            }

            DevLog("[DeathWraith] 已销毁未成功转移的物品: "
                + (string.IsNullOrEmpty(itemName) ? "<unknown>" : itemName)
                + " | reason=" + reason);
        }

        private bool TryAddItemToInventory_DeathWraith(Inventory inventory, Item item)
        {
            if (inventory == null || item == null)
            {
                return false;
            }

            try
            {
                item.Detach();
            }
            catch { }

            try
            {
                if (inventory.AddAndMerge(item, 0))
                {
                    return true;
                }
            }
            catch { }

            try
            {
                return inventory.AddItem(item);
            }
            catch
            {
                return false;
            }
        }


        #endregion
    }
}
