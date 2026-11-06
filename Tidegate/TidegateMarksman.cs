using System;
using ItemStatsSystem;
using ItemStatsSystem.Items;
using UnityEngine;

namespace BossRush
{
    internal static partial class TidegateEnemyActors
    {
        // 官方 SV98 / 普通狙击弹：Mode H 已核过的 starter_marksman_rifle 同一对物品。
        internal const int LookoutRifleId = 782, LookoutAmmoId = 622;

        static partial void TuneMarksman(CharacterRandomPreset clone)
        {
            // 不克隆原版狙击 Boss。单独调这一名拾荒者：守控制楼外的横廊，不追出掩体。
            // 官方 CreateCharacterAsync 把这些值交给常规视觉/射击 AI，没有自写锁定目标或伤害。
            clone.moveSpeedFactor = 0f;
            clone.canDash = clone.shootCanMove = false;
            clone.patrolRange = clone.combatMoveRange = 0f;
            clone.minTraceTargetChance = clone.maxTraceTargetChance = 0f;
            clone.forceTracePlayerDistance = 0f;
            clone.sightDistance = 30f;
            clone.sightAngle = 95f;
            clone.reactionTime = Mathf.Max(0.9f, clone.reactionTime);
            clone.shootDelay = Mathf.Max(0.65f, clone.shootDelay);
            clone.shootTimeSpaceRange = new Vector2(2.4f, 3.6f);
            clone.damageMultiplier = Mathf.Min(0.8f, clone.damageMultiplier);
        }

        static partial void EquipMarksman(CharacterMainControl character, ref bool handled)
        {
            Item body = character == null ? null : character.CharacterItem;
            Slot mount = body == null ? null : SkyIslandBossForge.FindSlot(body, "PrimaryWeapon");
            if (mount == null || body.Inventory == null || character.agentHolder == null) return;
            Item rifle = null, rounds = null, old = null;
            try
            {
                rifle = ItemAssetsCollection.InstantiateSync(LookoutRifleId);
                ItemSetting_Gun gun = rifle == null ? null : rifle.GetComponent<ItemSetting_Gun>();
                if (gun == null || rifle.DisplayNameRaw != "Item_Sniper_SV98_Normal" || gun.currentTriggerMode != ItemSetting_Gun.TriggerModes.bolt
                    || rifle.Inventory == null || !mount.CanPlug(rifle))
                    throw new InvalidOperationException("官方栓动步枪或主武器插槽不可用");
                rounds = ItemAssetsCollection.InstantiateSync(LookoutAmmoId);
                if (rounds == null || rounds.DisplayNameRaw != "Item_Bullet_SNP_Normal" || gun.Capacity < 1 || !gun.IsValidBullet(rounds))
                    throw new InvalidOperationException("官方普通狙击弹与步枪不兼容");
                int magazine = Math.Min(10, Math.Min(gun.Capacity, rounds.MaxStackCount));
                gun.SetTargetBulletType(rounds);
                rounds.StackCount = magazine;
                if (magazine < 1 || !rifle.Inventory.AddItem(rounds)) throw new InvalidOperationException("弹匣未能装填");
                rounds = null; // 弹药归临时步枪所有；失败时随整棵物品树释放。
                ModeFItemConfigHelper.SetHiddenMember(gun, "_bulletCountCache", -1);
                if (gun.BulletCount != magazine) throw new InvalidOperationException("弹匣读回不一致");
                if (rifle.UseDurability) rifle.Durability = rifle.MaxDurability * 0.65f;
                if (!mount.Plug(rifle, out old) || !(character.agentHolder.ChangeHoldItem(rifle) is ItemAgent_Gun))
                    throw new InvalidOperationException("步枪未能持握");
                handled = true; // 已持有装好弹的枪。后续装饰失败也不能再套精英随机配枪。
                rifle = null;
                if (old != null) { old.DestroyTree(); old = null; }
                character.SetAimPoint(character.transform.position + Vector3.left * 22f);
                // 只带一小盒备用普通弹。不补无限弹、不挤走原背包，也不再加第二把精英枪。
                rounds = ItemAssetsCollection.InstantiateSync(LookoutAmmoId);
                if (rounds != null)
                {
                    rounds.StackCount = Math.Min(rounds.MaxStackCount, 12);
                    if (body.Inventory.AddAndMerge(rounds)) rounds = null;
                }
            }
            finally
            {
                if (rounds != null) rounds.DestroyTree();
                if (rifle != null)
                {
                    if (mount.Content == rifle)
                    {
                        rifle.Detach();
                        Item displaced;
                        if (old != null && mount.Plug(old, out displaced)) old = null;
                        character.SwitchToFirstAvailableWeapon();
                    }
                    rifle.DestroyTree();
                }
                if (old != null) old.DestroyTree();
            }
        }
    }
}
