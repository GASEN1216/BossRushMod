using System;
using ItemStatsSystem;
using UnityEngine;

namespace BossRush
{
    /// <summary>动态 prefab 拥有生产图标副本或程序化兜底，实例销毁不释放共享贴图。</summary>
    internal sealed class ReloadMetronomeIcon : MonoBehaviour
    {
        private const int Size = 128;
        private Texture2D texture;
        private Sprite sprite;
        private int resourceOwnerId;

        internal static Sprite GetSprite(Item item)
        {
            ReloadMetronomeIcon owner = item.GetComponent<ReloadMetronomeIcon>();
            if (owner == null) owner = item.gameObject.AddComponent<ReloadMetronomeIcon>();
            return owner.EnsureSprite();
        }

        private Sprite EnsureSprite()
        {
            if (sprite != null) return sprite;
            Sprite authored = ReloadMetronomeAssets.Icon;
            if (authored != null && TryCopyProductionSprite(authored)) return sprite;
            Texture2D created = null;
            try
            {
                Color[] pixels = new Color[Size * Size];
                for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    // 四点覆盖抗锯齿，透明底不带白框；只在首次配置时生成。
                    Color sum = Color.clear;
                    for (int sy = 0; sy < 2; sy++)
                    for (int sx = 0; sx < 2; sx++)
                        sum += Pixel((x + (sx + 0.5f) * 0.5f) / Size, (y + (sy + 0.5f) * 0.5f) / Size);
                    Color color = sum * 0.25f;
                    if (color.a > 0f) { color.r /= color.a; color.g /= color.a; color.b /= color.a; }
                    pixels[y * Size + x] = color;
                }
                created = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
                created.name = "ReloadMetronome_Icon";
                created.hideFlags = HideFlags.DontSave;
                created.filterMode = FilterMode.Bilinear;
                created.wrapMode = TextureWrapMode.Clamp;
                created.SetPixels(pixels);
                created.Apply(false, true);
                sprite = Sprite.Create(created, new Rect(0, 0, Size, Size), new Vector2(0.5f, 0.5f), Size);
                sprite.hideFlags = HideFlags.DontSave;
                texture = created;
                resourceOwnerId = GetInstanceID();
                return sprite;
            }
            catch (Exception e)
            {
                if (created != null) UnityEngine.Object.Destroy(created);
                ModBehaviour.DevLog("[ReloadMetronome] 图标生成失败: " + e.Message);
                return null;
            }
        }

        private bool TryCopyProductionSprite(Sprite source)
        {
            Texture2D created = null;
            try
            {
                // 持久 prefab 不能借用会随 Mod 卸载的包贴图；不可读 BC7 贴图走 GPU 复制。
                created = ProductionIconCache.CloneTexture(source.texture, "ReloadMetronome_ProductionIcon");
                if (created == null) return false;
                created.hideFlags = HideFlags.DontSave;
                Rect rect = source.rect;
                Sprite result = Sprite.Create(created, rect,
                    new Vector2(source.pivot.x / rect.width, source.pivot.y / rect.height),
                    source.pixelsPerUnit, 0, SpriteMeshType.FullRect, source.border);
                result.hideFlags = HideFlags.DontSave;
                texture = created;
                sprite = result;
                resourceOwnerId = GetInstanceID();
                return true;
            }
            catch (Exception e)
            {
                if (created != null) UnityEngine.Object.Destroy(created);
                ModBehaviour.DevLog("[ReloadMetronome] 生产图标复制失败，使用程序化图标: " + e.Message);
                return false;
            }
        }

        private static Color Pixel(float x, float y)
        {
            Color color = Color.clear;
            float half = 0.36f - 0.27f * y;
            if (y >= 0.10f && y <= 0.89f && Math.Abs(x - 0.5f) < half)
            {
                color = new Color(0.13f, 0.17f, 0.18f, 1f);
                if (Math.Abs(x - 0.5f) > half - 0.032f || y < 0.15f || y > 0.85f)
                    color = new Color(0.79f, 0.55f, 0.25f, 1f);
                if (y > 0.24f && y < 0.78f && Math.Abs(x - 0.5f) < 0.048f)
                    color = new Color(0.04f, 0.06f, 0.07f, 1f);
                if (x > 0.56f && x < 0.61f && y > 0.32f && y < 0.77f && (int)(y * 40f) % 3 == 0)
                    color = new Color(0.91f, 0.79f, 0.52f, 1f);
            }
            if (y > 0.25f && y < 0.92f && Math.Abs(x - (0.50f - (y - 0.27f) * 0.26f)) < 0.012f)
                color = new Color(0.94f, 0.88f, 0.68f, 1f);
            if (y > 0.60f && y < 0.72f && Math.Abs(x - (0.50f - (y - 0.27f) * 0.26f)) < 0.065f)
                color = new Color(0.95f, 0.67f, 0.26f, 1f);
            float dx = x - 0.50f, dy = y - 0.27f;
            if (dx * dx + dy * dy < 0.003f) color = new Color(0.27f, 0.70f, 0.65f, 1f);
            return color;
        }

        private void OnDestroy()
        {
            if (resourceOwnerId != GetInstanceID()) return;
            if (sprite != null) UnityEngine.Object.Destroy(sprite);
            if (texture != null) UnityEngine.Object.Destroy(texture);
            sprite = null;
            texture = null;
        }
    }
}
