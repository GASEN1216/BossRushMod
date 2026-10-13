using System;
using ItemStatsSystem;
using UnityEngine;

namespace BossRush
{
    /// <summary>动态 prefab 持有图标副本，避免卸包后物品图标失效。</summary>
    internal sealed class PassTheBombIcon : MonoBehaviour
    {
        private Texture2D texture;
        private Sprite sprite;
        private int resourceOwnerId;

        internal static Sprite GetSprite(Item item, Sprite source)
        {
            if (item == null) return null;
            PassTheBombIcon owner = item.GetComponent<PassTheBombIcon>();
            if (owner == null) owner = item.gameObject.AddComponent<PassTheBombIcon>();
            return owner.EnsureSprite(source);
        }

        private Sprite EnsureSprite(Sprite source)
        {
            if (sprite != null) return sprite;
            if (source == null) return null;
            Texture2D created = null;
            try
            {
                // 生产贴图可能压缩且不可读，GPU 复制后由物品 prefab 管理独立资源。
                created = ProductionIconCache.CloneTexture(source.texture, "PassTheBomb_Icon");
                if (created == null) return null;
                created.hideFlags = HideFlags.DontSave;
                Rect rect = source.rect;
                Sprite result = Sprite.Create(created, rect,
                    new Vector2(source.pivot.x / rect.width, source.pivot.y / rect.height),
                    source.pixelsPerUnit, 0, SpriteMeshType.FullRect, source.border);
                result.hideFlags = HideFlags.DontSave;
                texture = created;
                sprite = result;
                resourceOwnerId = GetInstanceID();
                return sprite;
            }
            catch (Exception e)
            {
                if (created != null) UnityEngine.Object.Destroy(created);
                ModBehaviour.DevLog(PassTheBombConfig.LogPrefix + " 图标复制失败: " + e.Message);
                return null;
            }
        }

        private void OnDestroy()
        {
            // 实例会克隆此组件，只有分配资源的 owner 能释放共享图标。
            if (resourceOwnerId != GetInstanceID()) return;
            if (sprite != null) UnityEngine.Object.Destroy(sprite);
            if (texture != null) UnityEngine.Object.Destroy(texture);
            sprite = null;
            texture = null;
        }
    }
}
