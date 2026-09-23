// ============================================================================
// WishFountainDanmakuView.cs - 许愿台全屏弹幕层
// ============================================================================
// 模块说明：
//   打开许愿面板时，在面板「下方」、遮罩「上方」展示一层全屏弹幕：
//   把飞书里已保存的心愿内容随机铺开，从右往左匀速滚动，循环播放。
//
//   层级关系（从下到上）：
//     许愿面板的半透明遮罩(overlay) → 本弹幕层 → 许愿面板内容
//   本弹幕层作为 WishFountainView 的兄弟节点插入，且始终位于 View 之前，
//   由 WishFountainView 在 OnOpen 时 SetAsLastSibling 保证面板盖在弹幕之上。
//
// 性能策略（避免卡顿）：
//   - 固定泳道(lane)：屏幕纵向均分为若干条泳道，弹幕沿泳道水平滚动
//   - 打开时按泳道预铺满首屏，避免“要等几秒才像弹幕”的空场期
//   - 对象池：TMP 文本对象复用，不频繁 new/Destroy；活动对象数只和屏幕密度有关
//   - 文本预处理：Show 时一次性做单行化、截断、测宽，滚动阶段不重复格式化/测宽
//   - 每帧只做位移 + 少量入场/回收判断，无 GC 抖动、无网络请求
//   - raycastTarget 全部关闭，不拦截输入（输入交给上层面板遮罩）
//
// 观感（2026-09-23 审美审查 UD-22）：
//   - 只在屏幕上下各 25% 的两条带里飘，中间 50% 留给许愿面板，不再满屏文字墙。
//   - 三层景深：远 / 中 / 近 = 字号 18/22/26、不透明度 0.35/0.5/0.7、速度 60/80/100，按泳道分层。
//   - 字底：UI.Shadow 对 TMP 不起作用（TMP 自己出网格，不走 IMeshModifier），改为弹幕池共用一份
//     打开 UNDERLAY_ON 的材质实例，OnDestroy 销毁。
//   - 整层 0.3 秒 SmoothStep 淡入；收起时随面板根节点一起淡出。
//   - 配色只用 token（主文字 / 次要文字 / 一点暖金）。
// ============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BossRush
{
    public class WishFountainDanmakuView : MonoBehaviour
    {
        // ==== 布局与滚动参数 ====
        private const int MIN_LANES_PER_BAND = 2;         // 每条带的保底泳道数
        private const int MAX_LANES_PER_BAND = 7;         // 每条带的泳道上限
        private const float BAND_HEIGHT_RATIO = 0.25f;    // 上下两条带各占屏高 25%，中间 50% 不放弹幕
        private const float LANE_TARGET_HEIGHT = 72f;     // 期望泳道高度（据此按带高自适应泳道数）
        private const float ITEM_HEIGHT = 56f;
        private const float SPAWN_GAP_MIN = 200f;         // 同泳道两条弹幕的最小水平间距
        private const float SPAWN_GAP_MAX = 500f;         // 同泳道两条弹幕的最大水平间距
        private const float BASE_FONT_SIZE = 22f;         // 测宽基准字号；各层按字号比例缩放宽度
        private const float FADE_SECONDS = 0.3f;          // 整层淡入时长
        private const int MAX_CONTENT_LENGTH = 40;        // 单条弹幕最长字符数，超出省略
        private const float SIDE_PADDING = 40f;           // 弹幕文本左右内边距（估宽用）
        private const int MAX_SPAWNS_PER_LANE_PER_FRAME = 3;
        private const int POOL_WARMUP_IMMEDIATE_COUNT = 12;
        private const int POOL_WARMUP_BATCH_SIZE = 12;
        private const float ESTIMATED_AVERAGE_ITEM_WIDTH = 280f;
        private const float ESTIMATED_PREFILL_WIDTH_RATIO = 1.3f;
        private const int MAX_ESTIMATED_ITEMS_PER_LANE = 8;

        // 三层景深（远 / 中 / 近）：字号、不透明度、速度（像素/秒）。按泳道分层，同一泳道速度一致、不会追尾。
        private static readonly float[] DepthFontSizes = { 18f, 22f, 26f };
        private static readonly float[] DepthAlphas = { 0.35f, 0.5f, 0.7f };
        private static readonly float[] DepthSpeeds = { 60f, 80f, 100f };

        // 只用 token：主文字为主，夹一点次要文字与暖金（星光），alpha 由景深层决定
        private static readonly Color[] DanmakuColors =
        {
            BossRushUIColors.TextPrimary,
            BossRushUIColors.TextPrimary,
            BossRushUIColors.TextSecondary,
            BossRushUIColors.WarningText
        };

        private sealed class DanmakuItem
        {
            public RectTransform rect;
            public TextMeshProUGUI text;
            public float width;
            public bool active;
        }

        private sealed class PreparedDanmakuContent
        {
            public string text;
            public float width;             // 基准字号下的文字宽度（不含内边距）
        }

        private sealed class Lane
        {
            public float centerY;
            public int depth;               // 景深层：0 远 / 1 中 / 2 近
            public readonly List<DanmakuItem> items = new List<DanmakuItem>();
            public int nextContentIndex;    // 该泳道下一个要取用的内容索引
            public float nextSpawnGap;      // 下一条入场所需的额外间距
        }

        private RectTransform selfRect;
        private CanvasGroup canvasGroup;
        private TMP_FontAsset font;
        private Material textMaterial;      // 弹幕池共用的字底材质实例，OnDestroy 销毁
        private float fadeFrom;
        private float fadeElapsed = FADE_SECONDS;
        private readonly List<Lane> lanes = new List<Lane>();
        private readonly Stack<DanmakuItem> pool = new Stack<DanmakuItem>();
        private readonly List<PreparedDanmakuContent> preparedContents = new List<PreparedDanmakuContent>();
        private int globalContentCursor;
        private float viewWidth;
        private float viewHeight;
        private bool built;
        private int allocatedItemCount;

        /// <summary>
        /// 在指定父节点下创建弹幕层，并插入到 siblingBefore 之前。
        /// 常用场景是把它挂到 WishFountainView 根节点下，再插到主面板节点之前，
        /// 从而实现“遮罩之上、面板之下”的层级关系。
        /// </summary>
        public static WishFountainDanmakuView CreateRuntime(Transform parent, Transform siblingBefore)
        {
            if (parent == null)
            {
                return null;
            }

            GameObject host = new GameObject("BossRush_WishDanmakuLayer", typeof(RectTransform), typeof(CanvasGroup));
            host.transform.SetParent(parent, false);

            RectTransform hostRect = host.GetComponent<RectTransform>();
            hostRect.anchorMin = Vector2.zero;
            hostRect.anchorMax = Vector2.one;
            hostRect.offsetMin = Vector2.zero;
            hostRect.offsetMax = Vector2.zero;

            CanvasGroup cg = host.GetComponent<CanvasGroup>();
            cg.blocksRaycasts = false;
            cg.interactable = false;
            cg.alpha = 0f;   // 第一次 Show 从 0 淡入

            WishFountainDanmakuView view = host.AddComponent<WishFountainDanmakuView>();
            view.selfRect = hostRect;
            view.canvasGroup = cg;

            // 层级：插到主面板之前，使其渲染在面板下方
            if (siblingBefore != null)
            {
                int targetIndex = siblingBefore.GetSiblingIndex();
                host.transform.SetSiblingIndex(targetIndex);
            }

            host.SetActive(false);
            return view;
        }

        private void Awake()
        {
            if (selfRect == null)
            {
                selfRect = GetComponent<RectTransform>();
            }

            if (canvasGroup == null)
            {
                canvasGroup = GetComponent<CanvasGroup>();
            }

            font = ZombieModeUIHelper.GetGameFont();
            if (font == null)
            {
                font = TMP_Settings.defaultFontAsset;
            }
        }

        /// <summary>
        /// 用一批心愿内容启动弹幕。内容为空时不显示。每次打开面板都从 0 淡入。
        /// </summary>
        public void Show(List<string> wishContents)
        {
            if (canvasGroup != null)
            {
                canvasGroup.alpha = 0f;
            }
            ApplyContents(wishContents, false);
        }

        /// <summary>
        /// 在保持当前滚动状态的前提下更新后续弹幕来源，
        /// 让新数据无缝接管后续入场，不强制整层重置。
        /// </summary>
        public void UpdateContents(List<string> wishContents, bool preservePlayback)
        {
            ApplyContents(wishContents, preservePlayback);
        }

        public void Hide()
        {
            RecycleAllActiveItems();
            if (canvasGroup != null)
            {
                canvasGroup.alpha = 0f;   // 下次出现重新淡入
            }
            fadeElapsed = FADE_SECONDS;
            if (gameObject != null)
            {
                gameObject.SetActive(false);
            }
        }

        public void WarmupPoolImmediate()
        {
            EnsureLayout();
            EnsurePoolCapacity(Mathf.Min(EstimateRecommendedPoolSize(), POOL_WARMUP_IMMEDIATE_COUNT));
        }

        public IEnumerator WarmupPoolIncrementally()
        {
            EnsureLayout();

            int target = EstimateRecommendedPoolSize();
            while (this != null && allocatedItemCount < target)
            {
                EnsurePoolCapacity(Mathf.Min(target, allocatedItemCount + POOL_WARMUP_BATCH_SIZE));
                yield return null;
            }
        }

        private void ApplyContents(List<string> wishContents, bool preservePlayback)
        {
            if (wishContents == null || wishContents.Count == 0)
            {
                Hide();
                return;
            }

            gameObject.SetActive(true);

            EnsureLayout();
            EnsurePoolCapacity(EstimateRecommendedPoolSize());
            PrepareContents(wishContents);
            if (preparedContents.Count == 0)
            {
                Hide();
                return;
            }

            BeginFadeIn();
            globalContentCursor = 0;
            if (!preservePlayback || !HasActiveItems())
            {
                ResetLanes();
                return;
            }

            ReseedLaneSources();
        }

        private void EnsureLayout()
        {
            if (selfRect == null)
            {
                return;
            }

            viewWidth = selfRect.rect.width;
            viewHeight = selfRect.rect.height;

            if (viewWidth <= 0f)
            {
                viewWidth = Screen.width;
            }
            if (viewHeight <= 0f)
            {
                viewHeight = Screen.height;
            }

            int lanesPerBand = ComputeLanesPerBand(viewHeight);
            int laneCount = lanesPerBand * 2;

            // 首次、分辨率变化或泳道数变化时都重建中心线，避免尺寸变化后弹幕垂直位置漂移。
            if (!built || lanes.Count != laneCount)
            {
                RecycleAllActiveItems();
                lanes.Clear();
            }

            // 上带从屏幕顶往下排、下带从屏幕底往上排；中间 50% 是许愿面板，不放弹幕（UD-22）
            float laneHeight = viewHeight * BAND_HEIGHT_RATIO / lanesPerBand;
            float topY = viewHeight * 0.5f;
            for (int i = 0; i < laneCount; i++)
            {
                Lane lane;
                if (i < lanes.Count)
                {
                    lane = lanes[i];
                }
                else
                {
                    lane = new Lane();
                    lanes.Add(lane);
                }

                // 泳道中心 Y：以中心为原点的坐标系（父节点 pivot 0.5）
                int band = i / lanesPerBand;
                int slot = i % lanesPerBand;
                lane.centerY = band == 0
                    ? topY - laneHeight * (slot + 0.5f)
                    : -topY + laneHeight * (slot + 0.5f);
                // 相邻泳道错开景深层，远近交替
                lane.depth = (slot + band) % DepthFontSizes.Length;
            }

            built = true;
        }

        private static int ComputeLanesPerBand(float height)
        {
            return Mathf.Clamp(
                Mathf.FloorToInt(Mathf.Max(height, 1f) * BAND_HEIGHT_RATIO / LANE_TARGET_HEIGHT),
                MIN_LANES_PER_BAND,
                MAX_LANES_PER_BAND);
        }

        private void BeginFadeIn()
        {
            if (canvasGroup == null)
            {
                return;
            }

            fadeFrom = canvasGroup.alpha;
            fadeElapsed = fadeFrom >= 0.999f ? FADE_SECONDS : 0f;
        }

        private void AdvanceFade()
        {
            if (fadeElapsed >= FADE_SECONDS || canvasGroup == null)
            {
                return;
            }

            fadeElapsed += Time.unscaledDeltaTime;
            canvasGroup.alpha = Mathf.Lerp(fadeFrom, 1f, BossRushUI.SmoothStep(fadeElapsed / FADE_SECONDS));
        }

        private void ResetLanes()
        {
            RecycleAllActiveItems();

            if (lanes.Count == 0 || preparedContents.Count == 0)
            {
                return;
            }

            float leftEdge = -viewWidth * 0.5f;
            float rightEdge = viewWidth * 0.5f;

            // 给每条泳道分配一个错开的初始内容索引与初始间距，让弹幕不整齐划一
            for (int i = 0; i < lanes.Count; i++)
            {
                Lane lane = lanes[i];
                lane.items.Clear();
                lane.nextContentIndex = NextGlobalContentIndex();
                PrefillLane(lane, leftEdge, rightEdge);
                lane.nextSpawnGap = UnityEngine.Random.Range(SPAWN_GAP_MIN, SPAWN_GAP_MAX);
            }
        }

        private bool HasActiveItems()
        {
            for (int i = 0; i < lanes.Count; i++)
            {
                if (lanes[i].items.Count > 0)
                {
                    return true;
                }
            }

            return false;
        }

        private void ReseedLaneSources()
        {
            float leftEdge = -viewWidth * 0.5f;
            float rightEdge = viewWidth * 0.5f;

            for (int i = 0; i < lanes.Count; i++)
            {
                Lane lane = lanes[i];
                lane.nextContentIndex = NextGlobalContentIndex();
                lane.nextSpawnGap = UnityEngine.Random.Range(SPAWN_GAP_MIN, SPAWN_GAP_MAX);

                if (lane.items.Count == 0)
                {
                    PrefillLane(lane, leftEdge, rightEdge);
                }
            }
        }

        private int NextGlobalContentIndex()
        {
            if (preparedContents.Count == 0)
            {
                return 0;
            }

            int idx = globalContentCursor % preparedContents.Count;
            globalContentCursor++;
            return idx;
        }

        private void PrepareContents(List<string> wishContents)
        {
            preparedContents.Clear();
            if (wishContents == null || wishContents.Count == 0)
            {
                return;
            }

            DanmakuItem measureItem = AcquireItem();
            try
            {
                // 统一按基准字号测一次宽，各景深层按字号比例缩放（滚动阶段不再测宽）
                measureItem.text.fontSize = BASE_FONT_SIZE;
                for (int i = 0; i < wishContents.Count; i++)
                {
                    string display = FormatContent(wishContents[i]);
                    if (string.IsNullOrEmpty(display))
                    {
                        continue;
                    }

                    measureItem.text.text = display;
                    float preferred = measureItem.text.GetPreferredValues(display).x;
                    preparedContents.Add(new PreparedDanmakuContent
                    {
                        text = display,
                        width = Mathf.Max(preferred, 1f)
                    });
                }
            }
            finally
            {
                ReleaseItem(measureItem);
            }
        }

        private void Update()
        {
            // 许愿面板是官方 View，暂停菜单一般盖不上来；保险起见仍按共享口径停推进
            if (BossRushUI.IsGamePaused())
            {
                return;
            }

            AdvanceFade();

            if (preparedContents.Count == 0 || lanes.Count == 0)
            {
                return;
            }

            // 分辨率变化时重建布局
            if (selfRect != null &&
                (!Mathf.Approximately(selfRect.rect.width, viewWidth) ||
                 !Mathf.Approximately(selfRect.rect.height, viewHeight)))
            {
                EnsureLayout();
                ResetLanes();
                return;
            }

            float deltaTime = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            float rightEdge = viewWidth * 0.5f;
            float leftEdge = -viewWidth * 0.5f;

            for (int i = 0; i < lanes.Count; i++)
            {
                Lane lane = lanes[i];
                UpdateLane(lane, DepthSpeeds[lane.depth] * deltaTime, rightEdge, leftEdge);
            }
        }

        private void UpdateLane(Lane lane, float delta, float rightEdge, float leftEdge)
        {
            // 1. 移动并回收已出屏的项
            for (int i = lane.items.Count - 1; i >= 0; i--)
            {
                DanmakuItem item = lane.items[i];
                if (item == null || item.rect == null)
                {
                    lane.items.RemoveAt(i);
                    continue;
                }

                Vector2 pos = item.rect.anchoredPosition;
                pos.x -= delta;
                item.rect.anchoredPosition = pos;

                // 完全移出左边界（右边缘 < 左屏边）→ 回收
                if (pos.x + item.width * 0.5f < leftEdge)
                {
                    lane.items.RemoveAt(i);
                    ReleaseItem(item);
                }
            }

            // 2. 判断是否需要在右侧生成新弹幕
            bool needSpawn;
            if (lane.items.Count == 0)
            {
                needSpawn = true;
            }
            else
            {
                DanmakuItem last = lane.items[lane.items.Count - 1];
                // 最后一条的右边缘进入屏幕 + 满足间距要求后再放下一条
                float lastRightEdgeX = last.rect.anchoredPosition.x + last.width * 0.5f;
                needSpawn = lastRightEdgeX <= rightEdge - lane.nextSpawnGap;
            }

            int spawnCount = 0;
            while (needSpawn && spawnCount < MAX_SPAWNS_PER_LANE_PER_FRAME)
            {
                SpawnItemInLane(lane, rightEdge);
                lane.nextSpawnGap = UnityEngine.Random.Range(SPAWN_GAP_MIN, SPAWN_GAP_MAX);

                spawnCount++;
                if (lane.items.Count == 0)
                {
                    needSpawn = true;
                    continue;
                }

                DanmakuItem last = lane.items[lane.items.Count - 1];
                float lastRightEdgeX = last.rect.anchoredPosition.x + last.width * 0.5f;
                needSpawn = lastRightEdgeX <= rightEdge - lane.nextSpawnGap;
            }
        }

        private void PrefillLane(Lane lane, float leftEdge, float rightEdge)
        {
            float cursor = leftEdge - UnityEngine.Random.Range(0f, viewWidth * 0.12f);
            float fillTarget = rightEdge + UnityEngine.Random.Range(viewWidth * 0.1f, viewWidth * 0.25f);

            while (cursor < fillTarget)
            {
                DanmakuItem item = CreateItemForLane(lane);
                if (item == null)
                {
                    break;
                }

                item.rect.anchoredPosition = new Vector2(cursor + item.width * 0.5f, lane.centerY);
                lane.items.Add(item);
                cursor += item.width + UnityEngine.Random.Range(SPAWN_GAP_MIN, SPAWN_GAP_MAX);
            }
        }

        private void SpawnItemInLane(Lane lane, float rightEdge)
        {
            DanmakuItem item = CreateItemForLane(lane);
            if (item == null)
            {
                return;
            }

            // 从右边界外侧入场
            float startX = rightEdge + item.width * 0.5f;
            item.rect.anchoredPosition = new Vector2(startX, lane.centerY);

            lane.items.Add(item);
        }

        private DanmakuItem CreateItemForLane(Lane lane)
        {
            if (preparedContents.Count == 0)
            {
                return null;
            }

            int contentIndex = lane.nextContentIndex;
            lane.nextContentIndex = NextGlobalContentIndex();

            PreparedDanmakuContent content = preparedContents[contentIndex % preparedContents.Count];
            if (content == null || string.IsNullOrEmpty(content.text))
            {
                return null;
            }

            float fontSize = DepthFontSizes[lane.depth];
            Color color = DanmakuColors[UnityEngine.Random.Range(0, DanmakuColors.Length)];
            color.a = DepthAlphas[lane.depth];

            DanmakuItem item = AcquireItem();
            item.text.fontSize = fontSize;
            item.text.text = content.text;
            item.text.color = color;
            item.width = Mathf.Max(content.width * (fontSize / BASE_FONT_SIZE) + SIDE_PADDING, 40f);
            item.rect.sizeDelta = new Vector2(item.width, ITEM_HEIGHT);
            return item;
        }

        private string FormatContent(string raw)
        {
            if (string.IsNullOrEmpty(raw))
            {
                return null;
            }

            // 弹幕单行展示：换行折叠为空格
            string oneLine = raw.Replace('\n', ' ').Replace('\r', ' ').Trim();
            if (oneLine.Length == 0)
            {
                return null;
            }

            if (oneLine.Length > MAX_CONTENT_LENGTH)
            {
                oneLine = oneLine.Substring(0, MAX_CONTENT_LENGTH) + "…";
            }

            return oneLine;
        }

        private int EstimateRecommendedPoolSize()
        {
            float width = viewWidth > 0f ? viewWidth : (selfRect != null && selfRect.rect.width > 0f ? selfRect.rect.width : Screen.width);
            float height = viewHeight > 0f ? viewHeight : (selfRect != null && selfRect.rect.height > 0f ? selfRect.rect.height : Screen.height);

            int laneCount = ComputeLanesPerBand(height) * 2;
            float averageGap = (SPAWN_GAP_MIN + SPAWN_GAP_MAX) * 0.5f;
            float pitch = Mathf.Max(ESTIMATED_AVERAGE_ITEM_WIDTH + averageGap, 1f);
            float coverageWidth = Mathf.Max(width, 1f) * ESTIMATED_PREFILL_WIDTH_RATIO;
            int itemsPerLane = Mathf.Clamp(Mathf.CeilToInt(coverageWidth / pitch) + 1, 2, MAX_ESTIMATED_ITEMS_PER_LANE);
            return laneCount * itemsPerLane;
        }

        private void EnsurePoolCapacity(int targetCount)
        {
            while (allocatedItemCount < targetCount)
            {
                DanmakuItem item = CreateNewItem();
                ReleaseItem(item);
            }
        }

        // ==== 对象池 ====

        private DanmakuItem AcquireItem()
        {
            DanmakuItem item;
            if (pool.Count > 0)
            {
                item = pool.Pop();
                item.rect.gameObject.SetActive(true);
                item.active = true;
                return item;
            }

            return CreateNewItem();
        }

        private DanmakuItem CreateNewItem()
        {
            GameObject go = new GameObject("Danmaku", typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(selfRect, false);

            RectTransform rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);

            TextMeshProUGUI text = go.GetComponent<TextMeshProUGUI>();
            if (font != null)
            {
                text.font = font;
            }
            // 字底走 TMP 距离场的 Underlay（UI.Shadow 挂在 TMP 上不起作用）；整池共用一份材质
            Material material = GetTextMaterial();
            if (material != null)
            {
                text.fontSharedMaterial = material;
            }
            text.fontSize = BASE_FONT_SIZE;
            text.enableAutoSizing = false;
            text.alignment = TextAlignmentOptions.Left;
            text.richText = false;
            text.enableWordWrapping = false;
            text.overflowMode = TextOverflowModes.Overflow;
            text.raycastTarget = false;

            DanmakuItem item = new DanmakuItem
            {
                rect = rect,
                text = text,
                active = true
            };
            allocatedItemCount++;
            return item;
        }

        private void ReleaseItem(DanmakuItem item)
        {
            if (item == null)
            {
                return;
            }

            item.active = false;
            if (item.rect != null)
            {
                item.rect.gameObject.SetActive(false);
            }
            pool.Push(item);
        }

        private void RecycleAllActiveItems()
        {
            for (int i = 0; i < lanes.Count; i++)
            {
                Lane lane = lanes[i];
                for (int j = 0; j < lane.items.Count; j++)
                {
                    ReleaseItem(lane.items[j]);
                }
                lane.items.Clear();
            }
        }

        /// <summary>
        /// 弹幕字底材质：在字体材质上打开 UNDERLAY_ON（柔和黑色底影），整池共用一份，OnDestroy 销毁。
        /// 字体取不到时返回 null，文本保持字体默认材质。
        /// </summary>
        private Material GetTextMaterial()
        {
            if (textMaterial != null)
            {
                return textMaterial;
            }

            if (font == null || font.material == null)
            {
                return null;
            }

            textMaterial = new Material(font.material);
            textMaterial.name = font.material.name + " (BossRush Wish Danmaku)";
            textMaterial.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                textMaterial.EnableKeyword("UNDERLAY_ON");
                textMaterial.SetColor(ShaderUtilities.ID_UnderlayColor, new Color(0f, 0f, 0f, 0.6f));
                textMaterial.SetFloat(ShaderUtilities.ID_UnderlayOffsetX, 0f);
                textMaterial.SetFloat(ShaderUtilities.ID_UnderlayOffsetY, -0.6f);
                textMaterial.SetFloat(ShaderUtilities.ID_UnderlaySoftness, 0.4f);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[WishDanmaku] [WARNING] 字底材质参数写入失败: " + e.Message);
            }
            return textMaterial;
        }

        private void OnDestroy()
        {
            lanes.Clear();
            pool.Clear();
            preparedContents.Clear();
            allocatedItemCount = 0;
            if (textMaterial != null)
            {
                Destroy(textMaterial);
                textMaterial = null;
            }
        }
    }
}
