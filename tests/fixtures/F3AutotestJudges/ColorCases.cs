using System;
using System.Collections.Generic;
using BossRush;

/// <summary>4 / 5. 线性色彩空间的对比度、截图可见度与文字溢出判据（合成像素亮度样本）。</summary>
internal static partial class Program
{
    private static double[] Fill(int count, double value)
    {
        var values = new double[count];
        for (int i = 0; i < count; i++) values[i] = value;
        return values;
    }

    private static double[] Concat(params double[][] parts)
    {
        var all = new List<double>();
        foreach (double[] part in parts) all.AddRange(part);
        return all.ToArray();
    }

    private static double Mean(double[] values)
    {
        double sum = 0.0;
        foreach (double value in values) sum += value;
        return sum / values.Length;
    }

    private static void ContrastCases()
    {
        Check(F3AutotestJudges.SrgbToLinear(0.0) == 0.0, "SrgbToLinear(0) = 0");
        Check(Near(F3AutotestJudges.SrgbToLinear(1.0), 1.0, 1e-12), "SrgbToLinear(1) = 1");
        Check(Near(F3AutotestJudges.SrgbToLinear(0.5), 0.2140, 1e-4) && Near(F3AutotestJudges.SrgbToLinear(0.5), 0.21404114, 1e-7), "SrgbToLinear(0.5) ≈ 0.2140");
        Check(Near(F3AutotestJudges.SrgbToLinear(0.04), 0.04 / 12.92, 1e-12), "values below 0.04045 use the linear segment");
        Check(F3AutotestJudges.SrgbToLinear(double.NaN) == 0.0 && F3AutotestJudges.SrgbToLinear(-1.0) == 0.0 && F3AutotestJudges.SrgbToLinear(2.0) == 1.0,
            "NaN and out-of-range inputs are clamped");
        Check(Near(F3AutotestJudges.LuminanceLinear(1, 0, 0), 0.2126, 1e-12) && Near(F3AutotestJudges.LuminanceLinear(0, 1, 0), 0.7152, 1e-12)
            && Near(F3AutotestJudges.LuminanceLinear(0, 0, 1), 0.0722, 1e-12), "relative luminance weights");
        Check(Near(F3AutotestJudges.LuminanceSrgb(1, 1, 1), 1.0, 1e-12) && F3AutotestJudges.LuminanceSrgb(0, 0, 0) == 0.0, "white Y=1, black Y=0");
        Check(Near(F3AutotestJudges.ContrastRatio(1.0, 0.0), 21.0, 1e-9) && Near(F3AutotestJudges.ContrastRatio(0.0, 1.0), 21.0, 1e-9), "white on black is 21:1 either order");
        Check(Near(F3AutotestJudges.ContrastRatio(0.18, 0.18), 1.0, 1e-12), "equal luminance is 1:1");

        // 半透明合成发生在线性光里：白字 50% 压在黑底上 Y=0.5；在 sRGB 值上混合会得到 sRGB 0.5 → Y≈0.214。
        double linearMix = F3AutotestJudges.CompositeLuminance(1, 1, 1, 0.5, 0, 0, 0);
        double srgbMix = F3AutotestJudges.LuminanceSrgb(0.5, 0.5, 0.5);
        Check(Near(linearMix, 0.5, 1e-12), "50% white over black composites to Y=0.5 in linear light");
        Check(Math.Abs(linearMix - srgbMix) > 0.25, "linear compositing differs from sRGB-space mixing (" + linearMix + " vs " + srgbMix + ")");
        Check(Near(F3AutotestJudges.CompositeLuminance(0, 0, 0, 0.5, 0.5, 0.5, 0.5), 0.25, 1e-12), "background is taken as linear, not converted again");
        Check(Near(F3AutotestJudges.CompositeLuminance(0.5, 0.5, 0.5, 1.0, 1, 1, 1), 0.21404114, 1e-6), "opaque foreground ignores the background");
        Check(Near(F3AutotestJudges.CompositeLuminance(1, 1, 1, 2.0, 0, 0, 0), 1.0, 1e-12) && Near(F3AutotestJudges.CompositeLuminance(1, 1, 1, -1.0, 0.3, 0.3, 0.3), 0.3, 1e-12),
            "alpha is clamped to 0..1");

        double measured;
        string metrics, reason;
        double[] darkRing = Fill(16, 0.02);
        // 40 个字内像素：30 个近底色的抗锯齿、5 个半亮、5 个字心。取离底色最远的 max(4, ceil(40×12%)) = 5 个。
        double[] brightText = Concat(Fill(30, 0.03), Fill(5, 0.4), Fill(5, 0.9));
        double expected = (0.9 + 0.05) / (0.02 + 0.05);
        string result = F3AutotestJudges.JudgeTextContrast(darkRing, brightText, 0.9, 4.5, out measured, out metrics, out reason);
        Check(result == "PASS" && reason == null && Near(measured, expected, 1e-9), "bright text on dark ground PASS: " + metrics);
        Check(metrics.Contains("ring_samples=16") && metrics.Contains("inside_samples=40") && !metrics.Contains("declared=n/a"), "contrast metrics carry sample counts and declared ratio");
        Check(F3AutotestJudges.ContrastRatio(Mean(brightText), 0.02) < measured - 5, "the judge uses the text-most pixels, not the antialiased mean");
        F3AutotestJudges.JudgeTextContrast(Concat(Fill(15, 0.02), new[] { 0.95 }), brightText, double.NaN, 4.5, out measured, out metrics, out reason);
        Check(Near(measured, expected, 1e-9) && metrics.Contains("declared=n/a"), "background is the ring median (one bright outlier ignored); NaN declared reads n/a");
        F3AutotestJudges.JudgeTextContrast(darkRing, new[] { 0.9, 0.5, 0.5, 0.5, 0.02, 0.02, 0.02, 0.02 }, 0.9, 1.0, out measured, out metrics, out reason);
        Check(Near(measured, (0.6 + 0.05) / 0.07, 1e-9), "at least four text pixels are averaged: " + measured);
        Check(F3AutotestJudges.JudgeTextContrast(darkRing, brightText, 0.9, expected, out measured, out metrics, out reason) == "PASS", "a ratio exactly at the minimum passes");

        result = F3AutotestJudges.JudgeTextContrast(Fill(16, 0.2), Concat(Fill(34, 0.21), Fill(6, 0.25)), 0.25, 4.5, out measured, out metrics, out reason);
        Check(result == "FAIL" && reason == "contrast_below_min" && Near(measured, 0.30 / 0.25, 1e-9), "red: grey text on grey ground FAIL (" + measured + ")");
        result = F3AutotestJudges.JudgeTextContrast(Fill(7, 0.02), brightText, 0.9, 4.5, out measured, out metrics, out reason);
        Check(result == "SKIP" && reason == "too_few_samples" && measured == 0.0, "too few ring samples SKIP, not PASS");
        result = F3AutotestJudges.JudgeTextContrast(darkRing, Fill(7, 0.9), 0.9, 4.5, out measured, out metrics, out reason);
        Check(result == "SKIP" && reason == "too_few_samples", "too few inside samples SKIP");
        Check(F3AutotestJudges.JudgeTextContrast(null, null, 0.9, 4.5, out measured, out metrics, out reason) == "SKIP", "missing samples SKIP");
    }

