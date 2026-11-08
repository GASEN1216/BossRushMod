using System;
using System.Globalization;
using UnityEngine;

namespace BossRush
{
    /// <summary>岗位捏脸只应用到刚创建的 Scav 实例；不改共享 preset、材质或玩家捏脸。</summary>
    internal static class TidegateEnemyAppearance
    {
        private sealed class Look
        {
            internal string Id;
            internal int Hair, Eye, Brow, Mouth;
            internal int Feather, Accent, Beak, Iris;
            internal float Body = 1f, Head = 0.1f, Forehead = .08f, Round = .8f;
            internal float EyeScale = 1.35f, EyeDistance = 28f, EyeHeight = .04f, BrowTwist = -12f;
            internal float MouthScale = 1.4f, MouthHeight = -.06f, MouthTwist, MouthYaw;
            internal float WingScale = 1.1f, TailScale = 1.1f;
        }

        // ID 已核对 resources.assets / CustomFaceData 76353；每种嘴/眼均有官方可染色 renderer。
        // 头盔仍按岗位穿戴，辨识依靠露出的脸、夸张喙形、体羽/翼尾及身材，而非只靠帽下的头发。
        private static readonly Look Trooper = new Look { Id="brass_steeltooth", Hair=13, Eye=9, Brow=3, Mouth=30,
            Feather=0xA4AE64, Accent=0x293E32, Beak=0xDDC16C, Iris=0x231E17, Head=.08f, Forehead=.02f, Round=.55f, MouthScale=1f };
        private static readonly Look Captain = new Look { Id="burgundy_eagle", Hair=9, Eye=5, Brow=4, Mouth=1,
            Feather=0x8E4659, Accent=0xE5CB9A, Beak=0xD7943F, Iris=0xF2D77C, Body=1.08f, Head=.22f, Forehead=.20f, Round=.55f, EyeScale=1.15f, EyeDistance=23f, BrowTwist=-24f, MouthScale=1.75f };
        private static readonly Look Dockhand = new Look { Id="blue_pelican", Hair=14, Eye=9, Brow=1, Mouth=25,
            Feather=0x4385A2, Accent=0xDCE2CF, Beak=0xDEA25C, Iris=0xF2CA76, Body=1.04f, Head=.26f, Forehead=.02f, Round=.7f, EyeScale=1.5f, EyeDistance=38f, MouthScale=1.15f };
        private static readonly Look Ambusher = new Look { Id="reed_spoonbill", Hair=18, Eye=12, Brow=5, Mouth=21,
            Feather=0x8BB556, Accent=0x594E73, Beak=0x51445A, Iris=0xF5B649, Body=.96f, Head=-.08f, Forehead=.24f, Round=.45f, EyeScale=1.75f, EyeDistance=40f, MouthScale=.95f, MouthYaw=-9f };
        private static readonly Look Digger = new Look { Id="sand_shoebill", Hair=15, Eye=13, Brow=2, Mouth=26,
            Feather=0xC78369, Accent=0xF0D3A2, Beak=0x637487, Iris=0x6ED2C4, Body=1.06f, Head=.28f, Forehead=.03f, Round=.6f, EyeScale=1.35f, EyeDistance=32f, MouthScale=1.05f };
        private static readonly Look Grenadier = new Look { Id="ember_toucan", Hair=13, Eye=5, Brow=6, Mouth=29,
            Feather=0xD76235, Accent=0x402D49, Beak=0xEDC755, Iris=0x73D8D0, Body=.98f, Head=.02f, Forehead=.18f, Round=.5f, EyeScale=1.55f, EyeDistance=36f, MouthScale=1.05f, MouthTwist=-12f, MouthYaw=9f };
        private static readonly Look Guard = new Look { Id="mint_valve", Hair=8, Eye=13, Brow=7, Mouth=15,
            Feather=0xB9DDD4, Accent=0x3D697E, Beak=0x6D9673, Iris=0x297DAD, Head=.16f, Forehead=.22f, Round=.42f, EyeScale=1.65f, EyeDistance=32f, MouthScale=1.8f, MouthTwist=8f };
        private static readonly Look CapeGuard = new Look { Id="midnight_starbeak", Hair=14, Eye=11, Brow=3, Mouth=27,
            Feather=0x30465F, Accent=0xDCE9DE, Beak=0xD9E4D1, Iris=0xF1C858, Body=1.07f, Head=.1f, Forehead=.18f, Round=.65f, EyeScale=1.6f, EyeDistance=27f, MouthScale=1.05f };
        private static readonly Look Marksman = new Look { Id="lilac_nutcrow", Hair=16, Eye=12, Brow=8, Mouth=23,
            Feather=0xA797BB, Accent=0x443653, Beak=0x433A54, Iris=0xD3E788, Body=.97f, Head=-.04f, Forehead=.3f, Round=.38f, EyeScale=1.8f, EyeDistance=30f, MouthScale=1.35f, MouthYaw=7f };
        private static readonly Look CapeScout = new Look { Id="turquoise_flamingo", Hair=17, Eye=10, Brow=9, Mouth=24,
            Feather=0x5BBFB1, Accent=0xE0D997, Beak=0xE79791, Iris=0x385D69, Body=.95f, Head=.06f, Forehead=.12f, Round=.6f, EyeScale=1.45f, EyeDistance=37f, MouthScale=1.05f, MouthTwist=11f };
        private static readonly Look Boss = new Look { Id="rust_king", Hair=18, Eye=13, Brow=4, Mouth=30,
            Feather=0x8E4134, Accent=0xD9C8A7, Beak=0xDAC76F, Iris=0xE9A54D, Body=TidegateSandBoss.ModelScale, Head=.35f, Forehead=.16f, Round=.95f,
            EyeScale=2.05f, EyeDistance=35f, MouthScale=1.2f, WingScale=1.35f, TailScale=1.4f };

