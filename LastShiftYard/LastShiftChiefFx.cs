using System;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// 押运长招式的表现层（根 AGENTS.md §4.15：表现写独立类型；判定、伤害与时序全在 <see cref="LastShiftEscortChief"/>，这里一行不碰）。
    /// 2026-11-12 owner「特效有点塑料」之后的重做，替掉两根纯色 LineRenderer：
    /// - 封线：地上铺一条站场「封闭线路」的黄黑斜纹带（程序化贴图，Tile 平铺、沿锁定方向慢慢流动），从押运长脚下画出去、在第一处墙层 / 车厢处截断；
    ///   底下垫一层加色软边光，蓄力末段由琥珀转橙红。开火时是一串落在带子里的弹着点（共享扬尘 / 火花发射器）与几道一闪即灭的曳光，不是一根粗线。
    /// - 信号旗：一根旗杆 + 一面会飘的红旗（六点布条，程序化布褶贴图，随时间起伏）；半血后换成手提红灯（点光 + 亮芯，呼吸）。
    /// - 响墩：飞行时一颗拖着火星的红点，落地后贴地一圈红光随引信加快闪烁；预警圈与爆炸复用天空岛头目的地面圈与官方爆炸（在押运长那边调）。
    /// 全部懒建、复用，不按帧建对象；只在押运长出招或亮灯时由调用方逐帧驱动，其余时间什么都不跑（§4.12）。材质走共享工厂 BossRushFxMaterials，
    /// 两张程序化贴图在 <see cref="ResetStaticCaches"/>（LastShiftRuntimeModule.OnDestroy）销毁。
    /// </summary>
    internal sealed class LastShiftChiefFx : IDisposable
    {
        internal static readonly Color Amber = new Color(1f, .72f, .24f, 1f);
        internal static readonly Color Signal = new Color(.95f, .2f, .1f, 1f);
        private static readonly Color Hot = new Color(1f, .42f, .16f, 1f);
        private static readonly Color TracerTint = new Color(1f, .88f, .6f, 1f);
        private static readonly Color PoleTint = new Color(.2f, .17f, .14f, .95f);
        private static readonly Color ClothNear = new Color(.86f, .12f, .08f, 1f), ClothFar = new Color(.62f, .07f, .05f, 1f);
        internal const int LaneCount = 2, CapCount = 3;
        private const int TracerCount = 4, FlagPoints = 6, EmberCap = 160;
        private const float Lift = .16f, TracerLife = .07f;

        private static Texture2D stripeTexture, clothTexture;
        private static readonly int BaseMapSt = Shader.PropertyToID("_BaseMap_ST"), MainTexSt = Shader.PropertyToID("_MainTex_ST");

        private readonly Transform root, boss, model;
        private GameObject holder;
        private LineRenderer pole, cloth;
        private readonly LineRenderer[] stripes = new LineRenderer[LaneCount], glows = new LineRenderer[LaneCount];
        private readonly LineRenderer[] tracers = new LineRenderer[TracerCount];
        private readonly float[] tracerUntil = new float[TracerCount];
        private readonly float[] scroll = new float[LaneCount], scrollAt = new float[LaneCount];
        private readonly Transform[] caps = new Transform[CapCount];
        private readonly SpriteRenderer[] capGlow = new SpriteRenderer[CapCount];
        private readonly Vector3[] flag = new Vector3[FlagPoints];
        private readonly Vector3[] two = new Vector3[2];
        private ParticleSystem embers;
        private Light lamp;
        private SpriteRenderer lampGlow;
        private MaterialPropertyBlock block;
        private int nextTracer;
        private bool built, broken, flagUp, lampOn, disposed;

        /// <param name="chiefModel">押运长的模型节点（官方转身转的是它，不是角色根）：旗朝它的右手边飘，红灯挂在它左手上。</param>
        internal LastShiftChiefFx(Transform worldRoot, Transform chief, Transform chiefModel) { root = worldRoot; boss = chief; model = chiefModel != null ? chiefModel : chief; }

        /// <summary>材质与发射器都建得起来（首次调用时懒建）。封线没有这层就没有预警，押运长据此不出这一招。</summary>
        internal bool Available { get { return Ready(); } }

        private bool Ready()
        {
            if (disposed || broken) return false;
            // 建过一次就不重建：地图根被整棵销毁（切图）时 holder 跟着没了，这时押运长自己也在销毁路上。
            if (built) return holder != null;
            try { Build(); built = true; return true; }
            catch (Exception e)
            {
                broken = true;
                if (holder != null) UnityEngine.Object.Destroy(holder);
                holder = null;
                // 表现缺了招式照打：判定与伤害都不经过这里。
                Debug.LogWarning(LastShiftRaidScene.LogTag + " 押运长特效不可用（招式照打）：" + e.Message);
                return false;
            }
        }

        private void Build()
        {
            Material band = SkyIslandImpactFx.SoftLineMaterial(false), glow = SkyIslandImpactFx.SoftLineMaterial(true);
            Material stripe = BossRushFxMaterials.Get(BossRushFxBlend.Alpha, StripeTexture());
            Material clothMaterial = BossRushFxMaterials.Get(BossRushFxBlend.Alpha, ClothTexture());
            Sprite dot = BossRushFxKit.GetShapeSprite(BossRushParticleShape.GlowDot);
            Material dotMaterial = BossRushFxKit.GetSpriteMaterial(dot, BossRushFxBlend.Additive, BossRushFxKit.GainHot);
            Material ember = BossRushFxKit.GetShapeMaterial(BossRushParticleShape.GlowDot, BossRushFxBlend.Additive, BossRushFxKit.GainHot);
            if (band == null || glow == null || stripe == null || clothMaterial == null || dot == null || dotMaterial == null || ember == null)
                throw new InvalidOperationException("共享特效材质不可用");
            holder = new GameObject("LastShiftChiefFx");
            if (root != null) holder.transform.SetParent(root, false);
            block = new MaterialPropertyBlock();
            pole = Line("FlagPole", band, false);
            pole.widthMultiplier = .055f;
            pole.startColor = pole.endColor = PoleTint;
            cloth = Line("FlagCloth", clothMaterial, false);
            cloth.positionCount = FlagPoints;
            cloth.widthMultiplier = .42f;
            Gradient fade = new Gradient();
            fade.SetKeys(new[] { new GradientColorKey(ClothNear, 0f), new GradientColorKey(ClothFar, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(.92f, 1f) });
            cloth.colorGradient = fade;
            for (int i = 0; i < LaneCount; i++)
            {
                glows[i] = Line("LaneGlow" + i, glow, true);
                stripes[i] = Line("LaneStripes" + i, stripe, true);
                stripes[i].textureMode = LineTextureMode.Tile;
                stripes[i].sortingOrder = SkyIslandGroundRing.SortingOrder - 2;
                glows[i].sortingOrder = SkyIslandGroundRing.SortingOrder - 3;
            }
            for (int i = 0; i < TracerCount; i++)
            {
                tracers[i] = Line("Tracer" + i, glow, false);
                SkyIslandImpactFx.Taper(tracers[i], .09f, .25f);
                tracers[i].startColor = tracers[i].endColor = TracerTint;
            }
            for (int i = 0; i < CapCount; i++)
            {
                var go = new GameObject("Torpedo" + i);
                go.transform.SetParent(holder.transform, false);
                SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = dot;
                sr.sharedMaterial = dotMaterial;
                sr.color = Signal;
                sr.sortingOrder = SkyIslandGroundRing.SortingOrder + 1;
                capGlow[i] = sr;
                caps[i] = go.transform;
                go.SetActive(false);
            }
            embers = BossRushFxKit.CreateEmitter("Embers", holder.transform, Vector3.zero, ember, EmberCap, true);
            if (embers == null) throw new InvalidOperationException("火星发射器不可用");
            ParticleSystem.MainModule main = embers.main;
            main.gravityModifier = .6f;
            ParticleSystem.ColorOverLifetimeModule col = embers.colorOverLifetime;
            col.enabled = true;
            col.color = BossRushFxKit.FadeGradient(Color.white, Color.white, new Color(1f, 1f, 1f, 0f), 0f);
            ParticleSystem.SizeOverLifetimeModule size = embers.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, .3f));
            embers.Play();
            // 红灯挂在模型上跟着走（不按帧挪）；不在 holder 下，Dispose 单独收。
            var lampObject = new GameObject("LastShiftChiefSignalLamp");
            lampObject.transform.SetParent(model, false);
            lampObject.transform.localPosition = new Vector3(-.38f, 1.05f, .18f);
            lamp = lampObject.AddComponent<Light>();
            lamp.type = LightType.Point;
            lamp.color = Signal;
            lamp.range = 5f;
            lamp.intensity = 0f;
            lamp.shadows = LightShadows.None;
            lamp.enabled = false;
            lampGlow = new GameObject("SignalLampGlow").AddComponent<SpriteRenderer>();
            lampGlow.transform.SetParent(lampObject.transform, false);
            lampGlow.sprite = dot;
            lampGlow.sharedMaterial = dotMaterial;
            lampGlow.color = Signal;
            lampGlow.transform.localScale = Vector3.one * .45f;
            lampGlow.enabled = false;
        }

        private LineRenderer Line(string name, Material material, bool flat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(holder.transform, false);
            LineRenderer line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.positionCount = 2;
            line.numCapVertices = 0;
            line.numCornerVertices = 0;
            line.textureMode = LineTextureMode.Stretch;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.sharedMaterial = material;
            if (flat)
            {
                // 带子躺在地上：TransformZ 朝下，宽度铺在水平面里。
                line.alignment = LineAlignment.TransformZ;
                go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            }
            else line.alignment = LineAlignment.View;
            line.enabled = false;
            return line;
        }

        // ---------------- 信号旗 / 红灯 ----------------
        /// <summary>旗举着时逐帧调：旗杆在右手边，红旗朝押运长右侧飘，布条六个点按时间起伏。</summary>
        internal void Flag(bool up, float time)
        {
            if (!up) { if (flagUp && built) { pole.enabled = false; cloth.enabled = false; } flagUp = false; return; }
            if (!Ready() || boss == null) return;
            flagUp = true;
            Vector3 forward = model.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < .01f) forward = Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, forward.normalized), feet = boss.position;
            Vector3 hand = feet + right * .34f + Vector3.up * 1.2f, top = hand + Vector3.up * 1.55f;
            two[0] = hand; two[1] = top;
            pole.SetPositions(two);
            for (int i = 0; i < FlagPoints; i++)
            {
                float u = i / (float)(FlagPoints - 1);
                float wave = Mathf.Sin(time * 9f - u * 4.2f) * .09f * u + Mathf.Sin(time * 15f - u * 7f) * .025f * u;
                flag[i] = top + Vector3.down * .24f + right * (.04f + .78f * u * (1f - .06f * Mathf.Abs(wave) * 8f)) + Vector3.up * wave;
            }
            cloth.SetPositions(flag);
            pole.enabled = cloth.enabled = true;
        }

        /// <summary>半血后的手提红灯：挂在押运长左手，点光呼吸、亮芯朝镜头。只在玩家附近时由调用方开着。</summary>
        internal void Lamp(bool on, float time)
        {
            if (!on) { if (lampOn && built) { lamp.enabled = false; lampGlow.enabled = false; } lampOn = false; return; }
            if (!Ready() || boss == null) return;
            lampOn = true;
            float breathe = .8f + .2f * Mathf.Sin(time * 3.1f);
            lamp.intensity = 1.6f * breathe;
            lamp.enabled = true;
            lampGlow.transform.localScale = Vector3.one * (.38f + .1f * breathe);
            Camera cam = GameCamera.Instance != null ? GameCamera.Instance.renderCamera : null;
            if (cam != null) lampGlow.transform.rotation = cam.transform.rotation;
            lampGlow.enabled = true;
        }

        // ---------------- 封线 ----------------
        /// <summary>
        /// 一条封线带：<paramref name="start"/> 到 <paramref name="end"/>（贴地），半宽 <paramref name="half"/>。
        /// <paramref name="charge"/> 是蓄力 0..1（亮度、颜色往橙红走），<paramref name="firing"/> 时是开火中的热色，<paramref name="alpha"/> 是整体淡入淡出。
        /// </summary>
        internal void Lane(int index, Vector3 start, Vector3 end, float half, float charge, bool firing, float alpha, float time)
        {
            if (index < 0 || index >= LaneCount || !Ready()) return;
            LineRenderer s = stripes[index], g = glows[index];
            Vector3 a = start + Vector3.up * Lift, b = end + Vector3.up * Lift;
            if ((b - a).sqrMagnitude < .01f || alpha <= .01f) { s.enabled = g.enabled = false; return; }
            two[0] = a; two[1] = b;
            s.SetPositions(two);
            g.SetPositions(two);
            s.widthMultiplier = half * 2f;
            g.widthMultiplier = half * 2.5f;
            Color tone = firing ? Hot : Color.Lerp(Amber, Hot, charge * charge);
            // 蓄力越满闪得越快：相位按蓄力累积（约 1 Hz 涨到 4 Hz），不按「时间 × 变化的频率」算（那样相位会乱跳）。
            float pulse = firing ? 1f : .78f + .22f * Mathf.Sin(Mathf.PI * 2f * charge * (1.5f + 2.5f * charge));
            Color stripe = new Color(tone.r, tone.g, tone.b, Mathf.Lerp(.32f, .72f, firing ? 1f : charge) * alpha);
            Color glowColor = new Color(tone.r, tone.g, tone.b, (firing ? .4f : .12f + .22f * charge) * pulse * alpha);
            s.startColor = s.endColor = stripe;
            g.startColor = g.endColor = glowColor;
            // 斜纹沿锁定方向往外流（开火时更快）：读得出这条带子朝哪边打。
            float dt = Mathf.Clamp(time - scrollAt[index], 0f, .1f);
            scrollAt[index] = time;
            scroll[index] = Mathf.Repeat(scroll[index] + dt * (firing ? 2.4f : .8f), 1f);
            block.Clear();
            Vector4 st = new Vector4(1f, 1f, -scroll[index], 0f);
            block.SetVector(BaseMapSt, st);
            block.SetVector(MainTexSt, st);
            s.SetPropertyBlock(block);
            s.enabled = g.enabled = true;
        }

        internal void HideLanes()
        {
            if (!built) return;
            for (int i = 0; i < LaneCount; i++) { if (stripes[i] != null) stripes[i].enabled = false; if (glows[i] != null) glows[i].enabled = false; }
        }

        /// <summary>一道曳光：从枪口到弹着点，<see cref="TracerLife"/> 后熄。轮流复用四根。</summary>
        internal void Tracer(Vector3 from, Vector3 to, float now)
        {
            if (!Ready()) return;
            LineRenderer line = tracers[nextTracer];
            tracerUntil[nextTracer] = now + TracerLife;
            nextTracer = (nextTracer + 1) % TracerCount;
            two[0] = from; two[1] = to;
            line.SetPositions(two);
            line.enabled = true;
        }

        /// <summary>枪口一闪：几颗加色亮点。</summary>
        internal void Muzzle(Vector3 at, Vector3 direction)
        {
            if (!Ready()) return;
            var p = new ParticleSystem.EmitParams();
            for (int i = 0; i < 3; i++)
            {
                p.position = at;
                p.velocity = (direction + UnityEngine.Random.insideUnitSphere * .35f) * UnityEngine.Random.Range(2.5f, 5f);
                p.startSize = UnityEngine.Random.Range(.08f, .16f);
                p.startLifetime = UnityEngine.Random.Range(.06f, .12f);
                p.startColor = TracerTint;
                embers.Emit(p, 1);
            }
        }

        // ---------------- 响墩 ----------------
        /// <summary>
        /// 一颗响墩：<paramref name="landed"/> 前是飞行中的红点（拖火星），之后贴地一圈红光，按 <paramref name="fuse"/>（0..1）越闪越快。
        /// </summary>
        internal void Cap(int index, Vector3 at, bool landed, float fuse, float time)
        {
            if (index < 0 || index >= CapCount || !Ready()) return;
            Transform t = caps[index];
            if (!t.gameObject.activeSelf) t.gameObject.SetActive(true);
            SpriteRenderer sr = capGlow[index];
            if (!landed)
            {
                t.position = at;
                Camera cam = GameCamera.Instance != null ? GameCamera.Instance.renderCamera : null;
                if (cam != null) t.rotation = cam.transform.rotation;
                t.localScale = Vector3.one * .32f;
                sr.color = Signal;
                Trail(at);
                return;
            }
            t.position = at + Vector3.up * (Lift + .02f);
            t.rotation = Quaternion.Euler(90f, 0f, 0f);
            // 引信越短闪得越快（约 3 Hz 涨到 12 Hz）：闪的圈数按引信进度累积，不按「时间 × 变化的频率」算。
            float cycles = fuse * (3.6f + 5.4f * fuse);
            float blink = .5f + .5f * Mathf.Sin(Mathf.PI * 2f * cycles);
            t.localScale = Vector3.one * Mathf.Lerp(.55f, .95f, blink);
            sr.color = new Color(Signal.r, Signal.g * (.7f + .6f * blink), Signal.b, .55f + .45f * blink);
            if (UnityEngine.Random.value < .35f + .5f * fuse) Spit(at);
        }

        internal void HideCap(int index)
        {
            if (index < 0 || index >= CapCount || !built || caps[index] == null) return;
            caps[index].gameObject.SetActive(false);
        }

        private void Trail(Vector3 at)
        {
            var p = new ParticleSystem.EmitParams();
            p.position = at + UnityEngine.Random.insideUnitSphere * .05f;
            p.velocity = UnityEngine.Random.insideUnitSphere * .4f;
            p.startSize = UnityEngine.Random.Range(.05f, .1f);
            p.startLifetime = UnityEngine.Random.Range(.25f, .4f);
            p.startColor = Hot;
            embers.Emit(p, 1);
        }

        private void Spit(Vector3 at)
        {
            var p = new ParticleSystem.EmitParams();
            p.position = at + Vector3.up * .08f;
            p.velocity = new Vector3(UnityEngine.Random.Range(-.6f, .6f), UnityEngine.Random.Range(1.2f, 2.4f), UnityEngine.Random.Range(-.6f, .6f));
            p.startSize = UnityEngine.Random.Range(.04f, .08f);
            p.startLifetime = UnityEngine.Random.Range(.2f, .35f);
            p.startColor = Amber;
            embers.Emit(p, 1);
        }

        /// <summary>出招中逐帧调：到点的曳光熄掉。</summary>
        internal void Tick(float now)
        {
            if (!built) return;
            for (int i = 0; i < TracerCount; i++)
                if (tracers[i] != null && tracers[i].enabled && now >= tracerUntil[i]) tracers[i].enabled = false;
        }

        /// <summary>收招：旗、封线、曳光、响墩全收（红灯另由调用方管）。</summary>
        internal void Clear()
        {
            Flag(false, 0f);
            HideLanes();
            if (!built) return;
            for (int i = 0; i < TracerCount; i++) if (tracers[i] != null) tracers[i].enabled = false;
            for (int i = 0; i < CapCount; i++) HideCap(i);
        }

        public void Dispose()
        {
            if (disposed) return;
            // 旗、带子、响墩与红灯当场收起；火星已经飞出去的照常落完再销毁，不在同一帧整团消失。
            Clear();
            Lamp(false, 0f);
            disposed = true;
            if (lamp != null) UnityEngine.Object.Destroy(lamp.gameObject);
            lamp = null;
            lampGlow = null;
            if (holder != null) BossRushFxKit.Release(holder, .2f, 1f, false);
            holder = null;
        }

        // ---------------- 程序化贴图 ----------------
        /// <summary>
        /// 封闭线路斜纹：一格 1 m（LineRenderer Tile 按世界长度每米一格），一黄一黑斜着走；横向两侧 12% 羽化，像漆在地上的封线，不是一根平色光柱。
        /// 黑纹不是纯不透明：压在地面上只把地压暗，不盖死。
        /// </summary>
        private static Texture2D StripeTexture()
        {
            if (stripeTexture != null) return stripeTexture;
            const int w = 64, h = 32;
            var texture = new Texture2D(w, h, TextureFormat.RGBA32, false, false);
            texture.name = "LastShiftChief_Stripes";
            texture.wrapModeU = TextureWrapMode.Repeat;
            texture.wrapModeV = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            texture.hideFlags = HideFlags.HideAndDontSave;
            var pixels = new Color32[w * h];
            for (int y = 0; y < h; y++)
            {
                float v = (y + .5f) / h;
                float edge = Mathf.Min(v, 1f - v) / .12f;
                float rim = BossRushUI.SmoothStep(Mathf.Clamp01(edge));
                // 两侧各一道细亮边，带子的边界一眼读出来。
                float border = (v < .14f || v > .86f) && edge > .4f ? 1f : 0f;
                for (int x = 0; x < w; x++)
                {
                    float u = (x + .5f) / w;
                    float diagonal = Mathf.Repeat(u * 2f + v * 1.1f, 1f);
                    float yellow = BossRushUI.SmoothStep(Mathf.Clamp01((.5f - Mathf.Abs(diagonal - .25f) * 2f) * 6f + .5f));
                    float light = Mathf.Max(yellow, border);
                    byte c = (byte)Mathf.RoundToInt(Mathf.Lerp(28f, 255f, light));
                    byte a = (byte)Mathf.RoundToInt(rim * Mathf.Lerp(.55f, 1f, light) * 255f);
                    pixels[y * w + x] = new Color32(c, c, c, a);
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            stripeTexture = texture;
            return stripeTexture;
        }

        /// <summary>旗布：竖向几道布褶的明暗、外缘一圈收边，颜色走顶点色（红）。</summary>
        private static Texture2D ClothTexture()
        {
            if (clothTexture != null) return clothTexture;
            const int w = 32, h = 16;
            var texture = new Texture2D(w, h, TextureFormat.RGBA32, false, false);
            texture.name = "LastShiftChief_FlagCloth";
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            texture.hideFlags = HideFlags.HideAndDontSave;
            var pixels = new Color32[w * h];
            for (int y = 0; y < h; y++)
            {
                float v = (y + .5f) / h;
                for (int x = 0; x < w; x++)
                {
                    float u = (x + .5f) / w;
                    float fold = .82f + .18f * Mathf.Sin(u * Mathf.PI * 5f + .6f);
                    float hem = Mathf.Min(Mathf.Min(u, 1f - u) * w, Mathf.Min(v, 1f - v) * h) < 1.2f ? .72f : 1f;
                    float alpha = Mathf.Clamp01(Mathf.Min(v, 1f - v) * h * .8f);
                    byte c = (byte)Mathf.RoundToInt(255f * fold * hem);
                    pixels[y * w + x] = new Color32(c, c, c, (byte)Mathf.RoundToInt(alpha * 255f));
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            clothTexture = texture;
            return clothTexture;
        }

        /// <summary>Mod 卸载路径（LastShiftRuntimeModule.OnDestroy）：销毁两张程序化贴图。材质归 BossRushFxMaterials。</summary>
        internal static void ResetStaticCaches()
        {
            if (stripeTexture != null) UnityEngine.Object.Destroy(stripeTexture);
            if (clothTexture != null) UnityEngine.Object.Destroy(clothTexture);
            stripeTexture = null;
            clothTexture = null;
        }
    }
}
