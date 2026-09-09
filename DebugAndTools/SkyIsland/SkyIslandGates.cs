using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace BossRush
{
    /// <summary>桥上的剧情门同时阻挡玩家碰撞和当前地图 A*；状态来自已成功持久化的剧情快照。</summary>
    internal sealed class SkyIslandGates : IDisposable
    {
        private sealed class Gate
        {
            internal string Id;
            internal GameObject Root;
            internal BoxCollider Blocker;
            internal readonly List<TextMeshPro> Labels = new List<TextMeshPro>();
        }
        private readonly List<Gate> gates = new List<Gate>();
        private readonly List<GameObject> signs = new List<GameObject>();
        private readonly ArenaPrototypeNavigation navigation;
        private readonly SkyIslandContentData content;
        private int flags = -1;
        private int gateMask;

        internal SkyIslandGates(GameObject root, ArenaPrototypeNavigation navigation, SkyIslandContentData content, int wallLayer)
        {
            this.navigation = navigation; this.content = content;
            Material wood = null;
            foreach (MeshRenderer renderer in root.GetComponentsInChildren<MeshRenderer>(true))
                if (renderer.sharedMaterial != null && renderer.sharedMaterial.name.IndexOf("wood", StringComparison.OrdinalIgnoreCase) >= 0)
                { wood = renderer.sharedMaterial; break; }
            if (wood == null) throw new InvalidOperationException("天空岛进度门缺少作者木材质");
            Add(root, wood, wallLayer, "K1", new Vector3(-209.934f, 27.354f, 12.034f), 175.187f, 7);
            Add(root, wood, wallLayer, "K2", new Vector3(205, 43.116f, 97.058f), 180, 7);
            Add(root, wood, wallLayer, "K3", new Vector3(-87.896f, 40.955f, 79.614f), -105, 7);
            Add(root, wood, wallLayer, "BellCourt", new Vector3(40.618f, 42.809f, 192.889f), 14.754f, 9);
            Add(root, wood, wallLayer, "ZhelingPass", new Vector3(230.415f, 27.258f, 57.854f), 14.567f, 8);
            // 实际 layout 桥口向岛内退 5m、向旁边移 8m；落点由几何回归逐个验证。
            AddSign(root, wood, wallLayer, "K1", new Vector3(-218, 28, 25));
            AddSign(root, wood, wallLayer, "K1", new Vector3(-52, 8, -60));
            AddSign(root, wood, wallLayer, "K2", new Vector3(197, 44, 110));
            AddSign(root, wood, wallLayer, "K2", new Vector3(63, 8, -60));
            AddSign(root, wood, wallLayer, "K3", new Vector3(-17, 8, -60));
            AddSign(root, wood, wallLayer, "K3", new Vector3(-75, 42, 88));
            AddSign(root, wood, wallLayer, "BellCourt", new Vector3(48, 42, 180));
            AddSign(root, wood, wallLayer, "BellCourt", new Vector3(85, 62, 262));
            AddSign(root, wood, wallLayer, "ZhelingPass", new Vector3(238, 26, 45));
            AddSign(root, wood, wallLayer, "ZhelingPass", new Vector3(277, 44, 110));
        }
        private void Add(GameObject world, Material material, int layer, string id, Vector3 position, float yaw, float width)
        {
            GameObject root = new GameObject("StoryGate_" + id);
            root.transform.SetParent(world.transform, false); root.transform.localPosition = position;
            root.transform.localRotation = Quaternion.Euler(0, yaw, 0); root.layer = layer;
            BoxCollider blocker = root.AddComponent<BoxCollider>();
            blocker.center = Vector3.up * 3; blocker.size = new Vector3(width + 3, 8, 1.8f);
            // 木梁和竖栏均复用场景材质；整幅 collider 保证玩家无法挤过视觉间隙。
            for (int i = 0; i < 2; i++) Beam(root, material, layer, new Vector3(0, 1 + i * 2, 0), new Vector3(width + 1, 0.4f, 0.5f));
            for (int i = 0; i <= 8; i++) Beam(root, material, layer, new Vector3((i / 8f - 0.5f) * width, 2.1f, 0), new Vector3(0.35f, 4.2f, 0.45f));
            gates.Add(new Gate { Id = id, Root = root, Blocker = blocker });
        }
        private static void Beam(GameObject parent, Material material, int layer, Vector3 position, Vector3 scale)
        {
            GameObject beam = GameObject.CreatePrimitive(PrimitiveType.Cube);
            beam.name = "GateTimber"; beam.layer = layer; beam.transform.SetParent(parent.transform, false);
            beam.transform.localPosition = position; beam.transform.localScale = scale;
            Collider collider = beam.GetComponent<Collider>();
            collider.enabled = false;
            UnityEngine.Object.Destroy(collider);
            beam.GetComponent<MeshRenderer>().sharedMaterial = material;
        }
        private void AddSign(GameObject world, Material wood, int layer, string id, Vector3 position)
        {
            Gate gate = gates.Find(candidate => candidate.Id == id);
            if (gate == null) throw new InvalidOperationException("桥口木牌缺少对应剧情门 " + id);
            GameObject sign = new GameObject("BridgeNotice_" + id);
            signs.Add(sign);
            // 独立于开门时隐藏的 gate.Root，开放以后仍保留方向与通行提示。
            sign.transform.SetParent(world.transform, false);
            sign.transform.localPosition = position;
            sign.layer = layer;
            Beam(sign, wood, layer, new Vector3(-2.4f, 1.45f, 0), new Vector3(.22f, 2.9f, .22f));
            Beam(sign, wood, layer, new Vector3(2.4f, 1.45f, 0), new Vector3(.22f, 2.9f, .22f));
            GameObject board = new GameObject("BridgeNoticeBoard");
            board.transform.SetParent(sign.transform, false);
            board.transform.localPosition = Vector3.up * 3;
            Camera camera = GameCamera.Instance == null ? null : GameCamera.Instance.renderCamera;
            // 与固定俯视游戏相机同向，木板与文字一起倾斜，近桥时可直接阅读。
            if (camera != null) board.transform.rotation = camera.transform.rotation;
            Beam(board, wood, layer, Vector3.zero, new Vector3(7.5f, 2.8f, .28f));
            GameObject label = new GameObject("BridgeNoticeText");
            label.transform.SetParent(board.transform, false);
            label.transform.localPosition = new Vector3(0, 0, -.16f);
            TextMeshPro text = label.AddComponent<TextMeshPro>();
            TMP_FontAsset font = ZombieModeUIHelper.GetGameFont();
            if (font == null) throw new InvalidOperationException("桥口木牌缺少游戏中文字体");
            text.font = font;
            text.fontSize = 4.4f;
            text.enableAutoSizing = false;
            text.alignment = TextAlignmentOptions.Center;
            text.rectTransform.sizeDelta = new Vector2(7.1f, 2.5f);
            text.sortingOrder = BossRushUILayers.WorldOverlay;
            text.richText = false;
            text.raycastTarget = false;
            gate.Labels.Add(text);
        }
        private static string Notice(string id, bool open, SkyIslandStoryData story)
        {
            string title, condition;
            switch (id)
            {
                case "K1":
                    title = "悬根林 / 风铃集 · K1";
                    condition = story.Has(SkyIslandStoryFlag.WindBeacon) ? "风标已修复\n请到风标旁系牢回程绳桥" : "请先修复悬根林风标\n再到风标旁系牢回程绳桥"; break;
                case "K2":
                    title = "残星工坊 / 风铃集 · K2";
                    condition = story.Has(SkyIslandStoryFlag.StarLamp) ? "星灯已修复\n请到星灯旁打开检修廊" : "请先修复残星工坊星灯\n再到星灯旁打开检修廊"; break;
                case "K3":
                    title = "鸣风栈道 / 风铃集 · K3";
                    condition = story.BothBeacons ? "双航标已恢复\n请到鸣风栈道开启旧桥" : "请先恢复风标与星灯\n再到鸣风栈道开启旧桥"; break;
                case "BellCourt":
                    title = "鸣风栈道 / 归航钟庭";
                    condition = "请先恢复悬根林风标\n以及残星工坊星灯"; break;
                default:
                    title = "镜水寺 / 残星工坊";
                    condition = "请与镜水寺的折翎和解\n或明确挑战并战胜折翎"; break;
            }
            return title + (open ? "\n已通行" : "\n尚未通行\n" + condition);
        }
        /// <summary>只有门条件涉及的位才需要重扫导航；旧信、见闻等无关事实变化不应触发 3655 面的重算。</summary>
        private int GateMask()
        {
            if (gateMask != 0) return gateMask;
            foreach (SkyIslandGateDefinition gate in content.Gates) gateMask |= (int)gate.Required;
            return gateMask;
        }

        internal void Apply(SkyIslandStoryData story)
        {
            if (story == null) return;
            int relevant = story.flags & GateMask();
            if (flags == relevant) return;
            var blocked = new List<Bounds>();
            // 先采集激活的 collider 边界；关闭对象后 Bounds 会变空。
            foreach (Gate gate in gates)
            {
                bool open = content.IsGateOpen(gate.Id, story);
                gate.Root.SetActive(!open);
                if (!open) blocked.Add(gate.Blocker.bounds);
                foreach (TextMeshPro label in gate.Labels)
                {
                    label.text = Notice(gate.Id, open, story);
                    label.color = open ? BossRushUIColors.SuccessText : BossRushUIColors.WarningText;
                }
            }
            navigation.SetBlockedAreas(blocked.ToArray());
            flags = relevant;
        }
        public void Dispose()
        {
            foreach (Gate gate in gates) if (gate.Root != null) UnityEngine.Object.Destroy(gate.Root);
            gates.Clear();
            foreach (GameObject sign in signs) if (sign != null) UnityEngine.Object.Destroy(sign);
            signs.Clear();
        }
    }
}
