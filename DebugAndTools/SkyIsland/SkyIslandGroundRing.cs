using System;
using System.Reflection;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// COMPAT：贴地圆环的唯一建造点——噬风的预警圈与撤离点标识共用。
    ///
    /// 鸭科夫是固定俯视视角，地面圆环是唯一能让玩家读出「这一圈到底多大」的表现方式；
    /// 半径一律等于真实作用范围/判定半径，绝不用缩放过的示意图形。
    ///
    /// 两条实现纪律收在这里，避免两个调用方各写一遍再各漏一条：
    /// 1. 绕 X 轴转 90°，让 <see cref="LineAlignment.TransformZ"/> 把带子摊平在地面上；
    /// 2. 用 <c>sharedMaterial</c> 而**不是** <c>material</c>——后者会给每个 LineRenderer
    ///    实例化一份材质副本，而官方文档明说这种副本要调用方自己销毁，噬风每次脉冲建一次圈
    ///    就等于每次泄漏一份。颜色走 LineRenderer 的顶点色（startColor/endColor），
    ///    共用材质不影响各自上色。
    /// </summary>
    internal static class SkyIslandGroundRing
    {
        internal const int Segments = 64;
        /// <summary>透明排序序号。两个调用方共用，避免一处改了另一处压不住。</summary>
        internal const int SortingOrder = 120;

        private static Material shared;

        internal static LineRenderer Create(Transform parent, Vector3 localPosition)
        {
            GameObject go = new GameObject("SkyIslandGroundRing");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            LineRenderer line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.loop = true;
            line.positionCount = Segments;
            line.numCapVertices = 2;
            line.alignment = LineAlignment.TransformZ;
            line.textureMode = LineTextureMode.Stretch;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.sortingOrder = SortingOrder;
            Material material = SharedMaterial();
            if (material != null) line.sharedMaterial = material;
            return line;
        }

        /// <summary>按半径重画圆环。<paramref name="radius"/> 必须是真实判定半径。</summary>
        internal static void SetShape(LineRenderer line, float radius, float width, Color color)
        {
            if (line == null) return;
            for (int i = 0; i < Segments; i++)
            {
                float angle = i * (2f * Mathf.PI / Segments);
                line.SetPosition(i, new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * radius);
            }
            line.widthMultiplier = width;
            line.startColor = color;
            line.endColor = color;
        }

        private static Material SharedMaterial()
        {
            if (shared != null) return shared;
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            if (shader == null) shader = Shader.Find("Standard");
            if (shader == null) return null;
            shared = new Material(shader);
            shared.name = "SkyIslandGroundRingMat";
            return shared;
        }

        /// <summary>
        /// 由 <c>SkyIslandRuntimeModule.OnDestroy</c> 经 <c>SkyIslandStormBoss.ResetStaticCaches</c> 调用。
        /// 运行时 <c>new Material</c> 出来的材质必须显式 <c>Destroy</c>，口径同
        /// <see cref="SkyIslandRendering"/>.Dispose；只置 null 是把它丢给
        /// <c>Resources.UnloadUnusedAssets</c> 碰运气。
        /// </summary>
        internal static void ResetStaticCaches()
        {
            if (shared != null) UnityEngine.Object.Destroy(shared);
            shared = null;
        }
    }

    /// <summary>
    /// COMPAT：撤离点的地面标识。
    ///
    /// 撤离判定本身没有任何视觉载体——`Exit` / `BellExtraction` 在作者场景里只是空标记
    /// （`tools/generate_sky_island.py` 的 `marker()` 建的是 Blender Empty），自绘地图删除后
    /// 官方小地图也不标撤离点。而 F3 面板与中英 Wiki 三处都写着「码头蓝环 / 钟庭绿环」，
    /// 玩家会去找一个不存在的东西；归航钟庭那个尤其严重：它是终章后才开放、离最近地标 14 m、
    /// 既无标识也无地图点（CR-2026-09-09-012）。
    ///
    /// 这里把那两个环真的画出来：
    /// - 半径直接取 <c>SkyIslandSession.ExtractionRadius</c>，圈内即判定内，不做示意放大；
    /// - 落点按地面射线吸附，标记本身可能悬在地面上方；
    /// - 钟庭环只在敲钟结局后出现，与 <c>SkyIslandSession.BellExitIfUnlocked()</c> 同一事实源；
    /// - 纯表现层：无碰撞体、无每帧工作，<see cref="Apply"/> 只在解锁状态真的翻转时动一次。
    /// </summary>
    internal sealed class SkyIslandExtractionRings : IDisposable
    {
        /// <summary>环带宽度。够粗才能在俯视视角下一眼看见，又不至于糊住脚下的地面。</summary>
        internal const float RingWidth = 0.32f;
        /// <summary>抬离地面的高度，避免与地面共面产生 z-fighting。</summary>
        internal const float GroundOffset = 0.06f;

        private LineRenderer dockRing, bellRing;
        private bool bellVisible, disposed;

        internal SkyIslandExtractionRings(Transform root, Transform dock, Transform bell, float radius, int groundMask)
        {
            if (root == null) return;
            dockRing = Build(root, dock, radius, groundMask, BossRushUIColors.Accent);
            bellRing = Build(root, bell, radius, groundMask, BossRushUIColors.SuccessText);
            if (bellRing != null) bellRing.gameObject.SetActive(false);
        }

        private static LineRenderer Build(Transform root, Transform anchor, float radius, int groundMask, Color color)
        {
            if (anchor == null) return null;
            try
            {
                Vector3 position = anchor.position;
                RaycastHit hit;
                if (Physics.Raycast(position + Vector3.up * 2f, Vector3.down, out hit, 4f, groundMask,
                    QueryTriggerInteraction.Ignore) && hit.transform.IsChildOf(root))
                    position = hit.point;
                LineRenderer ring = SkyIslandGroundRing.Create(root, position + Vector3.up * GroundOffset);
                ring.gameObject.name = "SkyIslandExtractionRing_" + anchor.name;
                SkyIslandGroundRing.SetShape(ring, radius, RingWidth, color);
                return ring;
            }
            catch (Exception e)
            {
                // 纯表现层：画不出来也绝不能拦住旅程本身。
                Debug.LogWarning("[SkyIsland] 撤离点标识创建失败 " + anchor.name + "：" + e.Message);
                return null;
            }
        }

        /// <summary>按敲钟结局翻转钟庭环。幂等，状态没变时零开销。</summary>
        internal void Apply(bool bellUnlocked)
        {
            if (disposed || bellRing == null || bellVisible == bellUnlocked) return;
            bellVisible = bellUnlocked;
            bellRing.gameObject.SetActive(bellUnlocked);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (dockRing != null) UnityEngine.Object.Destroy(dockRing.gameObject);
            if (bellRing != null) UnityEngine.Object.Destroy(bellRing.gameObject);
            dockRing = null;
            bellRing = null;
        }
    }

    /// <summary>
    /// COMPAT：撤离读条复用官方 <c>EvacuationCountdownUI</c>（原版出口那枚圆环读条与 00:02.345 数字）。
    ///
    /// 岛上的撤离**判定**刻意不交给官方 <c>CountDownArea</c>：判定唯一留在会话 Update 的撤离圈里
    /// （距离 + 停留，CR-2026-09-08-002）。官方组件是触发器口径、按 Time.time 自己计时，
    /// 两套一起跑就会出现「圆环读满了人却没走」或反过来。所以这里只借它的**显示**：
    /// 官方控件每帧读 <c>target.Progress</c> / <c>target.RemainingTime</c>，这两者只取决于
    /// <c>requiredExtrationTime</c> 与 <c>timeWhenCountDownBegan</c>。挂一个**禁用**的 CountDownArea
    /// （禁用后它的 Update 与触发器回调全部短路，既不会自己读条也不会自己判成功），
    /// 每帧把起点写成 <c>Time.time - 会话已停留秒数</c>，官方读条就与会话判定逐帧一致。
    /// 这与丧尸模式守卫禁止的「改写真实撤离点的私有计时去驱动判定」不是一回事：这个组件从不参与判定。
    ///
    /// fail-open：官方控件不在场、或游戏更新改了字段名，<see cref="Show"/> 返回 false，
    /// 会话退回 HUD 卡片里的文字读秒；撤离本身不受影响。
    /// 每帧一次 FieldInfo.SetValue 有一次装箱，但只发生在站进撤离圈的那 3 秒里。
    /// </summary>
    internal sealed class SkyIslandExtractionCountdown : IDisposable
    {
        private static readonly FieldInfo RequiredField = BossRush.Common.Utils.ReflectionCache.GetField(
            typeof(CountDownArea), "requiredExtrationTime", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo BeganField = BossRush.Common.Utils.ReflectionCache.GetField(
            typeof(CountDownArea), "timeWhenCountDownBegan", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

        private GameObject host;
        private CountDownArea area;
        private bool requested, broken;

        /// <summary>官方控件是否可用。只读，给 F3 验收与日志用。</summary>
        internal bool Available
        {
            get { return !broken && area != null && EvacuationCountdownUI.Instance != null; }
        }

        internal SkyIslandExtractionCountdown(Transform parent, float holdSeconds)
        {
            if (parent == null || RequiredField == null || BeganField == null)
            {
                broken = true;
                Debug.LogWarning("[SkyIsland] 官方撤离读条字段缺失，撤离读秒改用 HUD 文字");
                return;
            }
            try
            {
                host = new GameObject("SkyIslandExtractionCountdown");
                host.transform.SetParent(parent, false);
                area = host.AddComponent<CountDownArea>();
                area.enabled = false;
                RequiredField.SetValue(area, holdSeconds);
            }
            catch (Exception e)
            {
                broken = true;
                Debug.LogWarning("[SkyIsland] 官方撤离读条接入失败，撤离读秒改用 HUD 文字：" + e.Message);
            }
        }

        /// <param name="heldSeconds">按会话判定已经在圈里停留的秒数。</param>
        /// <returns>官方控件是否接管了这一帧的显示。</returns>
        internal bool Show(float heldSeconds)
        {
            if (broken || area == null || EvacuationCountdownUI.Instance == null) return false;
            try
            {
                BeganField.SetValue(area, Time.time - Mathf.Max(0f, heldSeconds));
                if (!requested)
                {
                    EvacuationCountdownUI.Request(area);
                    requested = true;
                }
                return true;
            }
            catch (Exception e)
            {
                broken = true;
                Debug.LogWarning("[SkyIsland] 官方撤离读条驱动失败，撤离读秒改用 HUD 文字：" + e.Message);
                return false;
            }
        }

        /// <summary>离开撤离圈或撤离成功时收起官方读条。幂等，没显示过时零开销。</summary>
        internal void Hide()
        {
            if (!requested) return;
            requested = false;
            try { EvacuationCountdownUI.Release(area); }
            catch (Exception e) { Debug.LogWarning("[SkyIsland] 官方撤离读条收起失败：" + e.Message); }
        }

        public void Dispose()
        {
            Hide();
            if (host != null) UnityEngine.Object.Destroy(host);
            host = null;
            area = null;
        }
    }
}
