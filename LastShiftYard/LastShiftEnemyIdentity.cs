using System;
using ItemStatsSystem;
using ItemStatsSystem.Items;
using UnityEngine;

namespace BossRush
{
    /// <summary>末班押运队的四个岗位与押运长（设计稿「敌人身份与强度」）。</summary>
    /// <remarks>2026-11-13 追加三种小队长（不是 Boss）：信号工长、调车长、押运班长；只往后追加，旧值不变。</remarks>
    internal enum LastShiftPost { Walker, Inspector, Escort, Captain, Chief, Signalman, Shunter, Sergeant }

    /// <summary>
    /// COMPAT：末班编组站普通敌人的战斗强度（根 AGENTS.md §4.8 第二层）。沿用潮闸镇 v2.4.0 发版时 owner 认可的那一档：
    /// 生命 ×1.5、押运员与押运队长 ×1.8，伤害 ×1.25，反应与开火前摇 ÷1.3，散布 ÷1.2。倍率改 1 即还原。
    /// 只改本次刷怪的克隆 preset（身份设好之后、官方 CreateCharacterAsync 之前），每个克隆调一次，倍率不复利；
    /// 不换武器护甲、不改掉落与人数。押运长有自己的固定数值（<see cref="LastShiftEscortChief.Tune"/>），这里跳过。
    /// </summary>
    internal static class LastShiftCombatTuning
    {
        internal const float HealthFactor = 1.5f;
        internal const float EscortHealthFactor = 1.8f;
        /// <summary>小队长（2026-11-13）：比押运员厚一截，不到 Boss；伤害、反应与散布同普通档。</summary>
        internal const float LeaderHealthFactor = 2.6f;
        internal const float DamageFactor = 1.25f;
        internal const float ReflexFactor = 1.3f;
        internal const float AccuracyFactor = 1.2f;

        internal static void Apply(CharacterRandomPreset clone, LastShiftPost post)
        {
            if (clone == null || post == LastShiftPost.Chief) return;
            bool sturdy = post == LastShiftPost.Escort || post == LastShiftPost.Captain;
            bool leader = post == LastShiftPost.Signalman || post == LastShiftPost.Shunter || post == LastShiftPost.Sergeant;
            clone.health *= leader ? LeaderHealthFactor : sturdy ? EscortHealthFactor : HealthFactor;
            clone.damageMultiplier *= DamageFactor;
            clone.setMeleeDamageMultiplier = true;
            clone.meleeDamageMultiplier *= DamageFactor;
            clone.reactionTime /= ReflexFactor;
            clone.shootDelay /= ReflexFactor;
            clone.gunScatterMultiplier /= AccuracyFactor;
        }
    }

    /// <summary>
    /// 岗位身份、配装与实例捏脸。口径照潮闸镇 `TidegateEnemyIdentity` / `TidegateEnemyAppearance`（主工作区 v2.4.0，本分支尚未并入，故按同一做法另写一份）：
    /// 创建前写 nameKey，创建后插官方帽盔 / 身甲 / 面罩，再只对这个实例调官方 `SetFaceFromData`；不写共享 preset、不复制材质，失败整体回滚。
    /// 押运长保留官方「路障」原装外观，只换名字。
    /// </summary>
    internal static class LastShiftEnemyIdentity
    {
        private sealed class Outfit
        {
            internal readonly string Key, Zh, En;
            internal readonly int Helmet, Armor, Face;
            internal Outfit(string key, string zh, string en, int helmet, int armor, int face)
            { Key = key; Zh = zh; En = en; Helmet = helmet; Armor = armor; Face = face; }
        }

        private sealed class Look
        {
            internal int Hair, Eye, Brow, Mouth;
            internal int Feather, Accent, Beak, Iris;
            internal float Body = 1f, Head = .1f, Forehead = .08f, Round = .8f;
            internal float EyeScale = 1.35f, EyeDistance = 28f, BrowTwist = -12f;
            internal float MouthScale = 1.4f;
        }

        internal const string ChiefKey = "BossRush_LastShiftEnemy_Chief";

