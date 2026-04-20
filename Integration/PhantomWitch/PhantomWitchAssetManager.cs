using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Duckov.Buffs;
using ItemStatsSystem;
using UnityEngine;

namespace BossRush
{
    public static class PhantomWitchAssetManager
    {
        private static GameObject cachedCurseBuffGO = null;
        private static Buff cachedCurseBuff = null;
        private static Sprite cachedCurseBuffIcon = null;

        private static bool buffReflectionInitialized = false;
        private static FieldInfo buffIdField = null;
        private static FieldInfo buffLimitedLifeTimeField = null;
        private static FieldInfo buffTotalLifeTimeField = null;
        private static FieldInfo buffMaxLayersField = null;
        private static FieldInfo buffDisplayNameField = null;
        private static FieldInfo buffDescriptionField = null;
        private static FieldInfo buffIconField = null;
        private static FieldInfo buffExclusiveTagField = null;
        private static FieldInfo modifierTargetStatKeyField = null;
        private static FieldInfo modifierTypeField = null;
        private static FieldInfo modifierValueField = null;
        private static FieldInfo modifierBuffField = null;
        private static FieldInfo buffEffectsField = null;
        private static FieldInfo effectTriggersField = null;
        private static FieldInfo effectActionsField = null;

        private static Material cachedLineMaterial = null;
        private static Material cachedQuadMaterial = null;
        private static Material cachedParticleMaterial = null;
        private static Mesh cachedQuadMesh = null;
        private static Vector2[] cachedUnitCircle48 = null;
        private static GameObject cachedEffectTemplate = null;
        private static Material cachedEffectMaterial = null;
        private static int activeReferenceCount = 0;

        private static readonly string[] PreferredCurseIconRelativePaths = new string[]
        {
            Path.Combine("Assets", "ui", "Buffs", "PhantomWitch_CurseBuff.png"),
            Path.Combine("Assets", "ui", "Buffs", "phantomwitch_cursebuff.png"),
            Path.Combine("Assets", "ui", "Buffs", "phantom_witch_curse_buff.png"),
            Path.Combine("Assets", "ui", "Buffs", "PhantomWitchCurseBuff.png")
        };

        private const string CurseIconFolderRelativePath = "Assets/ui/Buffs";
        private const string CurseBuffDisplayNameKey = "Buff_PhantomWitch_Curse_Name";
        private const string CurseBuffDescriptionKey = "Buff_PhantomWitch_Curse_Desc";
        private const string CurseBuffDisplayNameCN = "幽灵诅咒";
        private const string CurseBuffDisplayNameEN = "Ghost Curse";
        private const string CurseBuffDescriptionCN = "每层降低30%移动速度，可叠加至3层。";
        private const string CurseBuffDescriptionEN = "Reduces move speed by 30% per layer, up to 3 layers.";

        private static void InitBuffReflection()
        {
            if (buffReflectionInitialized)
            {
                return;
            }

            BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            Type buffType = typeof(Buff);
            buffIdField = buffType.GetField("id", flags);
            buffLimitedLifeTimeField = buffType.GetField("limitedLifeTime", flags);
            buffTotalLifeTimeField = buffType.GetField("totalLifeTime", flags);
            buffMaxLayersField = buffType.GetField("maxLayers", flags);
            buffDisplayNameField = buffType.GetField("displayName", flags);
            buffDescriptionField = buffType.GetField("description", flags);
            buffIconField = buffType.GetField("icon", flags);
            buffExclusiveTagField = buffType.GetField("exclusiveTag", flags);

            Type modifierActionType = typeof(ModifierAction);
            modifierTargetStatKeyField = modifierActionType.GetField("targetStatKey", flags);
            modifierTypeField = modifierActionType.GetField("ModifierType", flags);
            modifierValueField = modifierActionType.GetField("modifierValue", flags);
            modifierBuffField = modifierActionType.GetField("buff", flags);

            buffEffectsField = buffType.GetField("effects", flags);

            Type effectType = typeof(Effect);
            effectTriggersField = effectType.GetField("triggers", flags);
            effectActionsField = effectType.GetField("actions", flags);

            buffReflectionInitialized = true;
        }

        private static void SetFieldSafe(object target, FieldInfo field, object value)
        {
            if (target == null || field == null)
            {
                return;
            }

            try
            {
                if (value == null)
                {
                    field.SetValue(target, null);
                    return;
                }

                Type fieldType = field.FieldType;
                Type valueType = value.GetType();

                if (fieldType.IsAssignableFrom(valueType))
                {
                    field.SetValue(target, value);
                    return;
                }

                if (fieldType.IsEnum && valueType == typeof(string))
                {
                    field.SetValue(target, Enum.Parse(fieldType, (string)value));
                    return;
                }

                field.SetValue(target, Convert.ChangeType(value, fieldType));
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PhantomWitch] [WARNING] SetFieldSafe失败: " + field.Name + " - " + e.Message);
            }
        }

        private static Sprite GetCurseBuffIcon()
        {
            if (cachedCurseBuffIcon != null)
            {
                return cachedCurseBuffIcon;
            }

            try
            {
                string modPath = ModBehaviour.GetModPath();
                if (!string.IsNullOrEmpty(modPath))
                {
                    for (int i = 0; i < PreferredCurseIconRelativePaths.Length; i++)
                    {
                        string fullPath = Path.Combine(modPath, PreferredCurseIconRelativePaths[i]);
                        Sprite loadedSprite = TryLoadSpriteFromFile(fullPath, "PhantomWitch_CurseBuffIcon");
                        if (loadedSprite != null)
                        {
                            cachedCurseBuffIcon = loadedSprite;
                            return cachedCurseBuffIcon;
                        }
                    }

                    string iconFolderPath = Path.Combine(modPath, CurseIconFolderRelativePath);
                    if (Directory.Exists(iconFolderPath))
                    {
                        string[] pngFiles = Directory.GetFiles(iconFolderPath, "*.png", SearchOption.TopDirectoryOnly);
                        for (int i = 0; i < pngFiles.Length; i++)
                        {
                            string fileName = Path.GetFileNameWithoutExtension(pngFiles[i]);
                            if (string.IsNullOrEmpty(fileName))
                            {
                                continue;
                            }

                            string lowered = fileName.ToLowerInvariant();
                            if (lowered.Contains("curse") || lowered.Contains("witch") || lowered.Contains("ghost"))
                            {
                                Sprite loadedSprite = TryLoadSpriteFromFile(pngFiles[i], "PhantomWitch_CurseBuffIcon");
                                if (loadedSprite != null)
                                {
                                    cachedCurseBuffIcon = loadedSprite;
                                    return cachedCurseBuffIcon;
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PhantomWitch] [WARNING] 加载诅咒Buff图标失败: " + e.Message);
            }

            cachedCurseBuffIcon = CreateFallbackCurseBuffIcon();
            return cachedCurseBuffIcon;
        }

        private static Sprite TryLoadSpriteFromFile(string fullPath, string spriteName)
        {
            if (string.IsNullOrEmpty(fullPath) || !File.Exists(fullPath))
            {
                return null;
            }

            try
            {
                byte[] bytes = File.ReadAllBytes(fullPath);
                if (bytes == null || bytes.Length == 0)
                {
                    return null;
                }

                Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!texture.LoadImage(bytes))
                {
                    UnityEngine.Object.Destroy(texture);
                    return null;
                }

                texture.name = spriteName;
                Sprite sprite = Sprite.Create(
                    texture,
                    new Rect(0f, 0f, texture.width, texture.height),
                    new Vector2(0.5f, 0.5f));
                sprite.name = spriteName;
                return sprite;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PhantomWitch] [WARNING] 从文件加载Sprite失败: " + fullPath + " - " + e.Message);
                return null;
            }
        }

        private static Sprite CreateFallbackCurseBuffIcon()
        {
            const int textureSize = 64;
            Texture2D texture = new Texture2D(textureSize, textureSize, TextureFormat.RGBA32, false);
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;

            Vector2 center = new Vector2((textureSize - 1) * 0.5f, (textureSize - 1) * 0.5f);
            float maxDistance = textureSize * 0.5f;
            Color clear = new Color(0f, 0f, 0f, 0f);
            Color outer = new Color(0.15f, 0.02f, 0.25f, 0.15f);
            Color inner = new Color(0.72f, 0.35f, 1f, 0.95f);

            for (int y = 0; y < textureSize; y++)
            {
                for (int x = 0; x < textureSize; x++)
                {
                    float distance = Vector2.Distance(new Vector2(x, y), center) / maxDistance;
                    if (distance > 1f)
                    {
                        texture.SetPixel(x, y, clear);
                        continue;
                    }

                    float t = 1f - Mathf.Clamp01(distance);
                    Color color = Color.Lerp(outer, inner, t);
                    color.a = Mathf.Lerp(outer.a, inner.a, t);
                    texture.SetPixel(x, y, color);
                }
            }

            texture.Apply();

            Sprite sprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, texture.width, texture.height),
                new Vector2(0.5f, 0.5f));
            sprite.name = "PhantomWitch_CurseBuffIcon_Fallback";
            return sprite;
        }

