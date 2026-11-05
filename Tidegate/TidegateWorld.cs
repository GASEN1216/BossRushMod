using System;
using System.Collections.Generic;
using Duckov.Utilities;
using UnityEngine;
using UnityEngine.Rendering;

namespace BossRush
{
    /// <summary>
    /// 潮闸镇世界根的索引与装配：标记点、碰撞分层、着色器校验、地面高度与泻湖遮罩查询。
    /// 层级由作者工程 `TidegateBuilder.BuildWorldPrefab` 生成：`Visual/VIS_*`、`Collision/COL_*`、`Markers/*`、`Navigation`。
    /// </summary>
    internal sealed class TidegateWorld : IDisposable
    {
        private static readonly string[] WorldShaders = { "BossRush/Tidegate/Environment", "BossRush/Tidegate/Terrain", "BossRush/Tidegate/Water", "BossRush/Tidegate/Beam" };
        internal readonly GameObject Root;
        internal readonly Transform PlayerSpawn;
        internal readonly Dictionary<string, Transform> Exits = new Dictionary<string, Transform>(StringComparer.Ordinal);
        internal readonly List<Transform> LootMarkers = new List<Transform>();
        internal readonly List<Transform> SpawnMarkers = new List<Transform>();
        internal readonly List<Transform> Valves = new List<Transform>();
        internal readonly List<Transform> Pois = new List<Transform>();
        internal readonly Dictionary<string, List<Vector3>> Waypoints = new Dictionary<string, List<Vector3>>(StringComparer.Ordinal);
        internal readonly Transform Lighthouse;
        internal readonly Renderer Water;
        private Texture2D heightTexture;
        private Vector4 heightRect;
        private bool heightReadable;
        private int groundMask;

        internal TidegateWorld(GameObject root)
        {
            Root = root;
            Transform markers = root.transform.Find("Markers");
            if (markers == null) throw new InvalidOperationException("潮闸镇缺少 Markers 节点");
            PlayerSpawn = markers.Find("PlayerSpawn");
            if (PlayerSpawn == null) throw new InvalidOperationException("潮闸镇缺少出生点");
            Collect(markers.Find("Exits"), delegate (Transform t)
            {
                string id = t.name.StartsWith("Exit_", StringComparison.Ordinal) ? t.name.Substring(5) : t.name;
                Exits[id] = t;
            });
            Collect(markers.Find("Loot"), LootMarkers.Add);
            Collect(markers.Find("Spawns"), SpawnMarkers.Add);
            Collect(markers.Find("POI"), Pois.Add);
            Transform interact = markers.Find("Interact");
            if (interact != null)
            {
                foreach (Transform child in interact)
                {
                    if (child.name.StartsWith("GP_valve_", StringComparison.Ordinal)) Valves.Add(child);
                    else if (child.name == "Lighthouse") Lighthouse = child;
                }
                Valves.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            }
            Collect(markers.Find("Waypoints"), delegate (Transform t)
            {
                // WP_<组>_<序号>
                string name = t.name;
                int last = name.LastIndexOf('_');
                if (!name.StartsWith("WP_", StringComparison.Ordinal) || last <= 3) return;
                string group = name.Substring(3, last - 3);
                List<Vector3> list;
                if (!Waypoints.TryGetValue(group, out list)) Waypoints[group] = list = new List<Vector3>();
                list.Add(t.position);
            });
            if (!Exits.ContainsKey("road")) throw new InvalidOperationException("潮闸镇缺少常驻撤离点（公路检查站）");
            if (LootMarkers.Count == 0 || SpawnMarkers.Count == 0) throw new InvalidOperationException("潮闸镇搜刮点或刷怪点缺失");
            SpawnMarkers.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            LootMarkers.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            Transform water = root.transform.Find("Visual/VIS_Water");
            Water = water == null ? null : water.GetComponent<Renderer>();
            BindHeightTexture();
        }

        private static void Collect(Transform group, Action<Transform> add)
        {
            if (group == null) return;
            foreach (Transform child in group) add(child);
        }

