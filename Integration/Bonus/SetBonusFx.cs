// ============================================================================
// SetBonusFx.cs - 套装表现层的独立类型：眼光、龙套装冲刺残影
// ============================================================================
// 为什么独立成文件（AGENTS 4.15）：这些只跟灯、精灵、粒子打交道，不读模式状态；
// 留在 partial class ModBehaviour 上只会继续堆高宿主（ModBehaviourPartialBudgetGuard 已顶格）。
// 宿主（SetBonusVisuals / DragonSetBonus / DragonSetBonus_Dash）只保留一行创建与销毁。
//
// 2026-09-23 审美修：
//   VA-06 眼光：旧版每只眼一盏 25 cm 的点光，点光本身看不见，只在头盔正面烫出一块偏色光斑；
//         雷霆的跳变按帧掷骰（144 fps 每秒约 6 次、60 fps 约 2 次）。现在每只眼一个面朝镜头的
//         6 cm HDR 小亮点（真的看得见两点眼光），两盏灯合成一盏 0.6 m 的补光，只给脸打一点光；
//         闪烁改为按时间的 Perlin 噪声，与帧率无关。
//   VA-07 冲刺残影：旧版是 2×3 m 的橙色实心椭圆，面片法线跟角色朝向走（侧对镜头时变成一条线），
//         每个残影一盏灯。现在是面朝镜头、角色大小（约 0.9×1.5 m）的暖橙柔光 + 一小团上飘的烟缕，
//         只有第一个残影带一盏弱灯。
//
// 生命周期（AGENTS 4.12）：只在穿齐套装 / 冲刺时创建；眼光随套装停用销毁，残影自己淡出销毁。
// ============================================================================

using UnityEngine;

namespace BossRush
{
    /// <summary>套装眼光：两颗面朝镜头的 HDR 亮点 + 一盏弱补光。挂在头骨下，随套装停用销毁。</summary>
    internal sealed class SetBonusEyeGlow : MonoBehaviour
    {
        private const float DotSize = 0.06f;
        private const float EyeSpacing = 0.08f;

        private Light _light;
        private Transform _left;
        private Transform _right;
        private SpriteRenderer _leftRenderer;
        private SpriteRenderer _rightRenderer;
        private Color _color;
        private float _lightIntensity;
        private bool _flicker;
        private float _seed;

        /// <summary>
        /// 在 head 下建一对眼光，返回根节点（调用方持有并在停用时销毁）。
        /// lightIntensity 是补光的基准亮度（建议 1.2–2）；flicker=true 为电闪式，否则慢呼吸。
        /// </summary>
        internal static GameObject Create(Transform head, Color color, float lightIntensity, bool flicker)
        {
            if (head == null) return null;
            GameObject root = new GameObject("SetBonusEyeEffect");
            root.transform.SetParent(head, false);
            root.transform.localPosition = new Vector3(0f, 0.15f, 0.2f);
            SetBonusEyeGlow glow = root.AddComponent<SetBonusEyeGlow>();
            glow.Build(color, lightIntensity, flicker);
            return root;
        }

        private void Build(Color color, float lightIntensity, bool flicker)
        {
            _color = color;
            _lightIntensity = Mathf.Clamp(lightIntensity, 0.5f, 2.5f);
            _flicker = flicker;
            _seed = Random.value * 10f;

            _light = gameObject.AddComponent<Light>();
            _light.type = LightType.Point;
            _light.color = new Color(color.r, color.g, color.b);
            _light.range = 0.6f;
            _light.intensity = _lightIntensity;
            _light.shadows = LightShadows.None;

            Sprite dot = BossRushFxKit.GetShapeSprite(BossRushParticleShape.GlowDot);
            Material material = dot != null
                ? BossRushFxKit.GetSpriteMaterial(dot, BossRushFxBlend.Additive, BossRushFxKit.GainHot)
                : null;
            if (material != null)
            {
                _leftRenderer = CreateDot("LeftEye", new Vector3(-EyeSpacing, 0f, 0f), dot, material);
                _rightRenderer = CreateDot("RightEye", new Vector3(EyeSpacing, 0f, 0f), dot, material);
                _left = _leftRenderer.transform;
                _right = _rightRenderer.transform;
            }
            Apply(1f);
        }

        private SpriteRenderer CreateDot(string name, Vector3 localPosition, Sprite sprite, Material material)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = localPosition;
            go.transform.localScale = new Vector3(DotSize, DotSize, 1f);
            SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sharedMaterial = material;
            sr.sortingOrder = 110;
            return sr;
        }

        private void Update()
        {
            float t = Time.time + _seed;
            float factor;
            if (_flicker)
            {
                // 电闪：底子是两个正弦的乘积，偶发跳亮由 Perlin 噪声决定（按时间，不按帧掷骰）
                float jitter = Mathf.Abs(Mathf.Sin(t * 9f) * Mathf.Sin(t * 2.3f + 1f));
                factor = 0.7f + 0.35f * jitter;
                if (Mathf.PerlinNoise(t * 8f, _seed) > 0.85f) factor += 0.6f;
            }
            else
            {
                factor = 0.85f + 0.2f * Mathf.Sin(t * 2f);
            }
            Apply(factor);
        }

