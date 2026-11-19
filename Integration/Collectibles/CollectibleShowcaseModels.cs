// ============================================================================
// CollectibleShowcaseModels.cs - 收集品的 3D 模型（官方陈列柜、地上掉落都显示这件东西本身）
// ============================================================================
// owner 2026-11-19：「要的」——让收集品能摆上官方陈列柜（Showcase01，12 个位全要「展示品」ShowCase 标签），
// 模型用 owner 的 Tripo 账号按稀有度生成（tools/collectible_showcase_mesh.py + collectible_showcase_pack.py）。
//
// 官方怎么摆：Showcase.RefreshSlot 调 ItemGraphicInfo.CreateAGraphic(物品, 展示位, snapToGround, useSpriteIfNoGraphic: false)，
// 物品没有 ItemGraphic 就什么都不摆；地上掉落（InteractablePickup）、作物、鱼缸也都经 CreateAGraphic。
// 所以只在 CreateAGraphic 前置补丁（CollectibleShowcasePatch）里按需给收集品挂模板，挂到实例和 prefab 上，后续实例直接带着。
//
// 模板（照官方 IG_Luxury_Trophy 的结构：根挂 ItemGraphicInfo，子物体 GroundPoint 在底面，网格在另一个子物体，图层 0）：
//   - 有 Assets/Collectibles/Models/<key>.brcm：网格 + DXT1 贴图，材质复制官方奖杯（TypeID 111）的 SodaLit 材质只换 _MainTex，
//     质感与原版摆件一致（_Color 0.59、无金属度）；
//   - 没有或读坏了：退回官方图标立牌（ItemGraphicInfo.spriteGraphicPfb 的复制品，换成这件的图标），陈列柜里至少看得到东西。
// 模板挂在一个停用的 DontDestroyOnLoad 根下：模板自身 activeSelf 为真，复制出来的实例换了父物体就显示，模板本身不显示。
// .brcm 是离线压好的原始数据，运行时只做内存拷贝（不解码图片、不留 CPU 副本）；第一次用到某件时才读，不用不占内存（§4.12）。
// 模块销毁时 ResetStaticCaches 销毁模板、网格、贴图与材质。
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using ItemStatsSystem;
using UnityEngine;

namespace BossRush
{
    internal static class CollectibleShowcaseModels
    {
        internal const string ModelFolder = "Assets/Collectibles/Models/";
        /// <summary>官方奖杯（Trophy）：它的展示模型用 SodaCraft/SodaLit、贴图在 _MainTex，复制它的材质当模板。</summary>
        internal const int OfficialMaterialTemplateTypeId = 111;
        private const uint BrcmMagic = 0x4D435242; // 'BRCM'
        private const int MaxVertices = 65535 * 2;

        private static readonly Dictionary<int, ItemGraphicInfo> templates = new Dictionary<int, ItemGraphicInfo>();
        private static readonly HashSet<int> unavailable = new HashSet<int>();
        private static readonly List<UnityEngine.Object> ownedObjects = new List<UnityEngine.Object>();
        private static GameObject templateRoot;
        private static Material cachedMaterialTemplate;
        private static readonly FieldInfo ItemGraphicField =
            typeof(Item).GetField("itemGraphic", BindingFlags.Instance | BindingFlags.NonPublic);

        /// <summary>配置收集品 prefab 时清掉克隆源带来的模型（否则地上掉落会长成便携安全区装置一类），交给按需挂载。</summary>
        internal static void ClearInheritedGraphic(Item item)
        {
            if (item == null || ItemGraphicField == null) return;
            ItemGraphicInfo current = item.ItemGraphic;
            if (current == null) return;
            ItemGraphicInfo ours;
            if (templates.TryGetValue(item.TypeID, out ours) && ours == current) return;
            try { ItemGraphicField.SetValue(item, null); }
            catch (Exception e) { ModBehaviour.DevLog(CollectibleTuning.LogPrefix + "[WARNING] 清除继承模型失败 " + item.TypeID + ": " + e.Message); }
        }

        /// <summary>CreateAGraphic 前置补丁调用：收集品还没有模型时按需建模板并挂上（实例与 prefab 都挂）。</summary>
        internal static void EnsureGraphic(Item item)
        {
            if (item == null || ItemGraphicField == null || CollectiblesRuntimeModule.Current == null) return;
            CollectibleDefinition def = CollectibleCatalog.Get(item.TypeID);
            if (def == null || item.ItemGraphic != null) return;
            ItemGraphicInfo template = GetTemplate(def, item);
            if (template == null) return;
            Assign(item, template);
            Item prefab = null;
            try { prefab = ItemAssetsCollection.GetPrefab(def.TypeId); }
            catch (Exception) { /* prefab 取不到就只挂实例 */ }
            if (prefab != null && prefab != item && prefab.ItemGraphic == null) Assign(prefab, template);
        }

        private static void Assign(Item item, ItemGraphicInfo template)
        {
            try { ItemGraphicField.SetValue(item, template); }
            catch (Exception e) { ModBehaviour.DevLog(CollectibleTuning.LogPrefix + "[WARNING] 挂模型失败 " + item.TypeID + ": " + e.Message); }
        }

