using UnityEngine;

namespace BossRush
{
    /// <summary>巢模型罩灯下的暖光。随建筑克隆/销毁，无 Update、扫描或阴影开销。</summary>
    internal static class PetNestLamp
    {
        internal static void Attach(GameObject model)
        {
            if (model == null || model.transform.Find("NestLampLight") != null) return;
            GameObject lamp = new GameObject("NestLampLight");
            lamp.transform.SetParent(model.transform, false);
            // 现有单位变换网格的罩灯位置，取自正式包几何与灯罩 UV；不依赖整体 bounds 居中。
            lamp.transform.localPosition = new Vector3(-0.25f, 1.1f, 0.15f);
            Light light = lamp.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.77f, 0.43f);
            light.intensity = 1.25f;
            light.range = 2f;
            light.shadows = LightShadows.None;
            light.renderMode = LightRenderMode.ForcePixel;
        }
    }
}