        // ==================== Buff ====================

        public static Buff GetCurseBuff()
        {
            if (cachedCurseBuff != null)
            {
                return cachedCurseBuff;
            }

            try
            {
                InitBuffReflection();
                InjectCurseBuffLocalization();

                // 根 GO 初始为 inactive，完成所有组装后再激活
                cachedCurseBuffGO = new GameObject("PhantomWitch_CurseBuff");
                cachedCurseBuffGO.SetActive(false);
                UnityEngine.Object.DontDestroyOnLoad(cachedCurseBuffGO);

                // ---- Buff 组件（在根 GO 上）----
                Buff buff = cachedCurseBuffGO.AddComponent<Buff>();

                SetFieldSafe(buff, buffIdField, PhantomWitchConfig.CurseBuffID);
                SetFieldSafe(buff, buffLimitedLifeTimeField, true);
                SetFieldSafe(buff, buffTotalLifeTimeField, PhantomWitchConfig.CurseBuffDuration);
                SetFieldSafe(buff, buffMaxLayersField, PhantomWitchConfig.CurseBuffMaxLayers);
                SetFieldSafe(buff, buffDisplayNameField, CurseBuffDisplayNameKey);
                SetFieldSafe(buff, buffDescriptionField, CurseBuffDescriptionKey);
                SetFieldSafe(buff, buffIconField, GetCurseBuffIcon());

                if (buffExclusiveTagField != null)
                {
                    try
                    {
                        object notExclusive = Enum.Parse(buffExclusiveTagField.FieldType, "NotExclusive");
                        SetFieldSafe(buff, buffExclusiveTagField, notExclusive);
                    }
                    catch (Exception exTag)
                    {
                        ModBehaviour.DevLog("[PhantomWitch] [WARNING] 设置ExclusiveTag失败: " + exTag.Message);
                    }
                }

                // ---- Effect 链（Effect + TriggerOnSetItem + ModifierAction 同一子 GO）----
                GameObject effectChild = new GameObject("CurseSlowEffect");
                effectChild.transform.SetParent(cachedCurseBuffGO.transform, false);

                Effect effect = effectChild.AddComponent<Effect>();
                TriggerOnSetItem trigger = effectChild.AddComponent<TriggerOnSetItem>();
                ModifierAction modifier = effectChild.AddComponent<ModifierAction>();

                // ModifierAction 公开字段
                modifier.targetStatKey = "WalkSpeed";
                modifier.modifierValue = PhantomWitchConfig.CurseSlowPerLayer;

                if (modifierTypeField != null)
                {
                    try
                    {
                        object percentMultiply = Enum.Parse(modifierTypeField.FieldType, "PercentageMultiply");
                        SetFieldSafe(modifier, modifierTypeField, percentMultiply);
                    }
                    catch (Exception modTypeEx)
                    {
                        ModBehaviour.DevLog("[PhantomWitch] [WARNING] 设置ModifierType失败: " + modTypeEx.Message);
                    }
                }

                // ModifierAction.buff → 指向父 Buff（层数回调用）
                SetFieldSafe(modifier, modifierBuffField, buff);

                // Effect.triggers → 加入 TriggerOnSetItem
                if (effectTriggersField != null)
                {
                    var triggersList = effectTriggersField.GetValue(effect) as System.Collections.IList;
                    if (triggersList != null) triggersList.Add(trigger);
                }

                // Effect.actions → 加入 ModifierAction
                if (effectActionsField != null)
                {
                    var actionsList = effectActionsField.GetValue(effect) as System.Collections.IList;
                    if (actionsList != null) actionsList.Add(modifier);
                }

                // Buff.effects → 加入 Effect
                if (buffEffectsField != null)
                {
                    var effectsList = buffEffectsField.GetValue(buff) as System.Collections.IList;
                    if (effectsList != null) effectsList.Add(effect);
                }

                // 组装完毕，激活预制体（触发 Awake，EffectComponent 自动找 Master）
                cachedCurseBuffGO.SetActive(true);

                cachedCurseBuff = buff;
                ModBehaviour.DevLog("[PhantomWitch] 诅咒Buff运行时构建成功（Effect链已连接）");
                return cachedCurseBuff;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PhantomWitch] [ERROR] 构建诅咒Buff失败: " + e.Message + "\n" + e.StackTrace);
                return null;
            }
        }

        /// <summary>
        /// 将诅咒 Buff 的显示名和描述注入本地化系统，确保 ToPlainText() 能查到。
        /// </summary>
        private static void InjectCurseBuffLocalization()
        {
            try
            {
                string name = L10n.T(CurseBuffDisplayNameCN, CurseBuffDisplayNameEN);
                string desc = L10n.T(CurseBuffDescriptionCN, CurseBuffDescriptionEN);
                LocalizationHelper.InjectLocalization(CurseBuffDisplayNameKey, name);
                LocalizationHelper.InjectLocalization(CurseBuffDescriptionKey, desc);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PhantomWitch] [WARNING] 诅咒Buff本地化注入失败: " + e.Message);
            }
        }

        // ==================== 共享基础设施 ====================

        private static Material GetLineMaterial()
        {
            if (cachedLineMaterial != null)
            {
                return cachedLineMaterial;
            }
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Unlit/Transparent");
            if (shader != null)
            {
                cachedLineMaterial = new Material(shader);
                cachedLineMaterial.name = "PW_SharedLine";
                cachedLineMaterial.enableInstancing = true;
            }
            return cachedLineMaterial;
        }

        private static Material GetQuadMaterial()
        {
            if (cachedQuadMaterial != null)
            {
                return cachedQuadMaterial;
            }
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Unlit/Transparent");
            if (shader != null)
            {
                cachedQuadMaterial = new Material(shader);
                cachedQuadMaterial.name = "PW_SharedQuad";
                cachedQuadMaterial.enableInstancing = true;
            }
            return cachedQuadMaterial;
        }

        private static Material GetParticleMaterial()
        {
            if (cachedParticleMaterial != null)
            {
                return cachedParticleMaterial;
            }

            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Unlit/Transparent");
            if (shader != null)
            {
                cachedParticleMaterial = new Material(shader);
                cachedParticleMaterial.name = "PW_SharedParticle";
                cachedParticleMaterial.enableInstancing = true;
            }
            return cachedParticleMaterial;
        }

        private static Mesh GetQuadMesh()
        {
            if (cachedQuadMesh != null)
            {
                return cachedQuadMesh;
            }
            cachedQuadMesh = new Mesh();
            cachedQuadMesh.name = "PW_Quad";
            cachedQuadMesh.vertices = new Vector3[]
            {
                new Vector3(-0.5f, 0f, -0.5f),
                new Vector3(0.5f, 0f, -0.5f),
                new Vector3(0.5f, 0f, 0.5f),
                new Vector3(-0.5f, 0f, 0.5f)
            };
            cachedQuadMesh.uv = new Vector2[]
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(1f, 1f),
                new Vector2(0f, 1f)
            };
            cachedQuadMesh.triangles = new int[] { 0, 2, 1, 0, 3, 2 };
            cachedQuadMesh.RecalculateNormals();
            return cachedQuadMesh;
        }

        private static Vector2[] GetUnitCircle48()
        {
            if (cachedUnitCircle48 != null)
            {
                return cachedUnitCircle48;
            }
            cachedUnitCircle48 = new Vector2[PhantomWitchConfig.FxRingSegments];
            for (int i = 0; i < PhantomWitchConfig.FxRingSegments; i++)
            {
                float a = (float)i / PhantomWitchConfig.FxRingSegments * Mathf.PI * 2f;
                cachedUnitCircle48[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            }
            return cachedUnitCircle48;
        }

        private static int ResolveAdaptiveCount(PhantomWitchFxDetailLevel detailLevel, int full, int reduced, int minimal)
        {
            switch (detailLevel)
            {
                case PhantomWitchFxDetailLevel.Minimal:
                    return Mathf.Max(0, minimal);
                case PhantomWitchFxDetailLevel.Reduced:
                    return Mathf.Max(0, reduced);
                default:
                    return Mathf.Max(0, full);
            }
        }

        private static float ResolveAdaptiveFloat(PhantomWitchFxDetailLevel detailLevel, float full, float reduced, float minimal)
        {
            switch (detailLevel)
            {
                case PhantomWitchFxDetailLevel.Minimal:
                    return Mathf.Max(0f, minimal);
                case PhantomWitchFxDetailLevel.Reduced:
                    return Mathf.Max(0f, reduced);
                default:
                    return Mathf.Max(0f, full);
            }
        }

        private static int ResolveRingSegments(PhantomWitchFxDetailLevel detailLevel)
        {
            return ResolveAdaptiveCount(
                detailLevel,
                PhantomWitchConfig.FxRingSegments,
                PhantomWitchConfig.FxReducedRingSegments,
                PhantomWitchConfig.FxMinimalRingSegments);
        }

        private static int ResolveArcSegments(PhantomWitchFxDetailLevel detailLevel)
        {
            return ResolveAdaptiveCount(
                detailLevel,
                PhantomWitchConfig.FxArcSegments,
                PhantomWitchConfig.FxReducedArcSegments,
                PhantomWitchConfig.FxMinimalArcSegments);
        }

        private static int ResolveSmallRingSegments(PhantomWitchFxDetailLevel detailLevel)
        {
            return ResolveAdaptiveCount(
                detailLevel,
                PhantomWitchConfig.FxSmallRingSegments,
                PhantomWitchConfig.FxReducedSmallRingSegments,
                PhantomWitchConfig.FxMinimalSmallRingSegments);
        }

        private static int ResolveHitRingSegments(PhantomWitchFxDetailLevel detailLevel)
        {
            return ResolveAdaptiveCount(
                detailLevel,
                PhantomWitchConfig.FxHitRingSegments,
                PhantomWitchConfig.FxReducedHitRingSegments,
                PhantomWitchConfig.FxMinimalHitRingSegments);
        }

        private static GameObject CreateFlatQuad(Transform parent, float scale, Color color, float yOffset)
        {
            GameObject go = new GameObject("FlatQuad");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, yOffset, 0f);
            go.transform.localScale = new Vector3(scale, 1f, scale);

            MeshFilter mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = GetQuadMesh();

            MeshRenderer mr = go.AddComponent<MeshRenderer>();
            Material mat = GetQuadMaterial();
            if (mat != null)
            {
                mr.sharedMaterial = mat;
                PhantomWitchFxRenderUtil.SetRendererColor(mr, color);
            }
            return go;
        }

        private static LineRenderer CreateRingLR(Transform parent, int segments, float radius, float width, Color color, float yOffset)
        {
            GameObject go = new GameObject("Ring");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, yOffset, 0f);

            LineRenderer lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.loop = true;
            lr.positionCount = segments;
            lr.widthMultiplier = width;
            Material mat = GetLineMaterial();
            if (mat != null)
            {
                lr.sharedMaterial = mat;
            }
            lr.startColor = color;
            lr.endColor = color;

            Vector2[] unit = GetUnitCircle48();
            if (segments == PhantomWitchConfig.FxRingSegments)
            {
                for (int i = 0; i < segments; i++)
                {
                    lr.SetPosition(i, new Vector3(unit[i].x * radius, 0f, unit[i].y * radius));
                }
            }
            else
            {
                for (int i = 0; i < segments; i++)
                {
                    float a = (float)i / segments * Mathf.PI * 2f;
                    lr.SetPosition(i, new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius));
                }
            }
            return lr;
        }

        private static LineRenderer CreateArcLR(Transform parent, int segments, float radius, float halfAngle, Vector3 forward, float width, Color color, float yOffset)
        {
            GameObject go = new GameObject("Arc");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, yOffset, 0f);

            LineRenderer lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.loop = false;
            lr.positionCount = segments + 1;
            lr.widthMultiplier = width;
            Material mat = GetLineMaterial();
            if (mat != null)
            {
                lr.sharedMaterial = mat;
            }
            lr.startColor = color;
            lr.endColor = color;

            forward.y = 0f;
            if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;
            forward = forward.normalized;

            float baseAngle = Mathf.Atan2(forward.x, forward.z);
            float startAngle = baseAngle - halfAngle * Mathf.Deg2Rad;
            float endAngle = baseAngle + halfAngle * Mathf.Deg2Rad;

            for (int i = 0; i <= segments; i++)
            {
                float t = (float)i / segments;
                float a = Mathf.Lerp(startAngle, endAngle, t);
                lr.SetPosition(i, new Vector3(Mathf.Sin(a) * radius, 0f, Mathf.Cos(a) * radius));
            }
            return lr;
        }

        private static void AddRuneMarks(Transform parent, float radius, int count, Color color, float yOffset)
        {
            Material mat = GetLineMaterial();
            for (int i = 0; i < count; i++)
            {
                GameObject seg = new GameObject("Rune_" + i);
                seg.transform.SetParent(parent, false);

                float baseAngle = (float)i / count * Mathf.PI * 2f;
                Vector3 center = new Vector3(Mathf.Cos(baseAngle) * radius, 0f, Mathf.Sin(baseAngle) * radius);

                LineRenderer lr = seg.AddComponent<LineRenderer>();
                lr.useWorldSpace = false;
                lr.loop = false;
                lr.widthMultiplier = 0.09f;
                lr.positionCount = 2;
                if (mat != null) lr.sharedMaterial = mat;
                lr.startColor = color;
                lr.endColor = new Color(color.r, color.g, color.b, color.a * 0.5f);
                seg.transform.localPosition = new Vector3(0f, yOffset, 0f);

                Vector3 tangent = new Vector3(-Mathf.Sin(baseAngle), 0f, Mathf.Cos(baseAngle)) * 0.45f;
                lr.SetPosition(0, center - tangent);
                lr.SetPosition(1, center + tangent);
            }
        }

        private static LineRenderer CreatePentagramLR(Transform parent, float radius, Color color, float yOffset)
        {
            GameObject go = new GameObject("Pentagram");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, yOffset, 0f);

            LineRenderer lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.loop = true;
            lr.widthMultiplier = 0.06f;
            Material mat = GetLineMaterial();
            if (mat != null) lr.sharedMaterial = mat;
            lr.startColor = color;
            lr.endColor = color;

            Vector3[] points = new Vector3[5];
            for (int i = 0; i < 5; i++)
            {
                float a = -Mathf.PI / 2f + i * (2f * Mathf.PI / 5f);
                points[i] = new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius);
            }
            Vector3[] order = new Vector3[5];
            for (int i = 0; i < 5; i++)
            {
                order[i] = points[(i * 2) % 5];
            }
            lr.positionCount = 5;
            lr.SetPositions(order);
            return lr;
        }

        private static ParticleSystem CreateBurstParticles(Transform parent, int count, float lifetime, Color color, float speedMin, float speedMax, float sizeMin, float sizeMax)
        {
            GameObject go = new GameObject("BurstParticles");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = Vector3.zero;

            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            ConfigureSharedParticleRenderer(ps);

            var main = ps.main;
            main.duration = lifetime + 0.1f;
            main.loop = false;
            main.startLifetime = lifetime;
            main.startSpeed = new ParticleSystem.MinMaxCurve(speedMin, speedMax);
            main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
            main.startColor = color;
            main.maxParticles = count + 4;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.gravityModifier = -0.1f;

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new ParticleSystem.Burst[] { new ParticleSystem.Burst(0f, (short)count) });

            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.x = new ParticleSystem.MinMaxCurve(0f, 0f);
            vel.y = new ParticleSystem.MinMaxCurve(speedMin * 0.5f, speedMax);
            vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);

            var col = ps.colorOverLifetime;
            col.enabled = true;
            Gradient g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(color, 0f), new GradientColorKey(new Color(color.r * 0.6f, color.g * 0.4f, color.b, 1f), 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0.6f, 0.6f), new GradientAlphaKey(0f, 1f) }
            );
            col.color = new ParticleSystem.MinMaxGradient(g);

            ps.Play();
            return ps;
        }

        private static ParticleSystem CreateCircleEmitter(Transform parent, float radius, float rate, float lifetime, Color color, int maxParticles)
        {
            GameObject go = new GameObject("CircleEmitter");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = Vector3.zero;

            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            ConfigureSharedParticleRenderer(ps);

            var main = ps.main;
            main.duration = 30f;
            main.loop = true;
            main.startLifetime = lifetime;
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 1.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.16f);
            main.startColor = color;
            main.maxParticles = maxParticles;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.gravityModifier = -0.12f;

            var emission = ps.emission;
            emission.rateOverTime = rate;

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = radius * 0.9f;
            shape.radiusThickness = 1f;

            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.x = new ParticleSystem.MinMaxCurve(0f, 0f);
            vel.y = new ParticleSystem.MinMaxCurve(0.8f, 1.4f);
            vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);

            var col = ps.colorOverLifetime;
            col.enabled = true;
            Gradient g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(color, 0f), new GradientColorKey(new Color(1f, 0.85f, 1f), 0.5f), new GradientColorKey(new Color(0.5f, 0.2f, 0.8f), 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.8f, 0.25f), new GradientAlphaKey(0.5f, 0.65f), new GradientAlphaKey(0f, 1f) }
            );
            col.color = new ParticleSystem.MinMaxGradient(g);

            var noise = ps.noise;
            noise.enabled = PhantomWitchFxRuntime.CurrentDetailLevel == PhantomWitchFxDetailLevel.Full;
            if (noise.enabled)
            {
                noise.strength = 0.18f;
                noise.frequency = 0.5f;
            }

            ps.Play();
            return ps;
        }

        /// <summary>
        /// 为粒子系统配置共享材质和渲染参数。公开供 SweatVfx 等外部粒子使用。
        /// </summary>
        public static void ConfigureSharedParticleRenderer(ParticleSystem ps)
        {
            ParticleSystemRenderer renderer = ps.GetComponent<ParticleSystemRenderer>();
            if (renderer == null) return;
            Material sharedParticle = GetParticleMaterial();
            if (sharedParticle != null)
            {
                renderer.sharedMaterial = sharedParticle;
            }
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.alignment = ParticleSystemRenderSpace.View;
            renderer.sortMode = ParticleSystemSortMode.Distance;
        }

        // ==================== 8 个特效工厂 ====================

        public static GameObject CreateTeleportEffect(Vector3 position, bool isAppear)
        {
            try
            {
                PhantomWitchFxDetailLevel detailLevel = PhantomWitchFxRuntime.CurrentDetailLevel;
                int ringSegments = ResolveRingSegments(detailLevel);
                int runeCount = ResolveAdaptiveCount(detailLevel, 3, 2, 0);
                int burstCount = ResolveAdaptiveCount(detailLevel, 10, 6, 4);
                GameObject root = new GameObject("PW_TeleportFX");
                root.transform.position = position;
                PhantomWitchFxRuntime.RegisterEffectRoot(root);
                float dur = PhantomWitchConfig.TeleportFxDuration;

                if (isAppear)
                {
                    LineRenderer ring = CreateRingLR(root.transform, ringSegments, 0.01f, 0.15f, PhantomWitchConfig.TeleportRingColor, 0.05f);
                    PhantomWitchExpandRing expand = ring.gameObject.AddComponent<PhantomWitchExpandRing>();
                    expand.Configure(PhantomWitchConfig.TeleportExpandRadius, 0.3f, PhantomWitchConfig.TeleportRingColor);

                    if (detailLevel != PhantomWitchFxDetailLevel.Minimal)
                    {
                        CreateFlatQuad(root.transform, 2f, new Color(PhantomWitchConfig.TeleportRingColor.r, PhantomWitchConfig.TeleportRingColor.g, PhantomWitchConfig.TeleportRingColor.b, 0.5f), 0.02f);
                        PhantomWitchFadeDestroy quadFade = root.transform.GetChild(root.transform.childCount - 1).gameObject.AddComponent<PhantomWitchFadeDestroy>();
                        quadFade.Configure(0.4f);
                    }

                    if (runeCount > 0)
                    {
                        GameObject runeHolder = new GameObject("Runes");
                        runeHolder.transform.SetParent(root.transform, false);
                        runeHolder.transform.localPosition = new Vector3(0f, 0.06f, 0f);
                        AddRuneMarks(runeHolder.transform, PhantomWitchConfig.TeleportExpandRadius * 0.7f, runeCount, PhantomWitchConfig.RuneMarkWhite, 0f);
                        PhantomWitchRingSpin runeSpin = runeHolder.AddComponent<PhantomWitchRingSpin>();
                        runeSpin.rotationSpeed = detailLevel == PhantomWitchFxDetailLevel.Full ? 120f : 90f;
                        PhantomWitchFadeDestroy runeFade = runeHolder.AddComponent<PhantomWitchFadeDestroy>();
                        runeFade.Configure(0.35f);
                    }

                    if (burstCount > 0)
                    {
                        CreateBurstParticles(root.transform, burstCount, 0.5f, PhantomWitchConfig.FxParticlePurple, 1.5f, 2.5f, 0.08f, 0.15f);
                    }
                }
                else
                {
                    LineRenderer ring = CreateRingLR(root.transform, ringSegments, PhantomWitchConfig.TeleportShrinkRadius, 0.12f, PhantomWitchConfig.TeleportRingColor, 0.05f);
                    PhantomWitchShrinkRing shrink = ring.gameObject.AddComponent<PhantomWitchShrinkRing>();
                    shrink.Configure(PhantomWitchConfig.TeleportShrinkRadius, 0.25f, PhantomWitchConfig.TeleportRingColor);

                    if (burstCount > 0)
                    {
                        CreateBurstParticles(root.transform, burstCount, 0.5f, PhantomWitchConfig.FxParticlePurple, 1.5f, 2.5f, 0.08f, 0.15f);
                    }
                }

                UnityEngine.Object.Destroy(root, dur);
                return root;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PhantomWitch] [ERROR] CreateTeleportEffect: " + e.Message);
                return null;
            }
        }

        public static GameObject CreateCurseAuraEffect(Vector3 position, float radius)
        {
            try
            {
                PhantomWitchFxDetailLevel detailLevel = PhantomWitchFxRuntime.CurrentDetailLevel;
                int ringSegments = ResolveRingSegments(detailLevel);
                int shockSegments = ResolveHitRingSegments(detailLevel);
                int runeCount = ResolveAdaptiveCount(detailLevel, 4, 2, 0);
                float particleRate = ResolveAdaptiveFloat(detailLevel, 10f, 6f, 0f);
                int particleMax = ResolveAdaptiveCount(detailLevel, 32, 18, 0);
                GameObject root = new GameObject("PW_CurseAuraFX");
                root.transform.position = position;
                PhantomWitchFxRuntime.RegisterEffectRoot(root);
                float dur = PhantomWitchConfig.CurseAuraFxDuration;

                GameObject ground = CreateFlatQuad(root.transform, radius * 2f, PhantomWitchConfig.CurseAuraGroundColor, 0.02f);
                PhantomWitchScaleIn scaleIn = ground.AddComponent<PhantomWitchScaleIn>();
                scaleIn.Configure(new Vector3(radius * 2f, 1f, radius * 2f), 0.35f);

                LineRenderer ring = CreateRingLR(root.transform, ringSegments, radius, 0.10f, PhantomWitchConfig.CurseAuraRingColor, 0.06f);
                PhantomWitchRingSpin spin = ring.gameObject.AddComponent<PhantomWitchRingSpin>();
                spin.rotationSpeed = detailLevel == PhantomWitchFxDetailLevel.Full ? 25f : 18f;

                if (runeCount > 0)
                {
                    GameObject runeHolder = new GameObject("AuraRunes");
                    runeHolder.transform.SetParent(root.transform, false);
                    runeHolder.transform.localPosition = new Vector3(0f, 0.08f, 0f);
                    AddRuneMarks(runeHolder.transform, radius * 0.85f, runeCount, PhantomWitchConfig.RuneMarkWhite, 0f);
                    PhantomWitchRingSpin runeSpin = runeHolder.AddComponent<PhantomWitchRingSpin>();
                    runeSpin.rotationSpeed = detailLevel == PhantomWitchFxDetailLevel.Full ? 25f : 18f;
                }

                if (particleMax > 0 && particleRate > 0f)
                {
                    CreateCircleEmitter(root.transform, radius, particleRate, 0.8f, PhantomWitchConfig.FxParticlePurple, particleMax);
                }

                LineRenderer shock = CreateRingLR(root.transform, shockSegments, 0.01f, 0.12f, PhantomWitchConfig.DamageHitFlashColor, 0.04f);
                PhantomWitchExpandRing shockExpand = shock.gameObject.AddComponent<PhantomWitchExpandRing>();
                shockExpand.Configure(radius * 1.1f, 0.25f, PhantomWitchConfig.DamageHitFlashColor);

                PhantomWitchFadeDestroy rootFade = root.AddComponent<PhantomWitchFadeDestroy>();
                rootFade.Configure(dur, 0.3f);

                UnityEngine.Object.Destroy(root, dur);
                return root;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PhantomWitch] [ERROR] CreateCurseAuraEffect: " + e.Message);
                return null;
            }
        }

        public static GameObject CreateScytheSweepEffect(Vector3 position, Vector3 forward, float radius, float halfAngle)
        {
            try
            {
                PhantomWitchFxDetailLevel detailLevel = PhantomWitchFxRuntime.CurrentDetailLevel;
                GameObject root = new GameObject("PW_SweepFX");
                root.transform.position = position;
                PhantomWitchFxRuntime.RegisterEffectRoot(root);
                float dur = PhantomWitchConfig.SweepFxDuration;

                LineRenderer arc = CreateArcLR(root.transform, ResolveArcSegments(detailLevel), radius * 0.3f, halfAngle, forward, 0.14f, PhantomWitchConfig.SweepArcColor, 0.5f);
                PhantomWitchExpandArc expandArc = arc.gameObject.AddComponent<PhantomWitchExpandArc>();
                expandArc.Configure(radius * 0.3f, radius, halfAngle, forward, 0.15f, PhantomWitchConfig.SweepArcColor);

                UnityEngine.Object.Destroy(root, dur);
                return root;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PhantomWitch] [ERROR] CreateScytheSweepEffect: " + e.Message);
                return null;
            }
        }

        public static GameObject CreateHeavySlashEffect(Vector3 position, Vector3 forward, float radius)
        {
            try
            {
                PhantomWitchFxDetailLevel detailLevel = PhantomWitchFxRuntime.CurrentDetailLevel;
                int arcSegments = ResolveAdaptiveCount(detailLevel, 12, 8, 6);
                int slashParticleCount = ResolveAdaptiveCount(detailLevel, 8, 5, 0);
                GameObject root = new GameObject("PW_HeavySlashFX");
                root.transform.position = position;
                PhantomWitchFxRuntime.RegisterEffectRoot(root);
                float dur = PhantomWitchConfig.HeavySlashFxDuration;

                LineRenderer arc = CreateArcLR(root.transform, arcSegments, 0.01f, 40f, forward, 0.18f, PhantomWitchConfig.HeavySlashColor, 0.5f);
                PhantomWitchExpandArc expandArc = arc.gameObject.AddComponent<PhantomWitchExpandArc>();
                expandArc.Configure(0.01f, radius, 40f, forward, 0.12f, PhantomWitchConfig.HeavySlashColor);

                CreateFlatQuad(root.transform, 1.5f, new Color(PhantomWitchConfig.HeavySlashColor.r * 0.5f, PhantomWitchConfig.HeavySlashColor.g * 0.2f, PhantomWitchConfig.HeavySlashColor.b * 0.6f, 0.45f), 0.02f);
                PhantomWitchFadeDestroy groundFade = root.transform.GetChild(root.transform.childCount - 1).gameObject.AddComponent<PhantomWitchFadeDestroy>();
                groundFade.Configure(0.7f);

                if (slashParticleCount > 0)
                {
                    forward.y = 0f;
                    if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;
                    forward = forward.normalized;
                    GameObject particleGo = new GameObject("SlashParticles");
                    particleGo.transform.SetParent(root.transform, false);
                    particleGo.transform.localRotation = Quaternion.LookRotation(forward);
                    ParticleSystem ps = particleGo.AddComponent<ParticleSystem>();
                    ConfigureSharedParticleRenderer(ps);
                    var main = ps.main;
                    main.duration = 0.5f;
                    main.loop = false;
                    main.startLifetime = 0.4f;
                    main.startSpeed = new ParticleSystem.MinMaxCurve(3f, 5f);
                    main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.12f);
                    main.startColor = PhantomWitchConfig.FxParticlePurple;
                    main.maxParticles = slashParticleCount + 4;
                    main.simulationSpace = ParticleSystemSimulationSpace.Local;
                    var emission = ps.emission;
                    emission.rateOverTime = 0f;
                    emission.SetBursts(new ParticleSystem.Burst[] { new ParticleSystem.Burst(0f, (short)slashParticleCount) });
                    var shape = ps.shape;
                    shape.enabled = true;
                    shape.shapeType = ParticleSystemShapeType.Cone;
                    shape.angle = 25f;
                    shape.radius = 0.1f;
                    var col = ps.colorOverLifetime;
                    col.enabled = true;
                    Gradient g = new Gradient();
                    g.SetKeys(
                        new[] { new GradientColorKey(PhantomWitchConfig.FxParticlePurple, 0f), new GradientColorKey(new Color(0.4f, 0.1f, 0.6f), 1f) },
                        new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) }
                    );
                    col.color = new ParticleSystem.MinMaxGradient(g);
                    ps.Play();
                }

                UnityEngine.Object.Destroy(root, dur);
                return root;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PhantomWitch] [ERROR] CreateHeavySlashEffect: " + e.Message);
                return null;
            }
        }

        public static GameObject CreateSummonCircleEffect(Vector3 position)
        {
            try
            {
                PhantomWitchFxDetailLevel detailLevel = PhantomWitchFxRuntime.CurrentDetailLevel;
                int ringSegments = ResolveRingSegments(detailLevel);
                int runeCount = ResolveAdaptiveCount(detailLevel, 5, 3, 0);
                float particleRate = ResolveAdaptiveFloat(detailLevel, 14f, 8f, 0f);
                int particleMax = ResolveAdaptiveCount(detailLevel, 36, 20, 0);
                GameObject root = new GameObject("PW_SummonCircleFX");
                root.transform.position = position;
                PhantomWitchFxRuntime.RegisterEffectRoot(root);
                float dur = PhantomWitchConfig.SummonCircleFxDuration;

                root.transform.localScale = Vector3.zero;
                PhantomWitchScaleIn rootScale = root.AddComponent<PhantomWitchScaleIn>();
                rootScale.Configure(Vector3.one, 0.4f);

                LineRenderer outerRing = CreateRingLR(root.transform, ringSegments, PhantomWitchConfig.SummonCircleRadius, 0.12f, PhantomWitchConfig.SummonCircleColor, 0.05f);
                PhantomWitchRingSpin outerSpin = outerRing.gameObject.AddComponent<PhantomWitchRingSpin>();
                outerSpin.rotationSpeed = detailLevel == PhantomWitchFxDetailLevel.Full ? 20f : 14f;

                LineRenderer pent = CreatePentagramLR(root.transform, PhantomWitchConfig.SummonPentagramRadius, PhantomWitchConfig.SummonPentagramColor, 0.07f);
                PhantomWitchRingSpin pentSpin = pent.gameObject.AddComponent<PhantomWitchRingSpin>();
                pentSpin.rotationSpeed = detailLevel == PhantomWitchFxDetailLevel.Full ? -15f : -10f;

                if (runeCount > 0)
                {
                    AddRuneMarks(outerRing.gameObject.transform, PhantomWitchConfig.SummonCircleRadius * 0.85f, runeCount, PhantomWitchConfig.RuneMarkWhite, 0.02f);
                }

                CreateFlatQuad(root.transform, 1.6f, new Color(PhantomWitchConfig.SummonCircleColor.r, PhantomWitchConfig.SummonCircleColor.g, PhantomWitchConfig.SummonCircleColor.b, 0.35f), 0.03f);
                MeshRenderer coreMR = root.transform.GetChild(root.transform.childCount - 1).GetComponent<MeshRenderer>();
                if (coreMR != null && detailLevel != PhantomWitchFxDetailLevel.Minimal)
                {
                    PhantomWitchCorePulse corePulse = root.transform.GetChild(root.transform.childCount - 1).gameObject.AddComponent<PhantomWitchCorePulse>();
                    corePulse.Configure(coreMR, new Color(PhantomWitchConfig.SummonCircleColor.r, PhantomWitchConfig.SummonCircleColor.g, PhantomWitchConfig.SummonCircleColor.b, 0.35f), 1.6f, 1.0f);
                }

                if (particleMax > 0 && particleRate > 0f)
                {
                    CreateCircleEmitter(root.transform, PhantomWitchConfig.SummonCircleRadius, particleRate, 1.0f, PhantomWitchConfig.FxParticlePurple, particleMax);
                }

                PhantomWitchFadeDestroy rootFade = root.AddComponent<PhantomWitchFadeDestroy>();
                rootFade.Configure(dur, 0.5f);

                UnityEngine.Object.Destroy(root, dur);
                return root;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PhantomWitch] [ERROR] CreateSummonCircleEffect: " + e.Message);
                return null;
            }
        }

        public static GameObject CreateMinionSpawnEffect(Vector3 position)
        {
            try
            {
                if (PhantomWitchFxRuntime.ShouldSkipEffect(PhantomWitchFxEffectImportance.Optional))
                {
                    return null;
                }

                PhantomWitchFxDetailLevel detailLevel = PhantomWitchFxRuntime.CurrentDetailLevel;
                int ringSegments = ResolveSmallRingSegments(detailLevel);
                int burstCount = ResolveAdaptiveCount(detailLevel, 5, 4, 0);
                GameObject root = new GameObject("PW_MinionSpawnFX");
                root.transform.position = position;
                PhantomWitchFxRuntime.RegisterEffectRoot(root);
                float dur = PhantomWitchConfig.MinionSpawnFxDuration;

                LineRenderer ring = CreateRingLR(root.transform, ringSegments, PhantomWitchConfig.MinionSpawnFxRadius, 0.08f, PhantomWitchConfig.SummonCircleColor, 0.04f);
                PhantomWitchRingSpin spin = ring.gameObject.AddComponent<PhantomWitchRingSpin>();
                spin.rotationSpeed = detailLevel == PhantomWitchFxDetailLevel.Full ? 40f : 28f;

                CreateFlatQuad(root.transform, 1f, new Color(PhantomWitchConfig.SummonCircleColor.r, PhantomWitchConfig.SummonCircleColor.g, PhantomWitchConfig.SummonCircleColor.b, 0.4f), 0.02f);
                PhantomWitchFadeDestroy quadFade = root.transform.GetChild(root.transform.childCount - 1).gameObject.AddComponent<PhantomWitchFadeDestroy>();
                quadFade.Configure(0.5f);

                if (burstCount > 0)
                {
                    CreateBurstParticles(root.transform, burstCount, 0.6f, PhantomWitchConfig.FxParticlePurple, 1.2f, 2.0f, 0.06f, 0.12f);
                }

                PhantomWitchFadeDestroy rootFade = root.AddComponent<PhantomWitchFadeDestroy>();
                rootFade.Configure(dur, 0.2f);

                UnityEngine.Object.Destroy(root, dur);
                return root;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PhantomWitch] [ERROR] CreateMinionSpawnEffect: " + e.Message);
                return null;
            }
        }

        public static GameObject CreateDamageHitEffect(Vector3 position)
        {
            try
            {
                if (PhantomWitchFxRuntime.ShouldSkipEffect(PhantomWitchFxEffectImportance.Optional))
                {
                    return null;
                }

                PhantomWitchFxDetailLevel detailLevel = PhantomWitchFxRuntime.CurrentDetailLevel;
                GameObject root = new GameObject("PW_HitFX");
                root.transform.position = position;
                PhantomWitchFxRuntime.RegisterEffectRoot(root);
                float dur = PhantomWitchConfig.DamageHitFxDuration;

                CreateFlatQuad(root.transform, 0.5f, PhantomWitchConfig.DamageHitFlashColor, 0.3f);
                PhantomWitchFadeDestroy quadFade = root.transform.GetChild(0).gameObject.AddComponent<PhantomWitchFadeDestroy>();
                quadFade.Configure(0.15f);

                LineRenderer ring = CreateRingLR(root.transform, ResolveHitRingSegments(detailLevel), 0.01f, 0.06f, PhantomWitchConfig.DamageHitFlashColor, 0.3f);
                PhantomWitchExpandRing expand = ring.gameObject.AddComponent<PhantomWitchExpandRing>();
                expand.Configure(0.4f, 0.2f, PhantomWitchConfig.DamageHitFlashColor);

                UnityEngine.Object.Destroy(root, dur);
                return root;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PhantomWitch] [ERROR] CreateDamageHitEffect: " + e.Message);
                return null;
            }
        }

        public static GameObject CreatePhaseTransitionEffect(Vector3 position)
        {
            try
            {
                PhantomWitchFxDetailLevel detailLevel = PhantomWitchFxRuntime.CurrentDetailLevel;
                int ringSegments = ResolveRingSegments(detailLevel);
                int burstCount = ResolveAdaptiveCount(detailLevel, 18, 10, 6);
                GameObject root = new GameObject("PW_PhaseTransFX");
                root.transform.position = position;
                PhantomWitchFxRuntime.RegisterEffectRoot(root);
                float dur = PhantomWitchConfig.PhaseTransitionFxDuration;

                LineRenderer outerShock = CreateRingLR(root.transform, ringSegments, 0.01f, 0.30f, PhantomWitchConfig.PhaseTransitionColor, 0.06f);
                PhantomWitchExpandRing outerExpand = outerShock.gameObject.AddComponent<PhantomWitchExpandRing>();
                outerExpand.Configure(PhantomWitchConfig.PhaseTransitionRadius, 0.6f, PhantomWitchConfig.PhaseTransitionColor);

                if (detailLevel != PhantomWitchFxDetailLevel.Minimal)
                {
                    LineRenderer innerShock = CreateRingLR(root.transform, ringSegments, 0.01f, 0.22f, new Color(PhantomWitchConfig.PhaseTransitionColor.r, PhantomWitchConfig.PhaseTransitionColor.g, PhantomWitchConfig.PhaseTransitionColor.b, 0.7f), 0.06f);
                    PhantomWitchExpandRing innerExpand = innerShock.gameObject.AddComponent<PhantomWitchExpandRing>();
                    innerExpand.Configure(PhantomWitchConfig.PhaseTransitionInnerRadius, 0.5f, new Color(PhantomWitchConfig.PhaseTransitionColor.r, PhantomWitchConfig.PhaseTransitionColor.g, PhantomWitchConfig.PhaseTransitionColor.b, 0.7f));
                    innerExpand.delay = 0.4f;
                }

                CreateFlatQuad(root.transform, 8f, new Color(PhantomWitchConfig.CurseAuraGroundColor.r, PhantomWitchConfig.CurseAuraGroundColor.g, PhantomWitchConfig.CurseAuraGroundColor.b, 0.35f), 0.02f);
                PhantomWitchFadeDestroy groundFade = root.transform.GetChild(root.transform.childCount - 1).gameObject.AddComponent<PhantomWitchFadeDestroy>();
                groundFade.Configure(2f);

                if (burstCount > 0)
                {
                    CreateBurstParticles(root.transform, burstCount, 1.2f, PhantomWitchConfig.FxParticlePurple, 1.0f, 3.0f, 0.10f, 0.20f);
                }

                UnityEngine.Object.Destroy(root, dur);
                return root;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PhantomWitch] [ERROR] CreatePhaseTransitionEffect: " + e.Message);
                return null;
            }
        }

        public static GameObject CreateEffect(Vector3 position, float duration)
        {
            try
            {
                PhantomWitchFxDetailLevel detailLevel = PhantomWitchFxRuntime.CurrentDetailLevel;
                int ringSegments = ResolveRingSegments(detailLevel);
                int runeCount = ResolveAdaptiveCount(detailLevel, 4, 2, 0);
                int burstCount = ResolveAdaptiveCount(detailLevel, 10, 6, 4);
                GameObject root = new GameObject("PW_SpawnFX");
                root.transform.position = position;
                PhantomWitchFxRuntime.RegisterEffectRoot(root);
                float effectDuration = Mathf.Max(0.1f, duration);

                Color groundColor = new Color(
                    PhantomWitchConfig.EffectColor.r,
                    PhantomWitchConfig.EffectColor.g,
                    PhantomWitchConfig.EffectColor.b,
                    0.35f);

                GameObject ground = CreateFlatQuad(root.transform, 2.4f, groundColor, 0.02f);
                PhantomWitchFadeDestroy groundFade = ground.AddComponent<PhantomWitchFadeDestroy>();
                groundFade.Configure(effectDuration, Mathf.Min(0.5f, effectDuration));

                LineRenderer outerRing = CreateRingLR(
                    root.transform,
                    ringSegments,
                    0.05f,
                    0.14f,
                    PhantomWitchConfig.EffectColor,
                    0.05f);
                PhantomWitchExpandRing expand = outerRing.gameObject.AddComponent<PhantomWitchExpandRing>();
                expand.Configure(1.8f, Mathf.Max(0.15f, effectDuration * 0.7f), PhantomWitchConfig.EffectColor);

                if (runeCount > 0)
                {
                    GameObject runeHolder = new GameObject("SpawnRunes");
                    runeHolder.transform.SetParent(root.transform, false);
                    runeHolder.transform.localPosition = new Vector3(0f, 0.07f, 0f);
                    AddRuneMarks(runeHolder.transform, 1.1f, runeCount, PhantomWitchConfig.RuneMarkWhite, 0f);
                    PhantomWitchRingSpin runeSpin = runeHolder.AddComponent<PhantomWitchRingSpin>();
                    runeSpin.rotationSpeed = detailLevel == PhantomWitchFxDetailLevel.Full ? 70f : 50f;
                    PhantomWitchFadeDestroy runeFade = runeHolder.AddComponent<PhantomWitchFadeDestroy>();
                    runeFade.Configure(effectDuration, Mathf.Min(0.5f, effectDuration));
                }

                if (burstCount > 0)
                {
                    CreateBurstParticles(
                        root.transform,
                        burstCount,
                        Mathf.Min(effectDuration, 0.8f),
                        PhantomWitchConfig.FxParticlePurple,
                        1.2f,
                        2.6f,
                        0.08f,
                        0.16f);
                }

                UnityEngine.Object.Destroy(root, effectDuration);
                return root;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PhantomWitch] [ERROR] CreateEffect: " + e.Message);
                return null;
            }
        }

        public static GameObject CreateDeathEffect(Vector3 position)
        {
            try
            {
                PhantomWitchFxDetailLevel detailLevel = PhantomWitchFxRuntime.CurrentDetailLevel;
                int ringSegments = ResolveRingSegments(detailLevel);
                int smallRingSegments = ResolveSmallRingSegments(detailLevel);
                int runeCount = ResolveAdaptiveCount(detailLevel, 5, 3, 0);
                int burstCount = ResolveAdaptiveCount(detailLevel, 16, 10, 6);
                GameObject root = new GameObject("PW_DeathFX");
                root.transform.position = position;
                PhantomWitchFxRuntime.RegisterEffectRoot(root);
                float dur = PhantomWitchConfig.DeathFxDuration;
                Color deathColor = new Color(PhantomWitchConfig.PhaseTransitionColor.r, PhantomWitchConfig.PhaseTransitionColor.g, PhantomWitchConfig.PhaseTransitionColor.b, 0.92f);

                CreateFlatQuad(root.transform, PhantomWitchConfig.DeathFxRadius * 2.1f, new Color(PhantomWitchConfig.CurseAuraGroundColor.r, PhantomWitchConfig.CurseAuraGroundColor.g, PhantomWitchConfig.CurseAuraGroundColor.b, 0.45f), 0.02f);

                LineRenderer outerRing = CreateRingLR(root.transform, ringSegments, 0.01f, 0.22f, deathColor, 0.06f);
                PhantomWitchExpandRing outerExpand = outerRing.gameObject.AddComponent<PhantomWitchExpandRing>();
                outerExpand.Configure(PhantomWitchConfig.DeathFxRadius, 0.55f, deathColor);

                if (detailLevel != PhantomWitchFxDetailLevel.Minimal)
                {
                    Color innerColor = new Color(deathColor.r, deathColor.g, deathColor.b, deathColor.a * 0.75f);
                    LineRenderer innerRing = CreateRingLR(root.transform, smallRingSegments, 0.01f, 0.14f, innerColor, 0.08f);
                    PhantomWitchExpandRing innerExpand = innerRing.gameObject.AddComponent<PhantomWitchExpandRing>();
                    innerExpand.Configure(PhantomWitchConfig.DeathFxRadius * 0.62f, 0.45f, innerColor);
                    innerExpand.delay = 0.12f;

                    LineRenderer pent = CreatePentagramLR(root.transform, PhantomWitchConfig.DeathFxRadius * 0.72f, new Color(deathColor.r, deathColor.g, deathColor.b, 0.55f), 0.10f);
                    PhantomWitchRingSpin pentSpin = pent.gameObject.AddComponent<PhantomWitchRingSpin>();
                    pentSpin.rotationSpeed = detailLevel == PhantomWitchFxDetailLevel.Full ? -24f : -14f;
                }

                if (runeCount > 0)
                {
                    GameObject runeHolder = new GameObject("DeathRunes");
                    runeHolder.transform.SetParent(root.transform, false);
                    runeHolder.transform.localPosition = new Vector3(0f, 0.08f, 0f);
                    AddRuneMarks(runeHolder.transform, PhantomWitchConfig.DeathFxRadius * 0.76f, runeCount, PhantomWitchConfig.RuneMarkWhite, 0f);
                    PhantomWitchRingSpin runeSpin = runeHolder.AddComponent<PhantomWitchRingSpin>();
                    runeSpin.rotationSpeed = detailLevel == PhantomWitchFxDetailLevel.Full ? 48f : 32f;
                }

                if (burstCount > 0)
                {
                    CreateBurstParticles(root.transform, burstCount, 0.9f, PhantomWitchConfig.FxParticlePurple, 1.4f, 3.0f, 0.08f, 0.18f);
                }

                PhantomWitchFadeDestroy rootFade = root.AddComponent<PhantomWitchFadeDestroy>();
                rootFade.Configure(dur, 0.6f);

                UnityEngine.Object.Destroy(root, dur);
                return root;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PhantomWitch] [ERROR] CreateDeathEffect: " + e.Message);
                return null;
            }
        }

        public static void AddReference()
        {
            activeReferenceCount++;
            if (activeReferenceCount < 0)
            {
                activeReferenceCount = 0;
            }

            ModBehaviour.DevLog("[PhantomWitch] 资源引用增加，当前引用计数: " + activeReferenceCount);
        }

        public static void ClearCache()
        {
            activeReferenceCount--;
            if (activeReferenceCount < 0)
            {
                activeReferenceCount = 0;
            }

            ModBehaviour.DevLog("[PhantomWitch] 资源缓存清理，引用计数: " + activeReferenceCount);

            if (activeReferenceCount > 0)
            {
                return;
            }

            if (cachedCurseBuffGO != null)
            {
                UnityEngine.Object.Destroy(cachedCurseBuffGO);
                cachedCurseBuffGO = null;
            }

            cachedCurseBuff = null;
        }

        public static void ForceCleanup()
        {
            activeReferenceCount = 0;

            if (cachedCurseBuffGO != null)
            {
                UnityEngine.Object.Destroy(cachedCurseBuffGO);
                cachedCurseBuffGO = null;
            }

            if (cachedLineMaterial != null)
            {
                UnityEngine.Object.Destroy(cachedLineMaterial);
                cachedLineMaterial = null;
            }

            if (cachedQuadMaterial != null)
            {
                UnityEngine.Object.Destroy(cachedQuadMaterial);
                cachedQuadMaterial = null;
            }

            if (cachedParticleMaterial != null)
            {
                UnityEngine.Object.Destroy(cachedParticleMaterial);
                cachedParticleMaterial = null;
            }

            if (cachedQuadMesh != null)
            {
                UnityEngine.Object.Destroy(cachedQuadMesh);
                cachedQuadMesh = null;
            }

            if (cachedEffectTemplate != null)
            {
                UnityEngine.Object.Destroy(cachedEffectTemplate);
                cachedEffectTemplate = null;
            }

            if (cachedCurseBuffIcon != null)
            {
                Texture2D iconTexture = cachedCurseBuffIcon.texture;
                UnityEngine.Object.Destroy(cachedCurseBuffIcon);
                cachedCurseBuffIcon = null;
                if (iconTexture != null)
                {
                    UnityEngine.Object.Destroy(iconTexture);
                }
            }

            if (cachedEffectMaterial != null)
            {
                UnityEngine.Object.Destroy(cachedEffectMaterial);
                cachedEffectMaterial = null;
            }

            cachedCurseBuff = null;
            cachedUnitCircle48 = null;
            PhantomWitchFxRuntime.Reset();

            ModBehaviour.DevLog("[PhantomWitch] 强制清理完成");
        }

        public static bool HasActiveReferences => activeReferenceCount > 0;
    }

    internal static class PhantomWitchFxRuntime
    {
        private static int activeRootCount = 0;

        internal static PhantomWitchFxDetailLevel CurrentDetailLevel
        {
            get
            {
                return PhantomWitchPerformancePolicy.ResolveFxDetailLevel(
                    activeRootCount,
                    IsLowSpecHardware(),
                    PhantomWitchConfig.FxReducedActiveRootThreshold,
                    PhantomWitchConfig.FxMinimalActiveRootThreshold);
            }
        }

        internal static bool ShouldSkipEffect(PhantomWitchFxEffectImportance importance)
        {
            return PhantomWitchPerformancePolicy.ShouldSkipEffect(
                CurrentDetailLevel,
                activeRootCount,
                PhantomWitchConfig.FxReducedActiveRootThreshold,
                PhantomWitchConfig.FxMinimalActiveRootThreshold,
                importance);
        }

        internal static void RegisterEffectRoot(GameObject root)
        {
            if (root == null || root.GetComponent<PhantomWitchFxRootTracker>() != null)
            {
                return;
            }

            root.AddComponent<PhantomWitchFxRootTracker>();
        }

        internal static void AdjustActiveRootCount(int delta)
        {
            activeRootCount += delta;
            if (activeRootCount < 0)
            {
                activeRootCount = 0;
            }
        }

        internal static void Reset()
        {
            activeRootCount = 0;
        }

        private static bool IsLowSpecHardware()
        {
            int processorCount = SystemInfo.processorCount;
            if (processorCount > 0 && processorCount <= PhantomWitchConfig.FxLowSpecProcessorCount)
            {
                return true;
            }

            int systemMemorySize = SystemInfo.systemMemorySize;
            if (systemMemorySize > 0 && systemMemorySize <= PhantomWitchConfig.FxLowSpecSystemMemoryMb)
            {
                return true;
            }

            int graphicsMemorySize = SystemInfo.graphicsMemorySize;
            return graphicsMemorySize > 0 && graphicsMemorySize <= PhantomWitchConfig.FxLowSpecGraphicsMemoryMb;
        }
    }

    internal sealed class PhantomWitchFxRootTracker : MonoBehaviour
    {
        private bool registered = false;

        private void Awake()
        {
            if (registered)
            {
                return;
            }

            registered = true;
            PhantomWitchFxRuntime.AdjustActiveRootCount(1);
        }

        private void OnDestroy()
        {
            if (!registered)
            {
                return;
            }

            registered = false;
            PhantomWitchFxRuntime.AdjustActiveRootCount(-1);
        }
    }

    internal static class PhantomWitchFxRenderUtil
    {
        internal static readonly int ColorPropertyId = Shader.PropertyToID("_Color");

        internal static Color GetRendererColor(Renderer renderer, MaterialPropertyBlock block)
        {
            if (renderer == null)
            {
                return Color.white;
            }

            if (block == null)
            {
                block = new MaterialPropertyBlock();
            }

            renderer.GetPropertyBlock(block);
            Color color = block.GetColor(ColorPropertyId);
            if (Mathf.Approximately(color.r, 0f) &&
                Mathf.Approximately(color.g, 0f) &&
                Mathf.Approximately(color.b, 0f) &&
                Mathf.Approximately(color.a, 0f))
            {
                Material shared = renderer.sharedMaterial;
                if (shared != null && shared.HasProperty(ColorPropertyId))
                {
                    color = shared.color;
                }
            }

            return color;
        }

        internal static void SetRendererColor(Renderer renderer, Color color)
        {
            if (renderer == null)
            {
                return;
            }

            MaterialPropertyBlock block = new MaterialPropertyBlock();
            SetRendererColor(renderer, block, color);
        }

        internal static void SetRendererColor(Renderer renderer, MaterialPropertyBlock block, Color color)
        {
            if (renderer == null)
            {
                return;
            }

            if (block == null)
            {
                block = new MaterialPropertyBlock();
            }

            renderer.GetPropertyBlock(block);
            block.SetColor(ColorPropertyId, color);
            renderer.SetPropertyBlock(block);
        }
    }

    internal sealed class PhantomWitchFadeDestroy : MonoBehaviour
    {
        private float duration = 0.3f;
        private float fadeDuration = 0.3f;
        private float elapsed;
        private bool initialized;

        private LineRenderer[] lineRenderers = new LineRenderer[0];
        private Renderer[] renderers = new Renderer[0];
        private MaterialPropertyBlock[] rendererBlocks = new MaterialPropertyBlock[0];
        private Color[] lineStartColors = new Color[0];
        private Color[] lineEndColors = new Color[0];
        private Color[] rendererBaseColors = new Color[0];

        public void Configure(float duration)
        {
            Configure(duration, duration);
        }

        public void Configure(float duration, float fadeDuration)
        {
            this.duration = Mathf.Max(0.01f, duration);
            this.fadeDuration = Mathf.Clamp(fadeDuration <= 0f ? this.duration : fadeDuration, 0.01f, this.duration);
            this.elapsed = 0f;
            CacheTargets();
            initialized = true;
        }

        private void Awake()
        {
            if (!initialized)
            {
                Configure(duration, fadeDuration);
            }
        }

        private void CacheTargets()
        {
            lineRenderers = GetComponentsInChildren<LineRenderer>(true);
            lineStartColors = new Color[lineRenderers.Length];
            lineEndColors = new Color[lineRenderers.Length];

            for (int i = 0; i < lineRenderers.Length; i++)
            {
                if (lineRenderers[i] == null)
                {
                    continue;
                }

                lineStartColors[i] = lineRenderers[i].startColor;
                lineEndColors[i] = lineRenderers[i].endColor;
            }

            Renderer[] allRenderers = GetComponentsInChildren<Renderer>(true);
            List<Renderer> rendererList = new List<Renderer>(4);
            List<MaterialPropertyBlock> blockList = new List<MaterialPropertyBlock>(4);
            List<Color> colorList = new List<Color>(4);

            for (int i = 0; i < allRenderers.Length; i++)
            {
                Renderer renderer = allRenderers[i];
                if (renderer == null || renderer is LineRenderer || renderer is ParticleSystemRenderer)
                {
                    continue;
                }

                MaterialPropertyBlock block = new MaterialPropertyBlock();
                rendererList.Add(renderer);
                blockList.Add(block);
                colorList.Add(PhantomWitchFxRenderUtil.GetRendererColor(renderer, block));
            }

            renderers = rendererList.ToArray();
            rendererBlocks = blockList.ToArray();
            rendererBaseColors = colorList.ToArray();
        }

        private void Update()
        {
            elapsed += Time.deltaTime;

            float fadeStart = duration - fadeDuration;
            if (elapsed < fadeStart)
            {
                return;
            }

            float t = Mathf.Clamp01((elapsed - fadeStart) / fadeDuration);
            float alpha = 1f - t;

            for (int i = 0; i < lineRenderers.Length; i++)
            {
                LineRenderer lr = lineRenderers[i];
                if (lr == null)
                {
                    continue;
                }

                Color start = lineStartColors[i];
                Color end = lineEndColors[i];
                start.a *= alpha;
                end.a *= alpha;
                lr.startColor = start;
                lr.endColor = end;
            }

            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null)
                {
                    continue;
                }

                Color color = rendererBaseColors[i];
                color.a *= alpha;
                PhantomWitchFxRenderUtil.SetRendererColor(renderer, rendererBlocks[i], color);
            }

            if (elapsed >= duration)
            {
                Destroy(gameObject);
            }
        }
    }

    internal sealed class PhantomWitchExpandRing : MonoBehaviour
    {
        public float delay;

        private LineRenderer lineRenderer;
        private float targetRadius;
        private float duration;
        private float startWidth;
        private Color baseColor;
        private float elapsed;

        public void Configure(float targetRadius, float duration, Color color)
        {
            lineRenderer = GetComponent<LineRenderer>();
            this.targetRadius = Mathf.Max(0f, targetRadius);
            this.duration = Mathf.Max(0.01f, duration);
            this.baseColor = color;
            this.startWidth = lineRenderer != null ? lineRenderer.widthMultiplier : 0.1f;
            this.elapsed = 0f;
        }

        private void Update()
        {
            if (lineRenderer == null)
            {
                Destroy(this);
                return;
            }

            elapsed += Time.deltaTime;
            if (elapsed < delay)
            {
                return;
            }

            float t = Mathf.Clamp01((elapsed - delay) / duration);
            float eased = 1f - (1f - t) * (1f - t);
            float radius = Mathf.Lerp(0f, targetRadius, eased);

            int count = lineRenderer.positionCount;
            for (int i = 0; i < count; i++)
            {
                float angle = (float)i / count * Mathf.PI * 2f;
                lineRenderer.SetPosition(i, new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius));
            }

            Color color = baseColor;
            color.a = baseColor.a * (1f - t);
            lineRenderer.startColor = color;
            lineRenderer.endColor = color;
            lineRenderer.widthMultiplier = Mathf.Lerp(startWidth, Mathf.Max(0.04f, startWidth * 0.3f), t);

            if (t >= 1f)
            {
                Destroy(gameObject);
            }
        }
    }

    internal sealed class PhantomWitchShrinkRing : MonoBehaviour
    {
        private LineRenderer lineRenderer;
        private float startRadius;
        private float duration;
        private float startWidth;
        private Color baseColor;
        private float elapsed;

        public void Configure(float startRadius, float duration, Color color)
        {
            lineRenderer = GetComponent<LineRenderer>();
            this.startRadius = Mathf.Max(0f, startRadius);
            this.duration = Mathf.Max(0.01f, duration);
            this.baseColor = color;
            this.startWidth = lineRenderer != null ? lineRenderer.widthMultiplier : 0.1f;
            this.elapsed = 0f;
        }

        private void Update()
        {
            if (lineRenderer == null)
            {
                Destroy(this);
                return;
            }

            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float eased = t * t;
            float radius = Mathf.Lerp(startRadius, 0f, eased);

            int count = lineRenderer.positionCount;
            for (int i = 0; i < count; i++)
            {
                float angle = (float)i / count * Mathf.PI * 2f;
                lineRenderer.SetPosition(i, new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius));
            }

            Color color = baseColor;
            color.a = baseColor.a * (1f - t);
            lineRenderer.startColor = color;
            lineRenderer.endColor = color;
            lineRenderer.widthMultiplier = Mathf.Lerp(startWidth, Mathf.Max(0.02f, startWidth * 0.4f), t);

            if (t >= 1f)
            {
                Destroy(gameObject);
            }
        }
    }

    internal sealed class PhantomWitchScaleIn : MonoBehaviour
    {
        private Vector3 targetScale = Vector3.one;
        private float duration = 0.3f;
        private float elapsed;

        public void Configure(Vector3 targetScale, float duration)
        {
            this.targetScale = targetScale;
            this.duration = Mathf.Max(0.01f, duration);
            this.elapsed = 0f;
            transform.localScale = Vector3.zero;
        }

        private void Update()
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float s = t - 1f;
            float eased = 1f + 2.70158f * s * s * s + 1.70158f * s * s;
            transform.localScale = targetScale * eased;

            if (t >= 1f)
            {
                transform.localScale = targetScale;
                Destroy(this);
            }
        }
    }

    internal sealed class PhantomWitchExpandArc : MonoBehaviour
    {
        private LineRenderer lineRenderer;
        private float startRadius;
        private float targetRadius;
        private float halfAngle;
        private Vector3 forward;
        private float duration;
        private float startWidth;
        private Color baseColor;
        private float elapsed;

        public void Configure(float startRadius, float targetRadius, float halfAngle, Vector3 forward, float duration, Color color)
        {
            lineRenderer = GetComponent<LineRenderer>();
            this.startRadius = startRadius;
            this.targetRadius = targetRadius;
            this.halfAngle = halfAngle;
            this.forward = forward;
            this.duration = Mathf.Max(0.01f, duration);
            this.baseColor = color;
            this.startWidth = lineRenderer != null ? lineRenderer.widthMultiplier : 0.1f;
            this.elapsed = 0f;
        }

        private void Update()
        {
            if (lineRenderer == null)
            {
                Destroy(this);
                return;
            }

            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float eased = 1f - (1f - t) * (1f - t);
            float radius = Mathf.Lerp(startRadius, targetRadius, eased);

            Vector3 localForward = forward;
            localForward.y = 0f;
            if (localForward.sqrMagnitude < 0.01f)
            {
                localForward = Vector3.forward;
            }
            localForward.Normalize();

            float baseAngle = Mathf.Atan2(localForward.x, localForward.z);
            float startAngle = baseAngle - halfAngle * Mathf.Deg2Rad;
            float endAngle = baseAngle + halfAngle * Mathf.Deg2Rad;
            int segments = Mathf.Max(1, lineRenderer.positionCount - 1);

            for (int i = 0; i <= segments; i++)
            {
                float segmentT = (float)i / segments;
                float angle = Mathf.Lerp(startAngle, endAngle, segmentT);
                lineRenderer.SetPosition(i, new Vector3(Mathf.Sin(angle) * radius, 0f, Mathf.Cos(angle) * radius));
            }

            Color color = baseColor;
            color.a = baseColor.a * (1f - t);
            lineRenderer.startColor = color;
            lineRenderer.endColor = color;
            lineRenderer.widthMultiplier = Mathf.Lerp(startWidth, Mathf.Max(0.03f, startWidth * 0.35f), t);

            if (t >= 1f)
            {
                Destroy(gameObject);
            }
        }
    }
}
