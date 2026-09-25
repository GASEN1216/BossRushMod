// ============================================================================
// PetNestChroma.cs - 遗种巢「炫彩 / 异色」判据与调色板（实施：2026-09-20 owner 需求 13/14）
// ============================================================================
// owner 口径：
//   - 异色（shiny）**极为稀有**，是最豪华的一档；
//   - 炫彩（chroma）由**任意两种颜色**搭配而成，比异色常见得多；
//   - 名字前面要带上搭配名，例如黑白炫彩显示成「黑白 - 阿花」；
//   - 黑白这类要做成渐变色，但**必须保证字能看见**（AGENTS 4.14：正文对比度不低于 4.5:1）。
//
// 本文件是这套外观的唯一判据来源：
//   - 调色板（id / 中英文名 / 世界空间粒子色 / 面板可读文字色）；
//   - roll（孵化时一次性定死，和异色、性格、天赋同层，不可洗）；
//   - 名字装饰（富文本，面板与演出共用同一份，避免两处各写一套）。
//
// 硬约束：
//   - **零 Unity 依赖**：粒子色用 (r,g,b) 三个 float 描述，转 Color 由表现层自己做。
//     这样本文件可以和 PetNestModels 一样被 guard / 离线夹具直接推理。
//   - 文字色是**实测可读的那一档**，不是「颜色本身」：黑在深色面板上是看不见的，
//     所以 Black 的文字色取深灰（#858B95，对卡片底约 5.2:1）。2026-09-22 之前取的是浅灰 #B9BEC6，
//     和白几乎一样亮，「黑白」渐变看上去就是一片白（owner 复查原需求时指出）；压到仍过 4.5:1 的
//     最深一档，黑白两端才看得出明暗。粒子色才用真实颜色。
//   - 调色板 id 是**存档兼容面**：发布后只增不改、不复用（存进 PetNestPetRecord.chromaA/B）。
// ============================================================================

using System;
using System.Collections.Generic;

namespace BossRush
{
    /// <summary>一种炫彩颜色。粒子色与文字色分开：文字色是保证可读的那一档。</summary>
    internal sealed class PetNestChromaColor
    {
        /// <summary>稳定 id（存档面，冻结）。</summary>
        internal string Id;
        /// <summary>中文单字名（拼出「黑白」这样的搭配名）。</summary>
        internal string NameCN;
        /// <summary>英文名。</summary>
        internal string NameEN;
        /// <summary>粒子色（真实颜色，0..1）。</summary>
        internal float ParticleR, ParticleG, ParticleB;
        /// <summary>面板文字色（深色面板上可读的那一档，#RRGGBB）。</summary>
        internal string TextHex;
    }

    /// <summary>炫彩 / 异色的调色板、roll 与名字装饰。静态、无状态、无 Unity 依赖。</summary>
    internal static class PetNestChroma
    {
        #region 调色板（id 冻结）

        /// <summary>
        /// 十色调色板。任意两种不同颜色可组成一种炫彩，共 45 种搭配。
        /// TextHex 全部经过「深色面板（BossRushUIColors.Surface）上正文 ≥ 4.5:1」的挑选：
        /// 黑与蓝、紫这类深色**不能**按本色写，否则在面板上就是一团看不见的字。
        /// </summary>
        private static readonly PetNestChromaColor[] Palette =
        {
            Make("black",  "黑", "Black",   0.08f, 0.08f, 0.10f, "#858B95"),
            // 白：略带冰蓝，和蓝色搭配时能看到两端的层次，不是一整条纯白。
            Make("white",  "白", "White",   0.96f, 0.985f, 1.00f, "#F8FCFF"),
            Make("red",    "赤", "Crimson", 0.95f, 0.22f, 0.22f, "#FF8A80"),
            Make("orange", "橙", "Amber",   1.00f, 0.55f, 0.12f, "#FFB74D"),
            Make("yellow", "黄", "Gold",    1.00f, 0.88f, 0.25f, "#FFE066"),
            // 绿：把蓝绿色相压开，粒子更像荧光叶片，文字仍保持深色面板上的 4.5:1 对比。
            Make("green",  "绿", "Verdant", 0.20f, 0.95f, 0.50f, "#78E8A0"),
            Make("cyan",   "青", "Cyan",    0.25f, 0.88f, 0.90f, "#7DE3E8"),
            // 蓝：提高青蓝通道并保留深蓝核心，渐变时不会发灰或变成普通薄荷色。
            Make("blue",   "蓝", "Azure",   0.16f, 0.52f, 1.00f, "#83B7FF"),
            Make("purple", "紫", "Violet",  0.62f, 0.32f, 0.92f, "#C29BFF"),
            Make("silver", "银", "Silver",  0.78f, 0.82f, 0.88f, "#D7DCE4"),
        };

