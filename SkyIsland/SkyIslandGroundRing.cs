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

        /// <summary>
        /// 贴地圈离地面碰撞体的抬高（米），撤离环、噬风预警圈与头目圈共用。
        /// 广场是生成器 paved_disc 铺的：调用高度比岛面高 0.03–0.05 m，台面顶在调用高度 +0.02 m、放射缝顶到约 +0.058 m，
        /// 而碰撞体只在岛面上。旧的 0.06 m 正好与悬根林广场、归航钟庭的台面共面，绿环被盖成几段碎弧（2026-09-15 第四轮截图）。
        /// 统一抬到最高装饰之上再留几厘米；俯视 55° 下视差约 0.1 m，读不出来。tests/SkyIslandGroundRingGuard.py 按生成器里的实际高度核对。
        /// </summary>
        internal const float GroundLift = 0.16f;

        private static Material shared;
        /// <summary>
        /// <see cref="shared"/> 是不是本类 new 出来的。退到共享特效材质工厂（<see cref="BossRushFxMaterials"/>）时那份归工厂所有，
        /// 这里不能销毁它。
        /// </summary>
        private static bool sharedOwned;
        private static bool sharedWarned;
        private static Texture2D bandTexture;
        private static Texture2D fillTexture;
        private static Sprite fillSprite;

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
            // 两条路都找不到着色器：不画（UE-19）。没有材质的 LineRenderer 在 URP 下是一圈品红，比没有更坏。
            else line.enabled = false;
            return line;
        }

        /// <summary>
        /// 半径变化才重画几何；蓄力只改带宽与颜色。首顶点恒为 (radius, 0, 0)，直接读它作半径回执，
        /// 不另建逐环缓存或组件。<paramref name="radius"/> 始终是真实判定半径，圆心跟随仍由 Transform 负责。
        /// </summary>
        internal static void SetShape(LineRenderer line, float radius, float width, Color color)
        {
            if (line == null) return;
            if (line.positionCount != Segments || line.GetPosition(0).x != radius)
            {
                line.positionCount = Segments;
                for (int i = 0; i < Segments; i++)
                {
                    float angle = i * (2f * Mathf.PI / Segments);
                    line.SetPosition(i, new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * radius);
                }
            }
            if (line.widthMultiplier != width) line.widthMultiplier = width;
            if (!line.startColor.Equals(color)) line.startColor = color;
            if (!line.endColor.Equals(color)) line.endColor = color;
        }

        /// <summary>
        /// 贴地圈的共享材质。首选 <c>Sprites/Default</c>（实机已验证：撤离环第五轮起按环带覆盖率判绿）；
        /// 找不到时退到全 Mod 共享的特效材质工厂（URP 粒子 Unlit、半透明），它同样吃顶点色与柔边贴图。
        /// 旧退回链是 <c>Unlit/Color</c>（忽略贴图与顶点色 → 纯白硬边带）再 <c>Standard</c>（URP 下是品红），
        /// 撤离环、全部头目预警圈与 Mode H 的圈会一起变成白带或品红而且不报错（2026-09-23 审美审查 UE-19）。
        /// 两条都没有就返回 null、告警一次，调用方不画——owner 口径：资源不对就硬失败，不做「看起来差不多」的降级。
        /// </summary>
        private static Material SharedMaterial()
        {
            if (shared != null) return shared;
            Texture2D band = BandTexture();
            Shader shader = Shader.Find("Sprites/Default");
            if (shader != null)
            {
                shared = new Material(shader);
                shared.name = "SkyIslandGroundRingMat";
                sharedOwned = true;
                // 没有贴图时 LineRenderer 画的是一条硬边平色带——两条笔直的边缘线，
                // 在俯视视角的地面上一眼就是「程序化画的方框」。挂一张横跨带宽的柔边即可，
                // 几何、半径、判定一字不动。
                if (band != null) shared.mainTexture = band;
                return shared;
            }
            shared = band != null ? BossRushFxMaterials.Get(BossRushFxBlend.Alpha, band) : null;
            sharedOwned = false;
            if (shared == null && !sharedWarned)
            {
                sharedWarned = true;
                Debug.LogWarning("[SkyIsland] 贴地圈找不到 Sprites/Default 与共享特效材质，撤离环与预警圈不绘制");
            }
            return shared;
        }

        /// <summary>
        /// 撤离环圈内的「内缘亮」填充精灵（UE-02）：圆心透明，从 55% 半径起 smoothstep 亮到环带下方，最外 3% 收回 0。
        /// 纯白 + alpha，颜色走 <see cref="SpriteRenderer.color"/>。读作「这一片是返航的光区」，而不是地上画了一个圈；
        /// 圆心留空，站在圈里的角色脚下不被罩一层色。只有 <c>Sprites/Default</c> 可用时才画（SpriteRenderer 按 _MainTex 取精灵图）。
        /// </summary>
        internal static Sprite FillSprite()
        {
            if (fillSprite != null) return fillSprite;
            try
            {
                const int size = 128;
                Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false, false);
                texture.name = "SkyIslandGroundRingFill";
                texture.wrapMode = TextureWrapMode.Clamp;
                texture.filterMode = FilterMode.Bilinear;
                texture.hideFlags = HideFlags.HideAndDontSave;
                Color32[] pixels = new Color32[size * size];
                float center = (size - 1) * 0.5f;
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        float dx = (x - center) / (size * 0.5f);
                        float dy = (y - center) / (size * 0.5f);
                        float d = Mathf.Sqrt(dx * dx + dy * dy);
                        float rise = Mathf.Clamp01((d - 0.55f) / 0.42f);
                        float fall = Mathf.Clamp01((d - 0.97f) / 0.03f);
                        float alpha = rise * rise * (3f - 2f * rise) * (1f - fall * fall * (3f - 2f * fall));
                        pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(alpha) * 255f));
                    }
                }
                texture.SetPixels32(pixels);
                texture.Apply(false, false);
                fillTexture = texture;
                fillSprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size * 0.5f,
                    0u, SpriteMeshType.FullRect);
                fillSprite.name = "SkyIslandGroundRingFill";
                fillSprite.hideFlags = HideFlags.HideAndDontSave;
            }
            catch (Exception e)
            {
                // 纯观感层：填充出不来，环照样画。
                Debug.LogWarning("[SkyIsland] 撤离环内缘填充贴图生成失败：" + e.Message);
            }
            return fillSprite;
        }

        /// <summary>
        /// 在 <paramref name="ring"/> 下面铺一层内缘亮填充（UE-02），直径 = 2 × <paramref name="radius"/>、同色。
        /// 精灵 PPU = 半边长，原生直径正好 2 个单位，缩放就是半径。只在共享材质是本类的 <c>Sprites/Default</c> 时才铺。
        /// </summary>
        internal static SpriteRenderer AddFill(LineRenderer ring, float radius, Color color)
        {
            if (ring == null || !sharedOwned || shared == null) return null;
            Sprite sprite = FillSprite();
            if (sprite == null) return null;
            GameObject go = new GameObject("RingFill");
            go.transform.SetParent(ring.transform, false);
            // 环自己已经绕 X 转了 90° 摊平在地面上，局部 XY 就是地面：填充不用再转。
            go.transform.localPosition = Vector3.zero;
            go.transform.localScale = new Vector3(radius, radius, 1f);
            SpriteRenderer renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sharedMaterial = shared;
            renderer.color = color;
            renderer.sortingOrder = SortingOrder - 1;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return renderer;
        }

        /// <summary>
        /// 横跨带宽的柔边。<see cref="LineTextureMode.Stretch"/> 下 U 沿线长、V 跨带宽，
        /// 所以只需要 2 列；V 方向两端 alpha→0、中间是平台。
        ///
        /// 颜色全白：上色走 LineRenderer 的顶点色（startColor/endColor），
        /// 图里带死颜色会让撤离环的青 / 绿与噬风的预警色全部失效。
        /// </summary>
        private static Texture2D BandTexture()
        {
            if (bandTexture != null) return bandTexture;
            try
            {
                const int width = 2;
                const int height = 32;
                const float edge = 0.28f;
                Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false, false);
                texture.name = "SkyIslandGroundRingBand";
                texture.wrapMode = TextureWrapMode.Clamp;
                texture.filterMode = FilterMode.Bilinear;
                texture.hideFlags = HideFlags.HideAndDontSave;
                Color32[] pixels = new Color32[width * height];
                for (int y = 0; y < height; y++)
                {
                    float v = y / (float)(height - 1);
                    float head = Mathf.Clamp01(v / edge);
                    float tail = Mathf.Clamp01((1f - v) / edge);
                    float alpha = head * head * (3f - 2f * head) * tail * tail * (3f - 2f * tail);
                    byte a = (byte)Mathf.RoundToInt(Mathf.Clamp01(alpha) * 255f);
                    for (int x = 0; x < width; x++) pixels[y * width + x] = new Color32(255, 255, 255, a);
                }
                texture.SetPixels32(pixels);
                texture.Apply(false, false);
                bandTexture = texture;
            }
            catch (Exception e)
            {
                // 纯观感层：贴图生成失败就退回硬边平带，圈照样画得出来。
                Debug.LogWarning("[SkyIsland] 圆环柔边贴图生成失败，退回硬边：" + e.Message);
            }
            return bandTexture;
        }

        /// <summary>
        /// 由 <c>SkyIslandRuntimeModule.OnDestroy</c> 经 <c>SkyIslandStormBoss.ResetStaticCaches</c> 调用。
        /// 运行时 <c>new Material</c> 出来的材质必须显式 <c>Destroy</c>，口径同
        /// <see cref="SkyIslandRendering"/>.Dispose；只置 null 是把它丢给
        /// <c>Resources.UnloadUnusedAssets</c> 碰运气。
        /// </summary>
        internal static void ResetStaticCaches()
        {
            // 退到共享特效材质时那份归 BossRushFxMaterials 所有，由它自己的 ResetStaticCaches 销毁。
            if (shared != null && sharedOwned) UnityEngine.Object.Destroy(shared);
            shared = null;
            sharedOwned = false;
            sharedWarned = false;
            // 贴图带 HideFlags.DontSave，切场景不会自动回收，必须和材质一起显式销毁。
            if (bandTexture != null) UnityEngine.Object.Destroy(bandTexture);
            bandTexture = null;
            if (fillSprite != null) UnityEngine.Object.Destroy(fillSprite);
            fillSprite = null;
            if (fillTexture != null) UnityEngine.Object.Destroy(fillTexture);
            fillTexture = null;
        }
    }

    /// <summary>
    /// 撤离环的开环与常态呼吸。**只动带宽与不透明度，半径一帧都不动**——
    /// 半径等于 <c>SkyIslandSession.ExtractionRadius</c> 是 COMPAT 契约（圈内即判定内），
    /// 让它呼吸等于画一个大小对不上的圈，比不画更坏。
    ///
    /// 【为什么要开环动画】风标 / 星灯点亮时新的撤离环靠 <c>SetActive(true)</c> 凭空出现。
    /// 玩家正在别处打怪，回头看到一个已经在那儿的圈，不会意识到「刚刚发生了一件事」。
    /// 0.35 秒 ease-out 把带子展开，这一下就是那件事的视觉回执。
    ///
    /// 挂在环自己身上，所以不需要会话每帧驱动；<c>SetActive</c> 一开就重播（OnEnable 复位）。
    /// 噬风的预警圈**不挂**这个：那条是计时器，读数必须线性且完全由 SetShape 说了算。
    /// </summary>
    internal sealed class SkyIslandGroundRingPulse : MonoBehaviour
    {
        private const float OpenSeconds = 0.35f;
        private const float BreathSeconds = 1.6f;
        private const float BreathWidth = 0.15f;
        private const float BreathAlpha = 0.14f;

        private LineRenderer line;
        private SpriteRenderer fill;
        private float baseWidth, age, fillAlpha;
        private Color baseColor;

        internal static void Attach(LineRenderer target, float width, Color color)
        {
            Attach(target, width, color, null, 0f);
        }

        /// <param name="ringFill">环下的内缘亮填充（UE-02），可为 null；开环时与带宽同一条 EaseOut 从 0 淡到 <paramref name="fillPeak"/>，之后同相呼吸。</param>
        internal static void Attach(LineRenderer target, float width, Color color, SpriteRenderer ringFill, float fillPeak)
        {
            if (target == null) return;
            SkyIslandGroundRingPulse pulse = target.gameObject.GetComponent<SkyIslandGroundRingPulse>();
            if (pulse == null) pulse = target.gameObject.AddComponent<SkyIslandGroundRingPulse>();
            pulse.line = target;
            pulse.baseWidth = width;
            pulse.baseColor = color;
            pulse.fill = ringFill;
            pulse.fillAlpha = fillPeak;
            pulse.age = 0f;
        }

        private void OnEnable() { age = 0f; }

        private void LateUpdate()
        {
            // 表现层淡变走 unscaled 时间，所以必须自己看暂停：暂停菜单开着时环不该继续呼吸。
            if (line == null || BossRushUI.IsGamePaused()) return;
            age += Time.unscaledDeltaTime;
            float open = age < OpenSeconds ? BossRushUI.EaseOut(age / OpenSeconds) : 1f;
            float breath = Mathf.Sin(age * (2f * Mathf.PI / BreathSeconds));
            line.widthMultiplier = baseWidth * open * (1f + breath * BreathWidth);
            Color color = baseColor;
            // 不透明度只往下呼吸（区间 [1-BreathAlpha, 1]）：顶点色超过 1 会被 Color32 夹回，
            // 往上摆等于半个周期没有动静。
            float level = open * (1f - BreathAlpha * (0.5f - breath * 0.5f));
            color.a = baseColor.a * level;
            line.startColor = color;
            line.endColor = color;
            if (fill != null)
            {
                color.a = fillAlpha * level;
                fill.color = color;
            }
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
    /// - 钟庭环在双航标点亮后出现，与 <c>SkyIslandSession.BellExitIfUnlocked()</c> 同一事实源；
    /// - 布局 v2 另有悬根林 / 残星工坊两处航标广场环，分别随风标 / 星灯点亮出现（<see cref="AddBeaconRings"/>）；
    /// - 纯表现层：无碰撞体；<see cref="Apply"/> 只在解锁状态真的翻转时动一次。
    ///   撤离环上挂的 <see cref="SkyIslandGroundRingPulse"/> 另有每帧的呼吸（只写带宽与顶点色，量级很小）。
    /// </summary>
    internal sealed class SkyIslandExtractionRings : IDisposable
    {
        /// <summary>
        /// 环带宽度。够粗才能在俯视视角下一眼看见，又不至于糊住脚下的地面。
        /// 0.32 → 0.24（2026-09-23 审美审查 UE-02）：1080p 下从约 22 px 收到约 17 px，圈里加了一层内缘亮填充，
        /// 「这里是光区」不再只靠一道粗线表达。F3 环带覆盖率探针按带宽取样点（内外各 1.5 个带宽），跟着自动收窄。
        /// </summary>
        internal const float RingWidth = 0.24f;
        /// <summary>
        /// 撤离环的世界色（UE-02）。和 UI 字色 token 分开：<c>Accent</c> / <c>SuccessText</c> 是给深底上的小字用的高饱和色，
        /// 原样铺进暖琥珀的地面就是一圈霓虹。这里饱和度降约三成、不透明度 0.78，地面纹理透得出来；
        /// 色相族不变（码头青、钟庭与航标广场绿），F3 面板、Wiki 与地图提示里的「青色环 / 绿环」仍然成立。
        /// </summary>
        internal static readonly Color DockRingColor = new Color(0.60f, 0.84f, 0.86f, 0.78f);
        internal static readonly Color BeaconRingColor = new Color(0.62f, 0.88f, 0.74f, 0.78f);
        /// <summary>圈内填充最亮处的不透明度（在环带下方，圆心透明）。</summary>
        internal const float RingFillAlpha = 0.16f;
        /// <summary>抬离地面碰撞体的高度：与噬风预警圈、头目圈共用 <see cref="SkyIslandGroundRing.GroundLift"/>，高过广场台面与放射缝。</summary>
        internal const float GroundOffset = SkyIslandGroundRing.GroundLift;

        private LineRenderer dockRing, bellRing, windRing, starRing;
        private bool bellVisible, windVisible, starVisible, disposed;

        internal SkyIslandExtractionRings(Transform root, Transform dock, Transform bell, float radius, int groundMask)
        {
            if (root == null) return;
            dockRing = Build(root, dock, radius, groundMask, DockRingColor);
            bellRing = Build(root, bell, radius, groundMask, BeaconRingColor);
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
                // 圈内一层内缘亮（UE-02）：同色、圆心透明，开环时和带宽一起从 0 淡进来。
                SpriteRenderer fill = SkyIslandGroundRing.AddFill(ring, radius, new Color(color.r, color.g, color.b, 0f));
                // 开环与呼吸挂在环自己身上：半径不动，只有带宽与不透明度在动。
                SkyIslandGroundRingPulse.Attach(ring, RingWidth, color, fill, RingFillAlpha);
                return ring;
            }
            catch (Exception e)
            {
                // 纯表现层：画不出来也绝不能拦住旅程本身。
                Debug.LogWarning("[SkyIsland] 撤离点标识创建失败 " + anchor.name + "：" + e.Message);
                return null;
            }
        }

        /// <summary>按双航标翻转钟庭环。幂等，状态没变时零开销。</summary>
        internal void Apply(bool bellUnlocked)
        {
            if (disposed || bellRing == null || bellVisible == bellUnlocked) return;
            bellVisible = bellUnlocked;
            bellRing.gameObject.SetActive(bellUnlocked);
        }

        /// <summary>
        /// 布局 v2：两处航标广场的撤离环，与钟庭环同色、同样默认隐藏；锚点缺失时就是没有这个环。
        /// 由会话按 <c>WindExitIfUnlocked()</c> / <c>StarExitIfUnlocked()</c> 经 <see cref="ApplyBeacons"/> 翻转。
        /// </summary>
        internal void AddBeaconRings(Transform root, Transform wind, Transform star, float radius, int groundMask)
        {
            if (root == null || disposed) return;
            windRing = Build(root, wind, radius, groundMask, BeaconRingColor);
            starRing = Build(root, star, radius, groundMask, BeaconRingColor);
            if (windRing != null) windRing.gameObject.SetActive(false);
            if (starRing != null) starRing.gameObject.SetActive(false);
        }

        /// <summary>按风标 / 星灯翻转两处广场环。幂等，状态没变时零开销。</summary>
        internal void ApplyBeacons(bool windUnlocked, bool starUnlocked)
        {
            if (disposed) return;
            if (windRing != null && windVisible != windUnlocked) { windVisible = windUnlocked; windRing.gameObject.SetActive(windUnlocked); }
            if (starRing != null && starVisible != starUnlocked) { starVisible = starUnlocked; starRing.gameObject.SetActive(starUnlocked); }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (dockRing != null) UnityEngine.Object.Destroy(dockRing.gameObject);
            if (bellRing != null) UnityEngine.Object.Destroy(bellRing.gameObject);
            if (windRing != null) UnityEngine.Object.Destroy(windRing.gameObject);
            if (starRing != null) UnityEngine.Object.Destroy(starRing.gameObject);
            dockRing = null;
            bellRing = null;
            windRing = null;
            starRing = null;
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
