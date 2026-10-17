using System;
using System.Collections.Generic;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// 天空岛上本来只有触发盒（外加一盏光、一块贴地光斑或一行走近才浮现的字）的交互体，补一件看得见的实体物件
    /// （owner 2026-10-17「地图上只要有交互的地方，都要有看得见的实体物件，不能是空交互」）：
    /// 信鸽、航路图架、五种采集物（青穗草丛 / 搁浅的浮木 / 云苔 / 残铜矿脉 / 风晶簇）。见闻点 `Search_*` 的木牌在
    /// <see cref="SkyIslandGates"/>（与桥口木牌同一套构件和换语言路径），这里不重复。
    ///
    /// 写法同纪念物木桩（<see cref="SkyIslandGates.AddMemorialPost"/>）：程序化小件 + 场景已加载的作者材质（按名字取，
    /// 不打进任何包，避开 URP 剥变体），**没有碰撞体**、不在交互层，挂在交互体自己下面，随它一起建、飞走、淡出、销毁；
    /// 摆在交互体正中，所以「看到的那件东西」就是「按 F 的那一处」。占地半径登记在
    /// `tools/sky_island_runtime_placements.py`（kind `interact_prop`），压路 / 穿模 / 互相重叠由场景净空审计与
    /// `tests/SkyIslandPointPropsPropertyTest.py` 复核；落在路面上的那几处交互体已在各自的落点数据里挪到路边。
    /// 材质缺失时缺哪块就不建哪块（纯外观，fail-open），并记一条警告。
    /// </summary>
    internal sealed class SkyIslandPropPalette
    {
        private readonly Dictionary<string, Material> materials = new Dictionary<string, Material>(StringComparer.Ordinal);
        private bool warned;

        /// <summary>扫一遍岛上渲染器，按材质名登记（每个 owner 每趟一次；岛上合批后约 4700 个物体、百余种材质）。</summary>
        internal SkyIslandPropPalette(Transform worldRoot)
        {
            if (worldRoot == null) return;
            foreach (MeshRenderer renderer in worldRoot.GetComponentsInChildren<MeshRenderer>(true))
            {
                Material material = renderer.sharedMaterial;
                if (material != null && !materials.ContainsKey(material.name)) materials.Add(material.name, material);
            }
        }

        internal Material Get(string name)
        {
            Material material;
            if (materials.TryGetValue(name, out material)) return material;
            if (!warned)
            {
                warned = true;
                Debug.LogWarning("[SkyIslandProps] 场景里没有作者材质 " + name + "，这件交互物件少画一块");
            }
            return null;
        }
    }

    internal static class SkyIslandPointProps
    {
        /// <summary>挂在交互体下面的物件根节点名（F3 与守卫按它找）。</summary>
        internal const string RootName = "PointProp";

        /// <summary>木纹 / 石纹大约每多少米重复一次（口径同桥口木牌，UE-24）。</summary>
        private const float TileMeters = 1.2f;

        /// <summary>
        /// 一块构件：基本体 + 作者材质，摘掉碰撞体；按尺寸写 <c>_BaseMap_ST</c> 平铺（只写这一个渲染器的属性块）。
        /// 材质为 null 时不建（fail-open）。
        /// </summary>
        private static void Piece(Transform parent, PrimitiveType shape, Material material, Vector3 position, Vector3 scale, Vector3 euler)
        {
            if (material == null) return;
            GameObject piece = GameObject.CreatePrimitive(shape);
            piece.name = "PropPiece";
            piece.layer = 0;
            piece.transform.SetParent(parent, false);
            piece.transform.localPosition = position;
            piece.transform.localRotation = Quaternion.Euler(euler);
            piece.transform.localScale = scale;
            Collider collider = piece.GetComponent<Collider>();
            if (collider != null)
            {
                collider.enabled = false;
                UnityEngine.Object.Destroy(collider);
            }
            MeshRenderer renderer = piece.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            if (material.HasProperty("_BaseMap_ST"))
            {
                var block = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(block);
                float offset = Mathf.Repeat(position.x * 0.37f + position.z * 0.53f + position.y * 0.61f, 1f);
                block.SetVector("_BaseMap_ST", new Vector4(Mathf.Max(0.25f, Mathf.Max(scale.x, scale.z) / TileMeters),
                    Mathf.Max(0.25f, scale.y / TileMeters), offset, 0f));
                renderer.SetPropertyBlock(block);
            }
        }

        private static Transform NewRoot(GameObject host, string key)
        {
            Transform existing = host.transform.Find(RootName);
            if (existing != null) UnityEngine.Object.Destroy(existing.gameObject);
            Transform root = new GameObject(RootName).transform;
            root.SetParent(host.transform, false);
            // 朝向按 id 散列：同一处每趟一样，相邻两处不一样。
            root.localRotation = Quaternion.Euler(0f, SkyIslandLootTables.StableHash(key) % 360, 0f);
            return root;
        }

        /// <summary>信鸽：一只蹲在地上的白鸽，背上绑着一卷信。随交互体一起飞走（<see cref="SkyIslandStoryInteractable.FlyAway"/>）。</summary>
        internal static void Pigeon(GameObject pigeon, string letterId, SkyIslandPropPalette palette)
        {
            if (pigeon == null || palette == null) return;
            Transform root = NewRoot(pigeon, letterId);
            Material white = palette.Get("Sky_LilyWhite") ?? palette.Get("Sky_Ivory");
            Material grey = palette.Get("Sky_Pebble");
            Material beak = palette.Get("Sky_Coral");
            Material paper = palette.Get("Sky_Chalk");
            Material twine = palette.Get("Sky_Copper");
            Piece(root, PrimitiveType.Sphere, white, new Vector3(0f, 0.26f, 0f), new Vector3(0.34f, 0.3f, 0.5f), Vector3.zero);
            Piece(root, PrimitiveType.Sphere, white, new Vector3(0f, 0.5f, 0.2f), new Vector3(0.2f, 0.2f, 0.2f), Vector3.zero);
            Piece(root, PrimitiveType.Cube, beak, new Vector3(0f, 0.49f, 0.32f), new Vector3(0.05f, 0.05f, 0.1f), new Vector3(12f, 0f, 0f));
            Piece(root, PrimitiveType.Sphere, grey, new Vector3(-0.15f, 0.3f, -0.03f), new Vector3(0.08f, 0.2f, 0.4f), new Vector3(0f, 0f, 12f));
            Piece(root, PrimitiveType.Sphere, grey, new Vector3(0.15f, 0.3f, -0.03f), new Vector3(0.08f, 0.2f, 0.4f), new Vector3(0f, 0f, -12f));
            Piece(root, PrimitiveType.Cube, grey, new Vector3(0f, 0.3f, -0.3f), new Vector3(0.2f, 0.04f, 0.2f), new Vector3(-18f, 0f, 0f));
            Piece(root, PrimitiveType.Cylinder, paper, new Vector3(0f, 0.43f, -0.05f), new Vector3(0.07f, 0.09f, 0.07f), new Vector3(0f, 0f, 90f));
            Piece(root, PrimitiveType.Cube, twine, new Vector3(0f, 0.43f, -0.05f), new Vector3(0.2f, 0.075f, 0.02f), Vector3.zero);
            Piece(root, PrimitiveType.Cube, beak, new Vector3(-0.07f, 0.05f, 0.02f), new Vector3(0.03f, 0.1f, 0.03f), Vector3.zero);
            Piece(root, PrimitiveType.Cube, beak, new Vector3(0.07f, 0.05f, 0.02f), new Vector3(0.03f, 0.1f, 0.03f), Vector3.zero);
        }

        /// <summary>航路图：木架上钉着一张画了航线的图纸，图面朝固定俯视相机（口径同纪念物木牌）。</summary>
        internal static void ChartStand(GameObject guide, string marker, SkyIslandPropPalette palette)
        {
            if (guide == null || palette == null) return;
            Transform root = NewRoot(guide, marker);
            Material wood = palette.Get("Sky_WoodDark");
            Material frame = palette.Get("Sky_Wood");
            Material paper = palette.Get("Sky_Chalk");
            Material ink = palette.Get("Sky_PaintTealDeep") ?? palette.Get("Sky_Teal");
            Material pin = palette.Get("Sky_Coral");
            Piece(root, PrimitiveType.Cube, wood, new Vector3(-0.4f, 0.65f, 0.1f), new Vector3(0.07f, 1.35f, 0.07f), new Vector3(-8f, 0f, 6f));
            Piece(root, PrimitiveType.Cube, wood, new Vector3(0.4f, 0.65f, 0.1f), new Vector3(0.07f, 1.35f, 0.07f), new Vector3(-8f, 0f, -6f));
            Piece(root, PrimitiveType.Cube, wood, new Vector3(0f, 0.6f, -0.28f), new Vector3(0.07f, 1.25f, 0.07f), new Vector3(18f, 0f, 0f));
            Transform board = new GameObject("ChartBoard").transform;
            board.SetParent(root, false);
            board.localPosition = new Vector3(0f, 1.15f, 0.05f);
            Camera camera = GameCamera.Instance == null ? null : GameCamera.Instance.renderCamera;
            if (camera != null) board.rotation = Quaternion.Euler(0f, camera.transform.eulerAngles.y, 0f) * Quaternion.Euler(-35f, 0f, 0f);
            Piece(board, PrimitiveType.Cube, frame, Vector3.zero, new Vector3(1.05f, 0.75f, 0.05f), Vector3.zero);
            Piece(board, PrimitiveType.Cube, paper, new Vector3(0f, 0f, -0.03f), new Vector3(0.92f, 0.62f, 0.01f), Vector3.zero);
            Piece(board, PrimitiveType.Cube, ink, new Vector3(-0.12f, 0.06f, -0.04f), new Vector3(0.5f, 0.025f, 0.01f), new Vector3(0f, 0f, 24f));
            Piece(board, PrimitiveType.Cube, ink, new Vector3(0.18f, -0.08f, -0.04f), new Vector3(0.42f, 0.025f, 0.01f), new Vector3(0f, 0f, -18f));
            Piece(board, PrimitiveType.Cube, ink, new Vector3(0.02f, 0.16f, -0.04f), new Vector3(0.3f, 0.025f, 0.01f), new Vector3(0f, 0f, -6f));
            Piece(board, PrimitiveType.Sphere, pin, new Vector3(0.36f, 0.04f, -0.05f), new Vector3(0.06f, 0.06f, 0.03f), Vector3.zero);
            Piece(board, PrimitiveType.Sphere, pin, new Vector3(-0.36f, -0.18f, -0.05f), new Vector3(0.06f, 0.06f, 0.03f), Vector3.zero);
        }

        /// <summary>采集点：按种类摆一小件实物。采走那一刻由调用方连同交互体一起摘掉（<see cref="Remove"/>）。</summary>
        internal static void Gather(GameObject node, string nodeId, SkyIslandGatherKind kind, SkyIslandPropPalette palette)
        {
            if (node == null || palette == null) return;
            Transform root = NewRoot(node, nodeId);
            switch (kind)
            {
                case SkyIslandGatherKind.Grass: GrassTuft(root, palette); break;
                case SkyIslandGatherKind.Driftwood: Driftwood(root, palette); break;
                case SkyIslandGatherKind.Moss: MossMound(root, palette); break;
                case SkyIslandGatherKind.Ore: OreVein(root, palette); break;
                case SkyIslandGatherKind.Crystal: CrystalCluster(root, palette); break;
            }
        }

        /// <summary>采走了：物件当帧拿掉（光斑与光照旧淡出）。</summary>
        internal static void Remove(GameObject host)
        {
            if (host == null) return;
            Transform prop = host.transform.Find(RootName);
            if (prop != null) UnityEngine.Object.Destroy(prop.gameObject);
        }

        /// <summary>青穗草丛：一丛斜着的草叶，顶上挂着几粒青穗。</summary>
        private static void GrassTuft(Transform root, SkyIslandPropPalette palette)
        {
            Material blade = palette.Get("Sky_Grass");
            Material light = palette.Get("Sky_GrassLight") ?? blade;
            Material ear = palette.Get("Sky_LeafGold");
            for (int i = 0; i < 9; i++)
            {
                float angle = i * 40f;
                float radians = angle * Mathf.Deg2Rad;
                float reach = 0.08f + (i % 3) * 0.08f;
                float height = 0.55f + (i % 4) * 0.12f;
                Vector3 foot = new Vector3(Mathf.Cos(radians) * reach, height * 0.5f, Mathf.Sin(radians) * reach);
                Piece(root, PrimitiveType.Cube, i % 2 == 0 ? blade : light, foot, new Vector3(0.05f, height, 0.025f),
                    new Vector3(Mathf.Sin(radians) * 16f, angle, -Mathf.Cos(radians) * 16f));
                if (i % 3 == 0)
                    Piece(root, PrimitiveType.Sphere, ear, new Vector3(foot.x * 1.6f, height * 0.98f, foot.z * 1.6f),
                        new Vector3(0.07f, 0.14f, 0.07f), new Vector3(Mathf.Sin(radians) * 16f, 0f, -Mathf.Cos(radians) * 16f));
            }
        }

        /// <summary>搁浅的浮木：一截被云风刷白的原木横躺着，带一根断枝。</summary>
        private static void Driftwood(Transform root, SkyIslandPropPalette palette)
        {
            Material bleached = palette.Get("Sky_WoodLight") ?? palette.Get("Sky_Wood");
            Material dark = palette.Get("Sky_WoodDark") ?? bleached;
            Piece(root, PrimitiveType.Cylinder, bleached, new Vector3(0f, 0.14f, 0f), new Vector3(0.26f, 0.6f, 0.26f), new Vector3(0f, 0f, 90f));
            Piece(root, PrimitiveType.Cylinder, dark, new Vector3(-0.62f, 0.14f, 0f), new Vector3(0.2f, 0.03f, 0.2f), new Vector3(0f, 0f, 90f));
            Piece(root, PrimitiveType.Cylinder, bleached, new Vector3(0.18f, 0.3f, 0.12f), new Vector3(0.08f, 0.22f, 0.08f), new Vector3(-30f, 25f, -40f));
            Piece(root, PrimitiveType.Cylinder, dark, new Vector3(0.36f, 0.12f, -0.18f), new Vector3(0.14f, 0.18f, 0.14f), new Vector3(0f, 60f, 90f));
        }

        /// <summary>云苔：几团贴地的苔垫。</summary>
        private static void MossMound(Transform root, SkyIslandPropPalette palette)
        {
            Material moss = palette.Get("Sky_Fern") ?? palette.Get("Sky_Leaf");
            Material light = palette.Get("Sky_LeafLight") ?? moss;
            Material stone = palette.Get("Sky_RockLight");
            Piece(root, PrimitiveType.Sphere, stone, new Vector3(0.05f, 0.08f, 0f), new Vector3(0.5f, 0.2f, 0.42f), Vector3.zero);
            Piece(root, PrimitiveType.Sphere, moss, new Vector3(0f, 0.16f, 0f), new Vector3(0.6f, 0.2f, 0.5f), Vector3.zero);
            Piece(root, PrimitiveType.Sphere, light, new Vector3(0.28f, 0.1f, 0.18f), new Vector3(0.34f, 0.14f, 0.3f), Vector3.zero);
            Piece(root, PrimitiveType.Sphere, moss, new Vector3(-0.26f, 0.08f, -0.16f), new Vector3(0.3f, 0.12f, 0.28f), Vector3.zero);
        }

        /// <summary>残铜矿脉：两块岩石上嵌着几片发亮的铜。</summary>
        private static void OreVein(Transform root, SkyIslandPropPalette palette)
        {
            Material rock = palette.Get("Sky_Rock");
            Material deep = palette.Get("Sky_RockDeep") ?? rock;
            Material copper = palette.Get("Sky_Copper") ?? palette.Get("Sky_Brass");
            Piece(root, PrimitiveType.Cube, rock, new Vector3(0f, 0.22f, 0f), new Vector3(0.6f, 0.45f, 0.5f), new Vector3(8f, 25f, -6f));
            Piece(root, PrimitiveType.Cube, deep, new Vector3(0.36f, 0.14f, 0.18f), new Vector3(0.36f, 0.28f, 0.32f), new Vector3(-10f, 60f, 12f));
            Piece(root, PrimitiveType.Cube, copper, new Vector3(-0.08f, 0.4f, 0.12f), new Vector3(0.16f, 0.08f, 0.12f), new Vector3(20f, 35f, 10f));
            Piece(root, PrimitiveType.Cube, copper, new Vector3(0.16f, 0.32f, -0.2f), new Vector3(0.12f, 0.07f, 0.1f), new Vector3(-15f, 70f, 25f));
            Piece(root, PrimitiveType.Cube, copper, new Vector3(0.42f, 0.27f, 0.1f), new Vector3(0.1f, 0.06f, 0.09f), new Vector3(10f, 10f, -20f));
        }

        /// <summary>风晶簇：一块底石上斜插着几根青色晶柱。</summary>
        private static void CrystalCluster(Transform root, SkyIslandPropPalette palette)
        {
            Material crystal = palette.Get("Sky_CrystalTeal") ?? palette.Get("Sky_TripoGlowCrystal");
            Material rock = palette.Get("Sky_RockDeep") ?? palette.Get("Sky_Rock");
            Piece(root, PrimitiveType.Sphere, rock, new Vector3(0f, 0.06f, 0f), new Vector3(0.6f, 0.18f, 0.5f), Vector3.zero);
            Piece(root, PrimitiveType.Cube, crystal, new Vector3(0f, 0.42f, 0f), new Vector3(0.14f, 0.8f, 0.14f), new Vector3(4f, 45f, 6f));
            Piece(root, PrimitiveType.Cube, crystal, new Vector3(0.15f, 0.3f, 0.06f), new Vector3(0.11f, 0.55f, 0.11f), new Vector3(0f, 20f, -24f));
            Piece(root, PrimitiveType.Cube, crystal, new Vector3(-0.14f, 0.26f, 0.08f), new Vector3(0.1f, 0.5f, 0.1f), new Vector3(16f, 70f, 22f));
            Piece(root, PrimitiveType.Cube, crystal, new Vector3(0.02f, 0.2f, -0.17f), new Vector3(0.09f, 0.38f, 0.09f), new Vector3(-26f, 10f, 0f));
        }
    }
}
