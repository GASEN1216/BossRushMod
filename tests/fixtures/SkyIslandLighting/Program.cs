using System;
using BossRush;

namespace BossRush { internal static class L10n { internal static string T(string zh, string en) { return zh; } } }

internal static class Program
{
    private static int count;
    private static void Check(bool value, string name)
    {
        if (!value) throw new Exception(name);
        count++;
    }

    private static double[] Weights(double hour)
    {
        int first, second; float blend;
        SkyIslandLighting.ResolveTimeBlend(hour, out first, out second, out blend);
        Check(first >= 0 && first < 4 && second >= 0 && second < 4 && blend >= 0 && blend <= 1, "合法插值范围");
        var weights = new double[4];
        weights[first] += 1 - blend; weights[second] += blend;
        return weights;
    }

    private static void Main()
    {
        Check(Weights(0)[2] == 1 && Weights(24)[2] == 1, "午夜跨日恒为星夜");
        Check(Weights(7)[3] == 1 && Weights(12)[0] == 1 && Weights(19)[1] == 1, "四时段锚点");
        Check(Math.Abs(Weights(6)[2] - .5) < 1e-6 && Math.Abs(Weights(6)[3] - .5) < 1e-6, "黎明中点");
        Check(Weights(double.NaN)[0] == 1 && Weights(double.PositiveInfinity)[0] == 1, "异常时钟回退晴昼");
        Check(Weights(-1)[2] == 1 && Weights(31)[3] == 1, "负数与跨日归一");
        foreach (double boundary in new double[] { 0, 5, 7, 10, 16, 19, 21, 24 })
        {
            double[] before = Weights(boundary - .0001), after = Weights(boundary + .0001);
            for (int i = 0; i < 4; i++) Check(Math.Abs(before[i] - after[i]) < .00001, "时段边界必须连续 " + boundary);
        }
        // 内容批次四：判夜收成一个口径（SkyIslandNight），光照的星夜整档、夜风与云蚋逐点一致。
        for (double hour = -2; hour <= 26; hour += 0.25)
        {
            int first, second; float blend;
            SkyIslandLighting.ResolveTimeBlend(hour, out first, out second, out blend);
            Check(SkyIslandNight.IsNight(hour) == (first == 2 && second == 2), "星夜整档与判夜唯一口径一致 " + hour);
        }
        // 官方 GameClock 没有实例时 TimeOfDay 恒为 00:00：读数改成 NaN 之后光照回退晴昼，不再整趟星夜。
        Check(Weights(SkyIslandNight.EffectiveHours(false, 0))[0] == 1 && !SkyIslandNight.IsNight(SkyIslandNight.EffectiveHours(false, 0)),
            "没有时钟实例时不算夜里、光照回退晴昼");
        SkyIslandNight.DevForceNight = true;
        Check(Weights(SkyIslandNight.EffectiveHours(true, 12))[2] == 1, "开发构建强制夜里时光照走星夜");
        SkyIslandNight.ResetStaticCaches();
        Check(Weights(SkyIslandNight.EffectiveHours(true, 12))[0] == 1, "模块销毁复位强制夜里");
        Console.WriteLine("SkyIslandLighting production-linked PASS assertions=" + count);
    }
}