        private void LateUpdate()
        {
            Camera camera = Camera.main;
            if (camera == null) return;
            Quaternion facing = camera.transform.rotation;
            if (_left != null) _left.rotation = facing;
            if (_right != null) _right.rotation = facing;
        }

        private void Apply(float factor)
        {
            if (_light != null) _light.intensity = _lightIntensity * factor;
            float alpha = Mathf.Clamp01(0.55f + 0.4f * factor);
            float scale = DotSize * Mathf.Lerp(0.9f, 1.15f, Mathf.Clamp01(factor - 0.6f));
            Color c = new Color(_color.r, _color.g, _color.b, alpha);
            if (_leftRenderer != null)
            {
                _leftRenderer.color = c;
                _left.localScale = new Vector3(scale, scale, 1f);
            }
            if (_rightRenderer != null)
            {
                _rightRenderer.color = c;
                _right.localScale = new Vector3(scale, scale, 1f);
            }
        }
    }

    /// <summary>龙套装冲刺残影：面朝镜头的暖橙柔光 + 一小团上飘烟缕，0.5 秒淡出自毁。</summary>
    internal sealed class DragonDashAfterimageFx : MonoBehaviour
    {
        private const float Duration = 0.5f;

        private SpriteRenderer _renderer;
        private Light _light;
        private Color _color;
        private Vector3 _baseScale;
        private float _lightIntensity;
        private float _elapsed;

        /// <summary>
        /// 在 position（脚底）处放一个残影。withLight 只给一次冲刺的第一个残影开，
        /// 一次冲刺三盏动态光既贵又把地面刷成一片橙。
        /// </summary>
        internal static GameObject Spawn(Vector3 position, Color color, bool withLight)
        {
            GameObject go = new GameObject("DragonAfterimage");
            go.transform.position = position + Vector3.up * 0.85f;

            DragonDashAfterimageFx fx = go.AddComponent<DragonDashAfterimageFx>();
            fx._color = color;
            fx._baseScale = new Vector3(0.9f, 1.5f, 1f);

            Sprite sprite = BossRushFxKit.GetSoftCircleSprite();
            Material material = sprite != null
                ? BossRushFxKit.GetSpriteMaterial(sprite, BossRushFxBlend.Additive, BossRushFxKit.GainBright)
                : null;
            if (material != null)
            {
                fx._renderer = go.AddComponent<SpriteRenderer>();
                fx._renderer.sprite = sprite;
                fx._renderer.sharedMaterial = material;
                fx._renderer.color = color;
                fx._renderer.sortingOrder = 100;
            }
            go.transform.localScale = fx._baseScale;

            if (withLight)
            {
                fx._light = go.AddComponent<Light>();
                fx._light.type = LightType.Point;
                fx._light.color = new Color(color.r, color.g, color.b);
                fx._light.intensity = 1.2f;
                fx._light.range = 1.5f;
                fx._light.shadows = LightShadows.None;
                fx._lightIntensity = 1.2f;
            }

            // 烟缕：几缕暖色烟从残影里上飘，比平面精灵更像「烧过去留下的热气」
            BossRushFxBurst smoke = BossRushFxKit.Dust(new Color(1f, 0.55f, 0.25f, 0.3f), 6);
            smoke.Radial = false;
            smoke.Upward = true;
            smoke.FlatOnGround = false;
            smoke.SpeedMin = 0.3f;
            smoke.SpeedMax = 0.7f;
            smoke.SizeMin = 0.3f;
            smoke.SizeMax = 0.5f;
            smoke.LifeMin = 0.45f;
            smoke.LifeMax = 0.7f;
            smoke.GrowTo = 1.6f;
            smoke.Drag = 1.5f;
            smoke.Blend = BossRushFxBlend.Additive;
            smoke.Gain = BossRushFxKit.GainBright;
            smoke.Main = new Color(0.9f, 0.4f, 0.15f, 0.22f);
            smoke.End = new Color(0.35f, 0.18f, 0.12f, 0f);
            BossRushFxKit.PlayBurst(position + Vector3.up * 0.6f, smoke);
            return go;
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            _elapsed += dt;
            float t = Mathf.Clamp01(_elapsed / Duration);
            float fade = 1f - BossRushUI.SmoothStep(t);
            if (_renderer != null)
            {
                _renderer.color = new Color(_color.r, _color.g, _color.b, _color.a * fade);
            }
            if (_light != null)
            {
                _light.intensity = _lightIntensity * fade * fade;
            }
            // 轻微向外散开
            float grow = Mathf.Lerp(1f, 1.2f, BossRushUI.EaseOut(t));
            transform.localScale = new Vector3(_baseScale.x * grow, _baseScale.y * grow, 1f);
            if (t >= 1f) Destroy(gameObject);
        }

        private void LateUpdate()
        {
            Camera camera = Camera.main;
            if (camera != null) transform.rotation = camera.transform.rotation;
        }
    }
}
