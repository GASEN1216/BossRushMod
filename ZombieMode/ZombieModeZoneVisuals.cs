// ============================================================================
// ZombieModeZoneVisuals.cs - 丧尸模式世界空间标记的表现层
// ============================================================================
// 模块说明：
//   2026-09-23 审美审查（UC-01 / UC-03 / UC-18 / UC-19，特效侧 VA-27 / VA-28 / VA-29 / VA-32）：
//   - 地面圈（安全区、Boss / 特殊丧尸预警、减速区、毒云、腐蚀领域、引力井、精英脚下标记）原来是
//     Cylinder 网格 + 首选 Standard 材质：URP 正式构建不画 Standard；退到 Sprites/Default 时上下两个盖都画，
//     0.62 的 alpha 叠成约 0.86 的一整块荧光绿硬边饼。预警圈没有倒计时，到点一帧消失。
//     现在是单面平面 quad + 一张程序化「内淡外亮」圆盘贴图（亮只留在边带，外沿羽化），
//     材质走全 Mod 共享的 BossRushFxMaterials（URP 粒子 Unlit 半透明），颜色走 MaterialPropertyBlock。
//   - ZombieModeZoneVisualFx：出现 0.18 s 淡入并从 0.9 长到 1；预警期内圈从中心涨满到边带
//     （进度由各 runtime 的 TickRuntime 推进，暂停时跟着停）；结束 0.18 s 淡出再销毁；
//     安全区最后几秒只做平滑的暖色呼吸，边线不做动画（ZombieModeSafeZoneVisualGuard）。
//   - 补给 / 医疗终端的外观：真 NPC 模型挂在终端交互体下面，交互体、碰撞体、服务状态不动。
//   - 净化点拾取：0.14 s 吸进玩家胸口再销毁，不再一帧消失。
//   只改表现：半径、伤害判定、触发时序全部不动（判定仍在各 runtime 里按原时间轴跑）。
//   逐帧成本只在动画进行中存在，播完 enabled = false；地面圈的数量上限沿用各 runtime 的 run-only 登记。
// ============================================================================

using UnityEngine;
using BossRush.Utils;

namespace BossRush
{
    internal static class ZombieModeZoneVisuals
    {
        /// <summary>安全区常态：SuccessText 同色相、压饱和。贴图中心只有边带的约 0.2，地面上的填充实际约 0.08。</summary>
        internal static readonly Color SafeZoneColor = new Color(0.50f, 0.86f, 0.66f, 0.42f);
        /// <summary>安全区最后几秒的警示色：WarningText 色相。</summary>
        internal static readonly Color SafeZoneWarningColor = new Color(1f, 0.79f, 0.40f, 0.60f);
        internal static readonly Color SafeZoneMapColor = new Color(0.50f, 0.86f, 0.66f, 0.80f);
        internal static readonly Color SafeZoneWarningMapColor = new Color(1f, 0.79f, 0.40f, 0.80f);
        /// <summary>安全区边线：顶点色承担颜色，材质是白（不再材质色 × 顶点色两次相乘）。</summary>
        internal static readonly Color SafeZoneRingColor = new Color(0.55f, 0.92f, 0.68f, 0.75f);

