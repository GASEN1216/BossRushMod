using System;
using System.Collections.Generic;
using UnityEngine;

namespace BossRush
{
    /// <summary>官方角色和准心各有一个 VisualOcclusion 检查器；这里按主角脚下的实际房间调用
    /// 官方 ShadowOnly 状态，只绑定作者单列的 Roof 子树（房顶、上层外壳与低墙以上结构）。
    /// 地板、低墙、完整室内家具和邻屋均不参与。
    /// 按 16 米网格索引，站定时不用重复找屋。</summary>
    internal sealed class RaidIndoorRoofs : IDisposable
    {
        private sealed class Room
        {
            internal Bounds Footprint;
            internal OcclusionFadeObject Fade;
        }
        private readonly Dictionary<Vector2Int,List<Room>> cells = new Dictionary<Vector2Int,List<Room>>();
        private readonly List<Room> rooms = new List<Room>();
        private readonly MaterialPropertyBlock transition = new MaterialPropertyBlock();
        private static readonly int BackAt = Shader.PropertyToID("_TidegateRoofBackAt");
        private readonly bool restoreTransition;
        private Room inside;
        private Vector3 lastFeet;
        private bool sampled;
        private static int Cell(float coordinate) { return Mathf.FloorToInt(coordinate / 16f); }

        internal RaidIndoorRoofs(GameObject world, bool pavilions)
        {
            // 天空岛亭顶保留既有过渡；潮闸镇出门立即恢复实体屋顶，不在室外虚化。
            restoreTransition = pavilions;
            Transform group = pavilions ? world.transform.Find("SkyPavilionRooms") : null;
            if (!pavilions)
                foreach (GameObject g in world.scene.GetRootGameObjects())
                    if (g.name == "TidegateIndoorRooms" || g.name == "LastShiftIndoorRooms") { group = g.transform; break; }
            if (group == null)
            {
                Debug.LogWarning("[BossRush] " + world.name + " 缺少室内触发区；请更新地图包。");
                return;
            }
            var roofs = new Dictionary<string, Transform>(StringComparer.Ordinal);
            Transform buildings = pavilions ? group : world.transform.Find("Visual/VIS_buildings");
            if (buildings == null) return;
            foreach (Transform node in buildings.GetComponentsInChildren<Transform>(true))
                if (node.name.StartsWith("BLD_", StringComparison.Ordinal) && node.name.EndsWith("_Roof", StringComparison.Ordinal))
                    roofs[node.name] = node;
            foreach (Transform volume in group)
            {
                Transform roof;
                BoxCollider box = volume.GetComponent<BoxCollider>();
                if (box == null || !roofs.TryGetValue("BLD_" + volume.name + "_Roof", out roof)) continue;
                Renderer[] roofRenderers = roof.GetComponentsInChildren<Renderer>(true);
                // 无作者隐藏组的建筑不自行扩展到整栋；金库高墙也由作者明确拆分。
                if (roofRenderers.Length == 0) continue;
                var fade = volume.gameObject.AddComponent<OcclusionFadeObject>();
                fade.triggers = new OcclusionFadeTrigger[0];
                fade.fadeType = OcclusionFadeTypes.ShadowOnly;
                fade.renderers = roofRenderers;
                var room = new Room { Footprint = box.bounds, Fade = fade };
                rooms.Add(room);
                for (int x = Cell(room.Footprint.min.x); x <= Cell(room.Footprint.max.x); x++)
                    for (int z = Cell(room.Footprint.min.z); z <= Cell(room.Footprint.max.z); z++)
                    {
                        var key = new Vector2Int(x,z);
                        List<Room> list;
                        if (!cells.TryGetValue(key, out list)) cells[key] = list = new List<Room>();
                        list.Add(room);
                    }
            }
        }

        internal bool UpdateFeet(Vector3 feet)
        {
            if (sampled && feet == lastFeet) return inside != null;
            sampled = true;
            lastFeet = feet;
            if (inside != null && inside.Footprint.Contains(feet)) return true;
            if (inside != null)
            {
                if (restoreTransition) foreach (Renderer r in inside.Fade.renderers)
                {
                    if (r == null) continue;
                    r.GetPropertyBlock(transition);
                    transition.SetFloat(BackAt, Time.time);
                    r.SetPropertyBlock(transition);
                }
                inside.Fade.OnLeave();
                inside = null;
            }
            List<Room> candidates;
            if (!cells.TryGetValue(new Vector2Int(Cell(feet.x), Cell(feet.z)), out candidates)) return false;
            foreach (Room room in candidates)
                if (room.Footprint.Contains(feet)) { inside = room; room.Fade.OnEnter(); return true; }
            return false;
        }

        public void Dispose()
        {
            if (inside != null && inside.Fade != null) inside.Fade.OnLeave();
            inside = null;
            foreach (Room room in rooms)
                if (room.Fade != null) UnityEngine.Object.Destroy(room.Fade);
            rooms.Clear();
            cells.Clear();
        }
    }
}
