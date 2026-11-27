using System;
using System.IO;
using System.Media;

namespace ClaudeUsageWidget
{
    /// <summary>Efek suara sistem atau file .wav kustom opsional untuk notifikasi dan alert.</summary>
    internal static class AudioAlert
    {
        private const long MaxWavBytes = 5 * 1024 * 1024;
        public static string CustomWavPath { get; set; }

        internal static bool IsValidWavFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            try
            {
                string trimmed = path.Trim();
                if (!trimmed.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)) return false;
                FileInfo fi = new FileInfo(trimmed);
                return fi.Exists && fi.Length > 44 && fi.Length <= MaxWavBytes;
            }
            catch { return false; }
        }

        private static void PlayOrFallback(SystemSound fallback)
        {
            string p = CustomWavPath;
            if (IsValidWavFile(p))
            {
                try
                {
                    using (SoundPlayer sp = new SoundPlayer(p.Trim()))
                        sp.Play();
                    return;
                }
                catch { }
            }
            try { if (fallback != null) fallback.Play(); } catch { }
        }

        public static void PlayPrompt() { PlayOrFallback(SystemSounds.Asterisk); }

        public static void PlayWarning() { PlayOrFallback(SystemSounds.Exclamation); }

        public static void PlayNotify() { PlayOrFallback(SystemSounds.Beep); }
    }
}
