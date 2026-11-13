using System;
using System.Linq;
using BossRush;
using Duckov.Weathers;
using SodaCraft;
using Umbra;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

internal static class Program
{
    private static void Main()
    {
        Run("官方方向光停用跨幕布，等出击图激活才绑定", LoadThroughCurtain);
        Run("只读采样官方更新后的线性色、强度和环境色", ReadOfficialChanges);
        Run("优先使用本图现成的官方主光", UseExistingRaidSun);
        Run("拒绝有其他脚本或多个候选的来源", RejectUnsafeSources);
        Run("转场取消与挂载失败都释放暂存和订阅", DisposeBeforeActivation);
        Run("活动场景与接管主光不受退出清理影响", DisposeAfterReplacement);
        Run("基地户外模板跨下水道卸载，副本独立使用实时主光", BaseOutdoorTemplate);
        Run("直接进下水道时使用官方运行时组件与阴影资源", NativeSun);
        Run("潮闸镇晴天改用官方户外时段，保留原雨天与原基地配置", OutdoorWeather);
        Run("诊断区分官方天气目标、混合值和当前实际渲染参数", DescribeLiveLighting);
    }

    private static void Run(string name, Action test)
    {
        SceneManager.Reset();
        QualitySettings.activeColorSpace = ColorSpace.Linear;
        Resources.Assets = Array.Empty<Object>();
        Debug.Warnings.Clear();
        Shader.Floats.Clear();
        Shader.Colors.Clear();
        VolumeManager.instance.stack = null;
        TimeOfDayController.Instance = null;
        GameCamera.Instance = null;
        LightControl.SetTarget(null);
        test();
        Check(SceneManager.ActiveSceneSubscribers == 0, "活动场景事件未释放");
        Console.WriteLine("PASS " + name);
    }

    private static void LoadThroughCurtain()
    {
        Scene warehouse = SceneManager.Load(1, "Level_HiddenWarehouse");
        Light official = AddSun(warehouse);
        var sourceShadows = official.GetComponent<UmbraSoftShadows>();
        sourceShadows.profile.contactStrength = 0.42f;
        SceneManager.Activate(warehouse);
        RenderSettings.sun = official;
        RenderSettings.ambientMode = AmbientMode.Trilight;

        using (var lighting = new RaidOfficialLighting())
        {
            lighting.Prepare(warehouse);
            Light clone = FindClone(official);
            var cloneShadows = clone.GetComponent<UmbraSoftShadows>();
            Check(!clone.isActiveAndEnabled && clone.gameObject.scene.handle == 0, "准备阶段已激活或未持久化");
            Check(clone.GetComponent<UniversalAdditionalLightData>().settings == 197, "官方 URP 参数未随方向光复制");
            Check(cloneShadows.profile != sourceShadows.profile && cloneShadows.profile.contactStrength == 0.42f,
                "官方阴影配置没有复制到独立实例");
            Check(RenderSettings.sun == official && official.isActiveAndEnabled, "准备阶段改动了来源场景");

            Scene curtain = SceneManager.LoadSingle(2, "DefaultCurtain");
            Check(official == null && clone != null && !clone.isActiveAndEnabled, "克隆未正确跨过 Single 幕布");
            Scene raid = SceneManager.Load(3, "LastShiftYardRaid");
            lighting.Apply(new GameObject("World", raid));
            Check(clone.gameObject.scene == raid && !clone.isActiveAndEnabled, "出击图未激活时主光已经启用");
            Check(SceneManager.GetActiveScene() == curtain && RenderSettings.sun == null, "主光误绑到幕布");
            Check(!RaidOfficialLighting.TryRead(raid, out _, out _, out _), "从未激活场景采样到光照");

            SceneManager.Activate(raid);
            Check(RenderSettings.sun == clone && clone.isActiveAndEnabled, "激活当帧没有绑定官方克隆");
            Check(RenderSettings.ambientMode == AmbientMode.Trilight, "官方三色环境光没有生效");
            Check(SceneManager.ActiveSceneSubscribers == 0, "绑定后的监听未释放");
            cloneShadows.profile.contactStrength = 0.11f;
            Check(sourceShadows.profile.contactStrength == 0.42f, "出击图天气改写了官方阴影资源");

            lighting.Dispose();
            Check(clone == null && cloneShadows.profile == null, "卸载前的清理没有释放克隆和阴影资源");
            Check(RenderSettings.sun == null && RenderSettings.ambientMode == AmbientMode.Custom, "出击图原设置未恢复");
        }
    }