        // 装备 ID / 原始名称沿用潮闸镇对照本机官方 ItemAssetsCollection 核过的那张表（见 OfficialKey）；只用普通 1–3 级件，穿在身上的就是尸体可搜到的。
        private static readonly Outfit Walker = new Outfit("BossRush_LastShiftEnemy_Walker", "巡道工", "Track Walker", 138, 32, 0);
        private static readonly Outfit Inspector = new Outfit("BossRush_LastShiftEnemy_Inspector", "检车员", "Carriage Inspector", 104, 32, 27);
        private static readonly Outfit Escort = new Outfit("BossRush_LastShiftEnemy_Escort", "押运员", "Escort Guard", 1144, 33, 0);
        private static readonly Outfit Captain = new Outfit("BossRush_LastShiftEnemy_Captain", "押运队长", "Escort Captain", 43, 2, 741);
        private static readonly Outfit Chief = new Outfit(ChiefKey, "押运长", "Chief Escort", 0, 0, 0);
        // 小队长（2026-11-13）：信号工长戴帽子与眼镜、二级甲；调车长戴头灯与护目镜、三级甲；押运班长戴三级盔与护目镜、三级甲。装备 ID 都在 OfficialKey 表里核过。
        private static readonly Outfit Signalman = new Outfit("BossRush_LastShiftEnemy_Signalman", "信号工长", "Signal Foreman", 104, 33, 741);
        private static readonly Outfit Shunter = new Outfit("BossRush_LastShiftEnemy_Shunter", "调车长", "Shunting Master", 138, 2, 27);
        private static readonly Outfit Sergeant = new Outfit("BossRush_LastShiftEnemy_Sergeant", "押运班长", "Escort Sergeant", 43, 2, 27);
        private static readonly Outfit[] All = { Walker, Inspector, Escort, Captain, Chief, Signalman, Shunter, Sergeant };

        // 部件 ID 只取潮闸镇已对照官方 CustomFaceData 核过的那几组（运行时 CheckParts 再核一遍）。服色走羽色 / 翼尾配色，帽盔照穿。
        // 巡道工：圆脸、短喙、浅色羽，瓶绿配色。
        private static readonly Look WalkerLook = new Look { Hair = 14, Eye = 10, Brow = 1, Mouth = 25,
            Feather = 0xE6DCC0, Accent = 0x2F5D45, Beak = 0xE3A04F, Iris = 0x2A2118, Head = .04f, Forehead = .02f, Round = .95f, EyeScale = 1.4f, EyeDistance = 34f, MouthScale = .85f };
        // 检车员：窄脸、长喙、深色眼圈，油污蓝配色。
        private static readonly Look InspectorLook = new Look { Hair = 16, Eye = 12, Brow = 8, Mouth = 23,
            Feather = 0x9AA3A6, Accent = 0x2E4A6B, Beak = 0x4A4652, Iris = 0x1C1C24, Body = .97f, Head = -.04f, Forehead = .26f, Round = .38f, EyeScale = 1.6f, EyeDistance = 26f, MouthScale = 1.65f };
        // 押运员：方脸、厚喙，砖红配奶油。
        private static readonly Look EscortLook = new Look { Hair = 15, Eye = 13, Brow = 7, Mouth = 26,
            Feather = 0xB5523B, Accent = 0xF0E2BE, Beak = 0xD9B25E, Iris = 0x2B2420, Body = 1.03f, Head = .2f, Forehead = .1f, Round = .3f, EyeScale = 1.3f, EyeDistance = 30f, MouthScale = 1.3f };
        // 押运队长：老派、眉压低，铜色配色，体型 ×1.06。
        private static readonly Look CaptainLook = new Look { Hair = 9, Eye = 5, Brow = 4, Mouth = 1,
            Feather = 0x9B4632, Accent = 0xC08A3E, Beak = 0xD7943F, Iris = 0xF2D77C, Body = 1.06f, Head = .22f, Forehead = .22f, Round = .5f, EyeScale = 1.15f, EyeDistance = 23f, BrowTwist = -26f, MouthScale = 1.6f };
        // 信号工长：奶白羽、信号红翼尾，细长脸，体型 ×1.04（部件 ID 沿用上面几组核过的）。
        private static readonly Look SignalmanLook = new Look { Hair = 16, Eye = 13, Brow = 4, Mouth = 25,
            Feather = 0xE8DCC2, Accent = 0xC0392B, Beak = 0xE3A04F, Iris = 0x1C1C24, Body = 1.04f, Head = .02f, Forehead = .2f, Round = .45f, EyeScale = 1.45f, EyeDistance = 27f, BrowTwist = -18f, MouthScale = 1.1f };
        // 调车长：橄榄羽、警示黄翼尾，宽脸厚喙，体型 ×1.05。
        private static readonly Look ShunterLook = new Look { Hair = 14, Eye = 12, Brow = 8, Mouth = 26,
            Feather = 0x6E7B5A, Accent = 0xE0B030, Beak = 0x4A4652, Iris = 0x2A2118, Body = 1.05f, Head = .16f, Forehead = .06f, Round = .7f, EyeScale = 1.2f, EyeDistance = 31f, BrowTwist = -8f, MouthScale = 1.45f };
        // 押运班长：深砖红羽、黑翼尾，方脸压眉，体型 ×1.08。
        private static readonly Look SergeantLook = new Look { Hair = 9, Eye = 5, Brow = 7, Mouth = 1,
            Feather = 0x7A2E22, Accent = 0x2B2B2B, Beak = 0xD7943F, Iris = 0xF2D77C, Body = 1.08f, Head = .24f, Forehead = .14f, Round = .28f, EyeScale = 1.1f, EyeDistance = 24f, BrowTwist = -30f, MouthScale = 1.55f };

