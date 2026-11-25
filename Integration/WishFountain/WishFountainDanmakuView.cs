// ============================================================================
// WishFountainDanmakuView.cs - 许愿台星灯层（原全屏弹幕）
// ============================================================================
// 模块说明：
//   打开许愿面板时，在面板「下方」、遮罩「上方」放一层星灯：
//   飞书里已保存的心愿写在一盏盏纸灯上，灯里有火，从屏幕底部缓缓升起、轻轻摇摆，到顶淡去。
//   （2026-11-24 玩家许愿第 3 项：横向飞字太吵，改成水墨东方的星灯；类名与公开入口保持不变。）
//
//   层级关系（从下到上）：
//     许愿面板的半透明遮罩(overlay) → 本星灯层 → 许愿面板内容
//   本层作为 WishFountainView 的兄弟节点插入，且始终位于面板之前，
//   由 WishFountainView 在 OnOpen 时 SetAsLastSibling 保证面板盖在星灯之上。
//
// 观感：
//   - 灯只在面板左右两侧的空处升（面板挡住的中间不放），左右按当前灯数平衡。
//   - 三层景深：远 / 中 / 近 = 缩放 0.62 / 0.8 / 1、不透明度 0.45 / 0.7 / 0.95、上升 22 / 32 / 44 像素每秒。
//     同一景深的灯同速，不会追尾；生成时避开同层已有的灯。
//   - 灯身：崩边墨块染成被火映暖的纸色 + 墨笔边 + 上下两道灯口墨笔；灯里一团暖光，底部火苗明灭；
//     灯外一圈暖晕。字是深墨色，字底用 TMP Underlay 打一层淡暖光（UI.Shadow 对 TMP 不起作用）。
//   - 底部淡入、顶部淡出；整层 0.6 秒 SmoothStep 淡入，收起时随面板根节点一起淡出。
//   - 同屏最多 12 盏，每盏最多三行（约 21 个汉字或 40 个西文字符）。
//
// 性能：
//   - 本层自带一个嵌套 Canvas：灯每帧移动只重排本层的批次，不牵动面板与输入框。
//   - 对象池：灯复用，不频繁 new / Destroy；活动灯数 ≤ 12。
//   - 文本预处理：Show 时一次性清洗去重（WishFountainService.FilterLanternWishes）、截断、测高，
//     上升阶段不再格式化 / 测量；每帧只做位移、摆动、透明度与火苗，无网络请求、无 GC 分配。
//   - raycastTarget 全部关闭，不拦截输入；官方暂停菜单开着时冻结。
//   - 面板右上角「星灯」开关熄了以后，WishFountainView 不调用本层，也不拉取心愿。
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
        // ==== 尺寸与节奏 ====
        private const int MAX_LANTERNS = 12;
        private const int MAX_CHARS_CJK = 21;            // 三行 × 约 7 个汉字
        private const int MAX_CHARS_LATIN = 40;
        private const float BASE_FONT_SIZE = 17f;
        private const float TEXT_WIDTH = 124f;
        private const float LANTERN_WIDTH = 156f;
        private const float LANTERN_MIN_HEIGHT = 92f;
        private const float LANTERN_PADDING_Y = 44f;     // 上下灯口与留白
        private const float MAX_TEXT_HEIGHT = 70f;       // 三行
        private const float FADE_SECONDS = 0.6f;         // 整层淡入
        private const float PANEL_GAP = 36f;             // 灯与许愿面板左右留空
        private const float SCREEN_MARGIN = 20f;
        private const float FADE_IN_TRAVEL = 0.12f;      // 升起前 12% 行程淡入
        private const float FADE_OUT_TRAVEL = 0.2f;      // 最后 20% 行程淡出
        private const float SPAWN_JITTER = 0.35f;
        private const int POOL_WARMUP_IMMEDIATE_COUNT = 4;
        private const int POOL_WARMUP_BATCH_SIZE = 2;

        // 三层景深（远 / 中 / 近）
        private static readonly float[] DepthScales = { 0.62f, 0.8f, 1f };
        private static readonly float[] DepthAlphas = { 0.45f, 0.7f, 0.95f };
        private static readonly float[] DepthSpeeds = { 22f, 32f, 44f };
        private static readonly float[] DepthWeights = { 0.4f, 0.35f, 0.25f };

        private static readonly Color LitPaper = new Color(1f, 0.87f, 0.64f, 0.94f);      // 被火映暖的灯纸
        private static readonly Color InnerLight = new Color(1f, 0.76f, 0.40f, 0.42f);
        private static readonly Color RimInk = new Color(0.16f, 0.10f, 0.06f, 0.9f);
        private static readonly Color FrameInk = new Color(0.22f, 0.13f, 0.07f, 0.75f);
        private static readonly Color TextInk = new Color(0.20f, 0.12f, 0.07f, 1f);
        private static readonly Color FlameColor = new Color(1f, 0.80f, 0.42f, 1f);
        private static readonly Color FlameCore = new Color(1f, 0.97f, 0.85f, 1f);

        private sealed class Lantern
        {
            public RectTransform rect;
            public CanvasGroup group;
            public RectTransform body;
            public Image halo;
            public Image glow;
            public Image flame;
            public Image core;
            public RectTransform topRim;
            public RectTransform bottomRim;
            public TextMeshProUGUI text;
            public int depth;
            public int contentIndex;
            public float baseX;
            public float y;
            public float height;
            public float swayAmplitude;
            public float swayFrequency;
            public float phase;
            public bool active;
        }

        private sealed class PreparedLantern
        {
            public string text;
            public float height;
        }

        private RectTransform selfRect;
        private RectTransform panelRect;
        private CanvasGroup canvasGroup;
        private TMP_FontAsset font;
        private Material textMaterial;      // 星灯共用的字底材质实例，OnDestroy 销毁
        private readonly RectTransform[] depthLayers = new RectTransform[3];
        private float fadeFrom;
        private float fadeElapsed = FADE_SECONDS;
        private readonly List<Lantern> activeLanterns = new List<Lantern>();
        private readonly Stack<Lantern> pool = new Stack<Lantern>();
        private readonly List<PreparedLantern> preparedContents = new List<PreparedLantern>();
        private readonly HashSet<int> activeContentIndices = new HashSet<int>();
        private int contentCursor;
        private float spawnTimer;
        private float clock;
        private int allocatedCount;
        private System.Random random = new System.Random();

        /// <summary>
        /// 在指定父节点下创建星灯层，并插入到 siblingBefore 之前。
        /// 常用场景是把它挂到 WishFountainView 根节点下，再插到主面板节点之前，
        /// 从而实现“遮罩之上、面板之下”的层级关系。siblingBefore 同时用来算面板两侧的空处。
        /// </summary>
        public static WishFountainDanmakuView CreateRuntime(Transform parent, Transform siblingBefore)
        {
            if (parent == null)
            {
                return null;
            }

            GameObject host = new GameObject("BossRush_WishLanternLayer", typeof(RectTransform), typeof(CanvasGroup), typeof(Canvas));
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
            view.panelRect = siblingBefore as RectTransform;
            for (int i = 0; i < view.depthLayers.Length; i++)
            {
                GameObject layer = ZombieModeUIHelper.CreateRect("Depth" + i, host.transform, new Vector2(0.5f, 0.5f), Vector2.zero);
                view.depthLayers[i] = layer.GetComponent<RectTransform>();   // 远的先建，渲染在下
            }

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
        /// 用一批心愿内容放星灯。内容为空（或清洗后为空）时不显示。每次打开面板都从 0 淡入。
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
        /// 在保持当前星灯的前提下更新后续来源：已经升起的灯照常升完，新灯写新内容。
        /// </summary>
        public void UpdateContents(List<string> wishContents, bool preservePlayback)
        {
            ApplyContents(wishContents, preservePlayback);
        }

        public void Hide()
        {
            RecycleAll();
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
            EnsurePoolCapacity(POOL_WARMUP_IMMEDIATE_COUNT);
        }

        public IEnumerator WarmupPoolIncrementally()
        {
            while (this != null && allocatedCount < MAX_LANTERNS)
            {
                EnsurePoolCapacity(Mathf.Min(MAX_LANTERNS, allocatedCount + POOL_WARMUP_BATCH_SIZE));
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
            PrepareContents(wishContents);
            if (preparedContents.Count == 0)
            {
                Hide();
                return;
            }

            contentCursor = random.Next(preparedContents.Count);
            if (preservePlayback && activeLanterns.Count > 0)
            {
                // 已升起的灯照常升完：按字重新登记到新列表的下标，免得新灯又写出同一句
                activeContentIndices.Clear();
                for (int i = 0; i < activeLanterns.Count; i++)
                {
                    string shown = activeLanterns[i].text.text;
                    int index = preparedContents.FindIndex(p => p.text == shown);
                    activeLanterns[i].contentIndex = index;
                    if (index >= 0) activeContentIndices.Add(index);
                }
                return;
            }

            BeginFadeIn();
            RecycleAll();
            Prefill();
        }

        // ==== 内容预处理 ====

        private void PrepareContents(List<string> wishContents)
        {
            preparedContents.Clear();
            List<string> cleaned = WishFountainService.FilterLanternWishes(wishContents);
            if (cleaned.Count == 0)
            {
                return;
            }

            Lantern measure = AcquireLantern(0);
            try
            {
                measure.text.fontSize = BASE_FONT_SIZE;
                for (int i = 0; i < cleaned.Count; i++)
                {
                    string display = FormatContent(cleaned[i]);
                    if (string.IsNullOrEmpty(display))
                    {
                        continue;
                    }

                    float textHeight = measure.text.GetPreferredValues(display, TEXT_WIDTH, 0f).y;
                    preparedContents.Add(new PreparedLantern
                    {
                        text = display,
                        height = Mathf.Max(LANTERN_MIN_HEIGHT, Mathf.Min(textHeight, MAX_TEXT_HEIGHT) + LANTERN_PADDING_Y)
                    });
                }
            }
            finally
            {
                ReleaseLantern(measure);
            }
        }

        private static string FormatContent(string cleaned)
        {
            if (string.IsNullOrEmpty(cleaned))
            {
                return null;
            }

            int latin = 0;
            for (int i = 0; i < cleaned.Length; i++)
            {
                if (cleaned[i] < 0x2E80) latin++;
            }
            int limit = latin * 2 > cleaned.Length ? MAX_CHARS_LATIN : MAX_CHARS_CJK;
            return cleaned.Length > limit ? cleaned.Substring(0, limit) + "…" : cleaned;
        }

        // ==== 每帧 ====

        private void Update()
        {
            // 许愿面板是官方 View，暂停菜单一般盖不上来；保险起见仍按共享口径停推进
            if (BossRushUI.IsGamePaused())
            {
                return;
            }

            AdvanceFade();
            if (preparedContents.Count == 0 && activeLanterns.Count == 0)
            {
                return;
            }

            float deltaTime = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            clock += deltaTime;
            float top = ViewHeight() * 0.5f;
            float bottom = -top;

            for (int i = activeLanterns.Count - 1; i >= 0; i--)
            {
                Lantern lantern = activeLanterns[i];
                lantern.y += DepthSpeeds[lantern.depth] * deltaTime;
                float half = lantern.height * DepthScales[lantern.depth] * 0.5f;
                if (lantern.y - half > top)
                {
                    activeLanterns.RemoveAt(i);
                    RetireLantern(lantern);
                    continue;
                }
                ApplyLanternMotion(lantern, bottom - half, top + half);
            }

            if (preparedContents.Count == 0)
            {
                return;
            }

            spawnTimer -= deltaTime;
            if (spawnTimer <= 0f)
            {
                if (activeLanterns.Count < TargetCount())
                {
                    SpawnLantern(-1f);
                }
                spawnTimer = NextSpawnInterval();
            }
        }

        private void ApplyLanternMotion(Lantern lantern, float startY, float endY)
        {
            float t = clock * lantern.swayFrequency + lantern.phase;
            float sway = Mathf.Sin(t);
            lantern.rect.anchoredPosition = new Vector2(lantern.baseX + sway * lantern.swayAmplitude, lantern.y);
            lantern.rect.localRotation = Quaternion.Euler(0f, 0f, -Mathf.Cos(t) * 2.6f);

            float travel = Mathf.InverseLerp(startY, endY, lantern.y);
            float fade = Mathf.Min(Mathf.Clamp01(travel / FADE_IN_TRAVEL), Mathf.Clamp01((1f - travel) / FADE_OUT_TRAVEL));
            lantern.group.alpha = DepthAlphas[lantern.depth] * BossRushUI.SmoothStep(fade);

            float flicker = 0.82f + 0.12f * Mathf.Sin(clock * 7.3f + lantern.phase * 3f) + 0.06f * Mathf.Sin(clock * 13.1f + lantern.phase);
            lantern.flame.color = new Color(FlameColor.r, FlameColor.g, FlameColor.b, flicker);
            lantern.core.color = new Color(FlameCore.r, FlameCore.g, FlameCore.b, 0.55f + 0.35f * flicker);
            lantern.flame.rectTransform.localScale = new Vector3(1f, 0.9f + 0.18f * flicker, 1f);
            lantern.glow.color = new Color(InnerLight.r, InnerLight.g, InnerLight.b, InnerLight.a * (0.85f + 0.15f * flicker));
            lantern.halo.color = new Color(1f, 0.62f, 0.28f, 0.26f + 0.08f * flicker);
        }

        private void BeginFadeIn()
        {
            fadeFrom = canvasGroup != null ? canvasGroup.alpha : 0f;
            fadeElapsed = 0f;
        }

        private void AdvanceFade()
        {
            if (canvasGroup == null || fadeElapsed >= FADE_SECONDS)
            {
                return;
            }

            fadeElapsed += Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            canvasGroup.alpha = Mathf.Lerp(fadeFrom, 1f, BossRushUI.SmoothStep(Mathf.Clamp01(fadeElapsed / FADE_SECONDS)));
        }

        // ==== 生成 ====

        private int TargetCount()
        {
            return Mathf.Min(MAX_LANTERNS, preparedContents.Count);
        }

        private float NextSpawnInterval()
        {
            float meanLife = (ViewHeight() + LANTERN_MIN_HEIGHT) / DepthSpeeds[1];
            float interval = meanLife / Mathf.Max(1, TargetCount());
            return interval * (1f + SPAWN_JITTER * ((float)random.NextDouble() * 2f - 1f));
        }

        /// <summary>打开时按目标数的七成预铺：灯散在不同高度，不用等半分钟才有灯。</summary>
        private void Prefill()
        {
            int count = Mathf.RoundToInt(TargetCount() * 0.7f);
            for (int i = 0; i < count; i++)
            {
                float slot = (i + (float)random.NextDouble()) / Mathf.Max(1, count);
                SpawnLantern(Mathf.Lerp(0.1f, 0.85f, slot));
            }
            spawnTimer = NextSpawnInterval() * 0.5f;
        }

        /// <param name="travel">预铺时的起始行程（0 底 – 1 顶）；负数表示从屏幕底下升起。</param>
        private void SpawnLantern(float travel)
        {
            int contentIndex = NextContentIndex();
            if (contentIndex < 0)
            {
                return;
            }

            int depth = PickDepth();
            Lantern lantern = AcquireLantern(depth);
            PreparedLantern content = preparedContents[contentIndex];
            lantern.contentIndex = contentIndex;
            lantern.height = content.height;
            lantern.text.text = content.text;
            LayoutLantern(lantern);

            float scale = DepthScales[depth];
            float half = lantern.height * scale * 0.5f;
            float top = ViewHeight() * 0.5f;
            float startY = -top - half;
            lantern.y = travel < 0f ? startY : Mathf.Lerp(startY, top + half, travel);
            lantern.baseX = PickX(lantern, scale);
            lantern.swayAmplitude = Mathf.Lerp(8f, 16f, (float)random.NextDouble()) * scale;
            lantern.swayFrequency = Mathf.Lerp(0.35f, 0.6f, (float)random.NextDouble());
            lantern.phase = (float)random.NextDouble() * Mathf.PI * 2f;
            lantern.rect.localScale = new Vector3(scale, scale, 1f);

            activeLanterns.Add(lantern);
            activeContentIndices.Add(contentIndex);
            ApplyLanternMotion(lantern, startY, top + half);
        }

        private int NextContentIndex()
        {
            int count = preparedContents.Count;
            for (int step = 0; step < count; step++)
            {
                int index = (contentCursor + step) % count;
                if (!activeContentIndices.Contains(index))
                {
                    contentCursor = index + 1;
                    return index;
                }
            }
            return -1;   // 每条心愿都已经挂在一盏灯上
        }

        private int PickDepth()
        {
            float roll = (float)random.NextDouble();
            for (int i = 0; i < DepthWeights.Length; i++)
            {
                roll -= DepthWeights[i];
                if (roll <= 0f) return i;
            }
            return DepthWeights.Length - 1;
        }

        /// <summary>挑面板左边或右边的空处（灯少的一侧优先），同一景深尽量不叠在一起。</summary>
        private float PickX(Lantern lantern, float scale)
        {
            float viewHalf = ViewWidth() * 0.5f;
            float panelHalf = panelRect != null && panelRect.rect.width > 0f ? panelRect.rect.width * 0.5f : viewHalf * 0.55f;
            float halfWidth = LANTERN_WIDTH * scale * 0.5f;
            float inner = panelHalf + PANEL_GAP + halfWidth;
            float outer = viewHalf - SCREEN_MARGIN - halfWidth;
            bool hasColumns = outer > inner;

            int left = 0;
            for (int i = 0; i < activeLanterns.Count; i++)
            {
                if (activeLanterns[i].baseX < 0f) left++;
            }
            float preferLeft = left * 2 < activeLanterns.Count ? 0.7f : (left * 2 > activeLanterns.Count ? 0.3f : 0.5f);

            float best = 0f;
            float bestClearance = float.MinValue;
            for (int attempt = 0; attempt < 5; attempt++)
            {
                float x;
                if (hasColumns)
                {
                    float side = random.NextDouble() < preferLeft ? -1f : 1f;
                    x = side * Mathf.Lerp(inner, outer, (float)random.NextDouble());
                }
                else
                {
                    x = Mathf.Lerp(-viewHalf + halfWidth, viewHalf - halfWidth, (float)random.NextDouble());
                }

                float clearance = Clearance(lantern, x, scale);
                if (clearance > bestClearance)
                {
                    bestClearance = clearance;
                    best = x;
                }
                if (clearance >= 0f) break;
            }
            return best;
        }

        /// <summary>与同景深已有灯的最小间隙（负数表示会叠上）。</summary>
        private float Clearance(Lantern candidate, float x, float scale)
        {
            float min = float.MaxValue;
            for (int i = 0; i < activeLanterns.Count; i++)
            {
                Lantern other = activeLanterns[i];
                if (other.depth != candidate.depth) continue;
                float dx = Mathf.Abs(other.baseX - x) - LANTERN_WIDTH * scale - 24f;
                float dy = Mathf.Abs(other.y - candidate.y) - (other.height + candidate.height) * scale * 0.5f - 12f;
                min = Mathf.Min(min, Mathf.Max(dx, dy));
            }
            return min;
        }

        private float ViewWidth()
        {
            return selfRect != null && selfRect.rect.width > 0f ? selfRect.rect.width : Screen.width;
        }

        private float ViewHeight()
        {
            return selfRect != null && selfRect.rect.height > 0f ? selfRect.rect.height : Screen.height;
        }

        // ==== 灯的外形 ====

        private static void LayoutLantern(Lantern lantern)
        {
            float h = lantern.height;
            lantern.rect.sizeDelta = new Vector2(LANTERN_WIDTH, h);
            lantern.halo.rectTransform.sizeDelta = new Vector2(LANTERN_WIDTH * 1.9f, h * 1.75f);
            lantern.glow.rectTransform.sizeDelta = new Vector2(LANTERN_WIDTH * 0.95f, h * 0.9f);
            lantern.glow.rectTransform.anchoredPosition = new Vector2(0f, -h * 0.08f);
            lantern.topRim.anchoredPosition = new Vector2(0f, h * 0.5f - 6f);
            lantern.bottomRim.anchoredPosition = new Vector2(0f, -h * 0.5f + 6f);
            lantern.flame.rectTransform.anchoredPosition = new Vector2(0f, -h * 0.5f + 16f);
            lantern.core.rectTransform.anchoredPosition = new Vector2(0f, -h * 0.5f + 14f);
            lantern.text.rectTransform.sizeDelta = new Vector2(TEXT_WIDTH, h - 28f);
            lantern.text.rectTransform.anchoredPosition = new Vector2(0f, 4f);
        }

        // ==== 对象池 ====

        private void EnsurePoolCapacity(int targetCount)
        {
            while (allocatedCount < targetCount)
            {
                Lantern lantern = CreateLantern();
                ReleaseLantern(lantern);
            }
        }

        private Lantern AcquireLantern(int depth)
        {
            Lantern lantern = pool.Count > 0 ? pool.Pop() : CreateLantern();
            lantern.depth = depth;
            RectTransform layer = depthLayers[Mathf.Clamp(depth, 0, depthLayers.Length - 1)];
            if (layer != null && lantern.rect.parent != layer)
            {
                lantern.rect.SetParent(layer, false);
            }
            lantern.group.alpha = 0f;
            lantern.rect.gameObject.SetActive(true);
            lantern.active = true;
            return lantern;
        }

        private Lantern CreateLantern()
        {
            Transform parent = depthLayers[0] != null ? (Transform)depthLayers[0] : transform;
            GameObject root = ZombieModeUIHelper.CreateRect("Lantern", parent, new Vector2(0.5f, 0.5f), new Vector2(LANTERN_WIDTH, LANTERN_MIN_HEIGHT));
            Lantern lantern = new Lantern
            {
                rect = root.GetComponent<RectTransform>(),
                group = root.AddComponent<CanvasGroup>()
            };
            lantern.group.blocksRaycasts = false;
            lantern.group.interactable = false;

            lantern.halo = CreateImage("Halo", lantern.rect, BossRushInkSkin.GetGlowSprite(), Vector2.zero);

            GameObject bodyObject = ZombieModeUIHelper.CreateRect("Body", lantern.rect, new Vector2(0.5f, 0.5f), Vector2.zero);
            lantern.body = bodyObject.GetComponent<RectTransform>();
            lantern.body.anchorMin = Vector2.zero;
            lantern.body.anchorMax = Vector2.one;
            lantern.body.offsetMin = Vector2.zero;
            lantern.body.offsetMax = Vector2.zero;
            Image paper = bodyObject.AddComponent<Image>();
            paper.sprite = BossRushInkSkin.GetSurfaceSprite();
            paper.type = Image.Type.Sliced;
            paper.pixelsPerUnitMultiplier = 1.5f;
            paper.color = LitPaper;
            paper.raycastTarget = false;

            lantern.glow = CreateImage("InnerLight", lantern.body, BossRushInkSkin.GetGlowSprite(), Vector2.zero);
            Image frame = BossRushInkSkin.AddInkFrame(paper, FrameInk, 1.6f);
            if (frame != null) frame.raycastTarget = false;

            lantern.topRim = CreateRim("TopRim", lantern.rect);
            lantern.bottomRim = CreateRim("BottomRim", lantern.rect);
            lantern.flame = CreateImage("Flame", lantern.rect, BossRushInkSkin.GetGlowSprite(), new Vector2(30f, 34f));
            lantern.core = CreateImage("FlameCore", lantern.rect, BossRushInkSkin.GetGlowSprite(), new Vector2(11f, 15f));

            GameObject textObject = ZombieModeUIHelper.CreateRect("Text", lantern.rect, new Vector2(0.5f, 0.5f), new Vector2(TEXT_WIDTH, LANTERN_MIN_HEIGHT));
            TextMeshProUGUI text = textObject.AddComponent<TextMeshProUGUI>();
            if (font != null)
            {
                text.font = font;
            }
            // 字底走 TMP 距离场的 Underlay（UI.Shadow 挂在 TMP 上不起作用）；整层共用一份材质
            Material material = GetTextMaterial();
            if (material != null)
            {
                text.fontSharedMaterial = material;
            }
            text.fontSize = BASE_FONT_SIZE;
            text.enableAutoSizing = false;
            text.alignment = TextAlignmentOptions.Center;
            text.richText = false;
            text.enableWordWrapping = true;
            text.overflowMode = TextOverflowModes.Overflow;
            text.color = TextInk;
            text.raycastTarget = false;
            lantern.text = text;

            LayoutLantern(lantern);
            allocatedCount++;
            return lantern;
        }

        private static Image CreateImage(string name, RectTransform parent, Sprite sprite, Vector2 size)
        {
            GameObject go = ZombieModeUIHelper.CreateRect(name, parent, new Vector2(0.5f, 0.5f), size);
            Image image = go.AddComponent<Image>();
            image.sprite = sprite;
            image.raycastTarget = false;
            return image;
        }

        /// <summary>灯口：一道短墨笔（整笔贴图，不加遮罩）。</summary>
        private static RectTransform CreateRim(string name, RectTransform parent)
        {
            Image rim = CreateImage(name, parent, BossRushInkSkin.GetBrushSprite(), new Vector2(LANTERN_WIDTH * 0.8f, 11f));
            rim.type = Image.Type.Sliced;
            rim.pixelsPerUnitMultiplier = 64f / 11f;
            rim.color = RimInk;
            return rim.rectTransform;
        }

        private void ReleaseLantern(Lantern lantern)
        {
            if (lantern == null)
            {
                return;
            }

            lantern.active = false;
            lantern.contentIndex = -1;
            if (lantern.rect != null)
            {
                lantern.rect.gameObject.SetActive(false);
            }
            pool.Push(lantern);
        }

        private void RetireLantern(Lantern lantern)
        {
            if (lantern.contentIndex >= 0)
            {
                activeContentIndices.Remove(lantern.contentIndex);
            }
            ReleaseLantern(lantern);
        }

        private void RecycleAll()
        {
            for (int i = 0; i < activeLanterns.Count; i++)
            {
                ReleaseLantern(activeLanterns[i]);
            }
            activeLanterns.Clear();
            activeContentIndices.Clear();
        }

        /// <summary>
        /// 星灯字底材质：在字体材质上打开 UNDERLAY_ON（一层柔和暖光托住墨字），整层共用一份，OnDestroy 销毁。
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
            textMaterial.name = font.material.name + " (BossRush Wish Lantern)";
            textMaterial.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                textMaterial.EnableKeyword("UNDERLAY_ON");
                textMaterial.SetColor(ShaderUtilities.ID_UnderlayColor, new Color(1f, 0.93f, 0.78f, 0.45f));
                textMaterial.SetFloat(ShaderUtilities.ID_UnderlayOffsetX, 0f);
                textMaterial.SetFloat(ShaderUtilities.ID_UnderlayOffsetY, 0f);
                textMaterial.SetFloat(ShaderUtilities.ID_UnderlaySoftness, 0.6f);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[WishLantern] [WARNING] 字底材质参数写入失败: " + e.Message);
            }
            return textMaterial;
        }

        private void OnDestroy()
        {
            activeLanterns.Clear();
            activeContentIndices.Clear();
            pool.Clear();
            preparedContents.Clear();
            allocatedCount = 0;
            if (textMaterial != null)
            {
                Destroy(textMaterial);
                textMaterial = null;
            }
        }
    }
}