    private static void ReadOfficialChanges()
    {
        Scene raid = SceneManager.Load(3, "LastShiftYardRaid");
        SceneManager.Activate(raid);
        Light sun = AddSun(raid);
        RenderSettings.sun = sun;
        sun.transform.forward = new Vector3(0, -1, 0);
        sun.color = new Color(0.7f, 0.3f, 0.4f);
        sun.intensity = 2.5f;
        RenderSettings.ambientEquatorColor = new Color(0.1f, 0.2f, 0.3f);

        Vector3 direction;
        Color sunlight, ambient;
        Check(RaidOfficialLighting.TryRead(raid, out direction, out sunlight, out ambient), "无法读取出击图当前主光");
        Check(direction.y == 1 && Close(sunlight.r, sun.color.linear.r * sun.intensity) &&
            Close(sunlight.g, sun.color.linear.g * sun.intensity), "主光方向、线性色或强度错误");
        Check(ambient.b == 0.3f, "未采样官方赤道环境光");

        // 模拟官方 LightControl 对同一主光和 RenderSettings 的下一次天气混合结果。
        sun.transform.forward = new Vector3(-1, 0, 0);
        sun.intensity = 0.6f;
        RenderSettings.ambientEquatorColor = new Color(0.2f, 0.3f, 0.4f);
        Check(RaidOfficialLighting.TryRead(raid, out direction, out sunlight, out ambient) && direction.x == 1 &&
            Close(sunlight.r, sun.color.linear.r * 0.6f) && ambient.b == 0.4f, "采样保留了旧天气数据");
        QualitySettings.activeColorSpace = ColorSpace.Gamma;
        Check(RaidOfficialLighting.TryRead(raid, out _, out sunlight, out _) &&
            Close(sunlight.r, sun.color.r * 0.6f), "Gamma 配置被额外转换");
        sun.enabled = false;
        Check(!RaidOfficialLighting.TryRead(raid, out _, out _, out _), "采样了停用的主光");
        sun.enabled = true;
        RenderSettings.sun = AddSun(SceneManager.Load(5, "Foreign"));
        Check(!RaidOfficialLighting.TryRead(raid, out _, out _, out _), "采样了其他场景的方向光");
    }

    private static void UseExistingRaidSun()
    {
        Scene source = SceneManager.Load(1, "Level_HiddenWarehouse");
        Light official = AddSun(source);
        SceneManager.Activate(source);
        RenderSettings.sun = official;
        // 配置里指明主光时，场景里的另一个同名方向光不造成歧义。
        Light otherSource = AddSun(source);
        using (var lighting = new RaidOfficialLighting())
        {
            lighting.Prepare(source);
            Scene raid = SceneManager.Load(3, "Raid");
            Light existing = AddSun(raid);
            SceneManager.Activate(raid);
            RenderSettings.sun = existing;
            lighting.Apply(new GameObject("World", raid));
            Check(RenderSettings.sun == existing && RenderSettings.ambientMode == AmbientMode.Custom,
                "本图已有的官方主光或环境模式被替换");
            Check(!FindClone(official, existing, otherSource).isActiveAndEnabled, "在已有主光时启用了第二个方向光");
        }
    }

    private static void RejectUnsafeSources()
    {
        Scene scene = SceneManager.Load(1, "Level_HiddenWarehouse");
        SceneManager.Activate(scene);
        Light bad = AddSun(scene);
        bad.gameObject.AddComponent<UnrelatedBehaviour>();
        RenderSettings.sun = bad;
        using (var lighting = new RaidOfficialLighting())
        {
            MustFail(() => lighting.Prepare(scene));
            Check(GameObject.All.Count(go => go != null && go.scene.handle == 0) == 0, "拒绝来源后残留暂存");
            AddSun(scene);
            AddSun(scene);
            MustFail(() => lighting.Prepare(scene));
            Check(RenderSettings.sun == bad, "拒绝来源后修改了官方主光");
        }
    }

