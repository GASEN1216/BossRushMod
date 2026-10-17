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
        /// <summary>见闻点小木牌上的地名：键是见闻点标记名，字取 <see cref="SkyIslandPointText.Name"/>。</summary>
        private readonly List<KeyValuePair<string, TextMeshPro>> pointLabels = new List<KeyValuePair<string, TextMeshPro>>();
        private readonly ArenaPrototypeNavigation navigation;
        private readonly SkyIslandContentData content;
        private int flags = -1;
        private int gateMask;

        internal SkyIslandGates(GameObject root, ArenaPrototypeNavigation navigation, SkyIslandContentData content, int wallLayer)
        {
            this.navigation = navigation; this.content = content;
            Material wood = FindWood(root);
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
            // 三处只有触发盒的装置各立一块小木牌（2026-10-17）：地图圈罩得住它们，走进圈里却要贴到跟前才出交互提示。
            // 牌在见闻点背向相机一侧 2–2.2 m、触发盒外，无碰撞体不挡交互；落点由运行时摆放表与场景净空审计复核。
            AddPointSign(root, wood, wallLayer, "Search_B", new Vector3(-12f, 5, -102f));
            AddPointSign(root, wood, wallLayer, "Search_E", new Vector3(-17.2f, 18, 60f));
            AddPointSign(root, wood, wallLayer, "Search_H", new Vector3(-26.1f, 26, 206.9f));
            // 其余 17 处见闻点同一口径（2026-10-17 交互点可见性盘点，docs/reports/reviews/2026-10-17-天空岛交互点可见性盘点.md）：
            // 落点离线求解——先试相机反方向 2.2 m，挡了再按 10° 摆开、2.0–3.0 m 外扩；木桩不压路缘、不穿实体，
            // 牌心 1 m 圈不罩硬物，不进任何触发盒，离自家见闻点最近（SkyIslandInteractionCompetitionPropertyTest / SkyIslandPointPropsPropertyTest）。
            AddPointSign(root, wood, wallLayer, "Search_A_02", new Vector3(30.1f, 0, -188.9f));
            AddPointSign(root, wood, wallLayer, "Search_B_02", new Vector3(30.9f, 5, -66.9f));
            AddPointSign(root, wood, wallLayer, "Search_C", new Vector3(-196.9f, 10, -98.9f));
            AddPointSign(root, wood, wallLayer, "Search_C_02", new Vector3(-134.1f, 10, -48.1f));
            AddPointSign(root, wood, wallLayer, "Search_D_02", new Vector3(-146.7f, 16, 107.4f));
            AddPointSign(root, wood, wallLayer, "Search_E_02", new Vector3(45.9f, 18, 111.9f));
            AddPointSign(root, wood, wallLayer, "Search_F", new Vector3(205f, 15, -42.4f));
            AddPointSign(root, wood, wallLayer, "Search_F_02", new Vector3(143.2f, 15, -41.6f));
            AddPointSign(root, wood, wallLayer, "Search_G", new Vector3(143.1f, 21, 56.1f));
            AddPointSign(root, wood, wallLayer, "Search_G_02", new Vector3(205.9f, 21, 106.9f));
            AddPointSign(root, wood, wallLayer, "Search_H_02", new Vector3(35.9f, 26, 256.9f));
            AddPointSign(root, wood, wallLayer, "Search_S1", new Vector3(-309.1f, 7, -113.1f));
            AddPointSign(root, wood, wallLayer, "Search_S2", new Vector3(-295.6f, 13, 92.9f));
            AddPointSign(root, wood, wallLayer, "Search_S3", new Vector3(288.4f, 10, -73.2f));
            AddPointSign(root, wood, wallLayer, "Search_S4", new Vector3(283f, 26, 68.2f));
            // 码头整备与风标两处站在铺装广场中间，3 m 内放不下两根木桩：只立一根桩的窄牌。
            // 码头那块贴着货仓外的空地 1.4 m；风标那块在广场东北沿 3.7 m，正对 9.6 m 外的大风标（场景里最近的可见件）。
            AddPointPost(root, wood, wallLayer, "Search_A", new Vector3(-29.5f, 0, -238.7f));
            AddPointPost(root, wood, wallLayer, "Search_D", new Vector3(-188.7f, 16, 58.5f));
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
        /// <summary>作者场景里第一张名字带 wood 的材质（Sky_Wood 一类，作者着色器 BossRush/SkyIsland/Environment）。找不到返回 null。</summary>
        internal static Material FindWood(GameObject root)
        {
            if (root == null) return null;
            foreach (MeshRenderer renderer in root.GetComponentsInChildren<MeshRenderer>(true))
                if (renderer.sharedMaterial != null && renderer.sharedMaterial.name.IndexOf("wood", StringComparison.OrdinalIgnoreCase) >= 0)
                    return renderer.sharedMaterial;
            return null;
        }

        /// <summary>木纹大约每多少米重复一次（UE-24）。</summary>
        private const float WoodTileMeters = 1.2f;

        /// <summary>
        /// 一根木料：立方体 + 作者木材质，无碰撞体。立方体每个面的 UV 都是 0–1，7.5 m 长的牌面会把一张木纹拉成糊掉的色带（UE-24），
        /// 所以按尺寸给这一根单独写贴图平铺：<c>_BaseMap_ST</c>（作者着色器声明在 UnityPerMaterial 里、贴图是 Repeat）约每 1.2 m 重复一次，
        /// 偏移按位置错开，相邻的木料纹理不一样。只写这一个渲染器的属性块，共享材质不动。
        /// </summary>
        private static void Beam(GameObject parent, Material material, int layer, Vector3 position, Vector3 scale)
        {
            GameObject beam = GameObject.CreatePrimitive(PrimitiveType.Cube);
            beam.name = "GateTimber"; beam.layer = layer; beam.transform.SetParent(parent.transform, false);
            beam.transform.localPosition = position; beam.transform.localScale = scale;
            Collider collider = beam.GetComponent<Collider>();
            collider.enabled = false;
            UnityEngine.Object.Destroy(collider);
            MeshRenderer renderer = beam.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            if (material != null && material.HasProperty("_BaseMap_ST"))
            {
                var block = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(block);
                float offset = Mathf.Repeat(position.x * 0.37f + position.y * 0.61f, 1f);
                block.SetVector("_BaseMap_ST", new Vector4(Mathf.Max(0.25f, Mathf.Max(scale.x, scale.z) / WoodTileMeters),
                    Mathf.Max(0.25f, scale.y / WoodTileMeters), offset, 0f));
                renderer.SetPropertyBlock(block);
            }
        }

        /// <summary>
        /// 纪念物脚下的一根木桩 + 一块小木牌（UE-08）：纪念物原来只是空地上悬一行字加一盏光，看不出「那里有个东西」。
        /// 复用桥口木牌同一张作者木材质与同一个 Beam 构件，无碰撞体、不挡交互触发，挂在纪念物下面随它一起重建 / 销毁。
        /// 木材质找不到就什么都不建（纯装饰，fail-open）。
        /// </summary>
        internal static void AddMemorialPost(GameObject memorial, Material wood)
        {
            if (memorial == null || wood == null) return;
            GameObject post = new GameObject("MemorialPost");
            post.transform.SetParent(memorial.transform, false);
            int layer = 0;
            Beam(post, wood, layer, new Vector3(0f, 0.6f, 0f), new Vector3(0.14f, 1.2f, 0.14f));
            GameObject board = new GameObject("MemorialBoard");
            board.transform.SetParent(post.transform, false);
            board.transform.localPosition = new Vector3(0f, 1.15f, -0.09f);
            // 牌面朝向固定俯视相机（口径同桥口木牌），近处读得到它是一块牌子。
            Camera camera = GameCamera.Instance == null ? null : GameCamera.Instance.renderCamera;
            if (camera != null) board.transform.rotation = Quaternion.Euler(0f, camera.transform.eulerAngles.y, 0f);
            Beam(board, wood, layer, Vector3.zero, new Vector3(0.9f, 0.46f, 0.07f));
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

        /// <summary>小木牌的木桩按官方默认相机朝向（<c>CameraArm.globalYaw</c>）排开，与朝相机的牌面平行；离线摆放表按同一朝向复算木桩落点。</summary>
        private const float PointSignYaw = -30f;

        /// <summary>
        /// 见闻点装置旁的小木牌（20 处 Search_* 原来只有触发盒，场景里看不见；先补 B / E / H，同日补齐其余 17 处）：两根木桩 + 一块朝相机的木牌 + 地名。
        /// 字与交互提示同词（<see cref="SkyIslandPointText.Name"/>，「 · 」处换行）。构件同桥口木牌，无碰撞体、不在交互层，
        /// 不抢交互；随 <see cref="signs"/> 回收。
        /// </summary>
        private void AddPointSign(GameObject world, Material wood, int layer, string key, Vector3 position)
        {
            GameObject sign = new GameObject("PointNotice_" + key);
            signs.Add(sign);
            sign.transform.SetParent(world.transform, false);
            sign.transform.localPosition = position;
            sign.transform.localRotation = Quaternion.Euler(0f, PointSignYaw, 0f);
            sign.layer = layer;
            Beam(sign, wood, layer, new Vector3(-0.9f, 1f, 0), new Vector3(.14f, 2f, .14f));
            Beam(sign, wood, layer, new Vector3(0.9f, 1f, 0), new Vector3(.14f, 2f, .14f));
            AddPointBoard(sign, wood, layer, key, 2.1f, new Vector2(2.8f, 1.1f));
        }

        /// <summary>
        /// 单桩窄牌：铺装广场中间的见闻点（码头整备、风标）3 m 内放不下两根木桩，只立一根桩。
        /// 字、换语言、回收与 <see cref="AddPointSign"/> 相同；离线摆放表按一根桩（`#P`）记。
        /// </summary>
        private void AddPointPost(GameObject world, Material wood, int layer, string key, Vector3 position)
        {
            GameObject sign = new GameObject("PointNotice_" + key);
            signs.Add(sign);
            sign.transform.SetParent(world.transform, false);
            sign.transform.localPosition = position;
            sign.transform.localRotation = Quaternion.Euler(0f, PointSignYaw, 0f);
            sign.layer = layer;
            Beam(sign, wood, layer, new Vector3(0f, 1f, 0), new Vector3(.14f, 2f, .14f));
            AddPointBoard(sign, wood, layer, key, 2.0f, new Vector2(1.9f, 0.95f));
        }

        /// <summary>见闻点木牌的牌面与地名：牌面朝固定俯视相机，字取 <see cref="PointNotice"/>，随语言在 <see cref="WriteLabels"/> 里重写。</summary>
        private void AddPointBoard(GameObject sign, Material wood, int layer, string key, float height, Vector2 size)
        {
            GameObject board = new GameObject("PointNoticeBoard");
            board.transform.SetParent(sign.transform, false);
            board.transform.localPosition = Vector3.up * height;
            Camera camera = GameCamera.Instance == null ? null : GameCamera.Instance.renderCamera;
            if (camera != null) board.transform.rotation = camera.transform.rotation;
            Beam(board, wood, layer, Vector3.zero, new Vector3(size.x, size.y, .1f));
            GameObject label = new GameObject("PointNoticeText");
            label.transform.SetParent(board.transform, false);
            label.transform.localPosition = new Vector3(0, 0, -.07f);
            TextMeshPro text = label.AddComponent<TextMeshPro>();
            TMP_FontAsset font = ZombieModeUIHelper.GetGameFont();
            if (font == null) throw new InvalidOperationException("见闻点木牌缺少游戏中文字体");
            text.font = font;
            // 两行地名；英文名最长约 60 个字母，自动缩放到 1.0 号仍放不下才省略号收尾（窄牌同一下限）。
            text.enableAutoSizing = true;
            text.fontSizeMin = 1f;
            text.fontSizeMax = 2.6f;
            text.fontSize = 2.6f;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.alignment = TextAlignmentOptions.Center;
            text.rectTransform.sizeDelta = new Vector2(size.x - 0.2f, size.y - 0.15f);
            text.color = BossRushUIColors.TextPrimary;
            text.sortingOrder = BossRushUILayers.WorldOverlay;
            text.richText = false;
            text.raycastTarget = false;
            pointLabels.Add(new KeyValuePair<string, TextMeshPro>(key, text));
        }

        private static string PointNotice(string key)
        {
            return SkyIslandPointText.Name(key).Replace(" · ", "\n");
        }
        private static string Notice(string id, bool open, SkyIslandStoryData story)
        {
            string title, condition;
            switch (id)
            {
                case "K1":
                    title = L10n.T("悬根林 / 风铃集 · K1", "Hanging Root Wood / Windchime Market · K1");
                    condition = story.Has(SkyIslandStoryFlag.WindBeacon)
                        ? L10n.T("风标修好了\n到风标旁系牢回程绳桥",
                            "Wind beacon repaired\nLash the rope bridge at the beacon")
                        : L10n.T("先修好悬根林的风标\n再到风标旁系牢回程绳桥",
                            "Repair the Hanging Root Wood beacon first\nthen lash the rope bridge at the beacon"); break;
                case "K2":
                    title = L10n.T("残星工坊 / 风铃集 · K2", "Fallen Star Workshop / Windchime Market · K2");
                    condition = story.Has(SkyIslandStoryFlag.StarLamp)
                        ? L10n.T("星灯修好了\n到星灯旁打开检修廊",
                            "Star lamp repaired\nOpen the maintenance walk at the lamp")
                        : L10n.T("先修好残星工坊的星灯\n再到星灯旁打开检修廊",
                            "Repair the Fallen Star Workshop lamp first\nthen open the maintenance walk at the lamp"); break;
                case "K3":
                    title = L10n.T("鸣风栈道 / 风铃集 · K3", "Windsong Boardwalk / Windchime Market · K3");
                    // 开桥在栈道南头的双航标门装置（Search_E）上点；那儿立着一块只写地名的小木牌（AddPointSign），
                    // 写通行状态的门牌在 K3 桥头与北头钟庭门两侧（2026-10-16 指引复核；2026-10-17 补小木牌）。
                    condition = story.BothBeacons
                        ? L10n.T("两端航标都修好了\n到栈道南头的双航标门开启旧桥",
                            "Both beacons restored\nOpen the old bridge at the twin-beacon gate, south end of the boardwalk")
                        : L10n.T("先修好风标和星灯\n再到栈道南头的双航标门开启旧桥",
                            "Restore the wind beacon and the star lamp first\nthen open the old bridge at the twin-beacon gate, south end of the boardwalk"); break;
                case "BellCourt":
                    title = L10n.T("鸣风栈道 / 归航钟庭", "Windsong Boardwalk / Homecoming Bell Court");
                    condition = L10n.T("先修好悬根林的风标\n和残星工坊的星灯",
                        "Restore the Hanging Root Wood beacon\nand the Fallen Star Workshop lamp"); break;
                default:
                    // ZhelingPass 只挡 FG 这一段。按 ArtSource/SkyIsland/layout.json，
                    // A→B→C→D→E→G 全程无门，残星工坊、星灯、S4 观星镜与 K2 都能绕到，
                    // 折翎并非必经。牌子必须如实说是近路，不能写成硬性前置误导玩家。
                    title = L10n.T("镜水寺 / 残星工坊 · 近路",
                        "Mirrorwater Temple / Fallen Star Workshop · shortcut");
                    condition = L10n.T("和折翎和解，或者打赢他，就能通\n也可以从悬根林绕到鸣风栈道",
                        "Opens once you reconcile with or defeat Zheling\nor go around via Hanging Root Wood and Windsong Boardwalk"); break;
            }
            return title + (open
                ? L10n.T("\n已通行", "\nOpen")
                : L10n.T("\n还没通\n", "\nClosed\n") + condition);
        }
        /// <summary>只有门条件涉及的位才需要重扫导航；旧信、见闻等无关事实变化不应触发 3655 面的重算。</summary>
        private int GateMask()
        {
            if (gateMask != 0) return gateMask;
            foreach (SkyIslandGateDefinition gate in content.Gates) gateMask |= (int)gate.Required;
            return gateMask;
        }

        /// <summary>木牌上一次按哪种语言写的：玩家在岛上切了语言时只重写字，不重扫导航。</summary>
        private bool labelsChinese;
        /// <summary>关着的门自检节拍（见 <see cref="VerifyBlocked"/>）；自检点不在挡区里时停掉，免得每秒重封。</summary>
        private float nextVerifyAt, nextLeakLogAt;
        private bool verifyDisabled;
        /// <summary>这一趟一共重封了几次：每次都数，日志限频到 10 秒一条，下一轮 F3 能看出丢失是否还在发生。</summary>
        private int leakCount;

        internal void Apply(SkyIslandStoryData story)
        {
            if (story == null) return;
            int relevant = story.flags & GateMask();
            bool chinese = L10n.IsChinese;
            if (flags == relevant)
            {
                // 门没变、只是切了语言：木牌按当前语言重写（语言在取用时解析，AGENTS §4.4）。
                if (chinese != labelsChinese) WriteLabels(story, chinese);
                VerifyBlocked(story);
                return;
            }
            WriteLabels(story, chinese);
            navigation.SetBlockedAreas(CollectBlocked(story, true));
            flags = relevant;
        }

        /// <summary>按剧情开关门体，返回关着的门的碰撞盒。先采集激活的 collider 边界；关闭对象后 Bounds 会变空。</summary>
        private Bounds[] CollectBlocked(SkyIslandStoryData story, bool toggle)
        {
            var blocked = new List<Bounds>();
            foreach (Gate gate in gates)
            {
                bool open = content.IsGateOpen(gate.Id, story);
                if (toggle) gate.Root.SetActive(!open);
                if (!open) blocked.Add(gate.Blocker.bounds);
            }
            return blocked.ToArray();
        }

        /// <summary>
        /// 门关着时每秒抽查一次门框中心的导航节点：能走说明挡区被别的东西冲掉了（2026-09-15 第七轮 F3：
        /// 真实状态段跑完之后，新档阶段钟庭门关着却走得到，而剧情标志全程没变、挡区只有这里在写）。
        /// 按当前碰撞盒重新封一遍，记一次日志与重封前后的可走节点数，方便下一轮查出是谁冲掉的。
        /// 平时每秒最多几次最近节点查询；重封之后门框中心仍然能走，说明自检点本来就不在挡区里，停掉自检。
        /// </summary>
        private void VerifyBlocked(SkyIslandStoryData story)
        {
            if (verifyDisabled || Time.time < nextVerifyAt) return;
            nextVerifyAt = Time.time + 1f;
            string leaked = LeakedGate(story);
            if (leaked == null) return;
            int before = navigation.CountWalkableNodes();
            navigation.SetBlockedAreas(CollectBlocked(story, false));
            string still = LeakedGate(story);
            if (still != null) verifyDisabled = true;
            leakCount++;
            if (still == null && Time.time < nextLeakLogAt) return;
            nextLeakLogAt = Time.time + 10f;
            Debug.LogWarning("[SkyIsland] gate navigation block was lost and re-applied: gate=" + leaked + ",count=" + leakCount
                + ",walkable_before=" + before + ",walkable_after=" + navigation.CountWalkableNodes()
                + (still != null ? ",probe_not_in_block=" + still + ",verify_disabled=True" : string.Empty));
        }

        private string LeakedGate(SkyIslandStoryData story)
        {
            foreach (Gate gate in gates)
            {
                if (gate.Root == null || !gate.Root.activeInHierarchy || content.IsGateOpen(gate.Id, story)) continue;
                if (navigation.IsWalkableAt(gate.Blocker.bounds.center)) return gate.Id;
            }
            return null;
        }

        private void WriteLabels(SkyIslandStoryData story, bool chinese)
        {
            foreach (Gate gate in gates)
            {
                bool open = content.IsGateOpen(gate.Id, story);
                foreach (TextMeshPro label in gate.Labels)
                {
                    label.text = Notice(gate.Id, open, story);
                    label.color = open ? BossRushUIColors.SuccessText : BossRushUIColors.WarningText;
                }
            }
            foreach (KeyValuePair<string, TextMeshPro> point in pointLabels)
                if (point.Value != null) point.Value.text = PointNotice(point.Key);
            labelsChinese = chinese;
        }
        public void Dispose()
        {
            foreach (Gate gate in gates) if (gate.Root != null) UnityEngine.Object.Destroy(gate.Root);
            gates.Clear();
            foreach (GameObject sign in signs) if (sign != null) UnityEngine.Object.Destroy(sign);
            signs.Clear();
            pointLabels.Clear();
        }
    }
}