    private static double[] RepeatRgb(int count, double r, double g, double b)
    {
        var values = new double[count * 3];
        for (int i = 0; i < count; i++)
        {
            values[i * 3] = r;
            values[i * 3 + 1] = g;
            values[i * 3 + 2] = b;
        }
        return values;
    }

    /// <summary>地面圆环沿环带的覆盖率（2026-09-15 第四轮：广场上被台面盖成碎弧的撤离环，旧亮度口径照样 PASS）。</summary>
    private static void RingCoverageCases()
    {
        double coverage;
        string metrics, reason;
        // 线性 RGB：日照石面偏米黄，撤离环青绿；环带像素是环色与石面各半。
        double[] stone = RepeatRgb(32, 0.30, 0.25, 0.20);
        const double ringR = 0.05, ringG = 0.45, ringB = 0.45;
        double[] ringOnStone = RepeatRgb(32, 0.175, 0.35, 0.325);
        string result = F3AutotestJudges.JudgeRingCoverage(ringOnStone, stone, ringR, ringG, ringB, 0.02, out coverage, out metrics, out reason);
        Check(result == "PASS" && reason == null && Near(coverage, 1.0, 1e-12), "a fully drawn ring covers every on-screen segment: " + metrics);
        Check(metrics.Contains("mode=ring_coverage") && metrics.Contains("samples=32") && metrics.Contains("visible=32"),
            "ring metrics carry mode, samples and visible count");

        result = F3AutotestJudges.JudgeRingCoverage(stone, stone, ringR, ringG, ringB, 0.02, out coverage, out metrics, out reason);
        Check(result == "PASS" && coverage == 0.0, "red: a ring buried under the plaza floor has zero coverage (" + coverage + ")");

        var dashes = new double[96];
        Array.Copy(ringOnStone, 0, dashes, 0, 48);
        Array.Copy(stone, 48, dashes, 48, 48);
        F3AutotestJudges.JudgeRingCoverage(dashes, stone, ringR, ringG, ringB, 0.02, out coverage, out metrics, out reason);
        Check(Near(coverage, 0.5, 1e-12), "a ring broken into dashes reports only its visible fraction: " + coverage);

        F3AutotestJudges.JudgeRingCoverage(RepeatRgb(32, 0.60, 0.50, 0.40), stone, ringR, ringG, ringB, 0.02, out coverage, out metrics, out reason);
        Check(coverage == 0.0, "red: sunlit stone brighter than its surroundings is not a ring (the old luminance probe passed this)");

        F3AutotestJudges.JudgeRingCoverage(RepeatRgb(32, 0.29, 0.254, 0.205), stone, ringR, ringG, ringB, 0.02, out coverage, out metrics, out reason);
        Check(coverage == 0.0, "a shift toward the ring colour below min_shift does not count");

        result = F3AutotestJudges.JudgeRingCoverage(ringOnStone, ringOnStone, 0.175, 0.35, 0.325, 0.02, out coverage, out metrics, out reason);
        Check(result == "PASS" && coverage == 0.0, "a ring the same colour as its neighbourhood is not visible");

        result = F3AutotestJudges.JudgeRingCoverage(RepeatRgb(15, 0.175, 0.35, 0.325), RepeatRgb(15, 0.30, 0.25, 0.20), ringR, ringG, ringB, 0.02,
            out coverage, out metrics, out reason);
        Check(result == "SKIP" && reason == "ring_mostly_off_screen" && coverage == 0.0, "fewer than 16 on-screen segments SKIP");
        Check(F3AutotestJudges.JudgeRingCoverage(null, stone, ringR, ringG, ringB, 0.02, out coverage, out metrics, out reason) == "SKIP",
            "missing ring samples SKIP");
    }

