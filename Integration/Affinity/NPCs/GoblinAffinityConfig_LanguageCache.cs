// 叮当文案缓存的语言失效策略，与配置主体共享同一份缓存。
namespace BossRush
{
    public partial class GoblinAffinityConfig
    {
        private static bool? cachedTextIsChinese;

        private static void EnsureTextLanguage()
        {
            bool isChinese = L10n.IsChinese;
            if (cachedTextIsChinese == isChinese) return;
            cachedTextIsChinese = isChinese;
            _unlocksByLevel = null;
            _positiveBubbles = null;
            _negativeBubbles = null;
            _normalBubbles = null;
        }
    }
}