        private static Outfit For(LastShiftPost post)
        {
            switch (post)
            {
                case LastShiftPost.Walker: return Walker;
                case LastShiftPost.Inspector: return Inspector;
                case LastShiftPost.Escort: return Escort;
                case LastShiftPost.Captain: return Captain;
                case LastShiftPost.Signalman: return Signalman;
                case LastShiftPost.Shunter: return Shunter;
                case LastShiftPost.Sergeant: return Sergeant;
                default: return Chief;
            }
        }

        private static Look LookFor(LastShiftPost post)
        {
            switch (post)
            {
                case LastShiftPost.Walker: return WalkerLook;
                case LastShiftPost.Inspector: return InspectorLook;
                case LastShiftPost.Escort: return EscortLook;
                case LastShiftPost.Captain: return CaptainLook;
                case LastShiftPost.Signalman: return SignalmanLook;
                case LastShiftPost.Shunter: return ShunterLook;
                case LastShiftPost.Sergeant: return SergeantLook;
                default: return null;
            }
        }

        internal static string Name(LastShiftPost post) { Outfit o = For(post); return L10n.T(o.Zh, o.En); }

        /// <summary>中英文名注入（根 AGENTS.md §4.4）；会话开局按当前语言注一次。</summary>
        internal static void InjectLabels()
        {
            foreach (Outfit outfit in All) LocalizationHelper.InjectLocalization(outfit.Key, L10n.T(outfit.Zh, outfit.En));
        }

        /// <summary>创建前写身份：官方血条和击杀记录都读 characterPreset.nameKey。</summary>
        internal static void Tune(CharacterRandomPreset clone, LastShiftPost post)
        {
            clone.nameKey = For(post).Key;
            clone.showName = true;
            clone.showHealthBar = true;
            clone.isBoss = post == LastShiftPost.Chief;
        }

        /// <summary>创建完成后配装与捏脸。可失败的装饰：失败的装备沿用原件，捏脸有独立回滚。</summary>
        internal static void Dress(CharacterMainControl character, LastShiftPost post)
        {
            if (character == null || post == LastShiftPost.Chief) return;
            Outfit outfit = For(post);
            try
            {
                Equip(character, "Helmat", outfit.Helmet);
                Equip(character, "Armor", outfit.Armor);
                // 面罩 / 耳机有岗位归属；移除随机件，免得遮住帽盔和脸。
                Equip(character, "FaceMask", outfit.Face);
                Equip(character, "Headset", 0);
            }
            catch (Exception e) { Debug.LogWarning(LastShiftRaidScene.LogTag + " 岗位外观准备失败 " + outfit.Key + "：" + e.Message); }
            ApplyFace(character, post);
        }

