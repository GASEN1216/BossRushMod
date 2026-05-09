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
        private static GameObject cachedEffectTemplate = null;
        private static Material cachedEffectMaterial = null;
        private static int activeReferenceCount = 0;
        private static bool pendingCacheCleanup = false;

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
                // WalkSpeed 减速
                GameObject effectChild = new GameObject("CurseSlowEffect");
                effectChild.transform.SetParent(cachedCurseBuffGO.transform, false);

                Effect effect = effectChild.AddComponent<Effect>();
                TriggerOnSetItem trigger = effectChild.AddComponent<TriggerOnSetItem>();
                ModifierAction modifier = effectChild.AddComponent<ModifierAction>();

                modifier.targetStatKey = "WalkSpeed";
                modifier.modifierValue = PhantomWitchConfig.CurseSlowPerLayer;

                SetFieldSafe(modifier, modifierBuffField, buff);

                if (effectTriggersField != null)
                {
                    var triggersList = effectTriggersField.GetValue(effect) as System.Collections.IList;
                    if (triggersList != null) triggersList.Add(trigger);
                }

                if (effectActionsField != null)
                {
                    var actionsList = effectActionsField.GetValue(effect) as System.Collections.IList;
                    if (actionsList != null) actionsList.Add(modifier);
                }

                // RunSpeed 减速（防止冲刺抵消减速）
                GameObject runEffectChild = new GameObject("CurseRunSlowEffect");
                runEffectChild.transform.SetParent(cachedCurseBuffGO.transform, false);

                Effect runEffect = runEffectChild.AddComponent<Effect>();
                TriggerOnSetItem runTrigger = runEffectChild.AddComponent<TriggerOnSetItem>();
                ModifierAction runModifier = runEffectChild.AddComponent<ModifierAction>();

                runModifier.targetStatKey = "RunSpeed";
                runModifier.modifierValue = PhantomWitchConfig.CurseSlowPerLayer;

                SetFieldSafe(runModifier, modifierBuffField, buff);

                if (effectTriggersField != null)
                {
                    var triggersList = effectTriggersField.GetValue(runEffect) as System.Collections.IList;
                    if (triggersList != null) triggersList.Add(runTrigger);
                }

                if (effectActionsField != null)
                {
                    var actionsList = effectActionsField.GetValue(runEffect) as System.Collections.IList;
                    if (actionsList != null) actionsList.Add(runModifier);
                }

                // Buff.effects → 加入两个 Effect
                if (buffEffectsField != null)
                {
                    var effectsList = buffEffectsField.GetValue(buff) as System.Collections.IList;
                    if (effectsList != null)
                    {
                        effectsList.Add(effect);
                        effectsList.Add(runEffect);
                    }
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

        private static Texture2D cachedSoftCircle;

        internal static Texture2D GetSoftCircleTexture()
        {
            if (cachedSoftCircle != null) return cachedSoftCircle;

            cachedSoftCircle = new Texture2D(32, 32, TextureFormat.Alpha8, false);
            Color[] pixels = new Color[32 * 32];
            Vector2 center = new Vector2(15.5f, 15.5f);
            for (int y = 0; y < 32; y++)
            {
                for (int x = 0; x < 32; x++)
                {
                    float dist = Vector2.Distance(new Vector2(x, y), center);
                    float alpha = Mathf.Clamp01(1f - (dist / 15.5f));
                    alpha = alpha * alpha * (3f - 2f * alpha);
                    pixels[y * 32 + x] = new Color(1f, 1f, 1f, alpha);
                }
            }
            cachedSoftCircle.SetPixels(pixels);
            cachedSoftCircle.Apply();
            return cachedSoftCircle;
        }

        internal static Material GetLineMaterial()
        {
            if (cachedLineMaterial != null)
            {
                return cachedLineMaterial;
            }
            Shader shader = Shader.Find("Legacy Shaders/Particles/Additive") ?? Shader.Find("Particles/Additive") ?? Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Unlit/Transparent");
            if (shader != null)
            {
                cachedLineMaterial = new Material(shader);
                cachedLineMaterial.name = "PW_SharedLine";
                cachedLineMaterial.enableInstancing = true;
                cachedLineMaterial.mainTexture = GetSoftCircleTexture();
            }
            return cachedLineMaterial;
        }

        internal static Material GetQuadMaterial()
        {
            if (cachedQuadMaterial != null)
            {
                return cachedQuadMaterial;
            }
            Shader shader = Shader.Find("Legacy Shaders/Particles/Additive") ?? Shader.Find("Particles/Additive") ?? Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Unlit/Transparent");
            if (shader != null)
            {
                cachedQuadMaterial = new Material(shader);
                cachedQuadMaterial.name = "PW_SharedQuad";
                cachedQuadMaterial.enableInstancing = true;
                cachedQuadMaterial.mainTexture = GetSoftCircleTexture();
            }
            return cachedQuadMaterial;
        }

        internal static Material GetParticleMaterial()
        {
            if (cachedParticleMaterial != null)
            {
                return cachedParticleMaterial;
            }

            Shader shader = Shader.Find("Particles/Standard Unlit") ?? Shader.Find("Mobile/Particles/Additive") ?? Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Unlit/Transparent");
            if (shader != null)
            {
                cachedParticleMaterial = new Material(shader);
                cachedParticleMaterial.name = "PW_SharedParticle";
                cachedParticleMaterial.enableInstancing = true;

                cachedParticleMaterial.mainTexture = GetSoftCircleTexture();

                // Additive 混合：粒子叠加发光而非覆盖，消除实色块感
                cachedParticleMaterial.SetInt("_SrcBlend", 5); // SrcAlpha
                cachedParticleMaterial.SetInt("_DstBlend", 1); // One
                cachedParticleMaterial.renderQueue = 3000;
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
                return PhantomWitchVfxRedesign.CreateTeleportEffect(position, isAppear);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PhantomWitch] [ERROR] CreateTeleportEffect: " + e.Message);
                return null;
            }
        }

        public static GameObject CreateTrackedTeleportMarkerEffect(Vector3 position, float duration)
        {
            try
            {
                if (PhantomWitchFxRuntime.ShouldSkipEffect(PhantomWitchFxEffectImportance.Critical))
                {
                    return null;
                }

                return PhantomWitchVfxRedesign.CreateTrackedTeleportMarkerEffect(position, duration);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PhantomWitch] [ERROR] CreateTrackedTeleportMarkerEffect: " + e.Message);
                return null;
            }
        }

        public static GameObject CreateTrackedTeleportFlashEffect(Vector3 position)
        {
            try
            {
                if (PhantomWitchFxRuntime.ShouldSkipEffect(PhantomWitchFxEffectImportance.Critical))
                {
                    return null;
                }

                return PhantomWitchVfxRedesign.CreateTrackedTeleportFlashEffect(position);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PhantomWitch] [ERROR] CreateTrackedTeleportFlashEffect: " + e.Message);
                return null;
            }
        }

        public static GameObject CreateCurseAuraEffect(Vector3 position, float radius)
        {
            try
            {
                return PhantomWitchVfxRedesign.CreateCurseAuraEffect(position, radius);
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
                if (PhantomWitchFxRuntime.ShouldSkipEffect(PhantomWitchFxEffectImportance.Standard))
                {
                    return null;
                }
                return PhantomWitchVfxRedesign.CreateScytheSweepEffect(position, forward, radius, halfAngle);
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
                if (PhantomWitchFxRuntime.ShouldSkipEffect(PhantomWitchFxEffectImportance.Standard))
                {
                    return null;
                }
                return PhantomWitchVfxRedesign.CreateHeavySlashEffect(position, forward, radius);
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
                if (PhantomWitchFxRuntime.ShouldSkipEffect(PhantomWitchFxEffectImportance.Standard))
                {
                    return null;
                }
                return PhantomWitchVfxRedesign.CreateSummonCircleEffect(position);
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
                if (PhantomWitchFxRuntime.ShouldSkipEffect(PhantomWitchFxEffectImportance.Critical))
                {
                    return null;
                }
                return PhantomWitchVfxRedesign.CreateMinionSpawnEffect(position);
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
                return PhantomWitchVfxRedesign.CreateDamageHitEffect(position);
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
                if (PhantomWitchFxRuntime.ShouldSkipEffect(PhantomWitchFxEffectImportance.Critical))
                {
                    return null;
                }
                return PhantomWitchVfxRedesign.CreatePhaseTransitionEffect(position);
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
                return PhantomWitchVfxRedesign.CreateSpawnEffect(position, Mathf.Max(0.1f, duration));
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PhantomWitch] [ERROR] CreateEffect: " + e.Message);
                return null;
            }
        }

        public static GameObject CreateChannelChargeEffect(Vector3 position, float radius, float duration, bool useBloodAccent)
        {
            try
            {
                if (PhantomWitchFxRuntime.ShouldSkipEffect(PhantomWitchFxEffectImportance.Standard))
                {
                    return null;
                }
                return PhantomWitchVfxRedesign.CreateChannelChargeEffect(position, radius, duration, useBloodAccent);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PhantomWitch] [ERROR] CreateChannelChargeEffect: " + e.Message);
                return null;
            }
        }

        public static GameObject CreateWraithWindupOutlineEffect(Vector3 position, Vector3 forward, float radius, float duration)
        {
            try
            {
                if (PhantomWitchFxRuntime.ShouldSkipEffect(PhantomWitchFxEffectImportance.Standard))
                {
                    return null;
                }
                return PhantomWitchVfxRedesign.CreateWraithWindupOutlineEffect(position, forward, radius, duration);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PhantomWitch] [ERROR] CreateWraithWindupOutlineEffect: " + e.Message);
                return null;
            }
        }

        public static GameObject CreateSemiStealthWindupEffect(Transform bossBody)
        {
            try
            {
                if (bossBody == null)
                {
                    return null;
                }

                if (PhantomWitchFxRuntime.ShouldSkipEffect(PhantomWitchFxEffectImportance.Critical))
                {
                    return null;
                }

                return PhantomWitchVfxRedesign.CreateSemiStealthWindupEffect(bossBody);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PhantomWitch] [ERROR] CreateSemiStealthWindupEffect: " + e.Message);
                return null;
            }
        }

        public static GameObject CreateCurseRealmWarningCircle(Vector3 position, float radius, float duration)
        {
            try
            {
                if (PhantomWitchFxRuntime.ShouldSkipEffect(PhantomWitchFxEffectImportance.Critical))
                {
                    return null;
                }
                return PhantomWitchVfxRedesign.CreateCurseRealmWarningCircle(position, radius, duration);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PhantomWitch] [ERROR] CreateCurseRealmWarningCircle: " + e.Message);
                return null;
            }
        }

        public static GameObject CreateBossCurseRealmVisual(Vector3 position, float radius, float duration)
        {
            try
            {
                if (PhantomWitchFxRuntime.ShouldSkipEffect(PhantomWitchFxEffectImportance.Standard))
                {
                    return null;
                }
                GameObject visual = PhantomWitchCurseRealmVisual.Create(position, radius, duration);
                if (visual != null)
                {
                    visual.name = "PhantomWitch_BossCurseRealm_Visual";
                }
                return visual;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PhantomWitch] [ERROR] CreateBossCurseRealmVisual: " + e.Message);
                return null;
            }
        }

        public static GameObject CreateDeathEffect(Vector3 position)
        {
            try
            {
                return PhantomWitchVfxRedesign.CreateDeathEffect(position);
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

            if (PhantomWitchFxRuntime.HasActiveRoots)
            {
                pendingCacheCleanup = true;
                return;
            }

            FinalizeCacheCleanup();
        }

        public static void ForceCleanup()
        {
            activeReferenceCount = 0;
            pendingCacheCleanup = false;

            FinalizeCacheCleanup();
            PhantomWitchFxRuntime.Reset();

            ModBehaviour.DevLog("[PhantomWitch] 强制清理完成");
        }

        private static void FinalizeCacheCleanup()
        {
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
            PhantomWitchVfxRedesign.ClearCache();
            pendingCacheCleanup = false;
        }

        public static bool HasActiveReferences => activeReferenceCount > 0;

        internal static void TryFinalizePendingCacheCleanup()
        {
            if (!pendingCacheCleanup || activeReferenceCount > 0 || PhantomWitchFxRuntime.HasActiveRoots)
            {
                return;
            }

            FinalizeCacheCleanup();
        }
    }

    internal static class PhantomWitchFxRuntime
    {
        private static int activeRootCount = 0;
        private static Camera cachedMainCamera = null;
        private static int cachedMainCameraFrame = -1;
        internal static bool HasActiveRoots => activeRootCount > 0;

        internal static Camera CurrentCamera
        {
            get
            {
                // Collapse repeated per-frame Camera.main lookups across PhantomWitch FX.
                if (cachedMainCameraFrame != Time.frameCount || cachedMainCamera == null)
                {
                    cachedMainCamera = Camera.main;
                    cachedMainCameraFrame = Time.frameCount;
                }

                return cachedMainCamera;
            }
        }

        internal static PhantomWitchFxDetailLevel CurrentDetailLevel
        {
            get
            {
                return PhantomWitchPerformancePolicy.ResolveFxDetailLevel(
                    activeRootCount,
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

            PhantomWitchAssetManager.TryFinalizePendingCacheCleanup();
        }

        internal static void Reset()
        {
            activeRootCount = 0;
            cachedMainCamera = null;
            cachedMainCameraFrame = -1;
        }
    }

    internal sealed class PhantomWitchFxRootTracker : MonoBehaviour
    {
        private bool registered = false;

        private void OnEnable()
        {
            if (registered)
            {
                return;
            }

            registered = true;
            PhantomWitchFxRuntime.AdjustActiveRootCount(1);
        }

        private void OnDisable()
        {
            if (!registered)
            {
                return;
            }

            registered = false;
            PhantomWitchFxRuntime.AdjustActiveRootCount(-1);
        }

        private void OnDestroy()
        {
            if (registered)
            {
                registered = false;
                PhantomWitchFxRuntime.AdjustActiveRootCount(-1);
            }
        }
    }

    internal static class PhantomWitchFxRenderUtil
    {
        internal static readonly int ColorPropertyId = Shader.PropertyToID("_Color");
        internal static readonly int TintColorPropertyId = Shader.PropertyToID("_TintColor");
        internal static readonly int BaseColorPropertyId = Shader.PropertyToID("_BaseColor");

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
                color = block.GetColor(TintColorPropertyId);
            }

            if (Mathf.Approximately(color.r, 0f) &&
                Mathf.Approximately(color.g, 0f) &&
                Mathf.Approximately(color.b, 0f) &&
                Mathf.Approximately(color.a, 0f))
            {
                color = block.GetColor(BaseColorPropertyId);
            }

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
                else if (shared != null && shared.HasProperty(TintColorPropertyId))
                {
                    color = shared.GetColor(TintColorPropertyId);
                }
                else if (shared != null && shared.HasProperty(BaseColorPropertyId))
                {
                    color = shared.GetColor(BaseColorPropertyId);
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
            Material shared = renderer.sharedMaterial;
            if (shared != null && shared.HasProperty(ColorPropertyId))
            {
                block.SetColor(ColorPropertyId, color);
            }
            else if (shared != null && shared.HasProperty(TintColorPropertyId))
            {
                block.SetColor(TintColorPropertyId, color);
            }
            else
            {
                block.SetColor(BaseColorPropertyId, color);
            }
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

        private void OnEnable()
        {
            if (initialized)
            {
                elapsed = 0f;
                ApplyAlpha(1f);
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
            ApplyAlpha(1f - t);

            if (elapsed >= duration)
            {
                Destroy(gameObject);
            }
        }

        private void ApplyAlpha(float alpha)
        {
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

                Color c = rendererBaseColors[i];
                c.a *= alpha;
                PhantomWitchFxRenderUtil.SetRendererColor(renderer, rendererBlocks[i], c);
            }
        }
    }

    internal sealed class PhantomWitchFlatPathMesh : MonoBehaviour
    {
        private MeshFilter meshFilter;
        private MeshRenderer meshRenderer;
        private MaterialPropertyBlock propertyBlock;
        private Mesh mesh;

        internal int PointCount { get; private set; }

        internal void Configure(IList<Vector3> points, float width, Material material, Color color)
        {
            meshFilter = GetComponent<MeshFilter>();
            if (meshFilter == null)
            {
                meshFilter = gameObject.AddComponent<MeshFilter>();
            }

            meshRenderer = GetComponent<MeshRenderer>();
            if (meshRenderer == null)
            {
                meshRenderer = gameObject.AddComponent<MeshRenderer>();
            }

            if (mesh == null)
            {
                mesh = new Mesh();
                mesh.name = "PW_FlatPathMesh";
            }

            meshFilter.sharedMesh = mesh;
            if (material != null)
            {
                meshRenderer.sharedMaterial = material;
            }

            if (propertyBlock == null)
            {
                propertyBlock = new MaterialPropertyBlock();
            }

            SetPath(points, width);
            SetColor(color);
        }

        internal void SetPath(IList<Vector3> points, float width)
        {
            PointCount = points != null ? points.Count : 0;
            BuildPathMesh(mesh, points, width);
        }

        internal void SetColor(Color color)
        {
            if (meshRenderer == null)
            {
                return;
            }

            if (propertyBlock == null)
            {
                propertyBlock = new MaterialPropertyBlock();
            }

            PhantomWitchFxRenderUtil.SetRendererColor(meshRenderer, propertyBlock, color);
        }

        private static void BuildPathMesh(Mesh mesh, IList<Vector3> points, float width)
        {
            if (mesh == null)
            {
                return;
            }

            if (points == null || points.Count < 2)
            {
                mesh.Clear();
                return;
            }

            float halfWidth = Mathf.Max(0.001f, width) * 0.5f;
            int pointCount = points.Count;
            Vector3[] vertices = new Vector3[pointCount * 2];
            Vector2[] uv = new Vector2[pointCount * 2];
            int[] triangles = new int[(pointCount - 1) * 6];

            for (int i = 0; i < pointCount; i++)
            {
                Vector3 tangent;
                if (i == 0)
                {
                    tangent = points[1] - points[0];
                }
                else if (i == pointCount - 1)
                {
                    tangent = points[pointCount - 1] - points[pointCount - 2];
                }
                else
                {
                    tangent = points[i + 1] - points[i - 1];
                }

                if (tangent.sqrMagnitude < 0.0001f)
                {
                    tangent = Vector3.right;
                }

                Vector3 side = Vector3.Cross(Vector3.up, tangent.normalized) * halfWidth;
                int vertexIndex = i * 2;
                vertices[vertexIndex] = points[i] - side;
                vertices[vertexIndex + 1] = points[i] + side;

                float u = pointCount > 1 ? (float)i / (pointCount - 1) : 0f;
                uv[vertexIndex] = new Vector2(u, 0f);
                uv[vertexIndex + 1] = new Vector2(u, 1f);

                if (i >= pointCount - 1)
                {
                    continue;
                }

                int triangleIndex = i * 6;
                triangles[triangleIndex] = vertexIndex;
                triangles[triangleIndex + 1] = vertexIndex + 1;
                triangles[triangleIndex + 2] = vertexIndex + 2;
                triangles[triangleIndex + 3] = vertexIndex + 1;
                triangles[triangleIndex + 4] = vertexIndex + 3;
                triangles[triangleIndex + 5] = vertexIndex + 2;
            }

            mesh.Clear();
            mesh.vertices = vertices;
            mesh.uv = uv;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
        }

        private void OnDestroy()
        {
            if (mesh != null)
            {
                Destroy(mesh);
                mesh = null;
            }
        }
    }

    internal sealed class PhantomWitchExpandRing : MonoBehaviour
    {
        public float delay = 0f;

        private LineRenderer lineRenderer;
        private PhantomWitchFlatRingMesh ringMesh;
        private float targetRadius;
        private float duration;
        private float startWidth;
        private Color baseColor;
        private float elapsed;
        private bool positionsNormalized;

        public void Configure(float targetRadius, float duration, Color color, float width)
        {
            lineRenderer = GetComponent<LineRenderer>();
            ringMesh = GetComponent<PhantomWitchFlatRingMesh>();
            this.targetRadius = Mathf.Max(0f, targetRadius);
            this.duration = Mathf.Max(0.01f, duration);
            this.baseColor = color;
            this.startWidth = lineRenderer != null ? lineRenderer.widthMultiplier : Mathf.Max(0.001f, width);
            this.elapsed = 0f;
        }

        private void Update()
        {
            if (lineRenderer == null && ringMesh == null)
            {
                Destroy(this);
                return;
            }

            if (lineRenderer != null && !positionsNormalized)
            {
                int count = lineRenderer.positionCount;
                for (int i = 0; i < count; i++)
                {
                    float angle = (float)i / count * Mathf.PI * 2f;
                    lineRenderer.SetPosition(i, new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)));
                }
                positionsNormalized = true;
            }

            elapsed += Time.deltaTime;
            if (elapsed < delay)
            {
                return;
            }

            float t = Mathf.Clamp01((elapsed - delay) / duration);
            float eased = 1f - (1f - t) * (1f - t);
            float radius = Mathf.Lerp(0f, targetRadius, eased);
            float width = Mathf.Lerp(startWidth, Mathf.Max(0.04f, startWidth * 0.3f), t);

            Color color = baseColor;
            color.a = baseColor.a * (1f - t);
            if (lineRenderer != null)
            {
                transform.localScale = new Vector3(radius, 1f, radius);
                lineRenderer.startColor = color;
                lineRenderer.endColor = color;
                lineRenderer.widthMultiplier = width;
            }
            else
            {
                ringMesh.SetShape(radius, width);
                ringMesh.SetColor(color);
            }

            if (t >= 1f)
            {
                Destroy(gameObject);
            }
        }
    }

    internal sealed class PhantomWitchShrinkRing : MonoBehaviour
    {
        private LineRenderer lineRenderer;
        private PhantomWitchFlatRingMesh ringMesh;
        private float startRadius;
        private float duration;
        private float startWidth;
        private Color baseColor;
        private float elapsed;
        private bool positionsNormalized;

        public void Configure(float startRadius, float duration, Color color, float width)
        {
            lineRenderer = GetComponent<LineRenderer>();
            ringMesh = GetComponent<PhantomWitchFlatRingMesh>();
            this.startRadius = Mathf.Max(0f, startRadius);
            this.duration = Mathf.Max(0.01f, duration);
            this.baseColor = color;
            this.startWidth = lineRenderer != null ? lineRenderer.widthMultiplier : Mathf.Max(0.001f, width);
            this.elapsed = 0f;
        }

        private void Update()
        {
            if (lineRenderer == null && ringMesh == null)
            {
                Destroy(this);
                return;
            }

            if (lineRenderer != null && !positionsNormalized)
            {
                int count = lineRenderer.positionCount;
                for (int i = 0; i < count; i++)
                {
                    float angle = (float)i / count * Mathf.PI * 2f;
                    lineRenderer.SetPosition(i, new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)));
                }
                positionsNormalized = true;
            }

            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float eased = t * t;
            float radius = Mathf.Lerp(startRadius, 0f, eased);
            float width = Mathf.Lerp(startWidth, Mathf.Max(0.02f, startWidth * 0.4f), t);

            Color color = baseColor;
            color.a = baseColor.a * (1f - t);
            if (lineRenderer != null)
            {
                transform.localScale = new Vector3(radius, 1f, radius);
                lineRenderer.startColor = color;
                lineRenderer.endColor = color;
                lineRenderer.widthMultiplier = width;
            }
            else
            {
                ringMesh.SetShape(radius, width);
                ringMesh.SetColor(color);
            }

            if (t >= 1f)
            {
                Destroy(gameObject);
            }
        }
    }

    internal sealed class PhantomWitchExpandArc : MonoBehaviour
    {
        private LineRenderer lineRenderer;
        private PhantomWitchFlatPathMesh pathMesh;
        private float startRadius;
        private float targetRadius;
        private float halfAngle;
        private Vector3 forward;
        private float duration;
        private float startWidth;
        private Color baseColor;
        private float elapsed;
        private bool positionsNormalized;

        public void Configure(float startRadius, float targetRadius, float halfAngle, Vector3 forward, float duration, Color color, float width)
        {
            lineRenderer = GetComponent<LineRenderer>();
            pathMesh = GetComponent<PhantomWitchFlatPathMesh>();
            this.startRadius = startRadius;
            this.targetRadius = targetRadius;
            this.halfAngle = halfAngle;
            this.forward = forward;
            this.duration = Mathf.Max(0.01f, duration);
            this.baseColor = color;
            this.startWidth = lineRenderer != null ? lineRenderer.widthMultiplier : Mathf.Max(0.001f, width);
            this.elapsed = 0f;
        }

        private void Update()
        {
            if (lineRenderer == null && pathMesh == null)
            {
                Destroy(this);
                return;
            }

            Vector3 localForward = forward;
            localForward.y = 0f;
            if (localForward.sqrMagnitude < 0.01f)
            {
                localForward = Vector3.forward;
            }
            localForward.Normalize();

            if (lineRenderer != null && !positionsNormalized)
            {
                float baseAngle = Mathf.Atan2(localForward.x, localForward.z);
                float startAngle = baseAngle - halfAngle * Mathf.Deg2Rad;
                float endAngle = baseAngle + halfAngle * Mathf.Deg2Rad;
                int segments = Mathf.Max(1, lineRenderer.positionCount - 1);

                for (int i = 0; i <= segments; i++)
                {
                    float segmentT = (float)i / segments;
                    float angle = Mathf.Lerp(startAngle, endAngle, segmentT);
                    lineRenderer.SetPosition(i, new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)));
                }
                positionsNormalized = true;
            }

            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float eased = 1f - (1f - t) * (1f - t);
            float radius = Mathf.Lerp(startRadius, targetRadius, eased);
            float width = Mathf.Lerp(startWidth, Mathf.Max(0.03f, startWidth * 0.35f), t);

            Color color = baseColor;
            color.a = baseColor.a * (1f - t);
            if (lineRenderer != null)
            {
                transform.localScale = new Vector3(radius, 1f, radius);
                lineRenderer.startColor = color;
                lineRenderer.endColor = color;
                lineRenderer.widthMultiplier = width;
            }
            else
            {
                int segments = Mathf.Max(1, pathMesh.PointCount - 1);
                float baseAngle = Mathf.Atan2(localForward.x, localForward.z);
                float startAngle = baseAngle - halfAngle * Mathf.Deg2Rad;
                float endAngle = baseAngle + halfAngle * Mathf.Deg2Rad;
                Vector3[] points = new Vector3[segments + 1];
                for (int i = 0; i <= segments; i++)
                {
                    float segmentT = (float)i / segments;
                    float angle = Mathf.Lerp(startAngle, endAngle, segmentT);
                    points[i] = new Vector3(Mathf.Sin(angle) * radius, 0f, Mathf.Cos(angle) * radius);
                }

                pathMesh.SetPath(points, width);
                pathMesh.SetColor(color);
            }

            if (t >= 1f)
            {
                Destroy(gameObject);
            }
        }
    }
}
