using System;
using System.Collections.Generic;
using SodaCraft;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace BossRush
{
    /// <summary>通过官方 LightControl 调日照，租约结束撤销 Volume 并返还灯光状态。</summary>
    internal sealed class ArenaPrototypeLighting : IDisposable
    {
        private GameObject volumeObject;
        private VolumeProfile profile;
        private Light sun;
        private Light ownSun;
        private Scene entryScene;
        private bool sunEnabled;
        private Color sunColor;
        private float sunIntensity;
        private Quaternion sunRotation;
        private Color sky, equator, ground;
        private AmbientMode ambientMode;
        private bool acquired;
        private readonly List<GameObject> lamps = new List<GameObject>();

        internal void Apply(GameObject root)
        {
            sun = RenderSettings.sun;
            entryScene = SceneManager.GetActiveScene();
            if (sun != null)
            {
                sunEnabled = sun.enabled;
                sunColor = sun.color;
                sunIntensity = sun.intensity;
                sunRotation = sun.transform.rotation;
            }
            sky = RenderSettings.ambientSkyColor;
            equator = RenderSettings.ambientEquatorColor;
            ground = RenderSettings.ambientGroundColor;
            ambientMode = RenderSettings.ambientMode;
            acquired = true;
            // 基地可能没有活动日照；本次自建主光源由 RenderSettings.sun 显式选择。
            GameObject sunObject = new GameObject("ArenaDaylightSun");
            sunObject.transform.SetParent(root.transform, false);
            ownSun = sunObject.AddComponent<Light>();
            ownSun.type = LightType.Directional;
            ownSun.color = new Color(1f, 0.94f, 0.82f);
            ownSun.intensity = 1.8f;
            ownSun.shadows = LightShadows.Soft;
            ownSun.transform.rotation = Quaternion.Euler(55, -35, 0);
            RenderSettings.sun = ownSun;
            if (sun != null) sun.enabled = false;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            volumeObject = new GameObject("ArenaDaylightVolume");
            volumeObject.transform.SetParent(root.transform, false);
            Volume volume = volumeObject.AddComponent<Volume>();
            // 使用摄像机已接收的官方 Volume 层，不假定作者工程的 Layer 编号。
            Camera camera = GameCamera.Instance.renderCamera;
            UniversalAdditionalCameraData cameraData = camera.GetComponent<UniversalAdditionalCameraData>();
            int mask = cameraData == null ? 1 : cameraData.volumeLayerMask.value;
            for (int i = 0; i < 32; i++) if ((mask & (1 << i)) != 0) { volumeObject.layer = i; break; }
            float priority = 0;
            foreach (Volume existing in UnityEngine.Object.FindObjectsOfType<Volume>())
                if (existing != volume && existing.priority >= priority) priority = existing.priority + 1;
            volume.priority = priority;
            volume.isGlobal = true;
            volume.weight = 1;
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            volume.sharedProfile = profile;
            LightControl light = profile.Add<LightControl>(false);
            light.enable.Override(true);
            light.skyColor.Override(new Color(0.85f, 0.92f, 1f));
            light.equatorColor.Override(new Color(0.65f, 0.70f, 0.76f));
            light.groundColor.Override(new Color(0.42f, 0.40f, 0.36f));
            light.sunColor.Override(new Color(1f, 0.94f, 0.82f));
            light.sunIntensity.Override(1.8f);
            light.sunRotation.Override(new Vector3(55, -35, 0));
            light.SodaLightTint.Override(Color.white);
            // 仓房采用游戏现有的体积灯材质/网格，复制对象只含渲染器和 SodaPointLight。
            SodaPointLight source = null;
            foreach (SodaPointLight candidate in Resources.FindObjectsOfTypeAll<SodaPointLight>())
                if (candidate.GetComponent<Renderer>() != null && candidate.GetComponent<MeshFilter>() != null)
                { source = candidate; break; }
            foreach (Transform marker in root.GetComponentsInChildren<Transform>(true))
            {
                if (!marker.name.StartsWith("Lamp", StringComparison.Ordinal) || marker.name == "LampFixture") continue;
                if (source == null) throw new InvalidOperationException("基地缺少可复用的 SodaPointLight 网格灯");
                GameObject lamp = new GameObject("OutpostRoomLight");
                lamps.Add(lamp);
                lamp.transform.SetParent(root.transform, false);
                lamp.transform.position = marker.position + Vector3.up * 2;
                lamp.layer = source.gameObject.layer;
                lamp.AddComponent<MeshFilter>().sharedMesh = source.GetComponent<MeshFilter>().sharedMesh;
                MeshRenderer renderer = lamp.AddComponent<MeshRenderer>();
                renderer.sharedMaterials = source.GetComponent<Renderer>().sharedMaterials;
                lamp.transform.localScale = new Vector3(14, 8, 14);
                MaterialPropertyBlock block = new MaterialPropertyBlock();
                block.SetColor("_LightColor", new Color(1.8f, 1.2f, 0.55f));
                block.SetFloat("_FallOff", 1.5f);
                block.SetFloat("_Hardness", 0.25f);
                block.SetFloat("_EnviromentTintOn", 0);
                renderer.SetPropertyBlock(block);
                lamp.SetActive(true);
            }
            Debug.Log("[ArenaPrototype] LIGHTING_READY sun=" + ownSun.name + " priority=" + priority + " rooms=" + lamps.Count);
        }

        public void Dispose()
        {
            if (!acquired) return;
            acquired = false;
            if (volumeObject != null) { volumeObject.SetActive(false); UnityEngine.Object.Destroy(volumeObject); }
            if (profile != null)
            {
                foreach (VolumeComponent component in profile.components) if (component != null) UnityEngine.Object.Destroy(component);
                UnityEngine.Object.Destroy(profile);
            }
            foreach (GameObject lamp in lamps) if (lamp != null) UnityEngine.Object.Destroy(lamp);
            lamps.Clear();
            if (ownSun != null)
            {
                ownSun.enabled = false;
                if (RenderSettings.sun == ownSun) RenderSettings.sun = sun;
                UnityEngine.Object.Destroy(ownSun.gameObject);
            }
            if (sun != null)
            {
                sun.enabled = sunEnabled;
                sun.color = sunColor;
                sun.intensity = sunIntensity;
                sun.transform.rotation = sunRotation;
            }
            if (entryScene.isLoaded && SceneManager.GetActiveScene().handle == entryScene.handle)
            {
                RenderSettings.ambientMode = ambientMode;
                RenderSettings.ambientSkyColor = sky;
                RenderSettings.ambientEquatorColor = equator;
                RenderSettings.ambientGroundColor = ground;
            }
        }
    }
}