        private const int DiskTextureSize = 128;
        private static Mesh diskMesh;
        private static Texture2D diskTexture;
        private static Texture2D bandTexture;
        private static readonly MaterialPropertyBlock colorBlock = new MaterialPropertyBlock();
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int TintColorId = Shader.PropertyToID("_TintColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        /// <summary>单位平面 quad（XZ 平面、边长 1、法线朝上）。调用方的 localScale = 直径。</summary>
        internal static Mesh GetDiskMesh()
        {
            if (diskMesh != null)
            {
                return diskMesh;
            }

            Mesh mesh = new Mesh();
            mesh.name = "ZombieMode_ZoneDisk";
            mesh.vertices = new Vector3[]
            {
                new Vector3(-0.5f, 0f, -0.5f), new Vector3(-0.5f, 0f, 0.5f),
                new Vector3(0.5f, 0f, 0.5f), new Vector3(0.5f, 0f, -0.5f)
            };
            mesh.uv = new Vector2[] { new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f) };
            mesh.normals = new Vector3[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            // URP 粒子着色器乘顶点色：显式给白，不赌缺省通道的值。
            Color32 white = new Color32(255, 255, 255, 255);
            mesh.colors32 = new Color32[] { white, white, white, white };
            mesh.triangles = new int[] { 0, 1, 2, 0, 2, 3 };
            mesh.RecalculateBounds();
            mesh.hideFlags = HideFlags.HideAndDontSave;
            diskMesh = mesh;
            return diskMesh;
        }

        /// <summary>地面圈材质：共享的半透明粒子材质 + 圆盘贴图。着色器找不到时为 null，调用方不画。</summary>
        internal static Material GetDiskMaterial()
        {
            return BossRushFxMaterials.Get(BossRushFxBlend.Alpha, GetDiskTexture());
        }

        /// <summary>安全区边线材质：共享的半透明粒子材质 + 横跨带宽的柔边贴图。共享，不随边线销毁。</summary>
        internal static Material GetRingMaterial()
        {
            return BossRushFxMaterials.Get(BossRushFxBlend.Alpha, GetBandTexture());
        }

        /// <summary>
        /// 圆盘 alpha 剖面（r = 到圆心距离 / 半径）：内部 0.16 → 0.26 微微外亮，0.82–0.94 升到边带 1，0.955–1.0 羽化到 0。
        /// 颜色全白，色与总强度走 <see cref="SetColor"/>。
        /// </summary>
        private static Texture2D GetDiskTexture()
        {
            if (diskTexture != null)
            {
                return diskTexture;
            }

            const int size = DiskTextureSize;
            Texture2D texture = new Texture2D(size, size, TextureFormat.ARGB32, true);
            texture.name = "ZombieMode_ZoneDisk";
            texture.filterMode = FilterMode.Trilinear;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.hideFlags = HideFlags.HideAndDontSave;
            Color32[] pixels = new Color32[size * size];
            float half = size * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f - half) / half;
                    float dy = (y + 0.5f - half) / half;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float alpha = 0f;
                    if (r < 1f)
                    {
                        float interior = 0.16f + 0.10f * r * r;
                        float rim = BossRushUI.SmoothStep((r - 0.82f) / 0.12f);
                        float feather = 1f - BossRushUI.SmoothStep((r - 0.955f) / 0.045f);
                        alpha = Mathf.Lerp(interior, 1f, rim) * feather;
                    }
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(alpha) * 255f));
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(true, false);
            diskTexture = texture;
            return diskTexture;
        }

        /// <summary>LineRenderer 用的柔边带：U 沿线长、V 跨带宽（Stretch），两侧各 35% 羽化。</summary>
        internal static Texture2D GetBandTexture()
        {
            if (bandTexture != null)
            {
                return bandTexture;
            }

            const int width = 4;
            const int height = 32;
            Texture2D texture = new Texture2D(width, height, TextureFormat.ARGB32, false);
            texture.name = "ZombieMode_RingBand";
            texture.filterMode = FilterMode.Bilinear;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.hideFlags = HideFlags.HideAndDontSave;
            Color32[] pixels = new Color32[width * height];
            for (int y = 0; y < height; y++)
            {
                float v = (y + 0.5f) / height;
                float alpha = BossRushUI.SmoothStep(v / 0.35f) * BossRushUI.SmoothStep((1f - v) / 0.35f);
                byte a = (byte)Mathf.RoundToInt(Mathf.Clamp01(alpha) * 255f);
                for (int x = 0; x < width; x++)
                {
                    pixels[y * width + x] = new Color32(255, 255, 255, a);
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            bandTexture = texture;
            return bandTexture;
        }

        /// <summary>
        /// 给地面圈上色。URP 粒子着色器读 <c>_BaseColor</c>（它也声明了一个不参与计算的旧 <c>_Color</c>，
        /// 所以旧的 SetZombieModeRendererColor 先写 <c>_Color</c> 在这里等于没写）；Legacy Alpha Blended 的片元是 2× <c>_TintColor</c>。
        /// </summary>
        internal static void SetColor(Renderer renderer, Color color)
        {
            if (renderer == null)
            {
                return;
            }

            Material shared = renderer.sharedMaterial;
            renderer.GetPropertyBlock(colorBlock);
            if (shared != null && shared.HasProperty(BaseColorId))
            {
                colorBlock.SetColor(BaseColorId, color);
            }
            else if (shared != null && shared.HasProperty(TintColorId))
            {
                colorBlock.SetColor(TintColorId, color * 0.5f);
            }
            else
            {
                colorBlock.SetColor(ColorId, color);
            }
            renderer.SetPropertyBlock(colorBlock);
        }

        /// <summary>挂表现组件并从透明起步（首帧就是 0，不会先满 alpha 闪一下）。</summary>
        internal static ZombieModeZoneVisualFx Attach(GameObject zone, Renderer renderer, Color color)
        {
            if (zone == null)
            {
                return null;
            }

            if (renderer != null)
            {
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.enabled = renderer.sharedMaterial != null;
            }

            ZombieModeZoneVisualFx fx = zone.GetComponent<ZombieModeZoneVisualFx>();
            if (fx == null)
            {
                fx = zone.AddComponent<ZombieModeZoneVisualFx>();
            }
            fx.Initialize(renderer, color);
            return fx;
        }

        /// <summary>安全区边线交给表现组件：跟着圆盘一起淡入淡出。边线本身不做任何脉冲。</summary>
        internal static void AttachRing(GameObject zone, LineRenderer ring, Color color)
        {
            ZombieModeZoneVisualFx fx = zone != null ? zone.GetComponent<ZombieModeZoneVisualFx>() : null;
            if (fx != null)
            {
                fx.AddRing(ring, color);
            }
            else if (ring != null)
            {
                ring.startColor = color;
                ring.endColor = color;
            }
        }

        /// <summary>
        /// 预警读条进度（纯表现，0 = 起手、1 = 结算）。调用方是各 runtime 的 TickRuntime：
        /// 模态 / 暂停时它本来就不跑、恢复后触发时刻顺延，读条跟着停、跟着顺延。
        /// </summary>
        internal static void SetCountdown(GameObject zone, float progress)
        {
            ZombieModeZoneVisualFx fx;
            if (zone != null && zone.TryGetComponent(out fx))
            {
                fx.SetCountdown(progress);
            }
        }

        internal static void SetSafeZoneWarning(GameObject zone, bool warning)
        {
            ZombieModeZoneVisualFx fx = zone != null ? zone.GetComponent<ZombieModeZoneVisualFx>() : null;
            if (fx != null)
            {
                fx.SetWarning(warning, SafeZoneWarningColor);
            }
        }

        /// <summary>
        /// 结束：有表现组件就 0.18 s 淡出再销毁，并停掉传进来的 runtime（判定已经结算完，淡出期间不再 tick）；
        /// 没有就直接销毁。
        /// </summary>
        internal static void FadeOutAndDestroy(GameObject zone, Behaviour runtime)
        {
            if (zone == null)
            {
                return;
            }

            ZombieModeZoneVisualFx fx = zone.GetComponent<ZombieModeZoneVisualFx>();
            if (fx == null || !zone.activeInHierarchy)
            {
                Object.Destroy(zone);
                return;
            }

            if (runtime != null)
            {
                runtime.enabled = false;
            }
            fx.BeginFadeOut();
        }

        /// <summary>销毁程序化网格与贴图（HideAndDontSave，切场景不会自动回收）。经 CleanupZombieModeOnDestroyRuntime 调用。</summary>
        internal static void ResetStaticCaches()
        {
            if (diskMesh != null)
            {
                Object.Destroy(diskMesh);
                diskMesh = null;
            }
            if (diskTexture != null)
            {
                Object.Destroy(diskTexture);
                diskTexture = null;
            }
            if (bandTexture != null)
            {
                Object.Destroy(bandTexture);
                bandTexture = null;
            }
        }
    }

    /// <summary>
    /// 地面圈的出现 / 读条 / 警示呼吸 / 消失。只收外部调用，动画播完 enabled = false，常态零开销。
    /// 走 unscaled 时间；暂停菜单开着时停推进（BossRushUI.IsGamePaused）。预警读条的进度由调用方给，
    /// 调用方（TimedRunScopedRuntime.TickRuntime）在模态 / 暂停时本来就不 tick，读条跟着停。
    /// </summary>
    internal sealed class ZombieModeZoneVisualFx : MonoBehaviour
    {
        private const float FadeInSeconds = 0.18f;
        private const float FadeOutSeconds = 0.18f;
        private const float GrowFrom = 0.9f;
        /// <summary>读条内圈相对外圈的强度：内圈的边带就是「涨潮前沿」，填满时与外圈边带重合。</summary>
        private const float FillAlphaFactor = 0.7f;
        private const float WarningCycleSeconds = 1.1f;

        private Renderer disk;
        private Transform fill;
        private Renderer fillRenderer;
        private LineRenderer[] rings = new LineRenderer[0];
        private Color[] ringColors = new Color[0];
        private Color baseColor;
        private Color warningColor;
        private Vector3 baseScale = Vector3.one;
        private float fadeIn;
        private float fadeOut = -1f;
        private bool warning;
        private float warningPhase;
        private float alphaFactor;
        private Color tint;

        internal void Initialize(Renderer renderer, Color color)
        {
            disk = renderer;
            baseColor = color;
            tint = color;
            baseScale = transform.localScale;
            fadeIn = 0f;
            fadeOut = -1f;
            alphaFactor = 0f;
            transform.localScale = baseScale * GrowFrom;
            Apply();
            enabled = true;
        }

        internal void AddRing(LineRenderer ring, Color color)
        {
            if (ring == null)
            {
                return;
            }
            // 边线是按世界尺寸挂上来的，而此刻父物体正处在出场缩放（0.9）里：把本地缩放按同一比例收一下，
            // 父物体长到 1 时边线正好落在真实半径上。
            float factor = baseScale.x != 0f ? transform.localScale.x / baseScale.x : 1f;
            if (ring.transform.parent == transform && factor > 0f && !Mathf.Approximately(factor, 1f))
            {
                ring.transform.localScale *= factor;
            }
            int count = rings.Length;
            System.Array.Resize(ref rings, count + 1);
            System.Array.Resize(ref ringColors, count + 1);
            rings[count] = ring;
            ringColors[count] = color;
            Apply();
        }

        internal void SetWarning(bool on, Color color)
        {
            warningColor = color;
            if (warning == on)
            {
                return;
            }
            warning = on;
            warningPhase = 0f;
            if (!warning)
            {
                tint = baseColor;
                Apply();
            }
            enabled = true;
        }

        /// <summary>预警读条：0 = 刚起手，1 = 结算。线性（读的是时间），内圈从圆心长到边带。</summary>
        internal void SetCountdown(float progress)
        {
            progress = Mathf.Clamp01(progress);
            if (fill == null)
            {
                if (progress >= 1f || fadeOut >= 0f)
                {
                    return;
                }
                CreateFill();
            }
            fill.localScale = new Vector3(progress, 1f, progress);
        }

        internal void BeginFadeOut()
        {
            if (fadeOut >= 0f)
            {
                return;
            }
            fadeOut = 0f;
            enabled = true;
        }

        private void CreateFill()
        {
            GameObject go = new GameObject("CountdownFill");
            go.transform.SetParent(transform, false);
            // 父物体 y 缩放是 height（约 0.02），0.5 个本地单位 ≈ 1 cm：略高于外圈，避免两张共面透明片来回排序。
            go.transform.localPosition = new Vector3(0f, 0.5f, 0f);
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = new Vector3(0f, 1f, 0f);
            go.AddComponent<MeshFilter>().sharedMesh = ZombieModeZoneVisuals.GetDiskMesh();
            MeshRenderer renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = disk != null ? disk.sharedMaterial : ZombieModeZoneVisuals.GetDiskMaterial();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.enabled = renderer.sharedMaterial != null;
            fill = go.transform;
            fillRenderer = renderer;
            Apply();
        }

        private void Update()
        {
            if (BossRushUI.IsGamePaused())
            {
                return;
            }

            float dt = Time.unscaledDeltaTime;
            bool busy = false;
            if (fadeOut >= 0f)
            {
                fadeOut += dt / FadeOutSeconds;
                if (fadeOut >= 1f)
                {
                    enabled = false;
                    Destroy(gameObject);
                    return;
                }
                alphaFactor = Mathf.Min(alphaFactor, 1f - BossRushUI.SmoothStep(fadeOut));
                busy = true;
            }
            else if (fadeIn < 1f)
            {
                fadeIn = Mathf.Min(1f, fadeIn + dt / FadeInSeconds);
                alphaFactor = BossRushUI.SmoothStep(fadeIn);
                transform.localScale = baseScale * Mathf.Lerp(GrowFrom, 1f, BossRushUI.EaseOut(fadeIn));
                busy = fadeIn < 1f;
            }

            if (warning && fadeOut < 0f)
            {
                warningPhase += dt / WarningCycleSeconds;
                if (warningPhase >= 1f)
                {
                    warningPhase -= 1f;
                }
                // 平滑呼吸（余弦），不是线性三角波；最暗时也有 45% 的警示色，读得出「快结束了」。
                float wave = 0.5f - 0.5f * Mathf.Cos(warningPhase * 2f * Mathf.PI);
                tint = Color.Lerp(baseColor, warningColor, 0.45f + 0.55f * wave);
                busy = true;
            }

            Apply();
            if (!busy)
            {
                enabled = false;
            }
        }

        private void Apply()
        {
            Color color = tint;
            color.a *= alphaFactor;
            ZombieModeZoneVisuals.SetColor(disk, color);
            if (fillRenderer != null)
            {
                Color fillColor = color;
                fillColor.a *= FillAlphaFactor;
                ZombieModeZoneVisuals.SetColor(fillRenderer, fillColor);
            }
            for (int i = 0; i < rings.Length; i++)
            {
                if (rings[i] == null)
                {
                    continue;
                }
                Color ringColor = ringColors[i];
                ringColor.a *= alphaFactor;
                rings[i].startColor = ringColor;
                rings[i].endColor = ringColor;
            }
        }
    }

    /// <summary>
    /// 补给 / 医疗终端的外观（UC-01 / VA-29）：真 NPC 模型挂到终端根物体下面当「皮」。
    /// 交互体（ZombieModeTemporaryNpcInteractable）、根上的胶囊碰撞体（交互与阻挡）与服务状态全部不动，
    /// 只把胶囊的网格渲染摘掉。模型上的碰撞体删掉、刚体改运动学，免得和根上的碰撞体打架或自己掉下去。
    /// 模型的 Animator 按预制体的默认状态播（站立待机），不挂真 NPC 的控制器、名牌、气泡与对话。
    /// </summary>
    internal static class ZombieModeServiceTerminalLook
    {
        internal static bool Dress(GameObject terminal, GameObject modelPrefab, string logTag)
        {
            if (terminal == null || modelPrefab == null)
            {
                return false;
            }

            GameObject model = null;
            try
            {
                model = Object.Instantiate(modelPrefab, terminal.transform, false);
                model.name = "TerminalLook";
                model.transform.localPosition = Vector3.zero;
                model.transform.localRotation = Quaternion.identity;
                model.SetActive(true);
                foreach (Transform child in model.GetComponentsInChildren<Transform>(true))
                {
                    child.gameObject.SetActive(true);
                }

                NPCCommonUtils.FixShaders(model, logTag);
                NPCCommonUtils.SetLayerRecursively(model, LayerMask.NameToLayer("Default"));
                Collider[] colliders = model.GetComponentsInChildren<Collider>(true);
                for (int i = 0; i < colliders.Length; i++)
                {
                    if (colliders[i] != null)
                    {
                        Object.Destroy(colliders[i]);
                    }
                }
                Rigidbody[] bodies = model.GetComponentsInChildren<Rigidbody>(true);
                for (int i = 0; i < bodies.Length; i++)
                {
                    if (bodies[i] != null)
                    {
                        bodies[i].isKinematic = true;
                    }
                }
                UnityEngine.AI.NavMeshAgent[] agents = model.GetComponentsInChildren<UnityEngine.AI.NavMeshAgent>(true);
                for (int i = 0; i < agents.Length; i++)
                {
                    if (agents[i] != null)
                    {
                        agents[i].enabled = false;
                    }
                }

                MeshRenderer capsuleRenderer = terminal.GetComponent<MeshRenderer>();
                if (capsuleRenderer != null)
                {
                    Object.Destroy(capsuleRenderer);
                }
                MeshFilter capsuleMesh = terminal.GetComponent<MeshFilter>();
                if (capsuleMesh != null)
                {
                    Object.Destroy(capsuleMesh);
                }
                return true;
            }
            catch (System.Exception e)
            {
                ModBehaviour.DevLog(logTag + " [WARNING] 终端外观挂载失败，退回胶囊: " + e.Message);
                if (model != null)
                {
                    Object.Destroy(model);
                }
                return false;
            }
        }
    }

    /// <summary>
    /// 净化点被拾取时 0.14 s 吸进玩家胸口（ease-in，越来越快）再销毁（UC-18 / VA-32）。
    /// 结算（加点）在调用方当帧就完成，这里只管表现；批量结算时每颗各自飞，没有额外粒子。
    /// </summary>
    internal sealed class ZombieModePickupAbsorbFx : MonoBehaviour
    {
        private const float Seconds = 0.14f;
        private Vector3 fromPosition;
        private Vector3 fromScale;
        private float progress;

        internal static void Play(GameObject point)
        {
            if (point == null)
            {
                return;
            }
            if (!point.activeInHierarchy)
            {
                Destroy(point);
                return;
            }

            ZombiePurificationPointController controller = point.GetComponent<ZombiePurificationPointController>();
            if (controller != null)
            {
                controller.enabled = false;
            }
            ZombieModePickupAbsorbFx fx = point.GetComponent<ZombieModePickupAbsorbFx>();
            if (fx == null)
            {
                fx = point.AddComponent<ZombieModePickupAbsorbFx>();
            }
            fx.fromPosition = point.transform.position;
            fx.fromScale = point.transform.localScale;
            fx.progress = 0f;
        }

        private void Update()
        {
            if (BossRushUI.IsGamePaused())
            {
                return;
            }

            progress = Mathf.Min(1f, progress + Time.unscaledDeltaTime / Seconds);
            CharacterMainControl player = CharacterMainControl.Main;
            Vector3 target = player != null ? player.transform.position + Vector3.up * 0.8f : fromPosition;
            float eased = progress * progress;
            transform.position = Vector3.Lerp(fromPosition, target, eased);
            transform.localScale = fromScale * (1f - eased);
            if (progress >= 1f)
            {
                enabled = false;
                Destroy(gameObject);
            }
        }
    }
}
