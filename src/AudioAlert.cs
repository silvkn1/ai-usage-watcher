using System;
using System.Media;

namespace ClaudeUsageWidget
{
    /// <summary>Efek suara sistem untuk notifikasi dan alert (tanpa file eksternal).</summary>
    internal static class AudioAlert
    {
        public static void PlayPrompt()
        {
            try { SystemSounds.Asterisk.Play(); } catch { }
        }

        public static void PlayWarning()
        {
            try { SystemSounds.Exclamation.Play(); } catch { }
        }

        public static void PlayNotify()
        {
            try { SystemSounds.Beep.Play(); } catch { }
        }
    }
}
