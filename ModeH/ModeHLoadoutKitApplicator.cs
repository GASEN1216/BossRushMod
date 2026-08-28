using System;
using System.Collections.Generic;
using ItemStatsSystem;
using ItemStatsSystem.Items;
using UnityEngine;

namespace BossRush
{
    /// <summary>一次 kit 装配事务的记录（只在内存，不入存档）。</summary>
    internal sealed class ModeHKitApplication
    {
        /// <summary>被装配的临时角色。</summary>
        public CharacterMainControl Character;
        /// <summary>本次实际写入的物品（含弹药），回收时逆序销毁。</summary>
        public List<Item> CreatedItems = new List<Item>();
        /// <summary>逐 kit 的失败原因（fail-closed 展示用）。</summary>
        public List<string> FailureReasons = new List<string>();
    }

    /// <summary>
    /// Mode H 虚拟整备装配器（设计提案 §17.7、§25.1）。
    ///
    /// 玩家资产访问白名单第二条：**只能**访问本事务 owner 标记且 inactive 的临时选手实例，
    /// 允许读写该临时角色的 slots/inventory；禁止解析 `CharacterMainControl.Main`、
    /// 禁止访问 `PlayerStorage`、禁止读写玩家 `ItemTreeData`。
    ///
    /// 其余冻结契约：
    /// - 只装配 `LoadoutKits.json` 已审计并在运行时解析成功的 kit；
    /// - 每名选手最多 4 件、槽位互不冲突（由 registry 的 ValidateSelection 前置校验）；
    /// - 枪械槽必须同时冻结弹药（固定 typeId 或按口径解析）；
    /// - 禁止装配 `controlMindType != none` 的武器（§17.6.5：会与 ERROR 互换抢控制权）；
    /// - 任一件失败即整批逆序回收，不留半套装备。
    /// </summary>
    internal static class ModeHLoadoutKitApplicator
    {
        #region 装配

        /// <summary>
        /// 给一名 owner 标记且 inactive 的临时选手装配 kit。
        /// 失败时已写入的物品被逆序回收，application 仍返回以便读失败原因。
        /// </summary>
        public static bool TryApply(
            ModeHSpawnHandle handle,
            IList<string> kitIds,
            out ModeHKitApplication application,
            out string failureReasonId)
        {
            application = new ModeHKitApplication();
            failureReasonId = null;

            if (handle == null || handle.Character == null)
            {
                failureReasonId = "kit_apply_handle_missing";
                return false;
            }
            if (string.IsNullOrEmpty(handle.ProfileId))
            {
                // ProfileId 是本事务的 owner 标记：没有它就不是 Mode H 自己的临时选手
                failureReasonId = "kit_apply_not_owned";
                return false;
            }
            if (handle.Activated)
            {
                failureReasonId = "kit_apply_character_active";
                return false;
            }
            if (handle.Character.gameObject != null && handle.Character.gameObject.activeSelf)
            {
                failureReasonId = "kit_apply_character_not_inactive";
                return false;
            }

            application.Character = handle.Character;
            if (kitIds == null || kitIds.Count == 0) return true; // 空整备合法

            if (kitIds.Count > ModeHConfig.MaxKitsPerFighter)
            {
                failureReasonId = "kit_apply_count_exceeded";
                return false;
            }

            Item characterItem = null;
            try { characterItem = handle.Character.CharacterItem; }
            catch (Exception)
            {
                // 角色刚创建时 CharacterItem 可能尚未装配好，按 fail-closed 处理
                characterItem = null;
            }
            if (characterItem == null)
            {
                failureReasonId = "kit_apply_character_item_missing";
                return false;
            }

            for (int i = 0; i < kitIds.Count; i++)
            {
                ModeHResolvedKit kit = ModeHLoadoutKitRegistry.GetKit(kitIds[i]);
                if (kit == null || kit.Spec == null || !kit.Available)
                {
                    failureReasonId = "kit_apply_unavailable:" + kitIds[i];
                    Recycle(application);
                    return false;
                }
                string reason;
                if (!TryApplyOne(characterItem, kit, application, out reason))
                {
                    application.FailureReasons.Add(reason);
                    failureReasonId = reason;
                    Recycle(application);
                    return false;
                }
            }
            return true;
        }

        /// <summary>装配单件 kit：主物品入槽，枪械槽再补冻结弹药。</summary>
        private static bool TryApplyOne(
            Item characterItem, ModeHResolvedKit kit, ModeHKitApplication application, out string failureReasonId)
        {
            failureReasonId = null;

            Item created = null;
            try { created = ItemAssetsCollection.InstantiateSync(kit.ResolvedTypeId); }
            catch (Exception)
            {
                // 官方物品表未就绪或 typeId 无效：按 fail-closed 处理，不做二次猜测
                created = null;
            }
            if (created == null)
            {
                failureReasonId = "kit_apply_instantiate_failed:" + kit.Spec.KitId;
                return false;
            }
            application.CreatedItems.Add(created);

            if (IsControlMindWeapon(created))
            {
                failureReasonId = "kit_apply_control_mind_forbidden:" + kit.Spec.KitId;
                return false;
            }

            Slot slot = null;
            try { slot = characterItem.Slots.GetSlot(kit.Spec.ReplaceSlot); }
            catch (Exception)
            {
                // 槽名不存在时 GetSlot 会抛：视作该临时角色不支持这个槽位
                slot = null;
            }
            if (slot == null)
            {
                failureReasonId = "kit_apply_slot_missing:" + kit.Spec.ReplaceSlot;
                return false;
            }

            // 先清空原有内容（临时角色的官方随机装备），再插入本 kit
            try
            {
                Item previous = slot.Content;
                if (previous != null)
                {
                    previous.Detach();
                    previous.DestroyTree();
                }
            }
            catch (Exception)
            {
                // 原装备可能已被原版回收；清空失败不阻断装配，Plug 会给出最终结论
            }

            bool plugged = false;
            try
            {
                Item unplugged;
                plugged = slot.Plug(created, out unplugged);
                if (unplugged != null)
                {
                    try { unplugged.DestroyTree(); }
                    catch (Exception)
                    {
                        // 被顶出的旧装备销毁失败不影响本次装配结论
                    }
                }
            }
            catch (Exception)
            {
                plugged = false;
            }
            if (!plugged)
            {
                failureReasonId = "kit_apply_plug_failed:" + kit.Spec.KitId;
                return false;
            }

            if (!IsWeaponSlot(kit.Spec.ReplaceSlot)) return true;
            return TryApplyAmmo(characterItem, kit, created, application, out failureReasonId);
        }