        /// <summary>这件收集品有没有随包的模型文件（守卫与 F3 用；不读文件内容）。</summary>
        internal static bool HasModelFile(CollectibleDefinition def)
        {
            string path = ModelPath(def);
            return path != null && File.Exists(path);
        }

        private static string ModelPath(CollectibleDefinition def)
        {
            string modPath = ModBehaviour.GetModPath();
            return string.IsNullOrEmpty(modPath) || def == null ? null : Path.Combine(modPath, ModelFolder + def.Key + ".brcm");
        }

        private static ItemGraphicInfo GetTemplate(CollectibleDefinition def, Item item)
        {
            ItemGraphicInfo template;
            if (templates.TryGetValue(def.TypeId, out template))
            {
                if (template != null) return template;
                templates.Remove(def.TypeId);
            }
            if (unavailable.Contains(def.TypeId)) return null;
            template = BuildModelTemplate(def) ?? BuildStandeeTemplate(def, item);
            if (template == null) { unavailable.Add(def.TypeId); return null; }
            templates[def.TypeId] = template;
            return template;
        }

        private static Transform TemplateRoot()
        {
            if (templateRoot == null)
            {
                templateRoot = new GameObject("BossRush_CollectibleGraphicTemplates");
                templateRoot.SetActive(false);
                UnityEngine.Object.DontDestroyOnLoad(templateRoot);
                templateRoot.hideFlags = HideFlags.HideAndDontSave;
            }
            return templateRoot.transform;
        }

        // ---------------- 模型模板 ----------------

        private static ItemGraphicInfo BuildModelTemplate(CollectibleDefinition def)
        {
            string path = ModelPath(def);
            if (path == null || !File.Exists(path)) return null;
            if (!SystemInfo.SupportsTextureFormat(TextureFormat.DXT1)) return null;
            Mesh mesh = null;
            Texture2D texture = null;
            Material material = null;
            try
            {
                if (!Parse(File.ReadAllBytes(path), def.Key, out mesh, out texture)) throw new InvalidDataException("模型文件格式不对");
                material = CreateMaterial(texture, def.Key);
                if (material == null) throw new InvalidOperationException("拿不到官方摆件材质");

                GameObject root = new GameObject("IG_BossRush_Collectible_" + def.Key);
                root.transform.SetParent(TemplateRoot(), false);
                GameObject ground = new GameObject("GroundPoint");
                ground.transform.SetParent(root.transform, false);
                GameObject model = new GameObject("Model");
                model.transform.SetParent(root.transform, false);
                model.AddComponent<MeshFilter>().sharedMesh = mesh;
                MeshRenderer renderer = model.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                ItemGraphicInfo info = root.AddComponent<ItemGraphicInfo>();
                info.groundPoint = ground.transform;   // 模型原点就在底面中心，贴地不用再偏移
                ownedObjects.Add(mesh);
                ownedObjects.Add(texture);
                ownedObjects.Add(material);
                return info;
            }
            catch (Exception e)
            {
                if (mesh != null) UnityEngine.Object.Destroy(mesh);
                if (texture != null) UnityEngine.Object.Destroy(texture);
                if (material != null) UnityEngine.Object.Destroy(material);
                Debug.LogWarning(CollectibleTuning.LogPrefix + "收集品模型读取失败，改用图标立牌 " + def.Key + ": " + e.Message);
                return null;
            }
        }

        private static Material CreateMaterial(Texture2D texture, string key)
        {
            Material template = MaterialTemplate();
            if (template == null) return null;
            Material material = new Material(template);
            material.name = "BossRush_Collectible_" + key;
            material.mainTexture = texture;
            if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", texture);
            return material;
        }

        private static Material MaterialTemplate()
        {
            if (cachedMaterialTemplate != null) return cachedMaterialTemplate;
            try
            {
                Item trophy = ItemAssetsCollection.GetPrefab(OfficialMaterialTemplateTypeId);
                ItemGraphicInfo graphic = trophy != null ? trophy.ItemGraphic : null;
                Renderer renderer = graphic != null ? graphic.GetComponentInChildren<Renderer>(true) : null;
                cachedMaterialTemplate = renderer != null ? renderer.sharedMaterial : null;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(CollectibleTuning.LogPrefix + "[WARNING] 读官方奖杯材质失败: " + e.Message);
            }
            return cachedMaterialTemplate;
        }

