using System;
using System.Collections.Generic;
using Duckov.Utilities;
using ItemStatsSystem;
using ItemStatsSystem.Items;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// COMPAT：普通巡逻的固定外观。仅由 SkyIslandPatrols 在官方生成完成后调用；不生成角色、
    /// 不改阵营/AI/Stats、不登记永久 NPC。脸与身体不使用随机流，帽甲只有模型，不进入物品树或掉落。
    /// </summary>
    internal static class SkyIslandPatrolAppearance
    {
        internal sealed class Profile
        {
            internal readonly string Region;
            internal readonly Color Feather, Trim;
            internal readonly int Hair, Eye, Brow, Mouth, Tail, Wing, Foot, Hat;
            internal readonly float Head, Forehead, Round, EyeScale, EyeAngle, BeakScale;
            internal readonly Vector3 BodyScale;

            internal Profile(string region, Color feather, Color trim, int hair, int eye, int brow, int mouth,
                int tail, int wing, int foot, int hat, float head, float forehead, float round,
                float eyeScale, float eyeAngle, float beakScale, Vector3 bodyScale)
            {
                Region = region; Feather = feather; Trim = trim;
                Hair = hair; Eye = eye; Brow = brow; Mouth = mouth;
                Tail = tail; Wing = wing; Foot = foot; Hat = hat;
                Head = head; Forehead = forehead; Round = round;
                EyeScale = eyeScale; EyeAngle = eyeAngle; BeakScale = beakScale; BodyScale = bodyScale;
            }
        }

        // ID 与部件已按 2026-09-30 本机官方 resources.assets 核对（不是自定义装备或 Boss 装备）：
        // ItemAssetsCollection pathID=76722，CustomFaceData pathID=76353。
        // A 普通帽 / B 一级盔 / C 头灯 / D 二级盔变体 / E 三级盔 / F 三级盔变体 / G 四级盔 / H 四级盔变体。
        // 遵循官方 EquipmentModel / ItemGraphic 两条模型来源。不要把视觉克隆插到角色槽里，否则加密巡逻会额外制造可卖装备。
        private static readonly Profile[] Profiles =
        {
            new Profile("A", Rgb(169, 188, 199), Rgb(65, 96, 117), 1, 0, 0, 1, 0, 0, 0, 104,
                -0.12f, 0.08f, 0.95f, 1.12f, 38f, 0.82f, new Vector3(0.94f, 0.96f, 0.94f)),
            new Profile("B", Rgb(206, 168, 112), Rgb(102, 63, 40), 4, 2, 1, 3, 1, 1, 1, 41,
                -0.05f, 0.12f, 0.78f, 1.04f, 46f, 1.12f, new Vector3(1.02f, 0.96f, 1.00f)),
            new Profile("C", Rgb(178, 197, 115), Rgb(61, 102, 50), 7, 3, 2, 5, 2, 2, 1, 138,
                0.04f, 0.22f, 0.90f, 1.20f, 52f, 0.91f, new Vector3(0.96f, 1.04f, 0.97f)),
            new Profile("D", Rgb(88, 128, 99), Rgb(34, 65, 47), 8, 4, 3, 6, 1, 3, 2, 1144,
                0.08f, 0.18f, 0.62f, 0.92f, 43f, 1.15f, new Vector3(1.04f, 1.00f, 1.03f)),
            new Profile("E", Rgb(101, 128, 171), Rgb(35, 50, 94), 10, 5, 4, 7, 3, 4, 3, 43,
                -0.02f, 0.28f, 0.48f, 0.84f, 56f, 1.04f, new Vector3(0.97f, 1.07f, 0.97f)),
            new Profile("F", Rgb(216, 214, 192), Rgb(87, 91, 105), 12, 6, 5, 8, 4, 2, 4, 1145,
                0.10f, 0.16f, 0.98f, 0.90f, 34f, 0.87f, new Vector3(1.06f, 1.02f, 1.04f)),
            new Profile("G", Rgb(106, 107, 113), Rgb(182, 98, 42), 14, 7, 6, 10, 3, 3, 2, 44,
                0.12f, 0.32f, 0.52f, 0.80f, 48f, 1.18f, new Vector3(1.08f, 1.05f, 1.07f)),
            new Profile("H", Rgb(128, 72, 82), Rgb(51, 32, 48), 16, 8, 7, 12, 4, 4, 3, 1147,
                0.15f, 0.36f, 0.42f, 0.78f, 58f, 1.24f, new Vector3(1.07f, 1.09f, 1.05f)),
            // 四小岛有独立主题；Rank 仍由巡逻数据传入 3/4/6/7，不把外观身份映射回大岛。
            // 蛙鸣池：浅青羽 / 宽草帽；倒挂邮亭：锈橙羽 / 鸭舌帽。
            new Profile("S1", Rgb(103, 193, 177), Rgb(211, 220, 136), 2, 9, 0, 4, 0, 1, 0, 1636,
                -0.10f, 0.10f, 1.00f, 1.30f, 60f, 0.78f, new Vector3(0.92f, 0.97f, 0.95f)),
            new Profile("S2", Rgb(186, 114, 72), Rgb(83, 48, 46), 3, 10, 8, 11, 2, 0, 1, 1136,
                0.02f, 0.25f, 0.70f, 1.05f, 36f, 1.10f, new Vector3(0.99f, 1.06f, 0.95f)),
            // 听雨洞：石紫羽 / 锅盔；残星瞭台：冰蓝羽 / 蓝色联合盔。
            new Profile("S3", Rgb(118, 103, 155), Rgb(188, 183, 205), 6, 11, 9, 13, 1, 4, 2, 59,
                0.06f, 0.14f, 0.84f, 0.88f, 48f, 0.96f, new Vector3(1.04f, 0.99f, 1.06f)),
            new Profile("S4", Rgb(196, 218, 236), Rgb(191, 147, 74), 18, 12, 2, 14, 4, 3, 4, 1146,
                0.11f, 0.40f, 0.50f, 0.82f, 54f, 1.18f, new Vector3(1.00f, 1.11f, 1.02f))
        };

        // 普通一级至四级护甲轮廓；Rank 1..8 每两档升一级。原物品、护甲值和掉落仍由生成 preset 管。
        private static readonly int[] ArmorByRank = { 32, 32, 33, 33, 2, 2, 34, 34 };
        private static readonly HashSet<string> Failed = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>
        /// 同步调用，不含 await，不保存/修改 UnityEngine.Random.state。
        /// false 表示外观未完整装配，调用者应回收本次生成或明确处理失败，不能把残缺外观当成功。
        /// 同一角色重复应用同一岛区/Rank 幂等；A..H 与 S1..S4 各有独立固定主题。
        /// </summary>
        internal static bool Apply(CharacterMainControl character, string regionId, int rank)
        {
            Profile profile = Resolve(regionId);
            if (profile == null || rank < 1 || rank > ArmorByRank.Length)
                return Failure("invalid_profile:" + regionId + ":" + rank);
            if (character == null || character.IsMainCharacter || character.CharacterItem == null || character.EquipmentController == null)
                return Failure("invalid_character:" + profile.Region);
            SkyIslandPatrolAppearanceVisuals owner = character.GetComponent<SkyIslandPatrolAppearanceVisuals>();
            if (owner != null && owner.Matches(profile.Region, rank)) return true;
            CharacterModel replacement = null;
            try
            {
                CharacterModel modelPrefab = GameplayDataSettings.Prefabs.DefaultCharacterModel;
                CustomFaceData data = GameplayDataSettings.CustomFaceData;
                if (modelPrefab == null || modelPrefab.CustomFace == null || data == null || data.DefaultPreset == null)
                    return Failure("default_model_or_face_missing");
                CustomFaceSettingData face;
                if (!TryFace(profile, data, out face)) return Failure("face_part_missing:" + profile.Region);
                VisualSource hat = EquipmentPrefab(profile.Hat);
                VisualSource armor = EquipmentPrefab(ArmorByRank[rank - 1]);
                if (hat.Prefab == null || armor.Prefab == null) return Failure("equipment_model_missing:" + profile.Region + ":" + rank);
                if (owner != null) owner.Release();
                // 使用官方默认鸭模，不留下 preset 的幽灵/怪物底模、随机身体比例或玩家复制脸。
                replacement = UnityEngine.Object.Instantiate(modelPrefab);
                if (character.characterModel != null) character.characterModel.gameObject.SetActive(false);
                character.SetCharacterModel(replacement);
                replacement = null; // 模型已移交 CharacterMainControl，失败后仍由调用者回收角色。
                CharacterModel model = character.characterModel;
                if (model == null || model.CustomFace == null || model.HelmatSocket == null || model.ArmorSocket == null)
                    return Failure("model_binding_failed:" + profile.Region);
                model.SetFaceFromData(face);
                model.transform.localScale = profile.BodyScale;
                if (owner == null) owner = character.gameObject.AddComponent<SkyIslandPatrolAppearanceVisuals>();
                if (!owner.Bind(character, profile.Region, rank, hat, armor))
                    return Failure("visual_binding_failed:" + profile.Region + ":" + rank);
                return true;
            }
            catch (Exception e)
            {
                if (owner != null) owner.Release();
                if (replacement != null) UnityEngine.Object.Destroy(replacement.gameObject);
                return Failure("apply_failed:" + profile.Region + ":" + rank, e);
            }
        }

        private static Color Rgb(int r, int g, int b) { return new Color(r / 255f, g / 255f, b / 255f, 1f); }
        private static Profile Resolve(string region)
        {
            for (int i = 0; i < Profiles.Length; i++) if (Profiles[i].Region == region) return Profiles[i];
            return null;
        }

        private static bool HasPart(CustomFacePartCollection collection, int id)
        {
            // 官方 GetPartPrefab 对坏 ID 会静默回落首项，必须回读真实 id。
            if (collection == null || collection.totalCount == 0) return false;
            CustomFacePart part = collection.GetPartPrefab(id);
            return part != null && part.id == id;
        }

        private static bool TryFace(Profile p, CustomFaceData data, out CustomFaceSettingData face)
        {
            face = data.DefaultPreset.settings; // 保留官方 radius/heightOffset，不从全零 struct 起手。
            if (!HasPart(data.Hairs, p.Hair) || !HasPart(data.Eyes, p.Eye) || !HasPart(data.Eyebrows, p.Brow) ||
                !HasPart(data.Mouths, p.Mouth) || !HasPart(data.Tails, p.Tail) || !HasPart(data.Wings, p.Wing) || !HasPart(data.Foots, p.Foot)) return false;
            face.savedSetting = true;
            face.hairID = p.Hair; face.eyeID = p.Eye; face.eyebrowID = p.Brow; face.mouthID = p.Mouth;
            face.tailID = p.Tail; face.wingID = p.Wing; face.footID = p.Foot;
            face.headSetting.mainColor = p.Feather; face.headSetting.headScaleOffset = p.Head;
            face.headSetting.foreheadHeight = p.Forehead; face.headSetting.foreheadRound = p.Round;
            face.hairInfo.color = p.Trim; face.eyeInfo.color = Rgb(23, 31, 39); face.eyebrowInfo.color = p.Trim;
            face.eyeInfo.scale = p.EyeScale; face.eyeInfo.distanceAngle = p.EyeAngle;
            face.eyeInfo.height = 0.02f + (p.Head + 0.12f) * 0.12f; face.eyeInfo.twist = 0f;
            face.eyebrowInfo.scale = 0.85f + p.EyeScale * 0.16f;
            face.eyebrowInfo.distanceAngle = p.EyeAngle; face.eyebrowInfo.height = 0.08f;
            face.eyebrowInfo.twist = (p.Round - 0.7f) * 24f;
            face.mouthInfo.color = Rgb(214, 144, 65); face.mouthInfo.scale = p.BeakScale;
            face.mouthInfo.height = -0.02f; face.mouthInfo.leftRightAngle = 0f; face.mouthInfo.twist = 0f;
            face.tailInfo.color = p.Trim; face.tailInfo.scale = 0.85f + p.Head * 0.6f;
            face.wingInfo.color = p.Feather; face.wingInfo.scale = 0.95f + p.Head * 0.3f;
            face.footInfo.scale = 1f;
            DuckNpcFaceCodec.Clamp(ref face);
            return true;
        }

        internal struct VisualSource
        {
            internal readonly GameObject Prefab;
            internal readonly bool UnitScale;
            internal VisualSource(GameObject prefab, bool unitScale) { Prefab = prefab; UnitScale = unitScale; }
        }

        private static VisualSource EquipmentPrefab(int id)
        {
            Item item = ItemAssetsCollection.GetPrefab(id); // InstantiateSync 的缺资源空壳不能证明供货。
            if (item == null || item.TypeID != id || item.AgentUtilities == null) return default(VisualSource);
            ItemAgent agent = item.AgentUtilities.GetPrefab("EquipmentModel");
            // 本机这些普通帽甲走官方 ChangeEquipmentModel 的 ItemGraphic 分支，不得只查 agent 后误报缺货。
            GameObject model = agent != null ? agent.gameObject : item.ItemGraphic == null ? null : item.ItemGraphic.gameObject;
            if (model == null || (model.GetComponentsInChildren<MeshRenderer>(true).Length == 0 &&
                model.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length == 0)) return default(VisualSource);
            return new VisualSource(model, agent == null);
        }

        internal static bool Failure(string reason, Exception error = null)
        {
            if (Failed.Add(reason))
                Debug.LogWarning("[SkyIslandPatrolAppearance] " + reason + (error == null ? string.Empty : ": " + error.Message));
            return false;
        }

        /// <summary>只清空低噪声失败去重表；活体外观由角色身上的 owner 销毁，不持有全局活体引用。</summary>
        internal static void ResetStaticCaches() { Failed.Clear(); }
    }

    /// <summary>纯表现 owner，无 Update/协程。原随机装备仍在物品树，只隐藏其模型，死亡掉落与价值不变。</summary>
    internal sealed class SkyIslandPatrolAppearanceVisuals : MonoBehaviour
    {
        private CharacterMainControl character;
        private string region;
        private int rank;
        private SkyIslandPatrolAppearance.VisualSource hatPrefab, armorPrefab;
        private GameObject hat, armor;
        private bool bound, refreshing, faceVisibilityCaptured;
        private GameObject hairObject, mouthObject;
        private bool hairWasActive, mouthWasActive;
        private readonly List<Slot> slots = new List<Slot>();
        private readonly Dictionary<Renderer, bool> hidden = new Dictionary<Renderer, bool>();
        private static readonly string[] SlotKeys = { "Helmat", "Armor", "FaceMask", "Headset", "Backpack" };

        internal bool Matches(string id, int level)
        { return bound && character != null && region == id && rank == level && hat != null && armor != null; }

        internal bool Bind(CharacterMainControl target, string id, int level,
            SkyIslandPatrolAppearance.VisualSource head, SkyIslandPatrolAppearance.VisualSource body)
        {
            Release();
            character = target; region = id; rank = level; hatPrefab = head; armorPrefab = body;
            try
            {
                foreach (string key in SlotKeys)
                {
                    Slot slot = target.CharacterItem.Slots.GetSlot(key);
                    if (slot == null) throw new InvalidOperationException("slot_missing:" + key);
                    slots.Add(slot);
                    slot.onSlotContentChanged += OnSlotChanged;
                }
                bound = true;
                if (Refresh()) return true;
            }
            catch (Exception e) { SkyIslandPatrolAppearance.Failure("visual_owner:" + id + ":" + level, e); }
            Release();
            return false;
        }

        private void OnSlotChanged(Slot ignored)
        {
            if (!bound || refreshing || character == null || character.Health == null || character.Health.IsDead) return;
            if (!Refresh()) Release();
        }

        private bool Refresh()
        {
            if (refreshing) return true;
            refreshing = true;
            try
            {
                CharacterModel model = character.characterModel;
                if (model == null || model.CustomFace == null) return false;
                if (hat == null) hat = CloneVisual(hatPrefab, model.HelmatSocket, "SkyIslandPatrolHat");
                if (armor == null) armor = CloneVisual(armorPrefab, model.ArmorSocket, "SkyIslandPatrolArmor");
                if (hat == null || armor == null) return false;
                foreach (Slot slot in slots)
                {
                    Item item = slot.Content;
                    ItemAgent original = item == null || item.AgentUtilities == null ? null : item.AgentUtilities.ActiveAgent;
                    if (original == null) continue;
                    foreach (Renderer renderer in original.GetComponentsInChildren<Renderer>(true))
                    {
                        if (!hidden.ContainsKey(renderer)) hidden.Add(renderer, renderer.enabled);
                        renderer.enabled = false;
                    }
                }
                // 隐藏随机面罩/头盔后也纠正官方由原物品常量计算的脸部显隐，避免仍遮住固定喙。
                if (!faceVisibilityCaptured)
                {
                    hairObject = model.CustomFace.hairSocket == null ? null : model.CustomFace.hairSocket.gameObject;
                    mouthObject = model.CustomFace.mouthPart == null || model.CustomFace.mouthPart.socket == null
                        ? null : model.CustomFace.mouthPart.socket.gameObject;
                    hairWasActive = hairObject != null && hairObject.activeSelf;
                    mouthWasActive = mouthObject != null && mouthObject.activeSelf;
                    faceVisibilityCaptured = true;
                }
                if (hairObject != null) hairObject.SetActive(false);
                if (mouthObject != null) mouthObject.SetActive(true);
                return true;
            }
            catch (Exception e) { return SkyIslandPatrolAppearance.Failure("visual_refresh:" + region + ":" + rank, e); }
            finally { refreshing = false; }
        }

        private GameObject CloneVisual(SkyIslandPatrolAppearance.VisualSource source, Transform socket, string objectName)
        {
            if (source.Prefab == null || socket == null) return null;
            GameObject staging = new GameObject("SkyIslandPatrolVisualStaging");
            staging.SetActive(false);
            staging.transform.SetParent(transform, false);
            GameObject result = null;
            bool retained = false;
            try
            {
                result = UnityEngine.Object.Instantiate(source.Prefab, staging.transform, false);
                // 克隆尚未激活，去掉 ItemAgent、脚本和物理组件。只复用模型，不创建/绑定 Item，不能拾取或触发伤害。
                foreach (MonoBehaviour script in result.GetComponentsInChildren<MonoBehaviour>(true))
                    UnityEngine.Object.DestroyImmediate(script);
                foreach (Collider collider in result.GetComponentsInChildren<Collider>(true))
                    UnityEngine.Object.DestroyImmediate(collider);
                foreach (Rigidbody body in result.GetComponentsInChildren<Rigidbody>(true))
                    UnityEngine.Object.DestroyImmediate(body);
                foreach (AudioSource audio in result.GetComponentsInChildren<AudioSource>(true))
                    UnityEngine.Object.DestroyImmediate(audio);
                foreach (Light light in result.GetComponentsInChildren<Light>(true))
                    UnityEngine.Object.DestroyImmediate(light); // 头灯只取固定外形，密集巡逻不新增实时灯或阴影。
                Renderer[] renderers = result.GetComponentsInChildren<Renderer>(true);
                if (renderers.Length == 0) return null;
                result.name = objectName;
                result.transform.SetParent(socket, false);
                result.transform.localPosition = Vector3.zero;
                result.transform.localRotation = Quaternion.identity;
                // 官方 ItemGraphicInfo.CreateAGraphic 重置根缩放；EquipmentModel 分支保留 prefab 根缩放。
                if (source.UnitScale) result.transform.localScale = Vector3.one;
                result.SetActive(true);
                foreach (Renderer renderer in renderers) character.characterModel.CustomFace.AddRendererToSubVisual(renderer);
                retained = true;
                return result;
            }
            finally
            {
                if (!retained && result != null) UnityEngine.Object.Destroy(result);
                UnityEngine.Object.Destroy(staging);
            }
        }

        internal void Release()
        {
            bound = false;
            foreach (Slot slot in slots) if (slot != null) slot.onSlotContentChanged -= OnSlotChanged;
            slots.Clear();
            foreach (KeyValuePair<Renderer, bool> pair in hidden) if (pair.Key != null) pair.Key.enabled = pair.Value;
            hidden.Clear();
            if (faceVisibilityCaptured)
            {
                if (hairObject != null) hairObject.SetActive(hairWasActive);
                if (mouthObject != null) mouthObject.SetActive(mouthWasActive);
            }
            hairObject = mouthObject = null;
            faceVisibilityCaptured = false;
            if (hat != null) { hat.SetActive(false); UnityEngine.Object.Destroy(hat); }
            if (armor != null) { armor.SetActive(false); UnityEngine.Object.Destroy(armor); }
            hat = armor = null;
            character = null;
            hatPrefab = armorPrefab = default(SkyIslandPatrolAppearance.VisualSource);
        }

        private void OnDestroy() { Release(); }
    }
}
