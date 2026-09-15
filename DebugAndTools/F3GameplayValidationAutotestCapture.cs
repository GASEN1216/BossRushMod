#if BOSSRUSH_DEV
// ============================================================================
// F3GameplayValidationAutotestCapture.cs - 全自动实机验收的截图与进程内像素检查（Dev 构建）
// ============================================================================
// 截图与官方拍照模式同口径：WaitForEndOfFrame 之后 UnityEngine.ScreenCapture。先 CaptureScreenshotAsTexture 在进程内取像素分析，再编码落盘。
// 不用 Steam 截图：它依赖 overlay 已初始化、文件落在 Steam userdata 而不在结果目录、进程内也拿不到像素（理由同样写进 manifest）。
//
// 进程内检查三类，判据都在纯函数里（F3AutotestJudges）：
//   文字对比度  元素字形矩形外圈取背景、矩形内离背景最远的一截取文字，像素按 sRGB 反解到线性光算 WCAG 比值（UI 审核 F-01）；
//              `路径#first` / `路径#last` 只取第一 / 最后几个可见字（长字幕行首行尾）。
//   文字溢出    面板与 HUD 里全部可见 TMP：画到框外的判红，被省略号截断的列出来。
//   世界可见度  地面光环、采集光斑、云蚋、回响风眼投影到屏幕，比较物体像素与邻域的亮度差。
// 截图前 F3 菜单等调试浮层必须确实不在屏幕上（tests/F3AutotestOrchestratorGuard.py 钉住调用顺序）；
// 单轮磁盘预算 ≤300 MB：UI PNG、世界 JPG，用量上去之后降级，满了只分析不落盘。
// ============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BossRush
{
    internal sealed partial class F3GameplayValidationRunner
    {
        private sealed class AutotestMeasure
        {
            internal string Result, Reason, Metrics;
            internal double Value;
            // 备选口径（采集光斑的色度偏移）：Value 没到门槛、Alt 到了 AltMin 也算过（F3AutotestJudges.VisibilityMet）。
            internal double Alt = double.NaN, AltMin = double.NaN;
        }

        private sealed class AutotestShotAnalysis
        {
            internal int Width, Height;
            internal readonly Dictionary<string, AutotestMeasure> Contrast = new Dictionary<string, AutotestMeasure>(StringComparer.Ordinal);
            internal readonly Dictionary<string, AutotestMeasure> Visibility = new Dictionary<string, AutotestMeasure>(StringComparer.Ordinal);
            internal readonly List<double> RowLuminance = new List<double>();
            internal AutotestMeasure Overflow;
        }

        private struct AutotestTextProbe
        {
            internal string Requested, Path;
            internal Rect Screen;
            internal Color Declared;
            // 字直接坐在一块底板上（父物体的 Image，例如 ESC 键帽）时底板的屏幕矩形；没有就是空矩形。
            internal Rect Plate;
        }

        private struct AutotestWorldProbe
        {
            internal string Target, Name;
            internal Rect Screen;
            // 地面圆环（撤离环、噬风预警圈）：沿环带逐段的屏幕采样点、紧挨环带内外两侧各一份，与环的声明颜色。
            internal bool Ring;
            internal Vector2[] Band, Inner, Outer;
            internal Color RingColor;
        }

        /// <summary>环带相对邻域朝环的颜色至少偏这么多（线性 RGB）才算这一段看得见。</summary>
        private const double AutotestRingMinShift = 0.02;

        /// <summary>
        /// 文字取色的字形框面积下限（px²）。再小，抗锯齿边就占满了字形：810×540 下 ESC 键帽只有 10×8 px，
        /// 第六轮同一个键帽在信鸽 / 码头面板量成 1.2–1.3、倒挂邮亭量成 6.0。低于它记 SKIP，1920×1080 下键帽约 24×19 px 照常量。
        /// </summary>
        private const float AutotestTextMinArea = 150f;

        /// <summary>截图前必须不在屏幕上的调试浮层根物体（F3 菜单、NPC 传送面板）。</summary>
        private static readonly string[] AutotestOverlayRoots = { "F3DebugCheatMenu", "NPCTeleportUI" };

        private static readonly double[] AutotestLinear = BuildAutotestLinearTable();

        private static double[] BuildAutotestLinearTable()
        {
            var table = new double[256];
            for (int i = 0; i < 256; i++) table[i] = F3AutotestJudges.SrgbToLinear(i / 255.0);
            return table;
        }

        private static bool EnsureAutotestOverlaysHidden(out string blocking)
        {
            blocking = null;
            foreach (string name in AutotestOverlayRoots)
            {
                GameObject overlay = GameObject.Find(name);
                if (overlay != null && overlay.activeInHierarchy)
                {
                    blocking = name;
                    return false;
                }
            }
            return true;
        }

        private IEnumerator CaptureAutotestShot(F3AutotestStepRecord record, string name, string kind, string canvasFilter,
            List<string> contrastPaths, List<string> worldTargets)
        {
            var shot = new F3AutotestShot { Name = string.IsNullOrEmpty(name) ? "shot" : name, Kind = kind == "world" ? "world" : "ui" };
            record.Shots.Add(shot);
            string blocking = null;
            float until = Time.realtimeSinceStartup + 10f;
            while (!EnsureAutotestOverlaysHidden(out blocking) && Time.realtimeSinceStartup < until) yield return null;
            if (blocking != null)
            {
                shot.Encoding = "skipped";
                shot.Metrics = "overlay_visible:" + blocking;
                _autotest.Info.ShotsSkipped++;
                yield break;
            }
            var texts = new List<AutotestTextProbe>();
            var overflowing = new List<string>();
            var truncated = new List<string>();
            var truncatedEarly = new List<string>();
            int inspected = CollectAutotestTextProbes(canvasFilter, contrastPaths, texts, overflowing, truncated, truncatedEarly);
            List<AutotestWorldProbe> worlds = CollectAutotestWorldProbes(worldTargets);
            List<Rect> rows = CollectAutotestRowRects();

            yield return new WaitForEndOfFrame();

            if (!EnsureAutotestOverlaysHidden(out blocking))
            {
                shot.Encoding = "skipped";
                shot.Metrics = "overlay_visible:" + blocking;
                _autotest.Info.ShotsSkipped++;
                yield break;
            }
            Texture2D texture = null, half = null;
            // 这一张在本步里的时刻：连拍判「0.15 秒提亮、0.5 秒内落回」这类时长要用（第五轮 manifest 没有逐张时间）。
            long atMs = AutotestStepElapsedMs(record);
            try
            {
                texture = ScreenCapture.CaptureScreenshotAsTexture();
                Color32[] pixels = texture.GetPixels32();
                var analysis = new AutotestShotAnalysis { Width = texture.width, Height = texture.height };
                AnalyzeAutotestText(pixels, analysis, texts, contrastPaths);
                string overflowMetrics, overflowReason;
                string overflowResult = F3AutotestJudges.JudgeTextOverflow(overflowing, truncated, truncatedEarly, inspected, out overflowMetrics, out overflowReason);
                analysis.Overflow = new AutotestMeasure { Result = overflowResult, Reason = overflowReason, Metrics = overflowMetrics };
                AnalyzeAutotestWorld(pixels, analysis, worlds, worldTargets);
                foreach (Rect row in rows) analysis.RowLuminance.Add(MeanAutotestLuminance(pixels, analysis.Width, analysis.Height, AutotestRowStrip(row)));
                _autotest.LastShot = analysis;

                string encoding = F3AutotestJudges.ChooseEncoding(_autotest.ShotBytes, shot.Kind == "ui", F3AutotestJudges.ShotBudgetBytes);
                byte[] bytes = null;
                if (encoding == "png") bytes = ImageConversion.EncodeToPNG(texture);
                else if (encoding == "jpg") bytes = ImageConversion.EncodeToJPG(texture, 88);
                else if (encoding == "jpg_half")
                {
                    half = DownsampleAutotestShot(pixels, analysis.Width, analysis.Height);
                    bytes = ImageConversion.EncodeToJPG(half, 82);
                }
                shot.Encoding = encoding;
                if (bytes != null)
                {
                    string file = AutotestFileName(record.Id + "__" + shot.Name) + (encoding == "png" ? ".png" : ".jpg");
                    File.WriteAllBytes(Path.Combine(_autotest.ShotsDir, file), bytes);
                    shot.File = "shots/" + file;
                    shot.Bytes = bytes.Length;
                    _autotest.ShotBytes += bytes.Length;
                    _autotest.Info.ShotCount++;
                    if (encoding == "jpg_half" || (encoding == "jpg" && shot.Kind == "ui")) _autotest.Info.ShotsDegraded++;
                }
                else _autotest.Info.ShotsSkipped++;
                shot.Metrics = "size=" + analysis.Width + "x" + analysis.Height + ",at_ms=" + atMs + ",texts=" + inspected + ",contrast_probes=" + analysis.Contrast.Count
                    + ",world_probes=" + analysis.Visibility.Count + ",rows=" + analysis.RowLuminance.Count + ",overflow=" + overflowResult;
            }
            catch (Exception e)
            {
                shot.Encoding = "failed";
                shot.Metrics = "capture_failed:" + e.GetType().Name + ":" + e.Message;
            }
            finally
            {
                if (texture != null) UnityEngine.Object.Destroy(texture);
                if (half != null) UnityEngine.Object.Destroy(half);
            }
        }

        #region 文字

        private static int CollectAutotestTextProbes(string canvasFilter, List<string> requested, List<AutotestTextProbe> probes,
            List<string> overflowing, List<string> truncated, List<string> truncatedEarly)
        {
            string[] roots = string.IsNullOrEmpty(canvasFilter) ? new[] { "SkyIslandHud", "SkyIslandStory" } : canvasFilter.Split('+');
            Transform officialDialogue = Dialogues.DialogueUI.instance != null ? Dialogues.DialogueUI.instance.transform : null;
            int inspected = 0;
            foreach (TextMeshProUGUI text in UnityEngine.Object.FindObjectsOfType<TextMeshProUGUI>())
            {
                if (text == null || !text.isActiveAndEnabled || string.IsNullOrEmpty(text.text)) continue;
                Canvas canvas = text.canvas;
                Canvas root = canvas == null ? null : canvas.rootCanvas;
                if (root == null || !AutotestMatchesRoot(root.gameObject.name, roots)) continue;
                float alpha = text.color.a * AutotestGroupAlpha(text.transform);
                if (alpha < 0.05f) continue;
                inspected++;
                string path = AutotestHierarchyPath(text.transform, root.transform);
                try
                {
                    if (text.overflowMode == TextOverflowModes.Overflow)
                    {
                        Bounds bounds = text.textBounds;
                        Rect box = text.rectTransform.rect;
                        bool outside = bounds.size.x > 0f && (bounds.min.x < box.xMin - 2f || bounds.max.x > box.xMax + 2f
                            || bounds.min.y < box.yMin - 2f || bounds.max.y > box.yMax + 2f);
                        // 官方对话框（名字条、台词、选项）是 TMP 默认 Overflow 配自适应布局与遮罩：第三轮 12 张对话框截图连中文也全判红，是误报。
                        // 这些只列进 truncated 供看截图，不判红；我们自己的 HUD 与面板照旧判红。
                        if (outside && officialDialogue != null && text.transform.IsChildOf(officialDialogue)) truncated.Add(path);
                        else if (outside) overflowing.Add(path);
                    }
                    else if (text.isTextTruncated || text.isTextOverflowing)
                    {
                        truncated.Add(path);
                        // 字幕设计上封顶两行再省略号收尾；只排出一行就被截断，是框高算小了（第五轮 7 条长字幕只剩一行、一直自动绿）。
                        if (path.IndexOf("SkyIslandCaption/", StringComparison.Ordinal) >= 0 && text.textInfo != null && text.textInfo.lineCount < 2)
                            truncatedEarly.Add(path);
                    }
                }
                catch (Exception) { }
                if (requested == null) continue;
                foreach (string want in requested)
                {
                    if (string.IsNullOrEmpty(want)) continue;
                    int hash = want.IndexOf('#');
                    string basePath = hash < 0 ? want : want.Substring(0, hash);
                    string part = hash < 0 ? null : want.Substring(hash + 1);
                    if (path.IndexOf(basePath, StringComparison.Ordinal) < 0) continue;
                    bool already = false;
                    foreach (AutotestTextProbe existing in probes) if (existing.Requested == want) already = true;
                    Rect screen;
                    if (already || !TryAutotestTextRect(text, root, part, out screen)) continue;
                    Color declared = text.color;
                    declared.a = alpha;
                    probes.Add(new AutotestTextProbe { Requested = want, Path = path, Screen = screen, Declared = declared, Plate = AutotestTextPlate(text, root, screen) });
                }
            }
            return inspected;
        }

        /// <summary>字直接坐在一块底板（父物体上启用的 Image）上、且字的中心落在底板里时，返回底板的屏幕矩形；否则空矩形。</summary>
        private static Rect AutotestTextPlate(TMP_Text text, Canvas root, Rect textRect)
        {
            Transform parent = text.transform.parent;
            Image plate = parent != null ? parent.GetComponent<Image>() : null;
            if (plate == null || !plate.enabled) return default(Rect);
            Camera camera = root.renderMode == RenderMode.ScreenSpaceOverlay ? null : root.worldCamera;
            var corners = new Vector3[4];
            plate.rectTransform.GetWorldCorners(corners);
            Vector2 a = RectTransformUtility.WorldToScreenPoint(camera, corners[0]);
            Vector2 b = RectTransformUtility.WorldToScreenPoint(camera, corners[2]);
            Rect rect = Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
            return rect.Contains(textRect.center) ? rect : default(Rect);
        }

        /// <summary>
        /// 剧情面板把「名称 + 计数」包进 TMP 的 &lt;nobr&gt;（SkyIslandStoryPresentation.KeepCountsTogether，2026-09-15 第五轮 D8）。
        /// 步骤表按玩家看到的字写，面板文字先剥标签再匹配（AutotestRowLabel / AutotestPanelBody）。
        /// 不用 GetParsedText：选项标签是省略号溢出，被截掉的尾巴会丢，而且要等网格生成。放在这里而不放动作文件：那份有 1200 行预算。
        /// </summary>
        private static string AutotestPlainText(string value)
        {
            return string.IsNullOrEmpty(value) ? value : value.Replace("<nobr>", string.Empty).Replace("</nobr>", string.Empty);
        }

        private static bool TryAutotestTextRect(TMP_Text text, Canvas root, string part, out Rect screen)
        {
            screen = default(Rect);
            Camera camera = root.renderMode == RenderMode.ScreenSpaceOverlay ? null : root.worldCamera;
            Vector3 min, max;
            if (part == "first" || part == "last")
            {
                TMP_TextInfo info = text.textInfo;
                if (info == null || info.characterCount == 0) return false;
                int index = -1;
                if (part == "first") { for (int i = 0; i < info.characterCount && index < 0; i++) if (info.characterInfo[i].isVisible) index = i; }
                else { for (int i = info.characterCount - 1; i >= 0 && index < 0; i--) if (info.characterInfo[i].isVisible) index = i; }
                if (index < 0) return false;
                int from = part == "first" ? index : Math.Max(0, index - 3);
                int to = part == "first" ? Math.Min(info.characterCount - 1, index + 3) : index;
                min = new Vector3(float.MaxValue, float.MaxValue, 0f);
                max = new Vector3(float.MinValue, float.MinValue, 0f);
                for (int i = from; i <= to; i++)
                {
                    TMP_CharacterInfo character = info.characterInfo[i];
                    if (!character.isVisible) continue;
                    min = Vector3.Min(min, character.bottomLeft);
                    max = Vector3.Max(max, character.topRight);
                }
                if (min.x == float.MaxValue) return false;
            }
            else
            {
                Bounds bounds = text.textBounds;
                if (bounds.size.x <= 0f || bounds.size.y <= 0f) return false;
                min = bounds.min;
                max = bounds.max;
            }
            Vector2 a = RectTransformUtility.WorldToScreenPoint(camera, text.rectTransform.TransformPoint(new Vector3(min.x, min.y, 0f)));
            Vector2 b = RectTransformUtility.WorldToScreenPoint(camera, text.rectTransform.TransformPoint(new Vector3(max.x, max.y, 0f)));
            screen = Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
            return screen.width >= 2f && screen.height >= 2f;
        }

        private static void AnalyzeAutotestText(Color32[] pixels, AutotestShotAnalysis analysis, List<AutotestTextProbe> probes, List<string> requested)
        {
            if (requested != null)
                foreach (string want in requested)
                    if (!analysis.Contrast.ContainsKey(want))
                        analysis.Contrast[want] = new AutotestMeasure { Result = "SKIP", Reason = "text_not_found_or_not_visible:" + want };
            foreach (AutotestTextProbe probe in probes)
            {
                Rect inner = probe.Screen;
                if (inner.width * inner.height < AutotestTextMinArea)
                {
                    analysis.Contrast[probe.Requested] = new AutotestMeasure
                    {
                        Result = "SKIP", Reason = "text_too_small_on_screen",
                        Metrics = "path=" + probe.Path + ",rect=" + AutotestRectText(inner) + ",min_area=" + AutotestTextMinArea.ToString("F0", CultureInfo.InvariantCulture)
                    };
                    continue;
                }
                float pad = Mathf.Clamp(Mathf.Min(inner.width, inner.height) * 0.35f, 4f, 14f);
                double[] inside = SampleAutotestY(pixels, analysis.Width, analysis.Height, inner, default(Rect), 2400);
                Rect outer = AutotestExpand(inner, 2f + pad);
                // 字坐在小底板上（ESC 键帽在 810×540 下只有 18×10 px）时外圈不能出底板：第五轮外圈上下落到主视觉插画上，
                // 量成了字对插画（倒挂邮亭 3.75 假红；只取底板内像素是 9.9:1）。
                if (probe.Plate.width > 0f) outer = AutotestIntersect(outer, probe.Plate);
                double[] ring = SampleAutotestY(pixels, analysis.Width, analysis.Height, outer, AutotestExpand(inner, 2f), 1200);
                double bgR, bgG, bgB;
                MeanAutotestLinearRgb(pixels, analysis.Width, analysis.Height, outer, AutotestExpand(inner, 2f), out bgR, out bgG, out bgB);
                double declared = F3AutotestJudges.CompositeLuminance(probe.Declared.r, probe.Declared.g, probe.Declared.b, probe.Declared.a, bgR, bgG, bgB);
                double measured;
                string metrics, reason;
                string result = F3AutotestJudges.JudgeTextContrast(ring, inside, declared, 0.0, out measured, out metrics, out reason);
                analysis.Contrast[probe.Requested] = new AutotestMeasure
                {
                    Result = result, Reason = reason, Value = measured,
                    Metrics = "path=" + probe.Path + ",rect=" + AutotestRectText(inner) + "," + metrics
                };
            }
        }

        private static float AutotestGroupAlpha(Transform transform)
        {
            float alpha = 1f;
            foreach (CanvasGroup group in transform.GetComponentsInParent<CanvasGroup>()) alpha *= group.alpha;
            return alpha;
        }

        private static bool AutotestMatchesRoot(string rootName, string[] roots)
        {
            foreach (string root in roots)
                if (!string.IsNullOrEmpty(root) && rootName.IndexOf(root, StringComparison.Ordinal) >= 0) return true;
            return false;
        }

        private static string AutotestHierarchyPath(Transform node, Transform root)
        {
            var parts = new List<string>();
            for (Transform t = node; t != null; t = t.parent)
            {
                parts.Add(t.name);
                if (t == root) break;
            }
            parts.Reverse();
            return string.Join("/", parts.ToArray());
        }

        private static List<Rect> CollectAutotestRowRects()
        {
            var rects = new List<Rect>();
            var corners = new Vector3[4];
            foreach (Button row in AutotestPanelRows())
            {
                RectTransform rect = row.transform as RectTransform;
                if (rect == null) continue;
                Canvas canvas = row.GetComponentInParent<Canvas>();
                Canvas root = canvas == null ? null : canvas.rootCanvas;
                Camera camera = root == null || root.renderMode == RenderMode.ScreenSpaceOverlay ? null : root.worldCamera;
                rect.GetWorldCorners(corners);
                Vector2 a = RectTransformUtility.WorldToScreenPoint(camera, corners[0]);
                Vector2 b = RectTransformUtility.WorldToScreenPoint(camera, corners[2]);
                rects.Add(Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y)));
            }
            return rects;
        }

        /// <summary>选项行的取色带：右侧 18% 宽、居中一半高——标签左对齐，这一段通常只有行底色。</summary>
        private static Rect AutotestRowStrip(Rect row)
        {
            float width = row.width * 0.18f;
            return new Rect(row.xMax - width - row.width * 0.02f, row.y + row.height * 0.25f, width, row.height * 0.5f);
        }

        #endregion

        #region 世界物体

        private List<AutotestWorldProbe> CollectAutotestWorldProbes(List<string> targets)
        {
            var probes = new List<AutotestWorldProbe>();
            if (targets == null || targets.Count == 0) return probes;
            Camera camera = GameCamera.Instance != null ? GameCamera.Instance.renderCamera : Camera.main;
            CharacterMainControl player = CharacterMainControl.Main;
            if (camera == null) return probes;
            Transform[] all = UnityEngine.Object.FindObjectsOfType<Transform>();
            foreach (string target in targets)
            {
                bool exact;
                string objectName = AutotestWorldObjectName(target, out exact);
                bool found = false;
                float bestDistance = float.MaxValue;
                AutotestWorldProbe best = default(AutotestWorldProbe);
                Transform bestTransform = null;
                foreach (Transform t in all)
                {
                    if (t == null || !t.gameObject.activeInHierarchy) continue;
                    if (exact ? t.name != objectName : !t.name.StartsWith(objectName, StringComparison.Ordinal)) continue;
                    Rect screen;
                    if (!TryAutotestProjectBounds(camera, t, out screen)) continue;
                    float distance = player == null ? 0f : Vector3.Distance(player.transform.position, t.position);
                    if (distance >= bestDistance) continue;
                    bestDistance = distance;
                    best = new AutotestWorldProbe { Target = target, Name = t.name, Screen = screen };
                    bestTransform = t;
                    found = true;
                }
                if (!found) continue;
                if (AutotestRingTarget(target)) CollectAutotestRingSamples(camera, bestTransform, ref best);
                probes.Add(best);
            }
            return probes;
        }

        private static string AutotestWorldObjectName(string target, out bool exact)
        {
            exact = true;
            switch (target)
            {
                case "gnat": return "SkyIslandGnat";
                // 撤离环建好后改名为 SkyIslandExtractionRing_<标记>（SkyIslandGroundRing.cs），按前缀取离玩家最近的那一个。
                case "ground_ring": exact = false; return "SkyIslandExtractionRing_";
                case "gather_glow": return "GatherGlowDisc";
                case "echo_ring": return "SkyIslandStormWarningRing";
                default:
                    exact = false;
                    return target;
            }
        }

        private static bool TryAutotestProjectBounds(Camera camera, Transform target, out Rect screen)
        {
            screen = default(Rect);
            bool any = false;
            Bounds bounds = default(Bounds);
            foreach (Renderer renderer in target.GetComponentsInChildren<Renderer>(false))
            {
                // 拖尾不算物体本身：云蚋的 TrailRenderer 把投影框撑大，框里大半是地面。
                if (renderer == null || !renderer.enabled || renderer is TrailRenderer) continue;
                if (!any) { bounds = renderer.bounds; any = true; }
                else bounds.Encapsulate(renderer.bounds);
            }
            if (!any) return false;
            Vector3 center = bounds.center, extents = bounds.extents;
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = center + new Vector3((i & 1) == 0 ? -extents.x : extents.x, (i & 2) == 0 ? -extents.y : extents.y,
                    (i & 4) == 0 ? -extents.z : extents.z);
                Vector3 point = camera.WorldToScreenPoint(corner);
                if (point.z <= 0f) return false;
                minX = Mathf.Min(minX, point.x); minY = Mathf.Min(minY, point.y);
                maxX = Mathf.Max(maxX, point.x); maxY = Mathf.Max(maxY, point.y);
            }
            screen = Rect.MinMaxRect(minX, minY, maxX, maxY);
            return screen.width >= 3f && screen.height >= 3f && screen.xMax > 0f && screen.yMax > 0f
                && screen.xMin < Screen.width && screen.yMin < Screen.height;
        }

        private static void AnalyzeAutotestWorld(Color32[] pixels, AutotestShotAnalysis analysis, List<AutotestWorldProbe> probes, List<string> targets)
        {
            if (targets != null)
                foreach (string target in targets)
                    if (!analysis.Visibility.ContainsKey(target))
                        analysis.Visibility[target] = new AutotestMeasure { Result = "SKIP", Reason = "target_not_found_on_screen:" + target };
            foreach (AutotestWorldProbe probe in probes)
            {
                if (probe.Ring)
                {
                    AnalyzeAutotestRing(pixels, analysis, probe);
                    continue;
                }
                Rect inner = probe.Screen;
                float pad = Mathf.Max(10f, Mathf.Max(inner.width, inner.height) * 0.5f);
                double[] objectY = SampleAutotestY(pixels, analysis.Width, analysis.Height, inner, default(Rect), 3000);
                double[] neighborY = SampleAutotestY(pixels, analysis.Width, analysis.Height, AutotestExpand(inner, pad), AutotestExpand(inner, 2f), 3000);
                Rect visible = AutotestIntersect(inner, new Rect(0f, 0f, analysis.Width, analysis.Height));
                double onScreen = inner.width * inner.height <= 0f ? 0.0 : (double)(visible.width * visible.height) / (inner.width * inner.height);
                double chroma = double.NaN, minChroma = double.NaN;
                if (probe.Target == "gather_glow")
                {
                    // 白天暖色光斑压在砂岩与花丛上主要靠色相（第五轮 A1 11 m 肉眼可见、亮度差只有 0.004）。
                    chroma = F3AutotestJudges.ChromaShift(SampleAutotestRgb(pixels, analysis.Width, analysis.Height, inner, default(Rect), 3000),
                        SampleAutotestRgb(pixels, analysis.Width, analysis.Height, AutotestExpand(inner, pad), AutotestExpand(inner, 2f), 3000));
                    minChroma = F3AutotestJudges.GlowMinChromaShift;
                }
                double weber;
                string metrics, reason;
                string result = F3AutotestJudges.JudgeWorldVisibility(objectY, neighborY, 0.0, onScreen, Mathf.Max(inner.width, inner.height),
                    chroma, minChroma, out weber, out metrics, out reason);
                analysis.Visibility[probe.Target] = new AutotestMeasure
                {
                    Result = result, Reason = reason, Value = weber, Alt = chroma, AltMin = minChroma,
                    Metrics = "object=" + probe.Name + ",rect=" + AutotestRectText(inner) + "," + metrics
                };
            }
        }

        /// <summary>地面圆环按环带逐段取样判覆盖率（<see cref="F3AutotestJudges.JudgeRingCoverage"/>），不按投影框取亮度。</summary>
        private static bool AutotestRingTarget(string target)
        {
            return target == "ground_ring" || target == "echo_ring";
        }

        /// <summary>
        /// 沿 LineRenderer 的每个顶点取环带中心线、向内与向外各 1.5 个带宽的屏幕点（只收三点都在屏幕内的段）。
        /// 找不到 LineRenderer 也记成圆环探针、样本为空，判据给 SKIP——不退回投影框口径，免得再被石面亮度骗过。
        /// </summary>
        private static void CollectAutotestRingSamples(Camera camera, Transform target, ref AutotestWorldProbe probe)
        {
            probe.Ring = true;
            probe.Band = probe.Inner = probe.Outer = new Vector2[0];
            LineRenderer line = target == null ? null : target.GetComponent<LineRenderer>();
            if (line == null || line.positionCount < 8) return;
            Vector3 center = target.position;
            float halfBand = Mathf.Max(0.05f, line.widthMultiplier * 0.5f);
            var band = new List<Vector2>(line.positionCount);
            var inner = new List<Vector2>(line.positionCount);
            var outer = new List<Vector2>(line.positionCount);
            for (int i = 0; i < line.positionCount; i++)
            {
                Vector3 local = line.GetPosition(i);
                Vector3 world = line.useWorldSpace ? local : line.transform.TransformPoint(local);
                Vector3 radial = world - center;
                float radius = radial.magnitude;
                if (radius < 0.001f) continue;
                Vector3 direction = radial / radius;
                Vector2 a, b, c;
                if (!AutotestScreenPoint(camera, world, out a)
                    || !AutotestScreenPoint(camera, center + direction * Mathf.Max(0f, radius - halfBand * 3f), out b)
                    || !AutotestScreenPoint(camera, center + direction * (radius + halfBand * 3f), out c)) continue;
                band.Add(a);
                inner.Add(b);
                outer.Add(c);
            }
            probe.Band = band.ToArray();
            probe.Inner = inner.ToArray();
            probe.Outer = outer.ToArray();
            probe.RingColor = line.startColor;
        }

        private static bool AutotestScreenPoint(Camera camera, Vector3 world, out Vector2 screen)
        {
            Vector3 point = camera.WorldToScreenPoint(world);
            screen = new Vector2(point.x, point.y);
            return point.z > 0f && point.x >= 1f && point.y >= 1f && point.x < Screen.width - 1 && point.y < Screen.height - 1;
        }

        private static void AnalyzeAutotestRing(Color32[] pixels, AutotestShotAnalysis analysis, AutotestWorldProbe probe)
        {
            int count = probe.Band == null ? 0 : probe.Band.Length;
            var band = new double[count * 3];
            var near = new double[count * 3];
            var inside = new double[3];
            var outside = new double[3];
            for (int i = 0; i < count; i++)
            {
                SampleAutotestLinearRgb(pixels, analysis.Width, analysis.Height, probe.Band[i], band, i * 3);
                SampleAutotestLinearRgb(pixels, analysis.Width, analysis.Height, probe.Inner[i], inside, 0);
                SampleAutotestLinearRgb(pixels, analysis.Width, analysis.Height, probe.Outer[i], outside, 0);
                for (int k = 0; k < 3; k++) near[i * 3 + k] = (inside[k] + outside[k]) * 0.5;
            }
            double coverage;
            string metrics, reason;
            string result = F3AutotestJudges.JudgeRingCoverage(band, near, F3AutotestJudges.SrgbToLinear(probe.RingColor.r),
                F3AutotestJudges.SrgbToLinear(probe.RingColor.g), F3AutotestJudges.SrgbToLinear(probe.RingColor.b),
                AutotestRingMinShift, out coverage, out metrics, out reason);
            analysis.Visibility[probe.Target] = new AutotestMeasure
            {
                Result = result, Reason = reason, Value = coverage,
                Metrics = "object=" + probe.Name + ",rect=" + AutotestRectText(probe.Screen) + "," + metrics
            };
        }

        /// <summary>以屏幕点为中心 3×3 取平均线性 RGB，写进 <paramref name="into"/> 的 offset..offset+2。</summary>
        private static void SampleAutotestLinearRgb(Color32[] pixels, int width, int height, Vector2 point, double[] into, int offset)
        {
            int cx = Mathf.RoundToInt(point.x), cy = Mathf.RoundToInt(point.y);
            double r = 0.0, g = 0.0, b = 0.0;
            int n = 0;
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    int x = cx + dx, y = cy + dy;
                    if (x < 0 || y < 0 || x >= width || y >= height) continue;
                    Color32 c = pixels[y * width + x];
                    r += AutotestLinear[c.r];
                    g += AutotestLinear[c.g];
                    b += AutotestLinear[c.b];
                    n++;
                }
            }
            if (n == 0) n = 1;
            into[offset] = r / n;
            into[offset + 1] = g / n;
            into[offset + 2] = b / n;
        }

        #endregion

        #region 取景

        /// <summary>
        /// 取景瞬移 <c>teleport_view:目标:右:上[:等待]</c>：让目标落在画面中心右 <c>右</c> 米、上 <c>上</c> 米处（沿相机水平朝向算），
        /// 再走生产的 <see cref="SkyIslandSession.DevAutotestTeleport"/>。按标记写死的世界偏移会因为相机朝向与采集点换位出画
        /// （第五轮 A2 两张 11 m 截图光斑在画面上沿外）。目标可以是标记，也可以是建好的采集点 <c>SkyIslandGather_A2</c>。
        /// </summary>
        private IEnumerator AutotestTeleportView(F3AutotestStepRecord record, string[] args)
        {
            string target = Arg(args, 0);
            SkyIslandSession session = SkyIslandSessionOrNull();
            if (session == null) { AutotestFail(record, "action:teleport_view", "no_island_session", target, true); yield break; }
            Camera camera = GameCamera.Instance != null ? GameCamera.Instance.renderCamera : Camera.main;
            Vector3 right = camera != null ? camera.transform.right : Vector3.zero;
            Vector3 up = camera != null ? camera.transform.forward : Vector3.zero;
            right.y = 0f;
            up.y = 0f;
            if (right.sqrMagnitude < 1e-4f || up.sqrMagnitude < 1e-4f) { AutotestFail(record, "action:teleport_view", "no_camera_heading", target, true); yield break; }
            right.Normalize();
            up.Normalize();
            Vector3 offset = -(right * ArgFloat(args, 1, 0f) + up * ArgFloat(args, 2, 0f));
            CloseAutotestPanels();
            string reason;
            if (!session.DevAutotestTeleport(session.DevAutotestFind(target), offset.x, offset.z, out reason))
            {
                AutotestFail(record, "action:teleport_view", reason, target, true);
                yield break;
            }
            record.Notes.Add("teleport_view=" + target + ",offset=" + offset.x.ToString("F1", CultureInfo.InvariantCulture)
                + "," + offset.z.ToString("F1", CultureInfo.InvariantCulture));
            yield return WaitAutotestReal(ArgFloat(args, 3, 1.5f));
        }

        /// <summary>
        /// 重播撤离环的展开 <c>ring_replay:环物体名</c>：环停用再启用，<see cref="SkyIslandGroundRingPulse"/> 的 OnEnable 把动画计时清零，
        /// 接着连拍就截得到 0.35 秒的展开（阶段推进那一刻玩家不在广场，第五轮截不到）。只动表现层，判定不看它。
        /// </summary>
        private void AutotestRingReplay(F3AutotestStepRecord record, string[] args)
        {
            string name = Arg(args, 0);
            GameObject ring = GameObject.Find(name);
            SkyIslandGroundRingPulse pulse = ring != null ? ring.GetComponentInChildren<SkyIslandGroundRingPulse>(false) : null;
            if (pulse == null) { AutotestFail(record, "action:ring_replay", "ring_not_found", name, true); return; }
            pulse.gameObject.SetActive(false);
            pulse.gameObject.SetActive(true);
            record.Notes.Add("ring_replay=" + name);
        }

        /// <summary>
        /// <c>wait_dialogue_typed:秒</c>：等官方对话「这一句已完整显示、在等确认」或「选项已淡入、在等玩家选」，再多等一帧让这个状态渲染出来——
        /// 截图的文字探针在 WaitForEndOfFrame 之前取，状态在官方 UniTask 的回调里变。只等不点：第五轮截图截在逐字显示中途，
        /// 单次推进又只把这句补完、不翻页，相邻两张截的是同一句。探测字段取不到时退回读 TMP：可见字数到齐，且不是进入等待时就停在那里的旧句。
        /// 依赖：前一条 dialogue_advance 点完固定等 0.45 秒，官方下一帧就把 ▼ 置回失活，这里读不到上一句的旧状态。
        /// </summary>
        private IEnumerator AutotestWaitDialogueTyped(F3AutotestStepRecord record, float seconds)
        {
            float started = Time.realtimeSinceStartup;
            float until = started + Mathf.Max(0.1f, seconds);
            TMP_Text text = OfficialDialogueText();
            string entryLine = text == null ? null : text.text;
            bool entryTyped = AutotestTmpLineTyped(text);
            string probe = "indicator";
            bool met = false, choices = false;
            while (Time.realtimeSinceStartup < until && !ShouldAbort())
            {
                if (!DialogueManager.IsDialogueActive) break;
                if (OfficialDialogueWaitingForChoice() == true) { met = true; choices = true; break; }
                bool? shown = OfficialDialogueLineShown();
                if (shown == true) { met = true; break; }
                if (shown == null)
                {
                    probe = "tmp_fallback";
                    text = OfficialDialogueText();
                    if (AutotestTmpLineTyped(text) && (!entryTyped || text.text != entryLine)) { met = true; break; }
                }
                yield return null;
            }
            if (met)
            {
                yield return null;
                text = OfficialDialogueText();
                record.Notes.Add("dialogue_typed=" + (choices ? "choices" : "line") + ",probe=" + probe
                    + ",waited_ms=" + ((int)((Time.realtimeSinceStartup - started) * 1000f)).ToString(CultureInfo.InvariantCulture)
                    + (choices || text == null ? string.Empty : ",line=" + AutotestShort(text.text, 60)));
                yield break;
            }
            if (ShouldAbort()) yield break;
            if (!DialogueManager.IsDialogueActive)
            {
                AutotestFail(record, "action:wait_dialogue_typed", "dialogue_not_active", DescribeAutotestDialogueUi(), true);
                yield break;
            }
            if (probe == "tmp_fallback" && AutotestTmpLineTyped(text))
            {
                record.Notes.Add("dialogue_typed=line,probe=tmp_fallback_timeout");
                yield break;
            }
            AutotestFail(record, "action:wait_dialogue_typed",
                "timeout_" + seconds.ToString("F1", CultureInfo.InvariantCulture) + "s", DescribeAutotestDialogueUi(), true);
        }

        private static bool AutotestTmpLineTyped(TMP_Text text)
        {
            if (text == null || string.IsNullOrEmpty(text.text) || !text.gameObject.activeInHierarchy) return false;
            int count = text.textInfo == null ? 0 : text.textInfo.characterCount;
            return count > 0 && text.maxVisibleCharacters >= count;
        }

        /// <summary>这一步开始到现在的毫秒数；StartedUtc 解析不了时 -1。</summary>
        private static long AutotestStepElapsedMs(F3AutotestStepRecord record)
        {
            DateTime started;
            if (record == null || !DateTime.TryParse(record.StartedUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out started)) return -1;
            return (long)(DateTime.UtcNow - started.ToUniversalTime()).TotalMilliseconds;
        }

        #endregion

        #region 像素

        private static Rect AutotestExpand(Rect rect, float amount)
        {
            return Rect.MinMaxRect(rect.xMin - amount, rect.yMin - amount, rect.xMax + amount, rect.yMax + amount);
        }

        private static Rect AutotestIntersect(Rect a, Rect b)
        {
            float xMin = Mathf.Max(a.xMin, b.xMin), yMin = Mathf.Max(a.yMin, b.yMin);
            float xMax = Mathf.Min(a.xMax, b.xMax), yMax = Mathf.Min(a.yMax, b.yMax);
            return xMax <= xMin || yMax <= yMin ? default(Rect) : Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        }

        /// <summary>取样步长同 <see cref="SampleAutotestY"/>，返回线性 RGB 连排（r,g,b,r,g,b…）。</summary>
        private static double[] SampleAutotestRgb(Color32[] pixels, int width, int height, Rect rect, Rect exclude, int maxSamples)
        {
            int x0 = Mathf.Clamp(Mathf.FloorToInt(rect.xMin), 0, width), x1 = Mathf.Clamp(Mathf.CeilToInt(rect.xMax), 0, width);
            int y0 = Mathf.Clamp(Mathf.FloorToInt(rect.yMin), 0, height), y1 = Mathf.Clamp(Mathf.CeilToInt(rect.yMax), 0, height);
            if (x1 <= x0 || y1 <= y0) return new double[0];
            int area = (x1 - x0) * (y1 - y0);
            int stride = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(area / (double)Math.Max(1, maxSamples))));
            bool excluding = exclude.width > 0f && exclude.height > 0f;
            var values = new List<double>(Math.Min(maxSamples + 64, area) * 3);
            for (int y = y0; y < y1; y += stride)
            {
                for (int x = x0; x < x1; x += stride)
                {
                    if (excluding && x >= exclude.xMin && x < exclude.xMax && y >= exclude.yMin && y < exclude.yMax) continue;
                    Color32 c = pixels[y * width + x];
                    values.Add(AutotestLinear[c.r]);
                    values.Add(AutotestLinear[c.g]);
                    values.Add(AutotestLinear[c.b]);
                }
            }
            return values.ToArray();
        }

        /// <summary>矩形内（可挖掉一块）按步长取相对亮度 Y；样本数封顶 <paramref name="maxSamples"/>。</summary>
        private static double[] SampleAutotestY(Color32[] pixels, int width, int height, Rect rect, Rect exclude, int maxSamples)
        {
            int x0 = Mathf.Clamp(Mathf.FloorToInt(rect.xMin), 0, width), x1 = Mathf.Clamp(Mathf.CeilToInt(rect.xMax), 0, width);
            int y0 = Mathf.Clamp(Mathf.FloorToInt(rect.yMin), 0, height), y1 = Mathf.Clamp(Mathf.CeilToInt(rect.yMax), 0, height);
            if (x1 <= x0 || y1 <= y0) return new double[0];
            int area = (x1 - x0) * (y1 - y0);
            int stride = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(area / (double)Math.Max(1, maxSamples))));
            bool excluding = exclude.width > 0f && exclude.height > 0f;
            var values = new List<double>(Math.Min(maxSamples + 64, area));
            for (int y = y0; y < y1; y += stride)
            {
                for (int x = x0; x < x1; x += stride)
                {
                    if (excluding && x >= exclude.xMin && x < exclude.xMax && y >= exclude.yMin && y < exclude.yMax) continue;
                    Color32 c = pixels[y * width + x];
                    values.Add(F3AutotestJudges.LuminanceLinear(AutotestLinear[c.r], AutotestLinear[c.g], AutotestLinear[c.b]));
                }
            }
            return values.ToArray();
        }

        private static double MeanAutotestLuminance(Color32[] pixels, int width, int height, Rect rect)
        {
            double[] values = SampleAutotestY(pixels, width, height, rect, default(Rect), 1200);
            if (values.Length == 0) return double.NaN;
            double sum = 0.0;
            foreach (double value in values) sum += value;
            return sum / values.Length;
        }

        private static void MeanAutotestLinearRgb(Color32[] pixels, int width, int height, Rect rect, Rect exclude,
            out double r, out double g, out double b)
        {
            r = g = b = 0.0;
            int x0 = Mathf.Clamp(Mathf.FloorToInt(rect.xMin), 0, width), x1 = Mathf.Clamp(Mathf.CeilToInt(rect.xMax), 0, width);
            int y0 = Mathf.Clamp(Mathf.FloorToInt(rect.yMin), 0, height), y1 = Mathf.Clamp(Mathf.CeilToInt(rect.yMax), 0, height);
            if (x1 <= x0 || y1 <= y0) return;
            int stride = Math.Max(1, (int)Math.Ceiling(Math.Sqrt((x1 - x0) * (y1 - y0) / 1200.0)));
            int count = 0;
            for (int y = y0; y < y1; y += stride)
            {
                for (int x = x0; x < x1; x += stride)
                {
                    if (x >= exclude.xMin && x < exclude.xMax && y >= exclude.yMin && y < exclude.yMax) continue;
                    Color32 c = pixels[y * width + x];
                    r += AutotestLinear[c.r];
                    g += AutotestLinear[c.g];
                    b += AutotestLinear[c.b];
                    count++;
                }
            }
            if (count == 0) return;
            r /= count;
            g /= count;
            b /= count;
        }

        private static Texture2D DownsampleAutotestShot(Color32[] pixels, int width, int height)
        {
            int w = Math.Max(1, width / 2), h = Math.Max(1, height / 2);
            var result = new Color32[w * h];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int sx = Math.Min(width - 1, x * 2), sy = Math.Min(height - 1, y * 2);
                    int sx1 = Math.Min(width - 1, sx + 1), sy1 = Math.Min(height - 1, sy + 1);
                    Color32 a = pixels[sy * width + sx], b = pixels[sy * width + sx1], c = pixels[sy1 * width + sx], d = pixels[sy1 * width + sx1];
                    result[y * w + x] = new Color32((byte)((a.r + b.r + c.r + d.r) / 4), (byte)((a.g + b.g + c.g + d.g) / 4),
                        (byte)((a.b + b.b + c.b + d.b) / 4), 255);
                }
            }
            var texture = new Texture2D(w, h, TextureFormat.RGB24, false);
            texture.SetPixels32(result);
            texture.Apply(false);
            return texture;
        }

        private static string AutotestFileName(string value)
        {
            var builder = new StringBuilder(value.Length);
            foreach (char c in value)
                builder.Append(char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_');
            string result = builder.ToString();
            return result.Length <= 120 ? result : result.Substring(0, 120);
        }

        private static string AutotestRectText(Rect rect)
        {
            return rect.xMin.ToString("F0", CultureInfo.InvariantCulture) + "," + rect.yMin.ToString("F0", CultureInfo.InvariantCulture)
                + "," + rect.width.ToString("F0", CultureInfo.InvariantCulture) + "x" + rect.height.ToString("F0", CultureInfo.InvariantCulture);
        }

        #endregion
    }
}
#endif
