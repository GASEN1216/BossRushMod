using System;
using ItemStatsSystem;
using ItemStatsSystem.Items;
using UnityEngine;

namespace BossRush
{
    /// <summary>官方 Scav 的身份与配装。只在创建时处理，不挂每帧外观扫描或复制共享材质。</summary>
    internal static class TidegateEnemyIdentity
    {
        private sealed class Outfit
        {
            internal readonly string Key, Zh, En;
            internal readonly int Helmet, Armor, Face;
            internal readonly float Scale;
            internal Outfit(string key, string zh, string en, int helmet, int armor, int face = 0, float scale = 1f)
            { Key = key; Zh = zh; En = en; Helmet = helmet; Armor = armor; Face = face; Scale = scale; }
        }

        // ID / 原始名称已对照本机官方 resources.assets 的 ItemAssetsCollection。
        // 穿在身上的就是尸体可搜到的装备；只用普通 1–4 级件，外形差异不靠额外奖励箱。
        private static readonly Outfit Trooper = new Outfit("BossRush_TidegateEnemy_Trooper", "突击队员", "Assault Trooper", 42, 33);
        private static readonly Outfit Captain = new Outfit("BossRush_TidegateEnemy_Captain", "突击队长", "Assault Captain", 43, 2, 744, 1.08f);
        private static readonly Outfit Dockhand = new Outfit("BossRush_TidegateEnemy_Dockhand", "码头游荡者", "Dock Prowler", 104, 32);
        private static readonly Outfit Ambusher = new Outfit("BossRush_TidegateEnemy_Ambusher", "芦苇伏兵", "Reed Ambusher", 1143, 32, 27);
        private static readonly Outfit Digger = new Outfit("BossRush_TidegateEnemy_Digger", "淘沙帮伙计", "Dredge Gang Hand", 138, 33, 27);
        private static readonly Outfit Grenadier = new Outfit("BossRush_TidegateEnemy_Grenadier", "淘沙帮投掷手", "Dredge Grenadier", 59, 33, 27);
        private static readonly Outfit Guard = new Outfit("BossRush_TidegateEnemy_Guard", "水厂警卫", "Waterworks Guard", 1146, 2);
        private static readonly Outfit CapeGuard = new Outfit("BossRush_TidegateEnemy_CapeGuard", "灯塔守卫", "Lighthouse Guard", 1145, 1142);
        private static readonly Outfit Marksman = new Outfit("BossRush_TidegateEnemy_Marksman", "水厂狙击手", "Waterworks Marksman", 104, 2, 741);
        private static readonly Outfit CapeScout = new Outfit("BossRush_TidegateEnemy_CapeScout", "岬角巡哨", "Cape Scout", 1144, 32);
        private static readonly Outfit Hound = new Outfit("BossRush_TidegateEnemy_Hound", "巡街猎犬", "Patrol Hound", 0, 0);
        private static readonly Outfit Boss = new Outfit(TidegateText.MapBossKey, "沙老大·铅锤", "Leadline, the Dredge Boss", 44, 34, 26);
        private static readonly Outfit[] All = { Trooper, Captain, Dockhand, Ambusher, Digger, Grenadier, Guard, CapeGuard, Marksman, CapeScout, Hound, Boss };

        private static Outfit For(string group, string kind, TidegateEnemyRole role)
        {
            if (kind == "boss") return Boss;
            if (role == TidegateEnemyRole.Hound) return Hound;
            if (role == TidegateEnemyRole.Marksman) return Marksman;
            if (role == TidegateEnemyRole.Gunner) return Grenadier;
            if (role == TidegateEnemyRole.Captain) return Captain;
            if (group == "dig" || group == "vault") return Digger;
            if (kind == "guard") return group == "cape" ? CapeGuard : Guard;
            if (group == "docks") return Dockhand;
            if (group == "reeds") return Ambusher;
            if (group == "cape") return CapeScout;
            return Trooper;
        }

        internal static void InjectLabels()
        {
            foreach (Outfit outfit in All) LocalizationHelper.InjectLocalization(outfit.Key, L10n.T(outfit.Zh, outfit.En));
        }

        internal static void Tune(CharacterRandomPreset clone, string group, string kind, TidegateEnemyRole role)
        {
            // 官方血条和击杀记录都读 characterPreset.nameKey；只改 GameObject 名字不会改玩家看到的名字。
            clone.nameKey = For(group, kind, role).Key;
            clone.showName = true;
            clone.showHealthBar = true;
            if (kind != "boss") clone.isBoss = false;
        }