        /// <summary>解析 .brcm（布局见 tools/collectible_showcase_pack.py）；任何一处不自洽都返回 false。</summary>
        internal static bool Parse(byte[] data, string name, out Mesh mesh, out Texture2D texture)
        {
            mesh = null;
            texture = null;
            if (data == null || data.Length < 64) return false;
            Vector3[] vertices;
            Vector3[] normals;
            Vector2[] uvs;
            int[] indices;
            int width, height, mipCount;
            byte[] textureData;
            using (MemoryStream stream = new MemoryStream(data, false))
            using (BinaryReader reader = new BinaryReader(stream))
            {
                if (reader.ReadUInt32() != BrcmMagic || reader.ReadInt32() != 1) return false;
                int vertexCount = reader.ReadInt32();
                int indexCount = reader.ReadInt32();
                int indexBits = reader.ReadInt32();
                if (vertexCount < 3 || vertexCount > MaxVertices || indexCount < 3 || indexCount % 3 != 0) return false;
                if (indexBits != 16 && indexBits != 32) return false;
                Vector3 min = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                Vector3 max = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                Vector3 span = max - min;
                long needed = (long)vertexCount * 14 + (long)indexCount * (indexBits / 8);
                if (stream.Length - stream.Position < needed) return false;

                vertices = new Vector3[vertexCount];
                for (int i = 0; i < vertexCount; i++)
                {
                    vertices[i] = new Vector3(
                        min.x + reader.ReadUInt16() / 65535f * span.x,
                        min.y + reader.ReadUInt16() / 65535f * span.y,
                        min.z + reader.ReadUInt16() / 65535f * span.z);
                }
                normals = new Vector3[vertexCount];
                for (int i = 0; i < vertexCount; i++)
                {
                    Vector3 n = new Vector3(reader.ReadSByte() / 127f, reader.ReadSByte() / 127f, reader.ReadSByte() / 127f);
                    reader.ReadSByte();
                    normals[i] = n.sqrMagnitude > 1e-6f ? n.normalized : Vector3.up;
                }
                uvs = new Vector2[vertexCount];
                for (int i = 0; i < vertexCount; i++) uvs[i] = new Vector2(reader.ReadUInt16() / 65535f, reader.ReadUInt16() / 65535f);
                indices = new int[indexCount];
                for (int i = 0; i < indexCount; i++)
                {
                    int index = indexBits == 32 ? reader.ReadInt32() : reader.ReadUInt16();
                    if (index < 0 || index >= vertexCount) return false;
                    indices[i] = index;
                }
                if (indexBits == 16 && indexCount % 2 == 1) reader.ReadUInt16();

                width = reader.ReadInt32();
                height = reader.ReadInt32();
                mipCount = reader.ReadInt32();
                int format = reader.ReadInt32();
                int textureBytes = reader.ReadInt32();
                if (format != 1 || width < 4 || width > 2048 || height != width || mipCount < 1 || mipCount > 12) return false;
                if (textureBytes != Dxt1Bytes(width, height, mipCount) || stream.Length - stream.Position != textureBytes) return false;
                textureData = reader.ReadBytes(textureBytes);
            }

            mesh = new Mesh();
            mesh.name = "BossRush_Collectible_" + name;
            if (vertices.Length > 65535) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.uv = uvs;
            mesh.triangles = indices;
            mesh.RecalculateBounds();
            mesh.UploadMeshData(true);

            texture = new Texture2D(width, height, TextureFormat.DXT1, mipCount, false);
            texture.name = "BossRush_Collectible_" + name;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Trilinear;
            texture.anisoLevel = 2;
            texture.LoadRawTextureData(textureData);
            texture.Apply(false, true);
            return true;
        }

        private static int Dxt1Bytes(int width, int height, int mipCount)
        {
            int total = 0;
            for (int i = 0; i < mipCount; i++)
            {
                int w = Math.Max(1, width >> i), h = Math.Max(1, height >> i);
                total += Math.Max(1, (w + 3) / 4) * Math.Max(1, (h + 3) / 4) * 8;
            }
            return total;
        }

        // ---------------- 图标立牌（兜底） ----------------

        private static ItemGraphicInfo BuildStandeeTemplate(CollectibleDefinition def, Item item)
        {
            try
            {
                ItemGraphicInfo source = ItemGraphicInfo.spriteGraphicPfb;
                if (source == null) return null;
                ItemGraphicInfo standee = UnityEngine.Object.Instantiate(source, TemplateRoot(), false);
                standee.name = "IG_BossRush_CollectibleStandee_" + def.Key;
                Sprite icon = item != null ? item.Icon : null;
                if (standee.fallbackSprite != null && icon != null) standee.fallbackSprite.sprite = icon;
                return standee;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(CollectibleTuning.LogPrefix + "[WARNING] 图标立牌失败 " + def.Key + ": " + e.Message);
                return null;
            }
        }

        // ---------------- 清理 ----------------

        internal static void ResetStaticCaches()
        {
            templates.Clear();
            unavailable.Clear();
            for (int i = 0; i < ownedObjects.Count; i++)
            {
                try { if (ownedObjects[i] != null) UnityEngine.Object.Destroy(ownedObjects[i]); }
                catch (Exception) { /* 销毁失败不得拖崩宿主清理链 */ }
            }
            ownedObjects.Clear();
            if (templateRoot != null)
            {
                try { UnityEngine.Object.Destroy(templateRoot); }
                catch (Exception) { /* 同上 */ }
            }
            templateRoot = null;
            cachedMaterialTemplate = null;
        }
    }
}
