using System;
using FOW;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// 把独立 NPC / 额外外观接到官方战争迷雾；只隐藏渲染，保留角色逻辑和技能自身的显隐状态。
    /// 模型建好后装配一次，后续只读官方观察者事件与已缓存的 Renderer / Light。
    /// </summary>
    internal sealed class ModEntityFogVisibility : MonoBehaviour
    {
        private FogOfWarHider source;
        private GameObject ownedProbe;
        private Renderer[] renderers = new Renderer[0];
        private Light[] lights = new Light[0];
        private bool[] rendererRestore = new bool[0];
        private int[] lightRestore = new int[0];
        private bool subscribed;
        private bool hidden;
        private bool mainCharacterDead;

        internal static ModEntityFogVisibility Attach(GameObject visualRoot, Transform samplePoint = null)
        {
            if (visualRoot == null) return null;
            ModEntityFogVisibility binding = visualRoot.GetComponent<ModEntityFogVisibility>();
            if (binding != null) return binding;
            try
            {
                binding = visualRoot.AddComponent<ModEntityFogVisibility>();
                binding.renderers = visualRoot.GetComponentsInChildren<Renderer>(true);
                binding.lights = visualRoot.GetComponentsInChildren<Light>(true);
                binding.rendererRestore = new bool[binding.renderers.Length];
                binding.lightRestore = new int[binding.lights.Length];
                Transform sample = samplePoint != null ? samplePoint : visualRoot.transform;
                binding.source = sample.GetComponentInChildren<FogOfWarHider>(true);
                if (binding.source == null)
                {
                    // 官方 hider 在启用时读采样数组，必须先在 inactive probe 上完成配置。
                    binding.ownedProbe = new GameObject("BossRush_FogVisibilityProbe");
                    binding.ownedProbe.SetActive(false);
                    binding.ownedProbe.transform.SetParent(visualRoot.transform, false);
                    binding.ownedProbe.transform.localPosition = new Vector3(0f, 0.526f, 0f);
                    Transform left = new GameObject("LeftSample").transform;
                    left.SetParent(binding.ownedProbe.transform, false);
                    left.localPosition = new Vector3(-0.15f, 0f, 0f);
                    Transform right = new GameObject("RightSample").transform;
                    right.SetParent(binding.ownedProbe.transform, false);
                    right.localPosition = new Vector3(0.15f, 0f, 0f);
                    binding.source = binding.ownedProbe.AddComponent<FogOfWarHider>();
                    binding.source.SamplePoints = new Transform[] { binding.ownedProbe.transform, left, right };
                    binding.source.PermanentlyReveal = false;
                }
                if (binding.isActiveAndEnabled) binding.Subscribe();
                if (binding.ownedProbe != null) binding.ownedProbe.SetActive(true);
                return binding;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModEntityFogVisibility] [WARNING] 视野绑定失败: " + e.Message);
                if (binding != null) Destroy(binding);
                return null;
            }
        }

        private void OnEnable()
        {
            Subscribe();
        }

        private void Subscribe()
        {
            if (subscribed || source == null) return;
            subscribed = true;
            source.OnActiveChanged += OnFogActiveChanged;
            LevelManager.OnMainCharacterDead += OnMainCharacterDead;
            CharacterMainControl main = CharacterMainControl.Main;
            mainCharacterDead = main != null && main.Health != null && main.Health.IsDead;
            // 官方禁用 hider 表示保持可见（含 PermanentlyReveal 首次看见后的状态）。
            OnFogActiveChanged(!source.isActiveAndEnabled || source.NumObservers > 0);
        }

        private void OnFogActiveChanged(bool visible)
        {
            bool shouldHide = !visible && !mainCharacterDead
                && LevelManager.Instance != null && LevelManager.Instance.IsRaidMap;
            if (shouldHide == hidden) return;
            if (shouldHide)
            {
                for (int i = 0; i < renderers.Length; i++)
                    if (renderers[i] != null) rendererRestore[i] = renderers[i].forceRenderingOff;
                for (int i = 0; i < lights.Length; i++)
                    if (lights[i] != null) lightRestore[i] = lights[i].cullingMask;
                hidden = true;
                EnforceHidden();
            }
            else
            {
                RestoreRendering();
            }
        }

        private void LateUpdate()
        {
            if (!hidden) return;
            EnforceHidden();
        }

        private void EnforceHidden()
        {
            for (int i = 0; i < renderers.Length; i++)
                if (renderers[i] != null && !renderers[i].forceRenderingOff) renderers[i].forceRenderingOff = true;
            for (int i = 0; i < lights.Length; i++)
                if (lights[i] != null && lights[i].cullingMask != 0) lights[i].cullingMask = 0;
        }

        private void RestoreRendering()
        {
            if (!hidden) return;
            hidden = false;
            for (int i = 0; i < renderers.Length; i++)
                if (renderers[i] != null) renderers[i].forceRenderingOff = rendererRestore[i];
            for (int i = 0; i < lights.Length; i++)
                if (lights[i] != null) lights[i].cullingMask = lightRestore[i];
        }

        private void OnMainCharacterDead(DamageInfo damage)
        {
            mainCharacterDead = true;
            RestoreRendering();
        }

        private void Unsubscribe()
        {
            if (subscribed)
            {
                subscribed = false;
                if (source != null) source.OnActiveChanged -= OnFogActiveChanged;
                LevelManager.OnMainCharacterDead -= OnMainCharacterDead;
            }
            RestoreRendering();
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        private void OnDestroy()
        {
            Unsubscribe();
            if (ownedProbe != null) Destroy(ownedProbe);
        }
    }
}
