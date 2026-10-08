using System;
using ItemStatsSystem;
using UnityEngine;

namespace BossRush
{
    /// <summary>由动态 prefab 拥有生产图标副本或程序化兜底，跨 Mod 热重载保持有效。</summary>
    internal sealed class EmptyMagazineMineIcon : MonoBehaviour
    {
        private const int Size = 128;
        private Texture2D texture;
        private Sprite sprite;
        private int resourceOwnerId;

        internal static Sprite GetSprite(Item item, Sprite source = null)
        {
            EmptyMagazineMineIcon owner = item.GetComponent<EmptyMagazineMineIcon>();
            if (owner == null) owner = item.gameObject.AddComponent<EmptyMagazineMineIcon>();
            return owner.EnsureSprite(source);
        }

        private Sprite EnsureSprite(Sprite source)
        {
            if (sprite != null) return sprite;
            if (source != null && TryCopyProductionSprite(source)) return sprite;
            Texture2D created = null;
            try
            {
                Color32[] pixels = new Color32[Size * Size];
                // 深色轮廓、斜面和槽纹组成废弹匣，黄铜绑带与红灯标出爆破装置。
                Quad(pixels, 22, 23, 76, 12, 109, 39, 55, 51, 23, 29, 29);
                Quad(pixels, 23, 30, 74, 20, 74, 92, 23, 103, 57, 72, 65);
                Quad(pixels, 74, 20, 105, 44, 105, 112, 74, 92, 35, 46, 43);
                Quad(pixels, 23, 103, 74, 92, 105, 112, 53, 122, 111, 130, 108);
                Quad(pixels, 31, 34, 37, 33, 37, 91, 31, 93, 29, 42, 37);
                Quad(pixels, 44, 31, 50, 30, 50, 88, 44, 90, 29, 42, 37);
                Quad(pixels, 58, 29, 64, 27, 64, 85, 58, 87, 29, 42, 37);
                Quad(pixels, 22, 53, 75, 42, 75, 61, 22, 72, 216, 150, 55);
                Quad(pixels, 75, 42, 107, 65, 107, 83, 75, 61, 143, 91, 33);
                Quad(pixels, 38, 53, 50, 51, 50, 64, 38, 67, 31, 36, 33);
                Quad(pixels, 39, 55, 49, 53, 49, 63, 39, 65, 239, 60, 43);
                Quad(pixels, 40, 61, 45, 60, 45, 64, 40, 65, 255, 190, 136);
                Quad(pixels, 48, 104, 78, 98, 91, 106, 61, 112, 31, 39, 36);
                created = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
                created.name = "EmptyMagazineMine_Icon";
                created.hideFlags = HideFlags.DontSave;
                created.filterMode = FilterMode.Bilinear;
                created.wrapMode = TextureWrapMode.Clamp;
                created.SetPixels32(pixels);
                created.Apply(false, true);
                Sprite result = Sprite.Create(created, new Rect(0, 0, Size, Size), new Vector2(0.5f, 0.5f), Size);
                result.hideFlags = HideFlags.DontSave;
                texture = created;
                sprite = result;
                resourceOwnerId = GetInstanceID();
                return sprite;
            }
            catch (Exception e)
            {
                if (created != null) UnityEngine.Object.Destroy(created);
                ModBehaviour.DevLog("[EmptyMagazineMine] 图标生成失败: " + e.Message);
                return null;
            }
        }

        private bool TryCopyProductionSprite(Sprite source)
        {
            Texture2D created = null;
            try
            {
                // Instantiate 保留生产贴图的压缩格式，不要求开启 Read/Write，也不读回像素。
                // 只复制一次；直接借用 Sprite 或只复制 Sprite 都会继续依赖原包的 Texture。
                created = UnityEngine.Object.Instantiate(source.texture);
                created.name = "EmptyMagazineMine_ProductionIcon";
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
                ModBehaviour.DevLog("[EmptyMagazineMine] 生产图标复制失败，使用程序化图标: " + e.Message);
                return false;
            }
        }

        private static void Quad(Color32[] pixels, int ax, int ay, int bx, int by,
            int cx, int cy, int dx, int dy, byte r, byte g, byte b)
        {
            Vector2[] points = { new Vector2(ax, ay), new Vector2(bx, by), new Vector2(cx, cy), new Vector2(dx, dy) };
            int minX = Math.Max(0, Math.Min(Math.Min(ax, bx), Math.Min(cx, dx)));
            int maxX = Math.Min(Size - 1, Math.Max(Math.Max(ax, bx), Math.Max(cx, dx)));
            int minY = Math.Max(0, Math.Min(Math.Min(ay, by), Math.Min(cy, dy)));
            int maxY = Math.Min(Size - 1, Math.Max(Math.Max(ay, by), Math.Max(cy, dy)));
            Color32 color = new Color32(r, g, b, 255);
            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    bool inside = false;
                    for (int i = 0, j = 3; i < 4; j = i++)
                    {
                        Vector2 a = points[i], other = points[j];
                        if ((a.y > y) != (other.y > y)
                            && x < (other.x - a.x) * (y - a.y) / (other.y - a.y) + a.x) inside = !inside;
                    }
                    if (inside) pixels[y * Size + x] = color;
                }
            }
        }

        private void OnDestroy()
        {
            // Unity 会克隆 prefab 上的组件；实例销毁不能释放其他物品仍在使用的共享图标。
            if (resourceOwnerId != GetInstanceID()) return;
            if (sprite != null) UnityEngine.Object.Destroy(sprite);
            if (texture != null) UnityEngine.Object.Destroy(texture);
            sprite = null;
            texture = null;
        }
    }
}