        private static PetNestChromaColor Make(string id, string cn, string en,
            float r, float g, float b, string textHex)
        {
            PetNestChromaColor color = new PetNestChromaColor();
            color.Id = id;
            color.NameCN = cn;
            color.NameEN = en;
            color.ParticleR = r;
            color.ParticleG = g;
            color.ParticleB = b;
            color.TextHex = textHex;
            return color;
        }

        /// <summary>异色的金色文字色（深色面板上可读）。</summary>
        internal const string ShinyTextHex = "#FFC93C";

        /// <summary>异色粒子色（金）。</summary>
        internal const float ShinyParticleR = 1.00f;
        internal const float ShinyParticleG = 0.79f;
        internal const float ShinyParticleB = 0.24f;

        /// <summary>调色板条目数（guard 与属性测试用）。</summary>
        internal static int PaletteSize { get { return Palette.Length; } }

        /// <summary>按 id 取颜色；未知 id 返回 null（老档 / 脏档 fail-open）。</summary>
        internal static PetNestChromaColor Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            for (int i = 0; i < Palette.Length; i++)
            {
                if (string.Equals(Palette[i].Id, id, StringComparison.Ordinal)) return Palette[i];
            }
            return null;
        }

        /// <summary>全部颜色 id（只读快照，测试用）。</summary>
        internal static List<string> AllIds()
        {
            List<string> ids = new List<string>(Palette.Length);
            for (int i = 0; i < Palette.Length; i++) ids.Add(Palette[i].Id);
            return ids;
        }

        #endregion

        #region roll

        /// <summary>
        /// 按 [0,1) 的两个随机数选一对**不同**颜色。
        /// 随机源由调用方注入，方便离线属性测试逐对覆盖 45 种搭配。
        /// </summary>
        internal static void RollPair(float roll0, float roll1, out string idA, out string idB)
        {
            int count = Palette.Length;
            int a = Clamp(roll0, count);
            // 第二个颜色在剩下的 count-1 个里选，保证两色不同（owner：任意两个颜色搭配）
            int offset = Clamp(roll1, count - 1);
            int b = offset >= a ? offset + 1 : offset;

            idA = Palette[a].Id;
            idB = Palette[b].Id;
        }

        private static int Clamp(float roll, int count)
        {
            if (count <= 0) return 0;
            if (roll < 0f) roll = 0f;
            int index = (int)(roll * count);
            if (index >= count) index = count - 1;
            if (index < 0) index = 0;
            return index;
        }

        #endregion

        #region 名字装饰

        /// <summary>该崽是否带炫彩（两色都能解析出来才算）。</summary>
        internal static bool HasChroma(PetNestPetRecord pet)
        {
            return pet != null && Find(pet.chromaA) != null && Find(pet.chromaB) != null;
        }

        /// <summary>搭配名，如「黑白」/「Black-White」。无炫彩返回空串。</summary>
        internal static string DescribePair(PetNestPetRecord pet, bool chinese)
        {
            return pet == null ? string.Empty : DescribePair(pet.chromaA, pet.chromaB, chinese);
        }

        /// <summary>
        /// 搭配名的**脱离 PetRecord** 重载。远征记录与纪念碑只有两个色 id：
        /// 崽真死后 PetRecord 已经被移出巢，再也 new 不出一份来。
        /// </summary>
        internal static string DescribePair(string chromaA, string chromaB, bool chinese)
        {
            PetNestChromaColor a = Find(chromaA);
            PetNestChromaColor b = Find(chromaB);
            if (a == null || b == null) return string.Empty;
            return chinese ? (a.NameCN + b.NameCN) : (a.NameEN + "-" + b.NameEN);
        }

        /// <summary>
        /// 名字装饰（富文本）。口径 owner 2026-09-20：
        ///   - 炫彩：「黑白 - 阿花」，前缀两字各用自己的**可读**色，名字本体走两色逐字渐变；
        ///   - 异色：整条金色 + 轻微特效标记（★），是最豪华的一档；
        ///   - 两者同时命中：异色优先，但搭配名仍然带上（异色的炫彩崽是双稀有）。
        /// rawName 已经是玩家看到的名字（PetNestService.GetPetDisplayName 的结果）。
        /// </summary>
        internal static string Decorate(PetNestPetRecord pet, string rawName, bool chinese)
        {
            if (pet == null) return rawName ?? string.Empty;
            return Decorate(pet.shiny, pet.chromaA, pet.chromaB, rawName, chinese);
        }

