using System;
using System.IO;
using UnityEngine;

namespace BossRush
{
    /// <summary>缺包/Dev 散图兜底的解码边界。成功后由调用方释放，失败不遗留原生对象。</summary>
    internal static class RawImageLoader
    {
        internal static Sprite LoadSprite(string path, string name)
        {
            Texture2D texture = null;
            Sprite sprite = null;
            bool retained = false;
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
                texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!texture.LoadImage(File.ReadAllBytes(path), true)) return null;
                texture.name = name; texture.hideFlags = HideFlags.DontSave;
                texture.wrapMode = TextureWrapMode.Clamp;
                sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(.5f, .5f));
                if (sprite == null) return null;
                sprite.name = name; sprite.hideFlags = HideFlags.DontSave;
                retained = true;
                return sprite;
            }
            catch (Exception e) { ModBehaviour.DevLog("[RawImageLoader] " + path + ": " + e.Message); return null; }
            finally
            {
                if (!retained)
                {
                    if (sprite != null) UnityEngine.Object.Destroy(sprite);
                    if (texture != null) UnityEngine.Object.Destroy(texture);
                }
            }
        }
    }
}
