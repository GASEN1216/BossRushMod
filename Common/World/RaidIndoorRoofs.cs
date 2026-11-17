using System;
using System.Collections.Generic;
using UnityEngine;

namespace BossRush
{
    /// <summary>官方角色和准心各有一个 VisualOcclusion 检查器；这里按主角脚下的实际房间调用
    /// 官方 ShadowOnly 状态，只绑定作者单列的 Roof 子树（房顶、上层外壳与低墙以上结构）。
    /// 地板、低墙、完整室内家具和邻屋均不参与。
    /// 按 16 米网格索引，站定时不用重复找屋。
    /// 门前区（<see cref="AddDoorway"/>）：门开在背对镜头那面墙时，主角站在门外那片被本栋挡住的地上，
    /// 同样收起本栋这一组（官方「挡住角色就收起」的语义，只限这一栋）；室内补光仍只认真正进屋。</summary>
    internal sealed class RaidIndoorRoofs : IDisposable
    {
        private sealed class Room
        {
            internal Bounds Footprint;
            internal OcclusionFadeObject Fade;
            internal Renderer[] Roof;
            internal string Name;
        }
        private sealed class Doorway
        {
            internal Bounds Zone;
            internal Room Room;
        }
        private readonly Dictionary<Vector2Int,List<Room>> cells = new Dictionary<Vector2Int,List<Room>>();
        private readonly List<Room> rooms = new List<Room>();
        private readonly List<Doorway> doorways = new List<Doorway>();
        private readonly MaterialPropertyBlock transition = new MaterialPropertyBlock();
        private static readonly int BackAt = Shader.PropertyToID("_TidegateRoofBackAt");
        private readonly bool restoreTransition;
        private Room inside;
        // 此刻收起的那一栋（屋里优先，其次门前区）；官方 OcclusionFadeObject 按计数，进出只经这一个字段成对调用。
        private Room faded;
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
                var room = new Room { Footprint = box.bounds, Fade = fade, Roof = roofRenderers, Name = volume.name };
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

        /// <summary>主角此刻所在的室内区（触发区名字 + 范围）；不在任何一间里返回 false。地图自己的室内照明按它点灯。</summary>
        internal bool TryGetInside(out string name, out Bounds footprint)
        {
            name = inside != null ? inside.Name : null;
            footprint = inside != null ? inside.Footprint : default(Bounds);
            return inside != null;
        }

        /// <summary>按触发区名字取一栋：范围与它的上部隐藏组（地图按门的位置和楼高算门前区用）。</summary>
        internal bool TryGetRoom(string name, out Bounds footprint, out Renderer[] roof)
        {
            foreach (Room room in rooms)
                if (string.Equals(room.Name, name, StringComparison.Ordinal))
                {
                    footprint = room.Footprint;
                    roof = room.Roof;
                    return true;
                }
            footprint = default(Bounds);
            roof = null;
            return false;
        }

        /// <summary>登记一处门前区：脚点在这里（且不在任何屋里）时收起这一栋。只给背对镜头的门用。</summary>
        internal bool AddDoorway(string name, Bounds zone)
        {
            foreach (Room room in rooms)
                if (string.Equals(room.Name, name, StringComparison.Ordinal))
                {
                    doorways.Add(new Doorway { Zone = zone, Room = room });
                    sampled = false;
                    return true;
                }
            return false;
        }

        internal bool UpdateFeet(Vector3 feet)
        {
            if (sampled && feet == lastFeet) return inside != null;
            sampled = true;
            lastFeet = feet;
            if (inside == null || !inside.Footprint.Contains(feet))
            {
                if (inside != null)
                {
                    if (restoreTransition) foreach (Renderer r in inside.Fade.renderers)
                    {
                        if (r == null) continue;
                        r.GetPropertyBlock(transition);
                        transition.SetFloat(BackAt, Time.time);
                        r.SetPropertyBlock(transition);
                    }
                }
                inside = FindRoom(feet);
            }
            Room show = inside ?? FindDoorway(feet);
            if (show != faded)
            {
                if (faded != null) faded.Fade.OnLeave();
                faded = show;
                if (faded != null) faded.Fade.OnEnter();
            }
            return inside != null;
        }

        private Room FindRoom(Vector3 feet)
        {
            List<Room> candidates;
            if (!cells.TryGetValue(new Vector2Int(Cell(feet.x), Cell(feet.z)), out candidates)) return null;
            foreach (Room room in candidates)
                if (room.Footprint.Contains(feet)) return room;
            return null;
        }

        private Room FindDoorway(Vector3 feet)
        {
            for (int i = 0; i < doorways.Count; i++)
                if (doorways[i].Zone.Contains(feet)) return doorways[i].Room;
            return null;
        }

        public void Dispose()
        {
            if (faded != null && faded.Fade != null) faded.Fade.OnLeave();
            faded = null;
            inside = null;
            foreach (Room room in rooms)
                if (room.Fade != null) UnityEngine.Object.Destroy(room.Fade);
            rooms.Clear();
            doorways.Clear();
            cells.Clear();
        }
    }
}
