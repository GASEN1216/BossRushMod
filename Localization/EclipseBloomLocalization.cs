namespace BossRush
{
    public static class EclipseBloomLocalization
    {
        public static void Inject()
        {
            LocalizationHelper.InjectLocalization(EclipseBloomBoss.NameKey, L10n.T("蚀日花冠", "Eclipse Bloom"));
            EclipseBloomIntroduction.InjectLocalizations();
        }
    }
}
