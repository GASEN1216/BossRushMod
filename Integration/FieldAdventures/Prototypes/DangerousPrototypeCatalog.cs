namespace BossRush
{
    /// <summary>每种试用品的双语名称、简介与开工说明，对话和百科共用一份。</summary>
    internal static class DangerousPrototypeCatalog
    {
        internal static string Name(DangerousPrototypeKind kind)
        {
            switch (kind)
            {
                case DangerousPrototypeKind.JetCannon: return L10n.T("倒鸭一号·超载", "Duckback Mk.I Overdrive");
                case DangerousPrototypeKind.RicochetSun: return L10n.T("折射太阳·暴走", "Ricochet Sun Unbound");
                case DangerousPrototypeKind.QuackRail: return L10n.T("嘎嘎轨道炮", "Quackrail Cannon");
                case DangerousPrototypeKind.Thunderbill: return L10n.T("雷霆鸭嘴", "Thunderbill");
                case DangerousPrototypeKind.GrandFinale: return L10n.T("压轴大烟花", "Grand Finale");
                default: return L10n.T("试用品", "Prototype");
            }
        }

        internal static string Brief(DangerousPrototypeKind kind)
        {
            switch (kind)
            {
                case DangerousPrototypeKind.JetCannon:
                    return L10n.T("巨炮每 2 秒蓄满，下一枪喷出 26 米、两米多粗的炎流，每个敌人吃 140 点，你会被往后推出一大截。150 秒内用炎流命中 6 次。",
                        "Charges every 2 seconds. Your next shot vents a 26-metre jet over two metres wide for 140 per enemy, and throws you well back. Land 6 jet hits within 150 seconds.");
                case DangerousPrototypeKind.RicochetSun:
                    return L10n.T("每 1.4 秒蓄满，光束最多弹墙 5 次、射程 60 米。直击只有 30，弹墙后 160 起步，每多弹一次再加 25。150 秒内取得 3 次弹墙命中。",
                        "Charges every 1.4 seconds. The beam bounces off walls up to 5 times across 60 metres. Direct hits do 30; bounced hits start at 160 and gain 25 per extra bounce. Land 3 bounced hits within 150 seconds.");
                case DangerousPrototypeKind.QuackRail:
                    return L10n.T("每 3.5 秒蓄满，下一枪射出贯穿一整排敌人的轨道炮，70 米内每个敌人 260 点，遇墙才停，后坐力很猛。150 秒内命中 6 次。",
                        "Charges every 3.5 seconds. Your next shot fires a rail slug through every enemy in a 70-metre line for 260 each, stopping only at walls, with brutal recoil. Land 6 hits within 150 seconds.");
                case DangerousPrototypeKind.Thunderbill:
                    return L10n.T("每 1.2 秒蓄满，下一枪劈出一道连锁闪电，最多连 7 个敌人，第一下 70 点，往后每跳少 6 点。150 秒内累计 12 跳。",
                        "Charges every 1.2 seconds. Your next shot cracks a chain of lightning through up to 7 enemies, 70 on the first and 6 less per jump. Rack up 12 jumps within 150 seconds.");
                case DangerousPrototypeKind.GrandFinale:
                    return L10n.T("每 3 秒蓄满，下一枪朝准星抛出一枚大烟花，落地炸 110 点，再分出 12 颗子弹各炸 45 点。150 秒内子弹或主弹命中 10 次。",
                        "Charges every 3 seconds. Your next shot lobs a huge firework at your aim point: 110 on landing, then 12 bomblets for 45 each. Land 10 hits within 150 seconds.");
                default: return string.Empty;
            }
        }
    }
}