        internal static void Dress(CharacterMainControl character, string group, string kind, TidegateEnemyRole role)
        {
            if (character == null || role == TidegateEnemyRole.Hound) return;
            Outfit outfit = For(group, kind, role);
            try
            {
                Equip(character, "Helmat", outfit.Helmet);
                Equip(character, "Armor", outfit.Armor);
                // 这些饰件有固定岗位归属；移除随机面罩 / 耳机，避免遮住指定帽盔和面部辨识点。
                Equip(character, "FaceMask", outfit.Face);
                Equip(character, "Headset", role == TidegateEnemyRole.Marksman ? 679 : 0);
                if (character.characterModel != null && outfit.Scale != 1f)
                    character.characterModel.transform.localScale = Vector3.one * outfit.Scale;
            }
            catch (Exception e) { Debug.LogWarning(TidegateRaidScene.LogTag + " 岗位外观准备失败 " + outfit.Key + "：" + e.Message); }
            // 官方异步创建已完成并装好岗位装备，此时捏脸不会被 ScavBlue 再覆盖。
            // 配装失败仍可应用脸型；失败装备沿用原件，捏脸本身有独立回滚。
            TidegateEnemyAppearance.Apply(character, outfit.Key);
        }

        /// <summary>F3 只读核对当前存活角色；不重新捏脸、不改装备或 renderer。</summary>
        internal static bool DescribeAppearance(CharacterMainControl character, string group, string kind, TidegateEnemyRole role, out string details)
        {
            if (role == TidegateEnemyRole.Hound)
            {
                bool native = character != null && character.characterModel != null && character.characterModel.CustomFace == null;
                details = "profile=native_wolf,custom_face=none,match=" + native;
                return native;
            }
            return TidegateEnemyAppearance.Matches(character, For(group, kind, role).Key, out details);
        }

        // Plug 成功才销毁旧件；失败仍保留官方配装，不把未穿上的新装备塞入背包成为额外掉落。
        private static void Equip(CharacterMainControl character, string slotName, int typeId)
        {
            Slot slot = SkyIslandBossForge.FindSlot(character.CharacterItem, slotName);
            if (slot == null) throw new InvalidOperationException("缺少装备槽 " + slotName);
            if (typeId == 0)
            {
                Item previous = slot.Content;
                if (previous != null) { previous.Detach(); previous.DestroyTree(); }
                slot.ForceInvokeSlotContentChangedEvent();
                return;
            }
            Item prefab = ItemAssetsCollection.GetPrefab(typeId);
            if (prefab == null || prefab.DisplayNameRaw != OfficialKey(typeId))
                throw new InvalidOperationException("官方岗位装备不可用 " + typeId);
            Item piece = null;
            try
            {
                piece = ItemAssetsCollection.InstantiateSync(typeId);
                if (piece == null || !slot.CanPlug(piece)) throw new InvalidOperationException("岗位装备不适配 " + slotName);
                if (piece.UseDurability) piece.Durability = piece.MaxDurability * .65f;
                Item previous;
                if (!slot.Plug(piece, out previous)) throw new InvalidOperationException("岗位装备未插入 " + slotName);
                piece = null;
                if (previous != null) previous.DestroyTree();
                slot.ForceInvokeSlotContentChangedEvent();
            }
            finally { if (piece != null) piece.DestroyTree(); }
        }

        private static string OfficialKey(int typeId)
        {
            switch (typeId)
            {
                case 2: return "Item_ArmorLV3";
                case 26: return "Item_GasMask_01";
                case 27: return "Item_Goggle";
                case 32: return "Item_ArmorLV1";
                case 33: return "Item_ArmorLV2";
                case 34: return "Item_ArmorLV4";
                case 42: return "Item_HelmatLV2";
                case 43: return "Item_HelmatLV3";
                case 44: return "Item_HelmatLV4";
                case 59: return "Item_Pot";
                case 104: return "Item_Hat";
                case 138: return "Item_HeadLantern";
                case 679: return "Item_HeadsetLV1";
                case 741: return "Item_Glass";
                case 744: return "Item_SunGlass";
                case 1142: return "Item_ArmorLV3_3";
                case 1143: return "Item_HelmatLV1_2";
                case 1144: return "Item_HelmatLV2_2";
                case 1145: return "Item_HelmatLV3_2";
                case 1146: return "Item_HelmatLV3_UN";
                default: throw new InvalidOperationException("未登记的岗位装备 " + typeId);
            }
        }
    }
}
