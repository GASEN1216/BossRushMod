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
        }

        private struct AutotestWorldProbe
        {
            internal string Target, Name;
            internal Rect Screen;
        }

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
            int inspected = CollectAutotestTextProbes(canvasFilter, contrastPaths, texts, overflowing, truncated);
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
            try
            {
                texture = ScreenCapture.CaptureScreenshotAsTexture();
                Color32[] pixels = texture.GetPixels32();
                var analysis = new AutotestShotAnalysis { Width = texture.width, Height = texture.height };
                AnalyzeAutotestText(pixels, analysis, texts, contrastPaths);
                string overflowMetrics, overflowReason;
                string overflowResult = F3AutotestJudges.JudgeTextOverflow(overflowing, truncated, inspected, out overflowMetrics, out overflowReason);
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
                shot.Metrics = "size=" + analysis.Width + "x" + analysis.Height + ",texts=" + inspected + ",contrast_probes=" + analysis.Contrast.Count
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
            List<string> overflowing, List<string> truncated)
        {
            string[] roots = string.IsNullOrEmpty(canvasFilter) ? new[] { "SkyIslandHud", "SkyIslandStory" } : canvasFilter.Split('+');
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
                        if (bounds.size.x > 0f && (bounds.min.x < box.xMin - 2f || bounds.max.x > box.xMax + 2f
                            || bounds.min.y < box.yMin - 2f || bounds.max.y > box.yMax + 2f)) overflowing.Add(path);
                    }
                    else if (text.isTextTruncated || text.isTextOverflowing) truncated.Add(path);
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
                    probes.Add(new AutotestTextProbe { Requested = want, Path = path, Screen = screen, Declared = declared });
                }
            }
            return inspected;
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
                float pad = Mathf.Clamp(Mathf.Min(inner.width, inner.height) * 0.35f, 4f, 14f);
                double[] inside = SampleAutotestY(pixels, analysis.Width, analysis.Height, inner, default(Rect), 2400);
                Rect outer = AutotestExpand(inner, 2f + pad);
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
                    found = true;
                }
                if (found) probes.Add(best);
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
                if (renderer == null || !renderer.enabled) continue;
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
                Rect inner = probe.Screen;
                float pad = Mathf.Max(10f, Mathf.Max(inner.width, inner.height) * 0.5f);
                double[] objectY = SampleAutotestY(pixels, analysis.Width, analysis.Height, inner, default(Rect), 3000);
                double[] neighborY = SampleAutotestY(pixels, analysis.Width, analysis.Height, AutotestExpand(inner, pad), AutotestExpand(inner, 2f), 3000);
                double weber;
                string metrics, reason;
                string result = F3AutotestJudges.JudgeWorldVisibility(objectY, neighborY, 0.0, out weber, out metrics, out reason);
                analysis.Visibility[probe.Target] = new AutotestMeasure
                {
                    Result = result, Reason = reason, Value = weber,
                    Metrics = "object=" + probe.Name + ",rect=" + AutotestRectText(inner) + "," + metrics
                };
            }
        }

        #endregion

        #region 像素

        private static Rect AutotestExpand(Rect rect, float amount)
        {
            return Rect.MinMaxRect(rect.xMin - amount, rect.yMin - amount, rect.xMax + amount, rect.yMax + amount);
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
