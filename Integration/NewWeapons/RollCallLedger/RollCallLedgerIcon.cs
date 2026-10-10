using System;
using ItemStatsSystem;
using UnityEngine;

namespace BossRush
{
    /// <summary>动态 prefab 拥有图标副本，避免 Mod 热重载卸包后物品图标失效。</summary>
    internal sealed class RollCallLedgerIcon : MonoBehaviour
    {
        private Texture2D texture;
        private Sprite sprite;
        private int resourceOwnerId;

        internal static Sprite GetSprite(Item item, Sprite source)
        {
            if (item == null) return null;
            RollCallLedgerIcon owner = item.GetComponent<RollCallLedgerIcon>();
            if (owner == null) owner = item.gameObject.AddComponent<RollCallLedgerIcon>();
            return owner.EnsureSprite(source);
        }

        private Sprite EnsureSprite(Sprite source)
        {
            if (sprite != null) return sprite;
            if (source == null) return null;
            Texture2D created = null;
            try
            {
                // 压缩生产图标不可读，必须 GPU 复制；不能 Instantiate 出没有像素的空壳。
                created = ProductionIconCache.CloneTexture(source.texture, "RollCallLedger_Icon");
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
                ModBehaviour.DevLog(RollCallLedgerConfig.LogPrefix + " 图标复制失败: " + e.Message);
                return null;
            }
        }

        private void OnDestroy()
        {
            // 物品实例会克隆此组件，只有分配资源的 owner 可以释放共享图标。
            if (resourceOwnerId != GetInstanceID()) return;
            if (sprite != null) UnityEngine.Object.Destroy(sprite);
            if (texture != null) UnityEngine.Object.Destroy(texture);
            sprite = null;
            texture = null;
        }
    }
}
