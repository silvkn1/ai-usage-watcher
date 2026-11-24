using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ClaudeUsageWidget
{
    // ---------------- menu klik kanan bertema ----------------
    internal class ThemedColorTable : ProfessionalColorTable
    {
        public ThemedColorTable() { UseSystemColors = false; }
        public override Color ToolStripDropDownBackground { get { return Palette.Surface; } }
        public override Color MenuBorder { get { return Palette.Border; } }
        public override Color MenuItemBorder { get { return Palette.Hover; } }
        public override Color MenuItemSelected { get { return Palette.Hover; } }
        public override Color MenuItemSelectedGradientBegin { get { return Palette.Hover; } }
        public override Color MenuItemSelectedGradientEnd { get { return Palette.Hover; } }
        public override Color MenuItemPressedGradientBegin { get { return Palette.Hover; } }
        public override Color MenuItemPressedGradientMiddle { get { return Palette.Hover; } }
        public override Color MenuItemPressedGradientEnd { get { return Palette.Hover; } }
        public override Color ImageMarginGradientBegin { get { return Palette.Surface; } }
        public override Color ImageMarginGradientMiddle { get { return Palette.Surface; } }
        public override Color ImageMarginGradientEnd { get { return Palette.Surface; } }
        public override Color SeparatorDark { get { return Palette.Border; } }
        public override Color SeparatorLight { get { return Palette.Surface; } }
        public override Color CheckBackground { get { return Palette.Hover; } }
        public override Color CheckSelectedBackground { get { return Palette.Hover; } }
        public override Color CheckPressedBackground { get { return Palette.Hover; } }
    }

    internal class ThemedRenderer : ToolStripProfessionalRenderer
    {
        public ThemedRenderer() : base(new ThemedColorTable()) { RoundedEdges = false; }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? Palette.Text : Palette.Dim;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            Graphics g = e.Graphics;
            Rectangle r = e.ImageRectangle;
            using (SolidBrush b = new SolidBrush(Palette.Hover)) g.FillRectangle(b, r);
            SmoothingMode old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (Pen p = new Pen(e.Item.Enabled ? Palette.Text : Palette.Dim, 1.8f))
                g.DrawLines(p, new[] {
                    new Point(r.Left + 4, r.Top + r.Height / 2),
                    new Point(r.Left + r.Width / 2 - 1, r.Bottom - 5),
                    new Point(r.Right - 4, r.Top + 4) });
            g.SmoothingMode = old;
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            using (Pen p = new Pen(Palette.Border))
                e.Graphics.DrawRectangle(p, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
        }
    }

    internal static class MenuTheme
    {
        /// <summary>Terapkan palet aktif ke menu (dipanggil lagi saat tema berganti).</summary>
        public static void Apply(ContextMenuStrip m)
        {
            m.Renderer = new ThemedRenderer();
            m.BackColor = Palette.Surface;
            m.ForeColor = Palette.Text;
            foreach (ToolStripItem it in m.Items)
                if (!(it is ToolStripSeparator)) it.ForeColor = Palette.Text;
        }
    }

    // ---------------- title bar & scrollbar gelap ----------------
    internal static class DarkUi
    {
        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int size);
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] private static extern int SetWindowTheme(IntPtr h, string app, string idList);

        public static void TitleBar(IntPtr hwnd, bool dark)
        {
            try
            {
                int v = dark ? 1 : 0;
                if (DwmSetWindowAttribute(hwnd, 20, ref v, 4) != 0) DwmSetWindowAttribute(hwnd, 19, ref v, 4);   // 19 = Windows 10 sebelum 20H1
            }
            catch { }
        }

        public static void ControlTheme(IntPtr hwnd, bool dark)
        {
            try { SetWindowTheme(hwnd, dark ? "DarkMode_Explorer" : "Explorer", null); } catch { }
        }
    }

    // ---------------- efek kaca buram Windows 11 Mica / Acrylic ----------------
    internal static class DwmBackdrop
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct MARGINS { public int cxLeftWidth, cxRightWidth, cyTopHeight, cyBottomHeight; }

        [StructLayout(LayoutKind.Sequential)]
        private struct AccentPolicy
        {
            public int AccentState;
            public int AccentFlags;
            public int GradientColor;
            public int AnimationId;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WindowCompositionAttributeData
        {
            public int Attribute;
            public IntPtr Data;
            public int SizeOfData;
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        [DllImport("dwmapi.dll")]
        private static extern int DwmExtendFrameIntoClientArea(IntPtr hWnd, ref MARGINS pMarInset);

        [DllImport("user32.dll")]
        private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

        public const int DWMWA_MICA_EFFECT = 1029;
        public const int DWMWA_SYSTEMBACKDROP_TYPE = 38;

        public const int BACKDROP_AUTO = 0;
        public const int BACKDROP_NONE = 1;
        public const int BACKDROP_MICA = 2;
        public const int BACKDROP_ACRYLIC = 3;
        public const int BACKDROP_MICA_ALT = 4;

        public static bool IsWindows11
        {
            get
            {
                try { return Environment.OSVersion.Version.Major >= 10 && Environment.OSVersion.Version.Build >= 22000; }
                catch { return false; }
            }
        }

        public static bool IsBackdropActive(string type)
        {
            return !string.IsNullOrEmpty(type) && type != "solid" && type != "none";
        }

        public static void Apply(IntPtr hwnd, string type, bool isDark)
        {
            if (hwnd == IntPtr.Zero) return;
            try
            {
                if (string.IsNullOrEmpty(type) || type == "solid" || type == "none")
                {
                    if (IsWindows11 && Environment.OSVersion.Version.Build >= 22621)
                    {
                        int none = BACKDROP_NONE;
                        DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref none, 4);
                    }
                    return;
                }

                MARGINS margins = new MARGINS { cxLeftWidth = -1, cxRightWidth = -1, cyTopHeight = -1, cyBottomHeight = -1 };
                DwmExtendFrameIntoClientArea(hwnd, ref margins);

                if (IsWindows11 && Environment.OSVersion.Version.Build >= 22621)
                {
                    int bType = BACKDROP_ACRYLIC;
                    if (type == "mica") bType = BACKDROP_MICA;
                    else if (type == "mica-alt") bType = BACKDROP_MICA_ALT;
                    DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref bType, 4);
                }
                else if (IsWindows11)
                {
                    int micaVal = 1;
                    DwmSetWindowAttribute(hwnd, DWMWA_MICA_EFFECT, ref micaVal, 4);
                }

                if (type == "acrylic" || !IsWindows11)
                {
                    int gradientColor = isDark ? unchecked((int)0xCC202024) : unchecked((int)0xCCF4F5F7);
                    AccentPolicy policy = new AccentPolicy
                    {
                        AccentState = 4, // ACCENT_ENABLE_ACRYLICBLURBEHIND
                        AccentFlags = 2,
                        GradientColor = gradientColor,
                        AnimationId = 0
                    };
                    int size = Marshal.SizeOf(policy);
                    IntPtr pPolicy = Marshal.AllocHGlobal(size);
                    try
                    {
                        Marshal.StructureToPtr(policy, pPolicy, false);
                        WindowCompositionAttributeData data = new WindowCompositionAttributeData
                        {
                            Attribute = 19, // WCA_ACCENT_POLICY
                            Data = pPolicy,
                            SizeOfData = size
                        };
                        SetWindowCompositionAttribute(hwnd, ref data);
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(pPolicy);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Write("dwm.backdrop", ex);
            }
        }
    }

    // ---------------- ikon tray: satu angka besar ----------------
    internal static class TrayIconPainter
    {
        // Warna lencana dibuat pekat agar terlihat di taskbar terang maupun gelap; warna teks dipilih menurut kontras.
        public static readonly Color StGreen = Color.FromArgb(34, 148, 60);
        public static readonly Color StAmber = Color.FromArgb(247, 176, 28);
        public static readonly Color StRed = Color.FromArgb(222, 58, 50);
        public static readonly Color StGray = Color.FromArgb(104, 104, 114);
        private static readonly Color DarkText = Color.FromArgb(24, 24, 24);

        public static Color StatusColor(double usedPercent)
        {
            if (usedPercent >= 80) return StRed;
            if (usedPercent >= 50) return StAmber;
            return StGreen;
        }

        /// <summary>Warna teks di atas lencana: gelap di atas kuning, putih selain itu.</summary>
        public static Color TextOn(Color badge) { return badge == StAmber ? DarkText : Color.White; }

        private static string _family;

        /// <summary>
        /// Arial Narrow Bold (angka tinggi dan tajam di 16 px; diuji dibanding Bahnschrift/Segoe UI/Tahoma),
        /// cadangan Arial Bold lalu Segoe UI Bold. Semuanya dirender tebal.
        /// </summary>
        private static void ResolveFont()
        {
            if (_family != null) return;
            System.Collections.Generic.HashSet<string> have = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (FontFamily f in FontFamily.Families) have.Add(f.Name);
            _family = have.Contains("Arial Narrow") ? "Arial Narrow" : (have.Contains("Arial") ? "Arial" : "Segoe UI");
        }

        /// <summary>
        /// Gambar satu angka (atau "--") memenuhi ikon. weekly = nilai ini adalah limit mingguan (penanda: garis putih di bawah angka).
        /// Ukuran kecil (&lt;24 px) dirender tanpa anti-alias agar garis angka tajam; ukuran besar memakai anti-alias.
        /// </summary>
        public static void Draw(Graphics g, int size, string text, Color badge, bool weekly)
        {
            ResolveFont();
            g.Clear(Color.Transparent);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int radius = Math.Max(2, (int)Math.Round(size * 0.2));
            using (GraphicsPath p = Round(new Rectangle(0, 0, size - 1, size - 1), radius))
            using (SolidBrush b = new SolidBrush(badge)) g.FillPath(b, p);

            int bar = weekly ? Math.Max(2, (int)Math.Round(size * 0.12)) : 0;
            float areaTop = Math.Max(1f, size * 0.06f);
            float areaBottom = size - areaTop - (weekly ? bar + Math.Max(1f, size * 0.06f) : 0f);
            float areaH = areaBottom - areaTop;
            float maxW = size - 2f * Math.Max(1f, size / 16f);
            FontStyle style = FontStyle.Bold;
            float em = areaH / 0.72f;                               // tinggi angka ~0,72 em: mulai dari yang mengisi tinggi
            g.TextRenderingHint = size < 24 ? System.Drawing.Text.TextRenderingHint.SingleBitPerPixelGridFit
                                            : System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            using (StringFormat sf = new StringFormat(StringFormat.GenericTypographic))
            {
                sf.FormatFlags |= StringFormatFlags.NoWrap;
                Font f = null;
                SizeF m = SizeF.Empty;
                for (; em >= 5f; em -= 0.5f)                         // kecilkan sampai lebar muat ("100" lebih lebar dari "47")
                {
                    if (f != null) f.Dispose();
                    f = new Font(_family, em, style, GraphicsUnit.Pixel);
                    m = g.MeasureString(text, f, PointF.Empty, sf);
                    if (m.Width <= maxW) break;
                }
                using (f)
                using (SolidBrush tb = new SolidBrush(TextOn(badge)))
                {
                    float digitH = em * 0.72f;
                    float ascent = f.FontFamily.GetCellAscent(style) / (float)f.FontFamily.GetEmHeight(style) * em;
                    float x = (size - m.Width) / 2f;
                    float y = areaTop + (areaH - digitH) / 2f - (ascent - digitH);
                    g.DrawString(text, f, tb, x, y, sf);
                }
            }
            if (weekly)
            {
                g.SmoothingMode = SmoothingMode.None;
                int bx = (int)Math.Round(size * 0.22), bw = size - 2 * bx;
                int by = (int)Math.Round(size - areaTop - bar);
                using (SolidBrush wb = new SolidBrush(TextOn(badge))) g.FillRectangle(wb, bx, by, bw, bar);
            }
        }

        private static GraphicsPath Round(Rectangle r, int rad)
        {
            GraphicsPath p = new GraphicsPath();
            int d = rad * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }

    internal static class AppIcon
    {
        private static Icon _icon;
        public static Icon Get()
        {
            if (_icon != null) return _icon;
            try
            {
                using (System.IO.Stream s = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("app.ico"))
                {
                    if (s != null) _icon = new Icon(s);
                }
            }
            catch { }
            if (_icon == null)
            {
                try
                {
                    string loc = System.Reflection.Assembly.GetExecutingAssembly().Location;
                    if (!string.IsNullOrEmpty(loc) && System.IO.File.Exists(loc))
                        _icon = Icon.ExtractAssociatedIcon(loc);
                }
                catch { }
            }
            return _icon;
        }
    }
}
