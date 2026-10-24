using System;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// 快递的实体表现：巨蛋有厚蛋壳、嘴和翅膀；汽水有罐身、拉环、弯吸管和泡沫。
    /// 只创建有界网格和粒子；所有对象挂在本趟根下，表现不造成伤害、不改玩家属性。
    /// </summary>
    internal sealed class OddParcelVisual : IDisposable
    {
        private readonly GameObject root;
        private readonly Transform body;
        private readonly OddParcelKind kind;
        private readonly bool station;
        private Transform leftWing, rightWing, beak, orbit, stamp;
        private readonly Transform[] gauge = new Transform[10];
        private ParticleSystem spray;
        private AudioSource voice;
        private AudioClip quack;
        private Mesh eggMesh;
        private float phase, bounce, shout;
        private bool disposed;
        private readonly Color cyan = new Color(0.12f, 0.9f, 0.92f, 1f);
        private readonly Color orange = new Color(1f, 0.53f, 0.10f, 1f);
        private static readonly Color Ink = new Color(0.08f, 0.12f, 0.18f, 1f);

        internal OddParcelVisual(GameObject anchor, OddParcelKind parcelKind, bool isStation, bool destination)
        {
            try
            {
            root = new GameObject("OddParcelVisual");
            root.transform.SetParent(anchor.transform, false);
            FieldAdventureVisualAssets.Acquire(root);
            kind = parcelKind;
            station = isStation;
            body = new GameObject("AnimatedBody").transform;
            body.SetParent(root.transform, false);
            if (isStation) BuildStation(destination);
            else if (kind == OddParcelKind.QuackingEgg) BuildEgg();
            else BuildSoda();
            BuildParticles(isStation ? new Vector3(0f, 1.5f, 0f) : new Vector3(0f, 2.25f, 0f));
            if (!isStation) BuildVoice();
            ModEntityFogVisibility.Attach(root, root.transform);
            }
            catch { Dispose(); throw; }
        }

        private Transform Piece(Transform parent, string name, PrimitiveType type, Vector3 pos, Vector3 scale, Color tint)
        {
            Material mat = FieldAdventureVisualAssets.Solid;
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.layer = 0;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            Collider col = go.GetComponent<Collider>();
            if (col != null) { col.enabled = false; UnityEngine.Object.Destroy(col); }
            Renderer renderer = go.GetComponent<Renderer>();
            renderer.sharedMaterial = mat;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            renderer.receiveShadows = true;
            var block = new MaterialPropertyBlock();
            if (mat != null && mat.HasProperty("_BaseColor")) block.SetColor("_BaseColor", tint);
            if (mat != null && mat.HasProperty("_TintColor"))
                block.SetVector("_TintColor", new Vector4(tint.r * .5f, tint.g * .5f, tint.b * .5f, tint.a * .5f));
            renderer.SetPropertyBlock(block);
            return go.transform;
        }

        private void BuildEgg()
        {
            Transform shell = Piece(body, "SculptedEggShell", PrimitiveType.Sphere,
                new Vector3(0f, 1.1f, 0f), Vector3.one, new Color(1f, .93f, .66f, 1f));
            // 扁圆底、收窄顶的蛋形，圆周 24 段、纬向 16 段，避免把默认球拉长就当蛋。
            eggMesh = EggMesh();
            shell.GetComponent<MeshFilter>().sharedMesh = eggMesh;
            for (int i = 0; i < 7; i++)
            {
                float a = i * 2.39996f;
                Piece(body, "ShellSpot", PrimitiveType.Sphere,
                    new Vector3(Mathf.Cos(a) * .68f, .7f + (i % 3) * .28f, Mathf.Sin(a) * .65f),
                    new Vector3(.28f, .24f, .12f), new Color(.93f, .60f, .18f, 1f)).localRotation = Quaternion.Euler(0f, -a * Mathf.Rad2Deg, 0f);
            }
            Eye(-.27f, 1.55f, .68f); Eye(.27f, 1.55f, .68f);
            beak = Piece(body, "EnormousQuackingBeak", PrimitiveType.Cube,
                new Vector3(0f, 1.25f, .87f), new Vector3(.67f, .16f, .56f), orange);
            Piece(body, "BeakSmile", PrimitiveType.Cube, new Vector3(0f, 1.24f, 1.16f),
                new Vector3(.51f, .035f, .025f), Ink);
            leftWing = Piece(body, "FlappingWingLeft", PrimitiveType.Sphere,
                new Vector3(-.89f, 1.1f, 0f), new Vector3(.25f, .78f, .65f), new Color(1f, .83f, .34f, 1f));
            rightWing = Piece(body, "FlappingWingRight", PrimitiveType.Sphere,
                new Vector3(.89f, 1.1f, 0f), new Vector3(.25f, .78f, .65f), new Color(1f, .83f, .34f, 1f));
            Piece(body, "ShippingStrap", PrimitiveType.Cube, new Vector3(0f, .8f, -.63f),
                new Vector3(.95f, .23f, .11f), cyan);
            BuildGauge(-.82f);
        }

        private void BuildSoda()
        {
            Piece(body, "GiantSodaCan", PrimitiveType.Cylinder, new Vector3(0f, 1.12f, 0f),
                new Vector3(1.5f, 1.05f, 1.5f), cyan);
            Piece(body, "CanBottom", PrimitiveType.Cylinder, new Vector3(0f, .12f, 0f),
                new Vector3(1.58f, .09f, 1.58f), Ink);
            Piece(body, "CanLid", PrimitiveType.Cylinder, new Vector3(0f, 2.18f, 0f),
                new Vector3(1.58f, .07f, 1.58f), new Color(.8f, .91f, .94f, 1f));
            Piece(body, "PullTab", PrimitiveType.Cube, new Vector3(0f, 2.31f, .1f),
                new Vector3(.22f, .1f, .47f), Ink).localRotation = Quaternion.Euler(-22f, 0f, 0f);
            Piece(body, "StrawStem", PrimitiveType.Cylinder, new Vector3(-.35f, 2.56f, -.1f),
                new Vector3(.14f, .39f, .14f), orange);
            Piece(body, "BentStraw", PrimitiveType.Cylinder, new Vector3(-.57f, 2.88f, -.1f),
                new Vector3(.14f, .25f, .14f), orange).localRotation = Quaternion.Euler(0f, 0f, 70f);
            Eye(-.29f, 1.49f, .73f); Eye(.29f, 1.49f, .73f);
            for (int i = -1; i <= 1; i += 2)
                Piece(body, "AngryEyebrow", PrimitiveType.Cube, new Vector3(i * .29f, 1.8f, .76f),
                    new Vector3(.48f, .08f, .05f), Ink).localRotation = Quaternion.Euler(0f, 0f, i * 20f);
            beak = Piece(body, "CanMouth", PrimitiveType.Sphere, new Vector3(0f, 1.05f, .76f),
                new Vector3(.37f, .20f, .05f), Ink);
            for (int i = 0; i < 5; i++)
                Piece(body, "FoamCrown", PrimitiveType.Sphere,
                    new Vector3(Mathf.Sin(i * 1.26f) * .49f, 2.28f, Mathf.Cos(i * 1.26f) * .49f),
                    Vector3.one * (.27f + i * .025f), Color.white);
            BuildGauge(-.81f);
        }

        private void Eye(float x, float y, float z)
        {
            Piece(body, "EyeWhite", PrimitiveType.Sphere, new Vector3(x, y, z), new Vector3(.38f, .46f, .16f), Color.white);
            Piece(body, "Pupil", PrimitiveType.Sphere, new Vector3(x, y - .03f, z + .09f), new Vector3(.16f, .23f, .07f), Ink);
            Piece(body, "EyeShine", PrimitiveType.Sphere, new Vector3(x - .035f, y + .04f, z + .13f), Vector3.one * .065f, Color.white);
        }

        private void BuildGauge(float z)
        {
            Piece(body, "PressureRail", PrimitiveType.Cube, new Vector3(0f, .9f, z), new Vector3(.9f, .14f, .1f), Ink);
            for (int i = 0; i < gauge.Length; i++)
                gauge[i] = Piece(body, "PressureBulb", PrimitiveType.Cube, new Vector3(-.37f + i * .082f, .9f, z - .07f),
                    new Vector3(.065f, .1f, .06f), i < 6 ? cyan : i < 8 ? orange : new Color(1f, .17f, .28f, 1f));
        }

        private void BuildStation(bool destination)
        {
            Piece(body, "HeavyFoot", PrimitiveType.Cylinder, new Vector3(0f, .12f, 0f), new Vector3(1.55f, .13f, 1.55f), Ink);
            Piece(body, "Post", PrimitiveType.Cylinder, new Vector3(0f, .64f, 0f), new Vector3(.25f, .52f, .25f), orange);
            Piece(body, "Counter", PrimitiveType.Cube, new Vector3(0f, 1.17f, 0f), new Vector3(1.85f, .19f, 1.02f), cyan);
            if (destination)
            {
                stamp = new GameObject("GiantReceiptStamp").transform;
                stamp.SetParent(body, false);
                Piece(stamp, "StampPad", PrimitiveType.Cube, new Vector3(0f, 1.6f, 0f), new Vector3(1.1f, .18f, .72f), orange);
                Piece(stamp, "StampHandle", PrimitiveType.Cylinder, new Vector3(0f, 2.05f, 0f), new Vector3(.25f, .4f, .25f), Ink);
                Piece(stamp, "StampGrip", PrimitiveType.Sphere, new Vector3(0f, 2.42f, 0f), new Vector3(.8f, .35f, .45f), cyan);
            }
            else
            {
                Piece(body, "WingedDispatchBox", PrimitiveType.Cube, new Vector3(0f, 1.65f, 0f), new Vector3(1.3f, .84f, .85f), orange);
                Piece(body, "MailSlot", PrimitiveType.Cube, new Vector3(0f, 1.73f, .44f), new Vector3(.9f, .13f, .04f), Ink);
                leftWing = Piece(body, "DispatchWingLeft", PrimitiveType.Sphere, new Vector3(-.85f, 1.8f, 0f), new Vector3(.62f, .17f, .45f), Color.white);
                rightWing = Piece(body, "DispatchWingRight", PrimitiveType.Sphere, new Vector3(.85f, 1.8f, 0f), new Vector3(.62f, .17f, .45f), Color.white);
            }
            // 地面一圈柔光环，亮弧绕着转：远处也认得出这是能交互的快递台。
            orbit = new GameObject("RotatingDeliveryHalo").transform;
            orbit.SetParent(root.transform, false);
            orbit.localPosition = Vector3.up * .06f;
            LineRenderer halo = orbit.gameObject.AddComponent<LineRenderer>();
            halo.sharedMaterial = BossRushFxKit.GetShapeMaterial(BossRushParticleShape.TrailStrip, BossRushFxBlend.Additive, BossRushFxKit.GainBright);
            halo.useWorldSpace = false; halo.loop = true; halo.positionCount = 48; halo.widthMultiplier = .1f;
            halo.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; halo.receiveShadows = false;
            for (int i = 0; i < 48; i++)
            {
                float a = i * Mathf.PI * 2f / 48f;
                halo.SetPosition(i, new Vector3(Mathf.Cos(a) * 1.5f, 0f, Mathf.Sin(a) * 1.5f));
            }
            Color tone = destination ? cyan : orange;
            Gradient arc = new Gradient();
            arc.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(tone, .18f), new GradientColorKey(tone, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(.55f, .3f), new GradientAlphaKey(.12f, .75f), new GradientAlphaKey(.9f, 1f) });
            halo.colorGradient = arc;
        }

        private void BuildParticles(Vector3 position)
        {
            spray = BossRushFxKit.CreateEmitter("ParcelFoamAndConfetti", root.transform, position,
                BossRushFxMaterials.Get(BossRushFxBlend.Alpha), 160, true);
            if (spray == null) return;
            spray.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            var main = spray.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(.45f, 1.4f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2f, 6f);
            main.startSize = new ParticleSystem.MinMaxCurve(.08f, .24f);
            main.startColor = new ParticleSystem.MinMaxGradient(Color.white, cyan);
            main.gravityModifier = .45f;
            var shape = spray.shape; shape.shapeType = ParticleSystemShapeType.Cone; shape.angle = 25f; shape.radius = .16f;
            var size = spray.sizeOverLifetime; size.enabled = true; size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, .4f, 1f, 0f));
            var color = spray.colorOverLifetime;
            color.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(cyan, .8f) },
                new[] { new GradientAlphaKey(.95f, 0f), new GradientAlphaKey(.7f, .6f), new GradientAlphaKey(0f, 1f) });
            color.color = gradient;
            spray.Play();
        }

        private void BuildVoice()
        {
            const int rate = 16000;
            var samples = new float[6400];
            for (int i = 0; i < samples.Length; i++)
            {
                float t = i / (float)rate;
                float envelope = Mathf.Sin(Mathf.PI * i / (samples.Length - 1));
                float pitch = kind == OddParcelKind.QuackingEgg ? 260f - 170f * t : 790f - 520f * t;
                float wave = Mathf.Sin(t * pitch * 2f * Mathf.PI);
                samples[i] = (wave + .26f * Mathf.Sin(t * pitch * 6f * Mathf.PI)) * envelope * .3f;
            }
            quack = AudioClip.Create("OddParcelSquawk", samples.Length, 1, rate, false);
            quack.SetData(samples, 0);
            voice = root.AddComponent<AudioSource>();
            voice.playOnAwake = false; voice.spatialBlend = 1f; voice.volume = 0f;
            voice.minDistance = 3f; voice.maxDistance = 36f; voice.dopplerLevel = 0f;
            voice.clip = quack;
        }

        internal void Tick(float dt, float pressure, bool carried)
        {
            if (root == null) return;
            phase += dt;
            bounce = Mathf.Max(0f, bounce - dt);
            shout = Mathf.Max(0f, shout - dt);
            float agitation = pressure / 100f;
            float hop = bounce > 0f ? Mathf.Sin((1f - bounce) * Mathf.PI) * 2.2f : 0f;
            body.localPosition = new Vector3(0f, hop + (station ? 0f : Mathf.Sin(phase * 3f) * .065f), 0f);
            body.localRotation = Quaternion.Euler(agitation * Mathf.Sin(phase * 19f) * 7f,
                station ? 0f : Mathf.Sin(phase * 2.2f) * 5f, agitation * Mathf.Cos(phase * 23f) * 11f);
            float flap = Mathf.Sin(phase * (shout > 0f ? 28f : 4f)) * (shout > 0f ? 65f : 18f);
            if (leftWing != null) leftWing.localRotation = Quaternion.Euler(0f, 0f, -flap);
            if (rightWing != null) rightWing.localRotation = Quaternion.Euler(0f, 0f, flap);
            if (orbit != null) orbit.localRotation = Quaternion.Euler(0f, phase * 32f, 0f);
            if (stamp != null) stamp.localPosition = Vector3.up * (bounce > 0f ? -.4f * Mathf.Sin((1f - bounce) * Mathf.PI) : Mathf.Sin(phase * 2f) * .08f);
            if (beak != null && kind == OddParcelKind.QuackingEgg) beak.localScale = new Vector3(.67f, shout > 0f ? .32f : .16f, .56f);
            // 压力灯按每格电量连续升起；最后两格接近满时跟着心跳一样跳。
            float beat = pressure >= 80f ? 1f + Mathf.Abs(Mathf.Sin(phase * 9f)) * .35f : 1f;
            for (int i = 0; i < gauge.Length; i++) if (gauge[i] != null)
            {
                float level = Mathf.Clamp01(pressure / 10f - i);
                gauge[i].localScale = new Vector3(.065f, Mathf.Lerp(.025f, .13f, level) * (i >= 8 && level > 0f ? beat : 1f), .06f);
            }
            if (spray != null)
            {
                var emission = spray.emission;
                emission.rateOverTime = !station && kind == OddParcelKind.AngrySoda && !carried && pressure > 1f ? 42f : pressure > 70f ? 8f : 0f;
            }
        }

        internal void Burst(bool celebrate)
        {
            bounce = 1f; shout = .5f;
            if (spray != null) spray.Emit(celebrate ? 100 : 65);
            PlayBurstFx(celebrate);
            Squawk();
        }

        private void PlayBurstFx(bool celebrate)
        {
            Vector3 top = root.transform.position + Vector3.up * (station ? 1.9f : 2.3f);
            try
            {
                if (celebrate)
                {
                    // 签收：巨型印章盖下，彩纸从台面炸开，一圈冲击环贴地推开。
                    for (int i = 0; i < 2; i++)
                    {
                        BossRushFxBurst confetti = BossRushFxKit.Dust(i == 0 ? cyan : orange, 18);
                        confetti.Shape = BossRushParticleShape.Shard; confetti.Radial = false; confetti.FlatOnGround = false; confetti.Upward = true;
                        confetti.SpeedMin = 4f; confetti.SpeedMax = 8f; confetti.Gravity = .55f; confetti.Drag = 1.4f;
                        confetti.SizeMin = .12f; confetti.SizeMax = .22f; confetti.LifeMin = 1f; confetti.LifeMax = 1.7f;
                        confetti.Spin = 540f; confetti.GrowTo = .9f; confetti.FadeIn = 0f;
                        BossRushFxKit.PlayBurst(top, confetti);
                    }
                    BossRushFxKit.PlayBurst(top, BossRushFxKit.Glint(Color.Lerp(cyan, Color.white, .4f), 1.3f, .2f));
                    BossRushFxKit.PlayBurst(root.transform.position + Vector3.up * .06f, BossRushFxKit.Shockwave(new Color(cyan.r, cyan.g, cyan.b, .65f), .9f, 3f, .45f, true));
                }
                else if (kind == OddParcelKind.AngrySoda)
                {
                    // 汽水爆开：一大团气泡从拉环喷出。
                    BossRushFxBurst foam = BossRushFxKit.Dust(new Color(.85f, 1f, 1f, .85f), 26);
                    foam.Shape = BossRushParticleShape.Bubble; foam.Radial = false; foam.FlatOnGround = false; foam.Cone = 28f; foam.Direction = Vector3.up;
                    foam.SpeedMin = 4f; foam.SpeedMax = 8f; foam.Gravity = .7f; foam.Drag = 1.2f; foam.SizeMin = .18f; foam.SizeMax = .4f;
                    foam.LifeMin = .8f; foam.LifeMax = 1.4f; foam.GrowTo = 1.2f;
                    BossRushFxKit.PlayBurst(top, foam);
                }
                else
                {
                    // 巨蛋弹起：羽毛和蛋壳屑一起蹦出来。
                    BossRushFxBurst feathers = BossRushFxKit.Dust(new Color(1f, .9f, .55f, .9f), 14);
                    feathers.Shape = BossRushParticleShape.Leaf; feathers.Radial = false; feathers.FlatOnGround = false; feathers.Upward = true;
                    feathers.SpeedMin = 2.5f; feathers.SpeedMax = 5f; feathers.Gravity = .18f; feathers.Drag = 2f;
                    feathers.SizeMin = .2f; feathers.SizeMax = .34f; feathers.LifeMin = 1.2f; feathers.LifeMax = 2f; feathers.Spin = 200f;
                    BossRushFxKit.PlayBurst(top, feathers);
                }
                BossRushFxBurst dust = BossRushFxKit.Dust(new Color(.5f, .45f, .4f, .4f), 10);
                dust.ShapeRadius = .6f;
                BossRushFxKit.PlayBurst(root.transform.position + Vector3.up * .1f, dust);
            }
            catch (Exception e) { ModBehaviour.DevLog("[OddParcel] 爆发表现失败: " + e.Message); }
        }

        internal void Squawk()
        {
            if (shout <= 0f) PlayShoutRing();
            shout = .5f;
            SyncVoiceVolume();
            if (voice != null && quack != null && voice.volume > 0f) voice.Play();
        }

        /// <summary>巨蛋叫一声时嘴边推开一圈声浪。</summary>
        private void PlayShoutRing()
        {
            if (beak == null || station) return;
            BossRushFxKit.PlayBurst(beak.position + beak.forward * .3f, BossRushFxKit.Shockwave(new Color(1f, .85f, .5f, .55f), .3f, 4f, .35f, false));
        }

        internal void SyncVoiceVolume()
        {
            if (voice == null) return;
            var master = Duckov.AudioManager.GetBus("Master");
            var sfx = Duckov.AudioManager.GetBus("Master/SFX");
            // 官方设置只改 FMOD 总线；这段 PCM 由 Unity 播放，需要显式桥接。
            // AI 的听觉声源由 runtime 单独投递，不受玩家的播放音量影响。
            voice.volume = master != null && sfx != null && !master.Mute && !sfx.Mute
                ? .7f * Mathf.Clamp01(master.Volume) * Mathf.Clamp01(sfx.Volume) : 0f;
        }

        private static Mesh EggMesh()
        {
            const int rings = 16, slices = 24;
            Vector3[] vertices = new Vector3[(rings + 1) * (slices + 1)];
            Vector2[] uv = new Vector2[vertices.Length];
            Color[] colors = new Color[vertices.Length];
            int[] triangles = new int[rings * slices * 6];
            for (int y = 0; y <= rings; y++)
            {
                float p = y * Mathf.PI / rings;
                float height = Mathf.Cos(p);
                float radius = Mathf.Sin(p) * (.80f - height * .15f);
                for (int x = 0; x <= slices; x++)
                {
                    int n = y * (slices + 1) + x;
                    float a = x * Mathf.PI * 2f / slices;
                    vertices[n] = new Vector3(Mathf.Cos(a) * radius, height * 1.05f, Mathf.Sin(a) * radius);
                    uv[n] = new Vector2(x / (float)slices, y / (float)rings);
                    float shade = .80f + .20f * Mathf.Max(0f, height * .65f - Mathf.Cos(a) * .35f);
                    colors[n] = new Color(shade, shade, shade, 1f);
                }
            }
            int t = 0;
            for (int y = 0; y < rings; y++) for (int x = 0; x < slices; x++)
            {
                int a = y * (slices + 1) + x, b = a + slices + 1;
                triangles[t++] = a; triangles[t++] = b; triangles[t++] = a + 1;
                triangles[t++] = a + 1; triangles[t++] = b; triangles[t++] = b + 1;
            }
            Mesh mesh = new Mesh { name = "OddParcelSculptedEgg" };
            mesh.vertices = vertices; mesh.uv = uv; mesh.colors = colors; mesh.triangles = triangles;
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            return mesh;
        }

        public void Dispose()
        {
            if (disposed) return; disposed = true;
            if (voice != null) voice.Stop();
            if (root != null) { root.SetActive(false); FieldAdventureVisualAssets.ReleaseOwner(root); UnityEngine.Object.Destroy(root); }
            if (quack != null) UnityEngine.Object.Destroy(quack);
            if (eggMesh != null) UnityEngine.Object.Destroy(eggMesh);
        }
    }
}
