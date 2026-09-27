using Duckov.UI;

namespace BossRush
{
    internal sealed partial class UIAndSignsRuntimeModule
    {
        internal void ClearPendingNotifications()
        {
            try
            {
                System.Type notifType = typeof(NotificationText);
                System.Reflection.FieldInfo pendingField = notifType.GetField("pendingTexts", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                if (pendingField != null)
                {
                    System.Collections.Generic.Queue<string> q = pendingField.GetValue(null) as System.Collections.Generic.Queue<string>;
                    if (q != null)
                    {
                        q.Clear();
                    }
                }
            }
            catch { }
        }
    }
}