        /// <summary>
        /// 名字装饰的**脱离 PetRecord** 重载：只吃 shiny + 两个色 id。
        ///
        /// 为什么需要它（2026-09-20 第三轮）：远征列表、翻牌卡与阵亡纪念碑显示的往往是
        /// **已经不在巢里**的崽——真死结算会把 PetRecord 移除，而那正是最需要把
        /// 「金灿灿的异色」「黑白炫彩」显示出来的三个地方。只认 PetRecord 的旧签名
        /// 在这条路径上必然退化成裸名字，玩家看到的就是「炫彩/异色信息丢了」。
        /// 于是把颜色三格随记录固化（petShiny / petChromaA / petChromaB、碑文的
        /// chromaA / chromaB），这里按同一套规则渲染，两条路径的观感完全一致。
        /// </summary>
        internal static string Decorate(bool shiny, string chromaA, string chromaB, string rawName, bool chinese)
        {
            if (string.IsNullOrEmpty(rawName)) return rawName ?? string.Empty;

            PetNestChromaColor a = Find(chromaA);
            PetNestChromaColor b = Find(chromaB);
            bool chroma = a != null && b != null;

            if (shiny)
            {
                string prefix = chroma
                    ? Gradient(DescribePair(chromaA, chromaB, chinese), a, b) + " - "
                    : string.Empty;
                // ★ 是 GBK 收录字符（AGENTS 4.14 的字形白名单），不会变豆腐块
                return "<color=" + ShinyTextHex + "><b>★ " + (chinese ? "异色" : "Shiny")
                    + "</b></color> " + prefix + "<color=" + ShinyTextHex + "><b>" + rawName + " ★</b></color>";
            }

            if (!chroma) return rawName;
            return Gradient(DescribePair(chromaA, chromaB, chinese), a, b) + " - " + Gradient(rawName, a, b);
        }

        /// <summary>
        /// 逐字线性插值成两色渐变。TMP 的 &lt;gradient&gt; 需要预制的 ColorGradient 资产，
        /// 运行时拿不到，所以这里用逐字 &lt;color&gt; 自己拼——同样是官方字体能渲染的富文本。
        /// 两端色都取自 TextHex（可读档），因此渐变全程可读。
        /// </summary>
        internal static string Gradient(string text, PetNestChromaColor a, PetNestChromaColor b)
        {
            if (string.IsNullOrEmpty(text) || a == null || b == null) return text ?? string.Empty;
            if (text.Length == 1) return "<color=" + a.TextHex + ">" + text + "</color>";

            int ar, ag, ab, br, bg, bb;
            if (!TryParseHex(a.TextHex, out ar, out ag, out ab)
                || !TryParseHex(b.TextHex, out br, out bg, out bb))
            {
                return text;
            }

            System.Text.StringBuilder sb = new System.Text.StringBuilder(text.Length * 20);
            int last = text.Length - 1;
            for (int i = 0; i < text.Length; i++)
            {
                float t = (float)i / last;
                int r = (int)(ar + (br - ar) * t);
                int g = (int)(ag + (bg - ag) * t);
                int bl = (int)(ab + (bb - ab) * t);
                sb.Append("<color=#");
                AppendByte(sb, r);
                AppendByte(sb, g);
                AppendByte(sb, bl);
                sb.Append('>');
                sb.Append(text[i]);
                sb.Append("</color>");
            }
            return sb.ToString();
        }

        private static void AppendByte(System.Text.StringBuilder sb, int value)
        {
            if (value < 0) value = 0;
            if (value > 255) value = 255;
            sb.Append("0123456789ABCDEF"[value >> 4]);
            sb.Append("0123456789ABCDEF"[value & 0xF]);
        }

        internal static bool TryParseHex(string hex, out int r, out int g, out int b)
        {
            r = g = b = 0;
            if (string.IsNullOrEmpty(hex) || hex.Length != 7 || hex[0] != '#') return false;
            return TryByte(hex, 1, out r) && TryByte(hex, 3, out g) && TryByte(hex, 5, out b);
        }

        private static bool TryByte(string hex, int index, out int value)
        {
            int hi = HexDigit(hex[index]);
            int lo = HexDigit(hex[index + 1]);
            value = 0;
            if (hi < 0 || lo < 0) return false;
            value = hi * 16 + lo;
            return true;
        }

        private static int HexDigit(char c)
        {
            if (c >= '0' && c <= '9') return c - '0';
            if (c >= 'a' && c <= 'f') return c - 'a' + 10;
            if (c >= 'A' && c <= 'F') return c - 'A' + 10;
            return -1;
        }

        #endregion
    }
}
