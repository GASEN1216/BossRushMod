using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;

namespace BossRush
{
    /// <summary>程序化废弹匣、固定半径预警圈与收缩引信。生命周期完全由单枚地雷 owner 控制。</summary>
    internal sealed class EmptyMagazineMineFx : MonoBehaviour
    {
        private static readonly Color Amber = new Color(1f, 0.62f, 0.15f, 0.8f);
        private static readonly Color Hot = new Color(1f, 0.22f, 0.08f, 0.95f);
        private Transform magazine;
        private Mesh magazineMesh;
        private LineRenderer boundary;
        private LineRenderer fuse;
        private SpriteRenderer lamp;
        private int lastBeep = -1;
        private MethodInfo audioPost;

        internal static EmptyMagazineMineFx Create(Vector3 position, float yaw, float radius)
        {
            GameObject go = null;
            try
            {
                go = new GameObject("EmptyMagazineMine_Fx");
                go.transform.position = position;
                EmptyMagazineMineFx fx = go.AddComponent<EmptyMagazineMineFx>();
                fx.Build(yaw, radius);
                fx.ShowFuse(0f, EmptyMagazineMineConfig.FuseSeconds);
                return fx;
            }
            catch (Exception e)
            {
                if (go != null) UnityEngine.Object.Destroy(go);
                ModBehaviour.DevLog("[EmptyMagazineMine] 地雷表现创建失败: " + e.Message);
                return null;
            }
        }

        private void Build(float yaw, float radius)
        {
            // 沿用项目调用官方音频的反射边界，避免引用 Post 返回值的 FMOD 程序集。
            audioPost = typeof(Duckov.AudioManager).GetMethod("Post", BindingFlags.Public | BindingFlags.Static,
                null, new Type[] { typeof(string), typeof(GameObject) }, null);
            GameObject body = new GameObject("SpentMagazine");
            body.transform.SetParent(transform, false);
            body.transform.localRotation = Quaternion.Euler(0f, yaw + 25f, 0f);
            magazine = body.transform;
            Material matte = BossRushFxMaterials.Get(BossRushFxBlend.Alpha, Texture2D.whiteTexture);
            if (matte != null)
            {
                magazineMesh = BuildMagazineMesh();
                body.AddComponent<MeshFilter>().sharedMesh = magazineMesh;
                MeshRenderer renderer = body.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = matte;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }

            Material lineMaterial = BossRushFxMaterials.Get(BossRushFxBlend.Additive, Texture2D.whiteTexture);
            if (lineMaterial != null)
            {
                boundary = CreateRing("BlastBoundary", radius, 0.04f, lineMaterial);
                fuse = CreateRing("FuseCountdown", radius, 0.055f, lineMaterial);
            }
            Sprite sprite = BossRushFxKit.GetSoftCircleSprite();
            Material lampMaterial = BossRushFxKit.GetSpriteMaterial(sprite, BossRushFxBlend.Additive, BossRushFxKit.GainBright);
            if (sprite != null && lampMaterial != null)
            {
                GameObject lampObject = new GameObject("WarningLamp");
                lampObject.transform.SetParent(magazine, false);
                lampObject.transform.localPosition = new Vector3(0f, 0.145f, 0.12f);
                lampObject.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                lampObject.transform.localScale = Vector3.one * 0.18f;
                lamp = lampObject.AddComponent<SpriteRenderer>();
                lamp.sprite = sprite;
                lamp.sharedMaterial = lampMaterial;
            }
        }

