using System;
using System.Collections;
using System.Reflection;
using System.Collections.Generic;
using Duckov.Utilities;
using ItemStatsSystem;
using ItemStatsSystem.Items;
using UnityEngine;

namespace BossRush
{
    internal sealed class ModeHPreparedGear
    {
        public int TypeId;
        public string Slot;
        public string Name;
        public Sprite Icon;
    }

    internal sealed class ModeHPreparedFighterStats
    {
        public float Health, Damage, MoveSpeed, Range, Armor, HeadArmor, CritChance, Power;
        public readonly List<ModeHPreparedGear> Gear = new List<ModeHPreparedGear>();
    }

    internal sealed partial class ModeHRuntimeModule
    {
        // 配装是本局准备状态，只有在候选/赛前页实际需要时准备；退出时由同一 owner 清理。
        private readonly Dictionary<string, List<ModeHResolvedKit>> _preparedOutfits =
            new Dictionary<string, List<ModeHResolvedKit>>(StringComparer.Ordinal);
        private readonly Dictionary<string, ModeHPreparedFighterStats> _preparedStats =
            new Dictionary<string, ModeHPreparedFighterStats>(StringComparer.Ordinal);

        internal bool IsMatchInProgress
        {
            get { return _runState != null && (_runState.Lifecycle == ModeHLifecycle.MatchFighting
                || _runState.Lifecycle == ModeHLifecycle.RelayPending
                || _runState.Lifecycle == ModeHLifecycle.ErrorRecoveryPending); }
        }

        private List<ModeHResolvedKit> GetPreparedOutfit(string key, long seed)
        {
            string identity = seed + "|" + key;
            List<ModeHResolvedKit> kits;
            if (!_preparedOutfits.TryGetValue(identity, out kits))
            {
                kits = ModeHLoadoutKitRegistry.BuildPreparedKits(seed, key);
                _preparedOutfits.Add(identity, kits);
            }
            return kits;
        }

        private List<ModeHResolvedKit> GetPreparedProfileOutfit(ModeHProfileDto profile)
        {
            return GetPreparedOutfit("fighter|" + profile.profileId, _runState.RunSeed);
        }

        private List<ModeHResolvedKit> GetPreparedEnemyOutfit(ModeHMatchPlanDto plan, int index)
        {
            return GetPreparedOutfit("enemy|" + index + "|" + plan.enemyStableKeys[index], plan.planSeed);
        }

        private ModeHPreparedFighterStats GetPreparedFighterStats(ModeHProfileDto profile, IList<string> kitIds = null)
        {
            if (profile == null || _runState == null) return null;
            return ReadPreparedStats(profile.stableKey, GetPreparedProfileOutfit(profile), kitIds, profile.injuryId);
        }

        private ModeHPreparedFighterStats GetPreparedEnemyStats(ModeHMatchPlanDto plan, int index)
        {
            if (plan == null || plan.enemyStableKeys == null || index < 0 || index >= plan.enemyStableKeys.Count) return null;
            return ReadPreparedStats(plan.enemyStableKeys[index], GetPreparedEnemyOutfit(plan, index), null, null);
        }