        private static Look For(string key)
        {
            switch (key)
            {
                case "BossRush_TidegateEnemy_Trooper": return Trooper;
                case "BossRush_TidegateEnemy_Captain": return Captain;
                case "BossRush_TidegateEnemy_Dockhand": return Dockhand;
                case "BossRush_TidegateEnemy_Ambusher": return Ambusher;
                case "BossRush_TidegateEnemy_Digger": return Digger;
                case "BossRush_TidegateEnemy_Grenadier": return Grenadier;
                case "BossRush_TidegateEnemy_Guard": return Guard;
                case "BossRush_TidegateEnemy_CapeGuard": return CapeGuard;
                case "BossRush_TidegateEnemy_Marksman": return Marksman;
                case "BossRush_TidegateEnemy_CapeScout": return CapeScout;
                case TidegateText.MapBossKey: return Boss;
                default: throw new InvalidOperationException("Unknown tidegate appearance " + key);
            }
        }

        internal static bool Apply(CharacterMainControl character, string identityKey)
        {
            if (character == null || character.characterModel == null || character.characterModel.CustomFace == null)
            {
                Debug.LogWarning(TidegateRaidScene.LogTag + " 敌人底模缺少官方捏脸组件：" + identityKey);
                return false;
            }
            CharacterModel model = character.characterModel;
            CustomFaceSettingData before = default(CustomFaceSettingData);
            Vector3 oldScale = model.transform.localScale;
            bool changed = false;
            try
            {
                before = model.CustomFace.ConvertToSaveData();
                Look look = For(identityKey);
                // 从角色实例完整值复制起步；radius/heightOffset 和脚/翼挂点不可清零。
                CustomFaceSettingData face = Design(before, look);
                CheckParts(face);
                changed = true; // LoadFromData 可能在改完部分五官后抛错，仍须回滚。
                model.SetFaceFromData(face);
                model.transform.localScale = Vector3.one * look.Body;
                string details;
                if (!Matches(character, identityKey, out details)) throw new InvalidOperationException("Face readback mismatch: " + details);
                return true;
            }
            catch (Exception e)
            {
                if (changed)
                {
                    try { model.SetFaceFromData(before); }
                    catch (Exception restore) { Debug.LogWarning(TidegateRaidScene.LogTag + " 敌人原脸恢复失败：" + restore.Message); }
                    model.transform.localScale = oldScale;
                }
                Debug.LogWarning(TidegateRaidScene.LogTag + " 敌人捏脸未应用 " + identityKey + "：" + e.Message);
                return false;
            }
        }

        private static CustomFaceSettingData Design(CustomFaceSettingData face, Look look)
        {
            face.savedSetting = true;
            face.headSetting.mainColor = ColorOf(look.Feather); face.headSetting.headScaleOffset = look.Head;
            face.headSetting.foreheadHeight = look.Forehead; face.headSetting.foreheadRound = look.Round;
            face.hairID = look.Hair; face.hairInfo.color = ColorOf(look.Accent);
            face.eyeID = look.Eye; face.eyeInfo.color = ColorOf(look.Iris); face.eyeInfo.scale = look.EyeScale;
            face.eyeInfo.distanceAngle = look.EyeDistance; face.eyeInfo.height = look.EyeHeight; face.eyeInfo.twist = 0f;
            face.eyebrowID = look.Brow; face.eyebrowInfo.color = ColorOf(look.Accent); face.eyebrowInfo.scale = 1.45f;
            face.eyebrowInfo.distanceAngle = look.EyeDistance; face.eyebrowInfo.height = .1f; face.eyebrowInfo.twist = look.BrowTwist;
            face.mouthID = look.Mouth; face.mouthInfo.color = ColorOf(look.Beak); face.mouthInfo.scale = look.MouthScale;
            face.mouthInfo.height = look.MouthHeight; face.mouthInfo.twist = look.MouthTwist; face.mouthInfo.leftRightAngle = look.MouthYaw;
            face.wingInfo.color = ColorOf(look.Accent); face.wingInfo.scale = look.WingScale;
            face.tailInfo.color = ColorOf(look.Accent); face.tailInfo.scale = look.TailScale;
            return face;
        }