    private static void DisposeBeforeActivation()
    {
        Scene source = SceneManager.Load(1, "Level_HiddenWarehouse");
        SceneManager.Activate(source);
        Light official = AddSun(source);
        var lighting = new RaidOfficialLighting();
        lighting.Prepare(source);
        Light clone = FindClone(official);
        lighting.Dispose();
        Check(clone == null && official != null, "转场取消后暂存仍在");

        lighting.Prepare(source);
        clone = FindClone(official);
        Scene raid = SceneManager.Load(3, "Raid");
        var root = new GameObject("World", raid);
        SceneManager.FailNextMove = true;
        MustFail(() => lighting.Apply(root));
        Check(clone == null && SceneManager.ActiveSceneSubscribers == 0, "挂载失败后暂存或订阅仍在");

        lighting.Prepare(source);
        clone = FindClone(official);
        lighting.Apply(root);
        Check(SceneManager.ActiveSceneSubscribers == 1, "未等待出击图激活");
        lighting.Dispose();
        SceneManager.Activate(raid);
        Check(clone == null && RenderSettings.sun == null, "清理后激活事件错误绑定");
    }

    private static void DisposeAfterReplacement()
    {
        Scene source = SceneManager.Load(1, "Level_HiddenWarehouse");
        SceneManager.Activate(source);
        AddSun(source);
        Scene raid = SceneManager.Load(3, "Raid");
        SceneManager.Activate(raid);
        using (var lighting = new RaidOfficialLighting())
        {
            lighting.Prepare(source);
            lighting.Apply(new GameObject("World", raid));
            Light other = AddSun(raid);
            RenderSettings.sun = other;
            RenderSettings.ambientMode = AmbientMode.Flat;
            lighting.Dispose();
            Check(RenderSettings.sun == other && RenderSettings.ambientMode == AmbientMode.Flat, "清理覆盖了后来接管的主光");
        }

        Scene baseScene = SceneManager.Load(4, "Base");
        Light baseSun = AddSun(baseScene);
        using (var lighting = new RaidOfficialLighting())
        {
            RenderSettings.sun = null;
            lighting.Prepare(source);
            lighting.Apply(new GameObject("World", raid));
            SceneManager.Activate(baseScene);
            RenderSettings.sun = baseSun;
            RenderSettings.ambientMode = AmbientMode.Flat;
            SceneManager.Unload(raid);
            lighting.Dispose();
            Check(RenderSettings.sun == baseSun && RenderSettings.ambientMode == AmbientMode.Flat, "离图清理改了基地设置");
        }
    }

    private static void BaseOutdoorTemplate()
    {
        Scene outdoors = SceneManager.Load(5, "Base_SceneV2");
        Light original = AddSun(outdoors);
        original.bakingOutput = new LightBakingOutput
        {
            isBaked = true,
            lightmapBakeType = LightmapBakeType.Mixed,
            probeOcclusionLightIndex = 1,
            occlusionMaskChannel = 0
        };
        using (var template = new RaidOfficialLighting())
        using (var lighting = new RaidOfficialLighting())
        {
            template.Prepare(outdoors);
            Light cached = FindClone(original);
            Check(original.bakingOutput.isBaked && !cached.bakingOutput.isBaked &&
                cached.bakingOutput.lightmapBakeType == LightmapBakeType.Realtime &&
                cached.bakingOutput.probeOcclusionLightIndex == -1 && cached.bakingOutput.occlusionMaskChannel == -1,
                "基地烘焙状态未隔离");
            SceneManager.LoadSingle(6, "Base_SceneV2_Sub_01");
            Check(original == null && cached != null && !cached.isActiveAndEnabled, "下水道切图后模板不可用");
            lighting.PrepareFromTemplate(template);
            Light copy = FindClone(cached);
            template.Dispose();
            Check(cached == null && copy != null && copy.GetComponent<UmbraSoftShadows>().profile != null, "清理模板时销毁了本趟副本");
            SceneManager.LoadSingle(7, "DefaultCurtain");
            Scene raid = SceneManager.Load(3, "TidegateRaid");
            lighting.Apply(new GameObject("World", raid));
            SceneManager.Activate(raid);
            Check(RenderSettings.sun == copy && copy.isActiveAndEnabled, "竹筏出发后的场景未使用本趟副本");
        }
    }