        private ModeHPreparedFighterStats ReadPreparedStats(string stableKey, IList<ModeHResolvedKit> prepared,
            IList<string> overrides, string injury)
        {
            List<ModeHResolvedKit> kits = new List<ModeHResolvedKit>();
            foreach (ModeHResolvedKit kit in prepared)
                if (!ModeHInjuryAndScarSystem.InjuryDisablesKitSlot(injury, kit.Spec.ReplaceSlot)) kits.Add(kit);
            foreach (string id in overrides ?? new List<string>())
            {
                ModeHResolvedKit kit = ModeHLoadoutKitRegistry.GetKit(id);
                if (kit == null || !kit.Available || ModeHInjuryAndScarSystem.InjuryDisablesKitSlot(injury, kit.Spec.ReplaceSlot)) continue;
                kits.RemoveAll(old => old.Spec.ReplaceSlot == kit.Spec.ReplaceSlot);
                kits.Add(kit);
            }
            string cacheKey = stableKey + "|" + injury + "|" + LevelManager.Rule.EnemyHealthFactor;
            foreach (ModeHResolvedKit kit in kits) cacheKey += "|" + kit.Spec.KitId;
            ModeHPreparedFighterStats result;
            if (_preparedStats.TryGetValue(cacheKey, out result)) return result;
            CharacterRandomPreset preset = ModeHPresetRegistry.GetAuditedPreset(stableKey);
            if (preset == null || kits.Count == 0) return null;
            Item body = null;
            try
            {
                body = ItemAssetsCollection.InstantiateSync(GameplayDataSettings.ItemAssets.DefaultCharacterItemTypeID);
                if (body == null) return null;
                PrepareVisibleBaseStats(body, preset);
                if (!ModeHLoadoutKitApplicator.TryApplyPreview(body, kits)) return null;
                Item weapon = SlotContent(body, "PrimaryWeapon") ?? SlotContent(body, "SecondaryWeapon") ?? SlotContent(body, "MeleeWeapon");
                ItemSetting_Gun gun = weapon != null ? weapon.GetComponent<ItemSetting_Gun>() : null;
                Item ammo = null;
                if (gun != null)
                {
                    foreach (ModeHResolvedKit kit in kits)
                    {
                        if (SlotContent(body, kit.Spec.ReplaceSlot) != weapon) continue;
                        int id = kit.Spec.AmmoTypeId > 0 ? kit.Spec.AmmoTypeId
                            : ModeHLoadoutKitRegistry.ResolvePreparedAmmoTypeId(weapon, gun);
                        ammo = id > 0 ? ItemAssetsCollection.GetPrefab(id) : null;
                        if (ammo == null || ammo.TypeID != id || !gun.IsValidBullet(ammo)) return null;
                        break;
                    }
                }
                result = CalculatePreparedStats(body, weapon, ammo);
                foreach (ModeHResolvedKit kit in kits)
                {
                    Item item = body.Slots.GetSlot(kit.Spec.ReplaceSlot).Content;
                    result.Gear.Add(new ModeHPreparedGear
                    { TypeId = kit.ResolvedTypeId, Slot = kit.Spec.ReplaceSlot, Name = item.DisplayName, Icon = item.Icon });
                }
                _preparedStats[cacheKey] = result;
                return result;
            }
            catch (Exception e) { LogFailure("prepared_stats", e); return null; }
            finally { if (body != null) body.DestroyTree(); }
        }

