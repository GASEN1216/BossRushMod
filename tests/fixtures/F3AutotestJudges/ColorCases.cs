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

    private static void VisibilityCases()
    {
        double weber;
        string metrics, reason;
        double[] ground = Fill(16, 0.1);
        string result = F3AutotestJudges.JudgeWorldVisibility(Concat(Fill(4, 0.6), Fill(16, 0.1)), ground, 0.15, out weber, out metrics, out reason);
        Check(result == "PASS" && reason == null && Near(weber, 0.5 / 0.15, 1e-9), "bright object on darker surroundings PASS: " + metrics);
        Check(metrics.Contains("wcag=") && metrics.Contains("min_weber=0.15") && metrics.Contains("object_samples=20"), "visibility metrics carry wcag, minimum and counts");
        result = F3AutotestJudges.JudgeWorldVisibility(Concat(Fill(15, 0.6), Fill(85, 0.1)), ground, 0.15, out weber, out metrics, out reason);
        Check(result == "PASS" && Near(weber, 0.5 / 0.15, 1e-9), "a small bright ring inside a large box is not diluted by the ground (" + weber + ")");
        result = F3AutotestJudges.JudgeWorldVisibility(Fill(8, 0.05), Fill(16, 0.5), 0.15, out weber, out metrics, out reason);
        Check(result == "PASS" && Near(weber, 0.45 / 0.55, 1e-9), "a dark object on a bright neighbourhood also counts");

        result = F3AutotestJudges.JudgeWorldVisibility(Fill(20, 0.1), ground, 0.15, out weber, out metrics, out reason);
        Check(result == "FAIL" && reason == "visibility_below_min" && weber == 0.0, "red: object as bright as its neighbourhood FAIL");
        result = F3AutotestJudges.JudgeWorldVisibility(Fill(20, 0.11), ground, 0.15, out weber, out metrics, out reason);
        Check(result == "FAIL" && Near(weber, 0.01 / 0.15, 1e-9), "red: barely brighter object FAIL with a low Weber value (" + weber + ")");

        result = F3AutotestJudges.JudgeWorldVisibility(Fill(3, 0.6), ground, 0.15, out weber, out metrics, out reason);
        Check(result == "SKIP" && reason == "target_not_on_screen" && weber == 0.0, "fewer than four object samples SKIP");
        Check(F3AutotestJudges.JudgeWorldVisibility(Fill(20, 0.6), Fill(7, 0.1), 0.15, out weber, out metrics, out reason) == "SKIP", "fewer than eight neighbour samples SKIP");
        Check(F3AutotestJudges.JudgeWorldVisibility(new double[0], null, 0.15, out weber, out metrics, out reason) == "SKIP" && metrics == "object_samples=0,neighbor_samples=0",
            "empty samples SKIP");

        result = F3AutotestJudges.JudgeTextOverflow(new List<string>(), new List<string>(), 0, out metrics, out reason);
        Check(result == "SKIP" && reason == "no_visible_text", "overflow with nothing inspected SKIP");
        result = F3AutotestJudges.JudgeTextOverflow(new List<string> { "Panel/Title" }, null, 12, out metrics, out reason);
        Check(result == "FAIL" && reason == "text_draws_outside_its_box" && metrics.Contains("overflow_list=Panel/Title"), "red: text drawn outside its box FAIL");
        var truncated = new List<string>();
        for (int i = 0; i < 10; i++) truncated.Add("Row" + i);
        result = F3AutotestJudges.JudgeTextOverflow(null, truncated, 12, out metrics, out reason);
        Check(result == "PASS" && metrics.Contains("truncated=10") && metrics.Contains("truncated_list=Row0+") && metrics.Contains("+…2"), "ellipsis truncation is listed, not red: " + metrics);
    }
}