        private static void CheckParts(CustomFaceSettingData face)
        {
            CustomFaceData data = Duckov.Utilities.GameplayDataSettings.CustomFaceData;
            if (data == null) throw new InvalidOperationException("Official CustomFaceData missing");
            CheckPart(data.Hairs, face.hairID); CheckPart(data.Eyes, face.eyeID); CheckPart(data.Eyebrows, face.eyebrowID);
            CheckPart(data.Mouths, face.mouthID); CheckPart(data.Tails, face.tailID); CheckPart(data.Wings, face.wingID); CheckPart(data.Foots, face.footID);
        }
        private static void CheckPart(CustomFacePartCollection collection, int id)
        {
            // 官方 GetPartPrefab 找不到 id 会静默返回首项，必须对照实际 id。
            CustomFacePart part = collection == null || collection.totalCount == 0 ? null : collection.GetPartPrefab(id);
            if (part == null || part.id != id) throw new InvalidOperationException("Official face part missing " + id);
        }

        internal static bool Matches(CharacterMainControl character, string identityKey, out string details)
        {
            try { return ReadMatch(character, identityKey, out details); }
            catch (Exception e) { details = "custom_face_read_failed:" + e.Message; return false; }
        }
        private static bool ReadMatch(CharacterMainControl character, string identityKey, out string details)
        {
            details = "custom_face_missing";
            if (character == null || character.characterModel == null || character.characterModel.CustomFace == null) return false;
            Look look = For(identityKey);
            CustomFaceSettingData actual = character.characterModel.CustomFace.ConvertToSaveData();
            CustomFaceSettingData expected = Design(actual, look);
            Vector3 scale = character.characterModel.transform.localScale;
            bool match = Same(actual.headSetting.mainColor, expected.headSetting.mainColor)
                && Close(actual.headSetting.headScaleOffset, look.Head) && Close(actual.headSetting.foreheadHeight, look.Forehead)
                && Close(actual.headSetting.foreheadRound, look.Round) && actual.hairID == look.Hair && Same(actual.hairInfo.color, expected.hairInfo.color)
                && actual.eyeID == look.Eye && SameFeature(actual.eyeInfo, expected.eyeInfo)
                && actual.eyebrowID == look.Brow && SameFeature(actual.eyebrowInfo, expected.eyebrowInfo)
                && actual.mouthID == look.Mouth && SameFeature(actual.mouthInfo, expected.mouthInfo)
                && Same(actual.wingInfo.color, expected.wingInfo.color) && Close(actual.wingInfo.scale, look.WingScale)
                && Same(actual.tailInfo.color, expected.tailInfo.color) && Close(actual.tailInfo.scale, look.TailScale)
                && Close(scale.x, look.Body) && Close(scale.y, look.Body) && Close(scale.z, look.Body);
            details = "profile=" + look.Id + ",match=" + match + ",feather=" + Hex(actual.headSetting.mainColor)
                + ",head=" + F(actual.headSetting.headScaleOffset + 1f) + "/" + F(actual.headSetting.foreheadHeight) + "/" + F(actual.headSetting.foreheadRound)
                + ",eye=" + actual.eyeID + "/" + F(actual.eyeInfo.scale) + "/" + Hex(actual.eyeInfo.color)
                + ",mouth=" + actual.mouthID + "/" + F(actual.mouthInfo.scale) + "/" + F(actual.mouthInfo.twist) + "/" + Hex(actual.mouthInfo.color)
                + ",hair=" + actual.hairID + ",wing=" + Hex(actual.wingInfo.color) + ",body=" + F(scale.x);
            return match;
        }
        private static bool SameFeature(CustomFacePartInfo a, CustomFacePartInfo b)
        { return Same(a.color,b.color) && Close(a.scale,b.scale) && Close(a.height,b.height) && Close(a.twist,b.twist)
            && Close(a.distanceAngle,b.distanceAngle) && Close(a.leftRightAngle,b.leftRightAngle); }
        private static bool Close(float a, float b) { return Math.Abs(a-b) < .001f; }
        private static bool Same(Color a, Color b) { return Close(a.r,b.r) && Close(a.g,b.g) && Close(a.b,b.b) && Close(a.a,b.a); }
        private static Color ColorOf(int rgb) { return new Color(((rgb >> 16) & 255)/255f, ((rgb >> 8) & 255)/255f, (rgb & 255)/255f, 1f); }
        private static string F(float value) { return value.ToString("0.###", CultureInfo.InvariantCulture); }
        private static string Hex(Color color) { return "#" + ((int)Math.Round(color.r*255f)).ToString("X2") + ((int)Math.Round(color.g*255f)).ToString("X2") + ((int)Math.Round(color.b*255f)).ToString("X2"); }
    }
}