        /// <summary>枪械槽的冻结弹药：固定 typeId 优先，否则按枪械口径解析。</summary>
        private static bool TryApplyAmmo(
            Item characterItem,
            ModeHResolvedKit kit,
            Item gunItem,
            ModeHKitApplication application,
            out string failureReasonId)
        {
            failureReasonId = null;
            if (kit.Spec.AmmoCount <= 0) return true;

            int ammoTypeId = kit.Spec.AmmoTypeId;
            if (ammoTypeId <= 0 && kit.Spec.ResolveAmmoByCaliber)
            {
                ammoTypeId = ResolveAmmoTypeIdByCaliber(gunItem);
            }
            if (ammoTypeId <= 0)
            {
                failureReasonId = "kit_apply_ammo_unresolved:" + kit.Spec.KitId;
                return false;
            }

            Item ammo = null;
            try { ammo = ItemAssetsCollection.InstantiateSync(ammoTypeId); }
            catch (Exception)
            {
                // 与主物品同样按 fail-closed 处理
                ammo = null;
            }
            if (ammo == null)
            {
                failureReasonId = "kit_apply_ammo_instantiate_failed:" + kit.Spec.KitId;
                return false;
            }
            application.CreatedItems.Add(ammo);

            try { ammo.StackCount = kit.Spec.AmmoCount; }
            catch (Exception)
            {
                // 堆叠上限低于冻结数量时保持原值，弹药仍然可用
            }

            bool plugged = false;
            try { plugged = characterItem.TryPlug(ammo, true, null, 0); }
            catch (Exception)
            {
                plugged = false;
            }
            if (!plugged)
            {
                failureReasonId = "kit_apply_ammo_plug_failed:" + kit.Spec.KitId;
                return false;
            }
            return true;
        }

        /// <summary>按枪械的 `ItemSetting_Gun.TargetBulletID` 解析弹药 typeId。</summary>
        private static int ResolveAmmoTypeIdByCaliber(Item gunItem)
        {
            if (gunItem == null) return 0;
            try
            {
                ItemSetting_Gun gun = gunItem.GetComponent<ItemSetting_Gun>();
                if (gun == null) return 0;
                return gun.TargetBulletID;
            }
            catch (Exception)
            {
                // 非枪械或设置缺失：交由调用方按 fail-closed 拒绝该 kit
                return 0;
            }
        }

        private static bool IsWeaponSlot(string slotName)
        {
            return string.Equals(slotName, "PrimaryWeapon", StringComparison.Ordinal)
                || string.Equals(slotName, "SecondaryWeapon", StringComparison.Ordinal);
        }

        /// <summary>
        /// §17.6.5：`controlMindType != none` 的武器会与 ERROR 完整互换抢夺控制权，
        /// 一律不得进入 Mode H 虚拟整备。
        ///
        /// 原版把控心类型存成物品属性（`ItemAgent_Gun.ControlMindType` 就是读同一个 hash），
        /// 这里直接读属性，避免依赖只有手持时才存在的 agent 实例。
        /// </summary>
        private static bool IsControlMindWeapon(Item item)
        {
            if (item == null) return false;
            try
            {
                float raw = item.GetStatValue(ControlMindTypeStatHash);
                return Mathf.RoundToInt(raw) != (int)ControlMindTypes.none;
            }
            catch (Exception)
            {
                // 读不到该属性时按“可能是控心武器”保守拒绝
                return true;
            }
        }

        /// <summary>控心类型属性 hash，与原版 `ItemAgent_Gun.ControlMindTypeHash` 同一算法。</summary>
        private static readonly int ControlMindTypeStatHash = "ControlMindType".GetHashCode();

        #endregion

        #region 回收

        /// <summary>
        /// 逆序回收本次事务创建的全部物品。幂等：重复调用安全。
        /// 只销毁本装配器自己创建的实例，绝不触碰角色原有的其它物品。
        /// </summary>
        public static void Recycle(ModeHKitApplication application)
        {
            if (application == null || application.CreatedItems == null) return;
            for (int i = application.CreatedItems.Count - 1; i >= 0; i--)
            {
                Item item = application.CreatedItems[i];
                if (item == null) continue;
                try { item.Detach(); }
                catch (Exception)
                {
                    // 已被原版回收的物品 Detach 会抛，继续走 DestroyTree
                }
                try { item.DestroyTree(); }
                catch (Exception)
                {
                    // 物件已销毁：回收目标已达成
                }
            }
            application.CreatedItems.Clear();
        }

        #endregion
    }
}