        private LineRenderer CreateRing(string name, float radius, float width, Material material)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.up * 0.07f;
            LineRenderer line = go.AddComponent<LineRenderer>();
            line.sharedMaterial = material;
            line.useWorldSpace = false;
            line.loop = true;
            line.positionCount = 64;
            line.startWidth = width;
            line.endWidth = width;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            for (int i = 0; i < 64; i++)
            {
                float angle = i * Mathf.PI * 2f / 64f;
                line.SetPosition(i, new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius));
            }
            return line;
        }

        internal void ShowFuse(float elapsed, float duration)
        {
            float progress = Mathf.Clamp01(elapsed / Mathf.Max(0.01f, duration));
            Color color = Color.Lerp(Amber, Hot, progress);
            float pulse = 0.7f + 0.3f * Mathf.Sin(elapsed * (10f + progress * 22f));
            if (boundary != null)
            {
                Color edge = color;
                edge.a = 0.38f + 0.18f * pulse;
                boundary.startColor = edge;
                boundary.endColor = edge;
            }
            if (fuse != null)
            {
                fuse.transform.localScale = new Vector3(1f - progress, 1f, 1f - progress);
                fuse.startColor = color;
                fuse.endColor = color;
            }
            if (lamp != null)
            {
                color.a *= pulse;
                lamp.color = color;
            }
            if (magazine != null)
            {
                float fall = Mathf.Clamp01(elapsed / 0.25f);
                magazine.localPosition = Vector3.up * (0.04f + (1f - fall) * (1f - fall) * 0.45f);
            }
            // 复用原版雷达短提示音；固定三声，倒计时加快但不会按帧狂播。
            int beep = elapsed >= duration * 0.8f ? 2 : (elapsed >= duration * 0.5f ? 1 : 0);
            if (beep > lastBeep)
            {
                lastBeep = beep;
                try { if (audioPost != null) audioPost.Invoke(null, new object[] { "SFX/Special/Radar/Beep", gameObject }); }
                catch (Exception) { }
            }
        }

        internal static void PlayExplosion(Vector3 center, float radius)
        {
            try
            {
                NewWeaponFx.PlayBurst(center, new Color(1f, 0.58f, 0.16f, 0.95f), radius, 0.4f, 0, true);
                BossRushFxBurst sparks = BossRushFxKit.Sparks(new Color(1f, 0.64f, 0.24f), 28);
                sparks.SpeedMin = 5f;
                sparks.SpeedMax = 10f;
                sparks.LifeMax = 0.4f;
                sparks.Upward = true;
                BossRushFxKit.PlayBurst(center + Vector3.up * 0.15f, sparks);
                BossRushFxBurst dust = BossRushFxKit.Dust(new Color(0.36f, 0.30f, 0.23f, 0.55f), 18);
                dust.SpeedMax = 4f;
                dust.GrowTo = 2.3f;
                BossRushFxKit.PlayBurst(center + Vector3.up * 0.08f, dust);
                NewWeaponFx.PlaySound(NewWeaponSfx.VenomBurst);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[EmptyMagazineMine] 爆炸表现失败: " + e.Message);
            }
        }

        private static Mesh BuildMagazineMesh()
        {
            List<Vector3> vertices = new List<Vector3>(72);
            List<int> triangles = new List<int>(108);
            List<Color> colors = new List<Color>(72);
            AddBox(vertices, triangles, colors, new Vector3(0f, 0.065f, 0f),
                new Vector3(0.27f, 0.13f, 0.48f), new Color(0.23f, 0.27f, 0.24f, 1f));
            AddBox(vertices, triangles, colors, new Vector3(0f, 0.139f, 0f),
                new Vector3(0.29f, 0.025f, 0.12f), new Color(0.70f, 0.49f, 0.20f, 1f));
            AddBox(vertices, triangles, colors, new Vector3(0f, 0.07f, -0.26f),
                new Vector3(0.30f, 0.16f, 0.05f), new Color(0.13f, 0.16f, 0.15f, 1f));
            Mesh mesh = new Mesh();
            mesh.name = "EmptyMagazineMine_ProceduralBox";
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.SetColors(colors);
            mesh.RecalculateBounds();
            mesh.RecalculateNormals();
            return mesh;
        }

        private static void AddBox(List<Vector3> vertices, List<int> triangles, List<Color> colors,
            Vector3 center, Vector3 size, Color color)
        {
            Vector3 h = size * 0.5f;
            Vector3[] corners =
            {
                new Vector3(-h.x, -h.y, -h.z), new Vector3(h.x, -h.y, -h.z),
                new Vector3(h.x, h.y, -h.z), new Vector3(-h.x, h.y, -h.z),
                new Vector3(-h.x, -h.y, h.z), new Vector3(h.x, -h.y, h.z),
                new Vector3(h.x, h.y, h.z), new Vector3(-h.x, h.y, h.z)
            };
            int[] faces = { 0, 3, 2, 1, 5, 6, 7, 4, 4, 7, 3, 0, 1, 2, 6, 5, 3, 7, 6, 2, 4, 0, 1, 5 };
            for (int face = 0; face < 6; face++)
            {
                int start = vertices.Count;
                float shade = face == 4 ? 1.2f : (face == 5 ? 0.55f : 0.8f + 0.07f * face);
                Color shaded = new Color(color.r * shade, color.g * shade, color.b * shade, color.a);
                for (int corner = 0; corner < 4; corner++)
                {
                    vertices.Add(center + corners[faces[face * 4 + corner]]);
                    colors.Add(shaded);
                }
                triangles.Add(start); triangles.Add(start + 1); triangles.Add(start + 2);
                triangles.Add(start); triangles.Add(start + 2); triangles.Add(start + 3);
            }
        }

        private void OnDestroy()
        {
            if (magazineMesh != null) Destroy(magazineMesh);
            magazineMesh = null;
        }
    }
}