        /// <summary>
        /// 渲染器与碰撞体分层（口径同 `SkyIslandRendering.Apply`）：可见网格进地面层，`COL_Ground*` 地面层，
        /// `COL_Wall*` / `COL_Prop*` 墙层。三套自研着色器必须被当前显卡支持且带 `UniversalGBuffer`，否则直接失败——
        /// owner 定过不做着色器降级（资源不对就硬失败，部署前判包）。
        /// </summary>
        internal void ApplyLayers()
        {
            groundMask = GameplayDataSettings.Layers.groundLayerMask.value;
            int groundLayer = FirstLayer(groundMask);
            int wallLayer = FirstLayer(GameplayDataSettings.Layers.wallLayerMask.value);
            if (groundLayer < 0 || wallLayer < 0) throw new InvalidOperationException("游戏碰撞层未就绪");
            var checkedShaders = new HashSet<Shader>();
            foreach (Renderer renderer in Root.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer is ParticleSystemRenderer || renderer is LineRenderer) continue;
                renderer.gameObject.layer = groundLayer;
                foreach (Material material in renderer.sharedMaterials)
                {
                    if (material == null) throw new InvalidOperationException("模型缺少材质：" + renderer.name);
                    Shader shader = material.shader;
                    if (shader == null || !checkedShaders.Add(shader)) continue;
                    if (Array.IndexOf(WorldShaders, shader.name) < 0)
                        throw new InvalidOperationException("潮闸镇材质使用了未登记的着色器 " + shader.name + "：" + material.name);
                    if (!shader.isSupported)
                        throw new InvalidOperationException("当前显卡不支持潮闸镇着色器 " + shader.name + "，请更新 Mod 资源包");
                }
            }
            int ground = 0, walls = 0;
            foreach (Collider collider in Root.GetComponentsInChildren<Collider>(true))
            {
                string name = collider.name;
                if (name.StartsWith("COL_Ground", StringComparison.Ordinal)) { collider.gameObject.layer = groundLayer; ground++; }
                else if (name.StartsWith("COL_Wall", StringComparison.Ordinal) || name.StartsWith("COL_Prop", StringComparison.Ordinal))
                { collider.gameObject.layer = wallLayer; walls++; }
            }
            if (ground == 0 || walls == 0) throw new InvalidOperationException("潮闸镇地面或墙体碰撞体缺失");
            Debug.Log(TidegateRaidScene.LogTag + " LAYERS_READY ground=" + ground + " walls=" + walls + " shaders=" + checkedShaders.Count);
        }

        internal void VerifyGround(int mask)
        {
            groundMask = mask;
            VerifyGround(PlayerSpawn);
            foreach (Transform exit in Exits.Values) VerifyGround(exit);
        }

        private void VerifyGround(Transform marker)
        {
            float y;
            if (!TryGroundAt(marker.position, out y)) throw new InvalidOperationException("点位脚下未命中自建地面：" + marker.name);
        }

        /// <summary>脚下地面高度：从上方 3 m 向下打地面层，命中必须属于本图。</summary>
        internal bool TryGroundAt(Vector3 position, out float y)
        {
            y = position.y;
            RaycastHit hit;
            if (Physics.Raycast(position + Vector3.up * 3f, Vector3.down, out hit, 12f, groundMask, QueryTriggerInteraction.Ignore)
                && hit.transform.IsChildOf(Root.transform))
            {
                y = hit.point.y;
                return true;
            }
            return false;
        }

        /// <summary>
        /// 水面着色器用的高度图（R 地面高、G 泻湖遮罩），由构建器写成可读的 RGHalf 资产。读得到就用它判「这一点归泻湖还是外海」；
        /// 读不到时按外海算（泻湖里的点会被当成外海水位，只影响泻湖放水前那几分钟的判定）。
        /// </summary>
        private void BindHeightTexture()
        {
            if (Water == null || Water.sharedMaterial == null) return;
            Material material = Water.sharedMaterial;
            heightTexture = material.HasProperty("_HeightTex") ? material.GetTexture("_HeightTex") as Texture2D : null;
            heightRect = material.HasProperty("_HeightRect") ? material.GetVector("_HeightRect") : Vector4.zero;
            heightReadable = heightTexture != null && heightTexture.isReadable && heightRect.z > 0f && heightRect.w > 0f;
            if (!heightReadable) Debug.LogWarning(TidegateRaidScene.LogTag + " 水面高度图不可读，泻湖判定按外海处理");
        }

        /// <summary>这一点是否属于泻湖（大闸内侧）。</summary>
        internal bool InLagoon(Vector3 position)
        {
            if (!heightReadable) return false;
            float u = (position.x - heightRect.x) / heightRect.z, v = (position.z - heightRect.y) / heightRect.w;
            if (u < 0f || v < 0f || u > 1f || v > 1f) return false;
            return heightTexture.GetPixelBilinear(u, v).g > 0.5f;
        }

        /// <summary>泻湖在世界 XZ 平面上的包围矩形（高度图 G 通道 &gt; 0.5 的像素）。高度图读不到或没有泻湖时返回 false。</summary>
        internal bool TryGetLagoonBounds(out Rect bounds)
        {
            bounds = default(Rect);
            if (!heightReadable) return false;
            int width = heightTexture.width, height = heightTexture.height;
            Color[] pixels = heightTexture.GetPixels();
            int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    if (pixels[y * width + x].g <= 0.5f) continue;
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            if (maxX < 0) return false;
            float cellX = heightRect.z / width, cellZ = heightRect.w / height;
            bounds = Rect.MinMaxRect(heightRect.x + minX * cellX, heightRect.y + minY * cellZ,
                heightRect.x + (maxX + 1) * cellX, heightRect.y + (maxY + 1) * cellZ);
            return true;
        }

        /// <summary>HUD 用的地名：离玩家最近的地标。</summary>
        internal string RegionNear(Vector3 position)
        {
            Transform best = null;
            float distance = float.MaxValue;
            foreach (Transform poi in Pois)
            {
                float d = (poi.position - position).sqrMagnitude;
                if (d < distance) { distance = d; best = poi; }
            }
            return best == null ? TidegateRaidScene.DisplayName : TidegateText.PlaceName(best.name);
        }

        private static int FirstLayer(int mask)
        {
            for (int i = 0; i < 32; i++) if ((mask & (1 << i)) != 0) return i;
            return -1;
        }

        public void Dispose() { heightTexture = null; }
    }
}
