using System;
using System.IO;
using System.Media;
using System.Threading.Tasks;

namespace ClaudeUsageWidget
{
    /// <summary>Efek suara sistem atau file .wav kustom opsional untuk notifikasi dan alert.</summary>
    internal static class AudioAlert
    {
        private const long MaxWavBytes = 5 * 1024 * 1024;
        private static readonly object Gate = new object();
        private static volatile string _customWavPath;
        public static string CustomWavPath { get { return _customWavPath; } set { _customWavPath = value; } }

        internal static bool IsValidWavFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            try
            {
                string trimmed = path.Trim();
                if (!trimmed.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)) return false;
                FileInfo fi = new FileInfo(trimmed);
                if (!fi.Exists || fi.Length <= 44 || fi.Length > MaxWavBytes) return false;
                byte[] hdr = new byte[12];
                using (FileStream fs = new FileStream(trimmed, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    if (fs.Read(hdr, 0, 12) < 12) return false;
                }
                return hdr[0] == 'R' && hdr[1] == 'I' && hdr[2] == 'F' && hdr[3] == 'F' &&
                       hdr[8] == 'W' && hdr[9] == 'A' && hdr[10] == 'V' && hdr[11] == 'E';
            }
            catch { return false; }
        }

        public static void PlayFile(string path)
        {
            PlayOrFallback(path, SystemSounds.Asterisk);
        }

        private static void PlayOrFallback(string path, SystemSound fallback)
        {
            string p = path;
            if (IsValidWavFile(p))
            {
                string clean = p.Trim();
                Task.Run(delegate
                {
                    try
                    {
                        lock (Gate)
                        {
                            using (SoundPlayer sp = new SoundPlayer(clean))
                                sp.PlaySync();
                        }
                    }
                    catch
                    {
                        try { if (fallback != null) fallback.Play(); } catch { }
                    }
                });
                return;
            }
            try { if (fallback != null) fallback.Play(); } catch { }
        }

        public static void PlayPrompt() { PlayOrFallback(_customWavPath, SystemSounds.Asterisk); }

        public static void PlayWarning() { PlayOrFallback(_customWavPath, SystemSounds.Exclamation); }

        public static void PlayNotify() { PlayOrFallback(_customWavPath, SystemSounds.Beep); }
    }
}