    private static void NativeSun()
    {
        var officialProfile = new UmbraProfile { name = "SodaUmbraShadowProfile", contactStrength = 0.6f };
        Resources.Assets = new Object[] { new UmbraProfile { name = "Unrelated" }, officialProfile };
        SceneManager.LoadSingle(6, "Base_SceneV2_Sub_01");
        using (var lighting = new RaidOfficialLighting())
        {
            lighting.PrepareNative();
            Light copy = FindClone();
            var shadows = copy.GetComponent<UmbraSoftShadows>();
            Check(copy.type == LightType.Directional && copy.shadows == LightShadows.Soft && !copy.isActiveAndEnabled &&
                copy.GetComponent<UniversalAdditionalLightData>() != null, "官方运行时灯光组件未准备");
            Check(shadows.profile != officialProfile && shadows.profile.contactStrength == 0.6f, "未隔离官方 Umbra 资源");
            SceneManager.LoadSingle(7, "DefaultCurtain");
            Scene raid = SceneManager.Load(3, "TidegateRaid");
            lighting.Apply(new GameObject("World", raid));
            SceneManager.Activate(raid);
            Check(RenderSettings.sun == copy && copy.isActiveAndEnabled, "补建的官方主光组件未绑定");
        }
        Check(officialProfile != null, "会话清理销毁了官方资源");

        Resources.Assets = Array.Empty<Object>();
        using (var lighting = new RaidOfficialLighting())
        {
            lighting.PrepareNative();
            Check(FindClone().GetComponent<UmbraSoftShadows>().profile != null, "没有载入官方 Umbra 资源时未使用官方默认配置");
        }
    }

    private static void OutdoorWeather()
    {
        var indoor = new VolumeProfile { name = "BaseVolume" };
        var day = new VolumeProfile { name = "Volume_Level_GroundZero_Morning" };
        var dawn = new VolumeProfile { name = "Volume_Level_GroundZero_AfterNoon" };
        var night = new VolumeProfile { name = "Volume_Level_GroundZero_ Night" };
        var rainDay = new VolumeProfile { name = "Volume_Level_GroundZero_Rainy" };
        var rainDawn = new VolumeProfile { name = "Volume_Level_GroundZero_Rainy Dawn" };
        var rainNight = new VolumeProfile { name = "Volume_Level_GroundZero_Rainy Night" };
        var rain = new TimeOfDayEntry(rainDay, rainDawn, rainNight);
        var original = new TimeOfDayEntry(indoor, indoor, indoor);
        var clone = new TimeOfDayConfig();
        clone.Entries.Add(Weather.Rainy, rain);
        clone.Entries.Add(Weather.Sunny, new TimeOfDayEntry(indoor, indoor, indoor));
        Resources.Assets = new Object[] { day, dawn, night };
        RaidOfficialLighting.ApplyOutdoorWeather(clone);
        TimeOfDayEntry sunny = clone.GetCurrentEntry(Weather.Sunny);
        Check(sunny.GetPhase(TimePhaseTags.day).volumeProfile == day && sunny.GetPhase(TimePhaseTags.dawn).volumeProfile == dawn &&
            sunny.GetPhase(TimePhaseTags.night).volumeProfile == night && Debug.Warnings.Count == 0, "晴天三时段未匹配官方户外资源");

        Resources.Assets = new Object[] { day };
        sunny = new TimeOfDayEntry(indoor, indoor, indoor);
        clone.Entries[Weather.Sunny] = sunny;
        RaidOfficialLighting.ApplyOutdoorWeather(clone);
        Check(sunny.GetPhase(TimePhaseTags.day).volumeProfile == day && sunny.GetPhase(TimePhaseTags.dawn).volumeProfile == rainDawn &&
            sunny.GetPhase(TimePhaseTags.night).volumeProfile == rainNight && Debug.Warnings.Count == 2, "缺少官方晴天资源时未按时段回退");
        Check(original.GetPhase(TimePhaseTags.night).volumeProfile == indoor && rain.GetPhase(TimePhaseTags.night).volumeProfile == rainNight,
            "替换晴天资源修改了基地或雨天配置");
    }