    private static void VisibilityCases()
    {
        double weber;
        string metrics, reason;
        double[] ground = Fill(16, 0.1);
        const double whole = 1.0, wide = 60.0, none = double.NaN;
        string result = F3AutotestJudges.JudgeWorldVisibility(Concat(Fill(4, 0.6), Fill(16, 0.1)), ground, 0.15, whole, wide, none, none, out weber, out metrics, out reason);
        Check(result == "PASS" && reason == null && Near(weber, 0.5 / 0.15, 1e-9), "bright object on darker surroundings PASS: " + metrics);
        Check(metrics.Contains("wcag=") && metrics.Contains("min_weber=0.15") && metrics.Contains("object_samples=20") && metrics.Contains("on_screen=1")
            && metrics.Contains("target_px=60") && !metrics.Contains("chroma="), "visibility metrics carry wcag, minimum, counts and framing; chroma only when asked");
        result = F3AutotestJudges.JudgeWorldVisibility(Concat(Fill(15, 0.6), Fill(85, 0.1)), ground, 0.15, whole, wide, none, none, out weber, out metrics, out reason);
        Check(result == "PASS" && Near(weber, 0.5 / 0.15, 1e-9), "a small bright ring inside a large box is not diluted by the ground (" + weber + ")");
        result = F3AutotestJudges.JudgeWorldVisibility(Fill(8, 0.05), Fill(16, 0.5), 0.15, whole, wide, none, none, out weber, out metrics, out reason);
        Check(result == "PASS" && Near(weber, 0.45 / 0.55, 1e-9), "a dark object on a bright neighbourhood also counts");

        result = F3AutotestJudges.JudgeWorldVisibility(Fill(20, 0.1), ground, 0.15, whole, wide, none, none, out weber, out metrics, out reason);
        Check(result == "FAIL" && reason == "visibility_below_min" && weber == 0.0, "red: object as bright as its neighbourhood FAIL");
        result = F3AutotestJudges.JudgeWorldVisibility(Fill(20, 0.11), ground, 0.15, whole, wide, none, none, out weber, out metrics, out reason);
        Check(result == "FAIL" && Near(weber, 0.01 / 0.15, 1e-9), "red: barely brighter object FAIL with a low Weber value (" + weber + ")");

        result = F3AutotestJudges.JudgeWorldVisibility(Fill(3, 0.6), ground, 0.15, whole, wide, none, none, out weber, out metrics, out reason);
        Check(result == "SKIP" && reason == "target_not_on_screen" && weber == 0.0, "fewer than four object samples SKIP");
        Check(F3AutotestJudges.JudgeWorldVisibility(Fill(20, 0.6), Fill(7, 0.1), 0.15, whole, wide, none, none, out weber, out metrics, out reason) == "SKIP", "fewer than eight neighbour samples SKIP");
        Check(F3AutotestJudges.JudgeWorldVisibility(new double[0], null, 0.15, whole, wide, none, none, out weber, out metrics, out reason) == "SKIP" && metrics == "object_samples=0,neighbor_samples=0",
            "empty samples SKIP");

        // 2026-09-15 第五轮：投影框大半在画面外（A2 11 m 只剩上沿一条）、目标只有十来个像素（810×540 下的云蚋）是拍法问题，记 SKIP，不判红也不判绿。
        result = F3AutotestJudges.JudgeWorldVisibility(Fill(20, 0.6), ground, 0.15, 0.3, wide, none, none, out weber, out metrics, out reason);
        Check(result == "SKIP" && reason == "target_mostly_off_screen" && metrics.Contains("on_screen=0.3"), "mostly off-screen target SKIP, even when bright");
        result = F3AutotestJudges.JudgeWorldVisibility(Fill(20, 0.1), ground, 0.15, whole, 16.0, none, none, out weber, out metrics, out reason);
        Check(result == "SKIP" && reason == "target_too_small_on_screen" && metrics.Contains("target_px=16"), "target a dozen pixels wide SKIP instead of red");
        result = F3AutotestJudges.JudgeWorldVisibility(Fill(20, 0.1), ground, 0.15, F3AutotestJudges.WorldProbeMinOnScreen, F3AutotestJudges.WorldProbeMinPixels,
            none, none, out weber, out metrics, out reason);
        Check(result == "FAIL", "exactly at the framing limits is still judged");

        // 光斑靠色相：亮度与地面一样、色度偏移够就算看得见；色度也不够照样红。
        result = F3AutotestJudges.JudgeWorldVisibility(Fill(20, 0.1), ground, 0.15, whole, wide, 0.18, F3AutotestJudges.GlowMinChromaShift, out weber, out metrics, out reason);
        Check(result == "PASS" && weber == 0.0 && metrics.Contains("chroma=0.18") && metrics.Contains("min_chroma=0.12"), "warm glow as bright as sunlit sand PASS on chroma: " + metrics);
        result = F3AutotestJudges.JudgeWorldVisibility(Fill(20, 0.1), ground, 0.15, whole, wide, 0.08, F3AutotestJudges.GlowMinChromaShift, out weber, out metrics, out reason);
        Check(result == "FAIL" && reason == "visibility_below_min", "red: neither brightness nor chroma stands out FAIL");
        Check(F3AutotestJudges.VisibilityMet(0.2, 0.1, none, none) && !F3AutotestJudges.VisibilityMet(0.05, 0.1, 0.5, none)
            && !F3AutotestJudges.VisibilityMet(0.05, 0.1, none, 0.12) && F3AutotestJudges.VisibilityMet(0.05, 0.1, 0.12, 0.12),
            "VisibilityMet: brightness, or chroma only when a chroma threshold is given");

        // 色度偏移：邻域是暖白砂岩，物体框里一小块偏橙；整块平均会被地面稀释，前 15% 不会。
        double[] sand = RepeatRgb(16, 0.60, 0.55, 0.45);
        double[] glowPatch = Concat(RepeatRgb(6, 0.70, 0.45, 0.20), RepeatRgb(34, 0.60, 0.55, 0.45));
        double shift = F3AutotestJudges.ChromaShift(glowPatch, sand);
        Check(Near(shift, 0.191, 0.005) && shift > F3AutotestJudges.GlowMinChromaShift, "small warm patch on sand shows a chroma shift (" + shift + ")");
        Check(F3AutotestJudges.ChromaShift(RepeatRgb(40, 0.60, 0.55, 0.45), sand) < 1e-9, "same colour: zero chroma shift");
        Check(F3AutotestJudges.ChromaShift(RepeatRgb(40, 0.30, 0.275, 0.225), sand) < 0.02, "same hue, only darker: brightness is not chroma");
        Check(double.IsNaN(F3AutotestJudges.ChromaShift(RepeatRgb(3, 0.7, 0.45, 0.2), sand))
            && double.IsNaN(F3AutotestJudges.ChromaShift(glowPatch, RepeatRgb(7, 0.6, 0.55, 0.45))), "too few samples: NaN");

        result = F3AutotestJudges.JudgeTextOverflow(new List<string>(), new List<string>(), null, 0, out metrics, out reason);
        Check(result == "SKIP" && reason == "no_visible_text", "overflow with nothing inspected SKIP");
        result = F3AutotestJudges.JudgeTextOverflow(new List<string> { "Panel/Title" }, null, null, 12, out metrics, out reason);
        Check(result == "FAIL" && reason == "text_draws_outside_its_box" && metrics.Contains("overflow_list=Panel/Title"), "red: text drawn outside its box FAIL");
        var truncated = new List<string>();
        for (int i = 0; i < 10; i++) truncated.Add("Row" + i);
        result = F3AutotestJudges.JudgeTextOverflow(null, truncated, null, 12, out metrics, out reason);
        Check(result == "PASS" && metrics.Contains("truncated=10") && metrics.Contains("truncated_list=Row0+") && metrics.Contains("+…2"), "ellipsis truncation is listed, not red: " + metrics);
        // 2026-09-15 第五轮：字幕封顶两行，却只排出一行就省略号（7 条长字幕丢了后半句），一直自动绿。
        result = F3AutotestJudges.JudgeTextOverflow(null, truncated, new List<string> { "SkyIslandHud/SkyIslandCaption/Text" }, 12, out metrics, out reason);
        Check(result == "FAIL" && reason == "text_truncated_before_its_line_budget" && metrics.Contains("truncated_early=1")
            && metrics.Contains("truncated_early_list=SkyIslandHud/SkyIslandCaption/Text"), "red: caption truncated at one line FAIL");
    }
}