        // Plug 成功才销毁旧件；失败仍保留官方配装，不把没穿上的新装备塞进背包变成额外掉落。
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
            if (prefab == null || prefab.DisplayNameRaw != OfficialKey(typeId)) throw new InvalidOperationException("官方岗位装备不可用 " + typeId);
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
                case 27: return "Item_Goggle";
                case 32: return "Item_ArmorLV1";
                case 33: return "Item_ArmorLV2";
                case 43: return "Item_HelmatLV3";
                case 104: return "Item_Hat";
                case 138: return "Item_HeadLantern";
                case 741: return "Item_Glass";
                case 1144: return "Item_HelmatLV2_2";
                default: throw new InvalidOperationException("未登记的岗位装备 " + typeId);
            }
        }

        private static void ApplyFace(CharacterMainControl character, LastShiftPost post)
        {
            Look look = LookFor(post);
            if (look == null) return;
            if (character.characterModel == null || character.characterModel.CustomFace == null)
            {
                Debug.LogWarning(LastShiftRaidScene.LogTag + " 敌人底模缺少官方捏脸组件：" + post);
                return;
            }
            CharacterModel model = character.characterModel;
            CustomFaceSettingData before = default(CustomFaceSettingData);
            Vector3 oldScale = model.transform.localScale;
            bool changed = false;
            try
            {
                before = model.CustomFace.ConvertToSaveData();
                // 从实例完整值起步：radius / heightOffset 与脚、翼挂点不能清零。
                CustomFaceSettingData face = Design(before, look);
                CheckParts(face);
                changed = true; // 官方加载可能改完部分五官后才抛错，仍须回滚。
                model.SetFaceFromData(face);
                model.transform.localScale = Vector3.one * look.Body;
                CustomFaceSettingData actual = model.CustomFace.ConvertToSaveData();
                if (actual.hairID != look.Hair || actual.eyeID != look.Eye || actual.mouthID != look.Mouth || actual.eyebrowID != look.Brow)
                    throw new InvalidOperationException("捏脸读回不一致");
            }
            catch (Exception e)
            {
                if (changed)
                {
                    try { model.SetFaceFromData(before); }
                    catch (Exception restore) { Debug.LogWarning(LastShiftRaidScene.LogTag + " 敌人原脸恢复失败：" + restore.Message); }
                    model.transform.localScale = oldScale;
                }
                Debug.LogWarning(LastShiftRaidScene.LogTag + " 敌人捏脸未应用 " + post + "：" + e.Message);
            }
        }

        private static CustomFaceSettingData Design(CustomFaceSettingData face, Look look)
        {
            face.savedSetting = true;
            face.headSetting.mainColor = ColorOf(look.Feather); face.headSetting.headScaleOffset = look.Head;
            face.headSetting.foreheadHeight = look.Forehead; face.headSetting.foreheadRound = look.Round;
            face.hairID = look.Hair; face.hairInfo.color = ColorOf(look.Accent);
            face.eyeID = look.Eye; face.eyeInfo.color = ColorOf(look.Iris); face.eyeInfo.scale = look.EyeScale;
            face.eyeInfo.distanceAngle = look.EyeDistance; face.eyeInfo.height = .04f; face.eyeInfo.twist = 0f;
            face.eyebrowID = look.Brow; face.eyebrowInfo.color = ColorOf(look.Accent); face.eyebrowInfo.scale = 1.45f;
            face.eyebrowInfo.distanceAngle = look.EyeDistance; face.eyebrowInfo.height = .1f; face.eyebrowInfo.twist = look.BrowTwist;
            face.mouthID = look.Mouth; face.mouthInfo.color = ColorOf(look.Beak); face.mouthInfo.scale = look.MouthScale;
            face.mouthInfo.height = -.06f; face.mouthInfo.twist = 0f; face.mouthInfo.leftRightAngle = 0f;
            face.wingInfo.color = ColorOf(look.Accent); face.wingInfo.scale = 1.1f;
            face.tailInfo.color = ColorOf(look.Accent); face.tailInfo.scale = 1.1f;
            return face;
        }

        private static void CheckParts(CustomFaceSettingData face)
        {
            CustomFaceData data = Duckov.Utilities.GameplayDataSettings.CustomFaceData;
            if (data == null) throw new InvalidOperationException("官方 CustomFaceData 缺失");
            CheckPart(data.Hairs, face.hairID); CheckPart(data.Eyes, face.eyeID); CheckPart(data.Eyebrows, face.eyebrowID);
            CheckPart(data.Mouths, face.mouthID); CheckPart(data.Tails, face.tailID); CheckPart(data.Wings, face.wingID); CheckPart(data.Foots, face.footID);
        }

        private static void CheckPart(CustomFacePartCollection collection, int id)
        {
            // 官方 GetPartPrefab 找不到 id 会静默返回首项，必须对照实际 id。
            CustomFacePart part = collection == null || collection.totalCount == 0 ? null : collection.GetPartPrefab(id);
            if (part == null || part.id != id) throw new InvalidOperationException("官方捏脸部件缺失 " + id);
        }

        private static Color ColorOf(int rgb) { return new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, 1f); }
    }
}
