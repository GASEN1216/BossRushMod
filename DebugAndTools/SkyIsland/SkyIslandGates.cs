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
            // 布局 v2（2026-09-10）：门在受控端往桥内 8m 的桥中心线上、横跨整个桥面；坐标由 layout.json 离线算出，
            // 32 种开闭组合与门体横跨桥宽由 tests/SkyIslandGateNavigationPropertyTest.py 逐个验证。
            // 三条回程捷径的门放在风铃集（B）一侧桥头：中继平台上的搜刮点只能从远端岛走过去，
            // 不会变成出生点旁不设防的高档箱；从风铃集出发也不用白走一整座锁着的桥。
            Add(root, wood, wallLayer, "K1", new Vector3(-33f, 5.754f, -34.5f), 180, 7);
            Add(root, wood, wallLayer, "K2", new Vector3(21.276f, 5.825f, -34.512f), -172.231f, 7);
            Add(root, wood, wallLayer, "K3", new Vector3(-6f, 6.66f, -34.5f), 0, 7);
            Add(root, wood, wallLayer, "BellCourt", new Vector3(5f, 19.333f, 140.5f), 0, 9);
            Add(root, wood, wallLayer, "ZhelingPass", new Vector3(190f, 16.606f, 3f), 0, 8);
            // 桥口向岛内退 5~8m、沿岸侧移 8~10m，木桩落在入口岛的导航面上；落点由几何回归逐个验证。
            AddSign(root, wood, wallLayer, "K1", new Vector3(-100f, 16, 42f));
            AddSign(root, wood, wallLayer, "K1", new Vector3(-25f, 5, -47.5f));
            AddSign(root, wood, wallLayer, "K2", new Vector3(122.5f, 21, 63f));
            AddSign(root, wood, wallLayer, "K2", new Vector3(29f, 5, -47.5f));
            AddSign(root, wood, wallLayer, "K3", new Vector3(2f, 5, -47.5f));
            AddSign(root, wood, wallLayer, "K3", new Vector3(-14f, 18, 32.5f));
            AddSign(root, wood, wallLayer, "BellCourt", new Vector3(13f, 18, 127.5f));
            AddSign(root, wood, wallLayer, "BellCourt", new Vector3(-3f, 26, 177.5f));
            AddSign(root, wood, wallLayer, "ZhelingPass", new Vector3(198f, 15, -10f));
            AddSign(root, wood, wallLayer, "ZhelingPass", new Vector3(182f, 21, 27.5f));
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
            // 英文关闭态有 6–7 行、约 3.3–3.9 单位高，木板却只有 2.8（文本框 2.5）：固定 4.4 号会整段溢出木板。
            // 开自动缩放、下限 2.4，仍放不下才省略号收尾；中文 4 行在 4.4 号本来就放得下，观感不变。
            text.enableAutoSizing = true;
            text.fontSizeMin = 2.4f;
            text.fontSizeMax = 4.4f;
            text.fontSize = 4.4f;
            text.overflowMode = TextOverflowModes.Ellipsis;
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
                    title = L10n.T("悬根林 / 风铃集 · K1", "Hanging Root Wood / Windchime Market · K1");
                    condition = story.Has(SkyIslandStoryFlag.WindBeacon)
                        ? L10n.T("风标已修复\n请到风标旁系牢回程绳桥",
                            "Wind beacon repaired\nLash the rope bridge at the beacon")
                        : L10n.T("请先修复悬根林风标\n再到风标旁系牢回程绳桥",
                            "Repair the Hanging Root Wood beacon first\nthen lash the rope bridge at the beacon"); break;
                case "K2":
                    title = L10n.T("残星工坊 / 风铃集 · K2", "Fallen Star Workshop / Windchime Market · K2");
                    condition = story.Has(SkyIslandStoryFlag.StarLamp)
                        ? L10n.T("星灯已修复\n请到星灯旁打开检修廊",
                            "Star lamp repaired\nOpen the maintenance walk at the lamp")
                        : L10n.T("请先修复残星工坊星灯\n再到星灯旁打开检修廊",
                            "Repair the Fallen Star Workshop lamp first\nthen open the maintenance walk at the lamp"); break;
                case "K3":
                    title = L10n.T("鸣风栈道 / 风铃集 · K3", "Windsong Boardwalk / Windchime Market · K3");
                    condition = story.BothBeacons
                        ? L10n.T("双航标已恢复\n请到鸣风栈道开启旧桥",
                            "Both beacons restored\nOpen the old bridge on Windsong Boardwalk")
                        : L10n.T("请先恢复风标与星灯\n再到鸣风栈道开启旧桥",
                            "Restore the wind beacon and the star lamp first\nthen open the old bridge on Windsong Boardwalk"); break;
                case "BellCourt":
                    title = L10n.T("鸣风栈道 / 归航钟庭", "Windsong Boardwalk / Homecoming Bell Court");
                    condition = L10n.T("请先恢复悬根林风标\n以及残星工坊星灯",
                        "Restore the Hanging Root Wood beacon\nand the Fallen Star Workshop lamp"); break;
                default:
                    // ZhelingPass 只挡 FG 这一段。按 ArtSource/SkyIsland/layout.json，
                    // A→B→C→D→E→G 全程无门，残星工坊、星灯、S4 观星镜与 K2 都能绕到，
                    // 折翎并非必经。牌子必须如实说是近路，不能写成硬性前置误导玩家。
                    title = L10n.T("镜水寺 / 残星工坊 · 近路",
                        "Mirrorwater Temple / Fallen Star Workshop · shortcut");
                    condition = L10n.T("与折翎和解或战胜他即可开启\n也可经悬根林绕行鸣风栈道抵达",
                        "Opens once you reconcile with or defeat Zheling\nor go around via Hanging Root Wood and Windsong Boardwalk"); break;
            }
            return title + (open
                ? L10n.T("\n已通行", "\nOpen")
                : L10n.T("\n尚未通行\n", "\nClosed\n") + condition);
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
