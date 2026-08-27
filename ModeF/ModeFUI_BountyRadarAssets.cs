using System;
using UnityEngine;

namespace BossRush
{
    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        private static Sprite GetModeFBountyRadarRegularSprite()
        {
            if (modeFBountyRadarRegularSprite == null)
            {
                modeFBountyRadarRegularSprite = LoadModeFBountyRadarSpriteFromFile(
                    MODEF_BOUNTY_RADAR_REGULAR_SPRITE_PATH,
                    MODEF_BOUNTY_RADAR_REGULAR_SPRITE_PATH_LEGACY);
                if (modeFBountyRadarRegularSprite == null)
                {
                    modeFBountyRadarRegularSprite = CreateModeFBountyRadarSprite(
                        new Color(0.95f, 0.28f, 0.18f, 0.18f),
                        new Color(1f, 0.72f, 0.32f, 0.95f),
                        0.22f,
                        0.40f);
                }
            }

            return modeFBountyRadarRegularSprite;
        }

        private static Sprite GetModeFBountyRadarLeaderSprite()
        {
            if (modeFBountyRadarLeaderSprite == null)
            {
                modeFBountyRadarLeaderSprite = LoadModeFBountyRadarSpriteFromFile(
                    MODEF_BOUNTY_RADAR_LEADER_SPRITE_PATH,
                    MODEF_BOUNTY_RADAR_LEADER_SPRITE_PATH_LEGACY);
                if (modeFBountyRadarLeaderSprite == null)
                {
                    modeFBountyRadarLeaderSprite = CreateModeFBountyRadarSprite(
                        new Color(0.95f, 0.78f, 0.18f, 0.20f),
                        new Color(1f, 0.93f, 0.55f, 1f),
                        0.18f,
                        0.44f);
                }
            }

            return modeFBountyRadarLeaderSprite;
        }

        private static Sprite GetModeFBountyRadarGuideSprite()
        {
            if (modeFBountyRadarGuideSprite == null)
            {
                modeFBountyRadarGuideSprite = CreateModeFBountyRadarSprite(
                    new Color(1f, 1f, 1f, 0f),
                    new Color(1f, 1f, 1f, 0.20f),
                    0.47f,
                    0.50f);
            }

            return modeFBountyRadarGuideSprite;
        }

        private static Sprite GetModeFBountyRadarArrowSprite()
        {
            if (modeFBountyRadarArrowSprite != null)
            {
                return modeFBountyRadarArrowSprite;
            }

            const int width = 32;
            const int height = 24;
            Texture2D texture = new Texture2D(width, height, TextureFormat.ARGB32, false);
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;

            Color clear = new Color(0f, 0f, 0f, 0f);
            for (int y = 0; y < height; y++)
            {
                float normalizedY = y / (height - 1f);
                float allowedHalfWidth = (1f - normalizedY) * (width - 2f) * 0.5f;
                for (int x = 0; x < width; x++)
                {
                    float offset = Mathf.Abs(x - (width - 1f) * 0.5f);
                    texture.SetPixel(x, y, offset <= allowedHalfWidth ? Color.white : clear);
                }
            }

            texture.Apply();
            modeFBountyRadarArrowSprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, texture.width, texture.height),
                new Vector2(0.5f, 0.5f),
                texture.width);
            return modeFBountyRadarArrowSprite;
        }

        private static Sprite CreateModeFBountyRadarSprite(Color fillColor, Color ringColor, float fillRadius, float ringRadius)
        {
            const int textureSize = 128;
            Texture2D texture = new Texture2D(textureSize, textureSize, TextureFormat.ARGB32, false);
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;

            float halfSize = (textureSize - 1) * 0.5f;
            Color clear = new Color(0f, 0f, 0f, 0f);
            for (int y = 0; y < textureSize; y++)
            {
                for (int x = 0; x < textureSize; x++)
                {
                    float normalizedX = (x - halfSize) / halfSize;
                    float normalizedY = (y - halfSize) / halfSize;
                    float distance = Mathf.Sqrt(normalizedX * normalizedX + normalizedY * normalizedY);

                    Color pixel = clear;
                    if (distance <= ringRadius)
                    {
                        if (distance <= fillRadius)
                        {
                            pixel = fillColor;
                        }
                        else
                        {
                            float ringBlend = Mathf.InverseLerp(fillRadius, ringRadius, distance);
                            pixel = Color.Lerp(fillColor, ringColor, Mathf.Clamp01(ringBlend));
                        }
                    }

                    texture.SetPixel(x, y, pixel);
                }
            }

            texture.Apply();
            return Sprite.Create(
                texture,
                new Rect(0f, 0f, texture.width, texture.height),
                new Vector2(0.5f, 0.5f),
                texture.width);
        }

        private static Sprite LoadModeFBountyRadarSpriteFromFile(params string[] relativePaths)
        {
            if (relativePaths == null || relativePaths.Length <= 0)
            {
                return null;
            }

            for (int i = 0; i < relativePaths.Length; i++)
            {
                string relativePath = relativePaths[i];
                if (string.IsNullOrEmpty(relativePath))
                {
                    continue;
                }

                try
                {
                    Sprite sprite = ItemFactory.GetSpriteFromFile(relativePath);
                    if (sprite != null)
                    {
                        DevLog("[ModeF] 已加载悬赏雷达贴图: " + relativePath);
                        return sprite;
                    }
                }
                catch (Exception e)
                {
                    DevLog("[ModeF] 加载悬赏雷达贴图失败: " + relativePath + " - " + e.Message);
                }
            }

            return null;
        }
    }
}