        private static ModeHPreparedFighterStats CalculatePreparedStats(Item body, Item weapon, Item ammo)
        {
            ItemSetting_Gun gun = weapon != null ? weapon.GetComponent<ItemSetting_Gun>() : null;
            float multiplier = ReadStat(body, gun != null ? "GunDamageMultiplier" : "MeleeDamageMultiplier");
            float bulletDamage = gun != null ? AmmoConstant(ammo, "damageMultiplier", 1f) : 1f;
            float shots = gun != null ? Mathf.Max(1f, Mathf.Round(ReadStat(weapon, "ShotCount"))) : 1f;
            float damage = ReadStat(weapon, "Damage") * multiplier * bulletDamage;
            // 数字展示一发扳机的总直击伤害；霰弹的保底 1 点按官方每颗弹丸分别处理。
            if (gun != null && ReadStat(weapon, "Damage") > 1f) damage = Mathf.Max(shots, damage);
            float critGain = ReadStat(body, gun != null ? "GunCritRateGain" : "MeleeCritRateGain")
                + (gun != null ? AmmoConstant(ammo, "CritRateGain", 0f) : 0f);
            float moveMultiplier = ReadStat(weapon, "MoveSpeedMultiplier");
            ModeHPreparedFighterStats result = new ModeHPreparedFighterStats
            {
                Health = ReadStat(body, "MaxHealth"), Damage = damage,
                MoveSpeed = ReadStat(body, "RunSpeed") * ReadStat(body, "Moveability")
                    * (moveMultiplier > 0f ? moveMultiplier : 1f),
                Range = gun != null ? ReadStat(weapon, "BulletDistance") * ReadStat(body, "GunDistanceMultiplier")
                    : ReadStat(weapon, "AttackRange"),
                Armor = ReadStat(body, "BodyArmor"), HeadArmor = ReadStat(body, "HeadArmor"),
                CritChance = Mathf.Clamp01(ReadStat(weapon, "CritRate") * (1f + critGain)),
            };
            float critDamage = (ReadStat(weapon, "CritDamageFactor")
                + (gun != null ? AmmoConstant(ammo, "CritDamageFactorGain", 0f) : 0f))
                * (1f + ReadStat(body, gun != null ? "GunCritDamageGain" : "MeleeCritDamageGain"));
            float attackSpeed = gun != null
                ? ReadStat(weapon, "ShootSpeed") * ReadStat(body, "GunShootSpeedMultiplier")
                : Mathf.Max(0.1f, ReadStat(weapon, "AttackSpeed"));
            // 公开装备参数估计，不宣称实测胜率；人数、接力与条件由报价另行计入。
            float dps = result.Damage * Mathf.Max(0.1f, attackSpeed)
                * (1f + result.CritChance * Mathf.Max(0f, critDamage - 1f));
            result.Power = Mathf.Sqrt(Mathf.Max(1f, result.Health) * Mathf.Max(1f, dps))
                * (1f + Mathf.Min(12f, result.Armor + result.HeadArmor) * 0.06f)
                * (0.8f + Mathf.Clamp(result.MoveSpeed, 0f, 10f) * 0.04f)
                * (0.85f + Mathf.Clamp(result.Range, 0f, 50f) * 0.006f);
            return result;
        }

        private static Item SlotContent(Item body, string key)
        {
            Slot slot = body != null && body.Slots != null ? body.Slots.GetSlot(key) : null;
            return slot != null ? slot.Content : null;
        }

        private static float AmmoConstant(Item ammo, string key, float fallback)
        { return ammo != null && ammo.Constants != null ? ammo.Constants.GetFloat(key, fallback) : fallback; }

        private static readonly string[] VisibleBaseStatKeys =
        {
            "MaxHealth", "GunDamageMultiplier", "MeleeDamageMultiplier", "RunSpeed", "WalkSpeed",
            "GunDistanceMultiplier", "GunCritRateGain", "MeleeCritRateGain", "Moveability",
            "GunShootSpeedMultiplier", "BodyArmor", "HeadArmor", "GunCritDamageGain", "MeleeCritDamageGain"
        };