    private static void DescribeLiveLighting()
    {
        Scene raid = SceneManager.Load(3, "LastShiftYardRaid");
        Check(RaidOfficialLighting.Describe(raid) == "lighting=inactive_scene", "未激活场景诊断读取了其他图");
        SceneManager.Activate(raid);
        Check(RaidOfficialLighting.Describe(raid).Contains("weather_controller=missing sun=missing"), "缺少控制器或太阳时无诊断");

        TimeOfDayController.Instance = new TimeOfDayController
        {
            Time = 15.25f,
            CurrentWeather = Weather.Cloudy,
            CurrentPhase = new TimeOfDayPhase
            {
                timePhaseTag = TimePhaseTags.day,
                volumeProfile = new VolumeProfile { name = "Volume_Level_GroundZero_Rainy" }
            }
        };
        var light = new LightControl { Active = true };
        light.sunIntensity.value = 0.9f;
        VolumeManager.instance.stack = new VolumeStack();
        VolumeManager.instance.stack.Components.Add(typeof(LightControl), light);
        VolumeManager.instance.stack.Components.Add(typeof(SunFogTD), new SunFogTD { Active = true });
        RenderSettings.sun = AddSun(raid);
        RenderSettings.sun.intensity = 0.8f;
        RenderSettings.ambientEquatorColor = new Color(0.6f, 0.7f, 0.8f);
        Shader.Floats.Add("SunFogNearDistance", 41);
        Shader.Floats.Add("SunFogFarDistance", 71);
        Shader.Colors.Add("SunFogColor", new Color(0.1f, 0.2f, 0.3f));
        GameCamera.Instance = new GameCamera { renderCamera = new GameObject("Main Camera", raid).AddComponent<Camera>() };
        var overlay = new GameObject("SunFog", raid);
        overlay.transform.SetParent(GameCamera.Instance.renderCamera.transform, false);
        overlay.AddComponent<SunFogEntry>();
        overlay.AddComponent<Renderer>();
        LightControl.SetTarget(RenderSettings.sun);

        string sample = RaidOfficialLighting.Describe(raid);
        Check(sample.Contains("hour=15.25 weather=Cloudy phase=day target_volume=Volume_Level_GroundZero_Rainy"),
            "未读官方天气目标");
        Check(sample.Contains("sun_intensity=0.8") && sample.Contains("stack_sun_intensity=0.9") &&
            sample.Contains("ambient_equator=(0.6,0.7,0.8)"), "混合目标与实际渲染值被混用");
        Check(sample.Contains("unity_fog=False stack_fog=True fog_overlay=on fog_near=41 fog_far=71 fog_rgb=(0.1,0.2,0.3)"),
            "未读取实际着色器雾量");
        Check(sample.Contains("light_control_target=scene_sun"), "未诊断官方日光目标");
        Check(RenderSettings.sun.intensity == 0.8f && light.sunIntensity.value == 0.9f &&
            Shader.Floats["SunFogNearDistance"] == 41, "诊断改动了渲染状态");

        LightControl.SetTarget(AddSun(SceneManager.Load(5, "Other")));
        overlay.SetActive(false);
        sample = RaidOfficialLighting.Describe(raid);
        Check(sample.Contains("light_control_target=other@Other") &&
            sample.Contains("stack_fog=True fog_overlay=off"), "错绑主光或停用的雾幕未被区分");
    }

    private static Light AddSun(Scene scene)
    {
        var go = new GameObject("Directional Light", scene);
        var light = go.AddComponent<Light>();
        light.type = LightType.Directional;
        go.AddComponent<UniversalAdditionalLightData>().settings = 197;
        go.AddComponent<UmbraSoftShadows>().profile = new UmbraProfile();
        return light;
    }

    private static Light FindClone(params Light[] sources)
    {
        return GameObject.All.Where(go => go != null).Select(go => go.GetComponent<Light>())
            .Single(light => light != null && !sources.Contains(light));
    }

    private static bool Close(float left, float right)
    {
        return Math.Abs(left - right) < 0.0001f;
    }

    private static void MustFail(Action run)
    {
        try { run(); }
        catch (InvalidOperationException) { return; }
        throw new Exception("预期拒绝的操作返回成功");
    }

    private static void Check(bool passed, string message)
    {
        if (!passed) throw new Exception(message);
    }

    private sealed class UnrelatedBehaviour : Component { }
}
