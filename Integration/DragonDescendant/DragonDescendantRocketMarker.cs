// ============================================================================
// DragonDescendantRocketMarker.cs - 火箭弹落点预警（纯表现）
// ============================================================================
// 边界环画在真实爆炸半径上、全程不动；环内的填充随蓄力从圆心长到边界，给玩家读「还剩多久」。
// 口径同天空岛头目圈与随机事件空投标记：共享软边精灵 + 共享材质，不 new 材质。

using UnityEngine;

namespace BossRush
{
    internal sealed class DragonDescendantRocketMarker : MonoBehaviour
    {
        /// <summary>共享细环精灵的可见半径（整张图 1 个单位宽），同 RandomEventAirdropMarker。</summary>
        private const float RingSpriteRadius = 0.41f;
        private const float SoftCircleSpriteRadius = 0.5f;
        private const float GroundLift = 0.08f;
        private const float FadeInSeconds = 0.12f;
        private const float FadeOutSeconds = 0.18f;
        private const float FillAlpha = 0.32f;

        private static readonly Color RimColor = new Color(1f, 0.52f, 0.16f, 1f);
        private static readonly Color FillColor = new Color(1f, 0.32f, 0.08f, 1f);

        private SpriteRenderer _ring;
        private SpriteRenderer _fill;
        private float _radius;
        private float _chargeSeconds;
        private float _age;
        private DragonDescendantAbilityController _owner;
        private LevelManager _level;
        private int _sceneHandle;

        internal static GameObject Create(Vector3 groundPosition, float radius, float chargeSeconds,
            DragonDescendantAbilityController owner, LevelManager level, int sceneHandle)
        {
            Sprite ringSprite = BossRushProceduralSprites.GetRingSprite();
            Material ringMaterial = BossRushFxKit.GetSpriteMaterial(ringSprite, BossRushFxBlend.Additive, BossRushFxKit.GainBright);
            if (ringSprite == null || ringMaterial == null) return null;

            GameObject root = new GameObject("DragonDescendantRocketMarker");
            try
            {
                root.transform.position = groundPosition + Vector3.up * GroundLift;
                DragonDescendantRocketMarker marker = root.AddComponent<DragonDescendantRocketMarker>();
                marker._owner = owner;
                marker._level = level;
                marker._sceneHandle = sceneHandle;
                marker._radius = Mathf.Max(0.1f, radius);
                marker._chargeSeconds = Mathf.Max(0.05f, chargeSeconds);
                marker._ring = CreateFlatSprite(root.transform, "Rim", ringSprite, ringMaterial);
                float ringScale = marker._radius / RingSpriteRadius;
                marker._ring.transform.localScale = new Vector3(ringScale, ringScale, 1f);

                Sprite fillSprite = BossRushFxKit.GetSoftCircleSprite();
                Material fillMaterial = BossRushFxKit.GetSpriteMaterial(fillSprite, BossRushFxBlend.Alpha, BossRushFxKit.GainSoft);
                if (fillSprite != null && fillMaterial != null)
                {
                    marker._fill = CreateFlatSprite(root.transform, "Fill", fillSprite, fillMaterial);
                }
                marker.Apply(0f, 0f);
                return root;
            }
            catch
            {
                Destroy(root);
                throw;
            }
        }

        private static SpriteRenderer CreateFlatSprite(Transform parent, string name, Sprite sprite, Material material)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            SpriteRenderer renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return renderer;
        }

        private void Update()
        {
            // Unity 停止协程时不保证执行 iterator 的 finally；预警自身也持有相同的 owner 门。
            if (_owner == null || !_owner.CanCompleteRocket(_level, _sceneHandle))
            {
                Destroy(gameObject);
                return;
            }
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            _age += dt;

            float envelope = BossRushUI.SmoothStep(_age / FadeInSeconds);
            float fadeOut = _age - _chargeSeconds;
            if (fadeOut > 0f)
            {
                if (fadeOut >= FadeOutSeconds)
                {
                    Destroy(gameObject);
                    return;
                }
                envelope *= 1f - BossRushUI.SmoothStep(fadeOut / FadeOutSeconds);
            }

            float charge = Mathf.Clamp01(_age / _chargeSeconds);
            Apply(envelope, charge);
        }

        private void Apply(float envelope, float charge)
        {
            if (_ring != null)
            {
                // 越接近爆炸越亮，最后 0.15 s 急促闪一下
                float urgency = Mathf.Lerp(0.55f, 1f, charge);
                if (charge > 0.8f) urgency *= 0.75f + 0.25f * Mathf.Sin(_age * 60f);
                _ring.color = new Color(RimColor.r, RimColor.g, RimColor.b, envelope * urgency);
            }
            if (_fill != null)
            {
                float fillRadius = _radius * BossRushUI.EaseOut(charge);
                float scale = Mathf.Max(0.001f, fillRadius / SoftCircleSpriteRadius);
                _fill.transform.localScale = new Vector3(scale, scale, 1f);
                _fill.color = new Color(FillColor.r, FillColor.g, FillColor.b, envelope * FillAlpha);
            }
        }
    }
}