        /// <summary>
        /// 预览与新生成角色共用同一组公开基础属性。setStats 的随机区间固定为中点，
        /// 消除选完人后官方再次 roll 造成的面板漂移；未显示的技能/AI/元素行为仍由 preset 决定。
        /// </summary>
        private static void PrepareVisibleBaseStats(Item body, CharacterRandomPreset preset)
        {
            Item template = ItemAssetsCollection.GetPrefab(GameplayDataSettings.ItemAssets.DefaultCharacterItemTypeID);
            foreach (string key in VisibleBaseStatKeys)
            {
                Stat original = template != null ? template.GetStat(key) : null;
                if (original != null) SetPreviewBase(body, key, original.BaseValue, false);
            }
            SetPreviewBase(body, "MaxHealth", preset.health * LevelManager.Rule.EnemyHealthFactor, false);
            // 实际官方 CreateCharacterAsync 的顺序：生命 → setStats → 公开倍率覆盖。
            FieldInfo field = typeof(CharacterRandomPreset).GetField("setStats", BindingFlags.Instance | BindingFlags.NonPublic);
            IEnumerable rows = field != null ? field.GetValue(preset) as IEnumerable : null;
            if (rows != null)
            {
                foreach (object row in rows)
                {
                    if (row == null) continue;
                    Type type = row.GetType();
                    FieldInfo name = type.GetField("statName");
                    FieldInfo value = type.GetField("statBaseValue");
                    string key = name != null ? name.GetValue(row) as string : null;
                    if (key == null || Array.IndexOf(VisibleBaseStatKeys, key) < 0 || value == null) continue;
                    object raw = value.GetValue(row);
                    if (!(raw is Vector2)) continue;
                    Vector2 range = (Vector2)raw;
                    SetPreviewBase(body, key, range.x * 0.5f + range.y * 0.5f, false);
                }
            }
            SetPreviewBase(body, "GunDamageMultiplier", preset.damageMultiplier, false);
            SetPreviewBase(body, "MeleeDamageMultiplier", preset.meleeDamageMultiplier, false);
            SetPreviewBase(body, "RunSpeed", preset.moveSpeedFactor, true);
            SetPreviewBase(body, "WalkSpeed", preset.moveSpeedFactor, true);
            SetPreviewBase(body, "GunDistanceMultiplier", preset.gunDistanceMultiplier, false);
            SetPreviewBase(body, "GunCritRateGain", preset.gunCritRateGain, false);
        }

        private static float ReadStat(Item item, string key)
        { return item != null ? item.GetStatValue(key.GetHashCode()) : 0f; }

        private static void SetPreviewBase(Item item, string key, float value, bool multiply)
        {
            Stat stat = item.GetStat(key);
            if (stat != null) stat.BaseValue = multiply ? stat.BaseValue * value : value;
        }

        private bool ApplyPreparedOutfit(ModeHSpawnHandle handle, IList<ModeHResolvedKit> kits, string injury, out string reason)
        {
            reason = null;
            if (handle == null || handle.Character == null || string.IsNullOrEmpty(handle.ProfileId)
                || handle.Activated || handle.Character.gameObject.activeSelf)
            { reason = "prepared_character_not_isolated"; return false; }
            CharacterRandomPreset preset = ModeHPresetRegistry.GetAuditedPreset(handle.StableKey);
            if (preset == null) { reason = "prepared_preset_missing"; return false; }
            PrepareVisibleBaseStats(handle.Character.CharacterItem, preset);
            List<ModeHResolvedKit> enabled = new List<ModeHResolvedKit>();
            foreach (ModeHResolvedKit kit in kits)
                if (!ModeHInjuryAndScarSystem.InjuryDisablesKitSlot(injury, kit.Spec.ReplaceSlot)) enabled.Add(kit);
            // 预览使用空角色树。未纳入本次计划的旧装备也必须移除，尤其伤病禁掉的 Armor，
            // 否则真实角色会保留底模护甲，而面板和报价按无甲计算。
            foreach (Slot slot in handle.Character.CharacterItem.Slots)
            {
                bool replaced = false;
                foreach (ModeHResolvedKit kit in enabled)
                    if (kit.Spec.ReplaceSlot == slot.Key) { replaced = true; break; }
                if (!replaced && slot.Content != null)
                {
                    Item removed = slot.Unplug();
                    if (removed != null) removed.DestroyTree();
                }
            }
            ModeHKitApplication application;
            // 成功后物品归角色树，角色事务负责销毁；失败由 applicator 回滚。
            if (!ModeHLoadoutKitApplicator.TryApplyPrepared(handle, enabled, out application, out reason)) return false;
            // 仅此新生成且 inactive 的入场路径补齐装备后的生命；不用于受伤存档恢复。
            if (handle.Health != null) handle.Health.SetHealth(handle.Health.MaxHealth);
            return true;
        }
    }
}
