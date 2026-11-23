// Claude Usage Widget - teks antarmuka dua bahasa (en = default, id = Bahasa Indonesia).
// Aturan: setiap kunci wajib punya dua entri dengan placeholder {n} yang sama (dicek oleh skrip uji).
using System;
using System.Collections.Generic;
using System.Globalization;

namespace ClaudeUsageWidget
{
    internal static class L
    {
        public const string AppName = "AI Usage";
        public const string AppFullName = "AI Usage Watcher";

        public static string Lang = "en";
        public static bool IsId { get { return Lang == "id"; } }

        private static readonly CultureInfo CultureEn = CultureInfo.GetCultureInfo("en-US");
        private static readonly CultureInfo CultureId = CultureInfo.GetCultureInfo("id-ID");
        private static Dictionary<string, string> _pack;      // terjemahan dari paket bahasa (file JSON), bila Lang bukan en/id
        private static CultureInfo _packCulture;
        public static CultureInfo Culture { get { return IsId ? CultureId : (_packCulture ?? CultureEn); } }

        /// <summary>Atur bahasa: "en", "id", atau kode paket bahasa terpasang. Kode tak dikenal kembali ke "en".</summary>
        public static void Set(string lang)
        {
            _pack = null; _packCulture = null;
            if (lang == "id") { Lang = "id"; return; }
            Lang = "en";
            if (string.IsNullOrEmpty(lang) || lang == "en") return;
            LangPackInfo info = LangPacks.Find(lang);
            if (info == null) return;
            Dictionary<string, string> strings; CultureInfo culture;
            if (!LangPacks.Load(info, out strings, out culture)) return;
            _pack = strings; _packCulture = culture; Lang = info.Code;
        }

        /// <summary>
        /// Terjemahan untuk kunci. Paket bahasa yang kehilangan kunci memakai teks Inggris. Bila string format salah
        /// (placeholder tak terisi), kembali ke templat Inggris, lalu ke kunci, jadi paket yang rusak tidak membuat widget mogok.
        /// </summary>
        public static string T(string key, params object[] args)
        {
            string[] v;
            if (!D.TryGetValue(key, out v)) return key;
            string tpl;
            if (IsId) tpl = v[1];
            else
            {
                string s;
                tpl = _pack != null && _pack.TryGetValue(key, out s) && !string.IsNullOrEmpty(s) ? s : v[0];
            }
            if (args == null || args.Length == 0) return tpl;
            try { return string.Format(Culture, tpl, args); }
            catch (FormatException)
            {
                try { return string.Format(CultureEn, v[0], args); } catch (FormatException) { return key; }
            }
        }

        public static IEnumerable<KeyValuePair<string, string[]>> All { get { return D; } }

        /// <summary>Tulis templat terjemahan (semua kunci dengan teks Inggris) sebagai JSON, untuk dijadikan paket bahasa baru.</summary>
        public static void ExportTemplate(string path)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.Append("{\n  \"_name\": \"New language\",\n  \"_culture\": \"en-US\"");
            foreach (KeyValuePair<string, string[]> kv in D)
                sb.Append(",\n  ").Append(J.Ser.Serialize(kv.Key)).Append(": ").Append(J.Ser.Serialize(kv.Value[0]));
            sb.Append("\n}\n");
            System.IO.File.WriteAllText(path, sb.ToString(), new System.Text.UTF8Encoding(false));
        }

        private static readonly Dictionary<string, string[]> D = new Dictionary<string, string[]>
        {
            // ---- widget ----
            { "loading",        new[] { "Loading data...", "Memuat data..." } },
            { "compact",        new[] { "5h {0}  ·  7d {1}  ·  today ≈{2}", "5j {0}  ·  7h {1}  ·  hari ini ≈{2}" } },
            { "lastchat",       new[] { "last chat {0}", "chat terakhir {0}" } },
            { "limit5",         new[] { "Claude 5-hour limit", "Limit 5 jam Claude" } },
            { "limit7",         new[] { "Claude weekly limit", "Limit mingguan Claude" } },
            { "limit5.ag",      new[] { "Antigravity 5-hour limit", "Limit 5 jam Antigravity" } },
            { "limit7.ag",      new[] { "Antigravity weekly limit", "Limit mingguan Antigravity" } },
            { "nostatusline",   new[] { "Limit: statusline not installed", "Limit: statusline belum dipasang" } },
            { "snapshot",       new[] { "Limit snapshot: {0} · source: {1}", "Snapshot limit: {0} · sumber: {1}" } },
            { "sync.schedule",   new[] { "Updated {0} · Next sync {1} (or on chat)", "Diperbarui {0} · Sinkronisasi {1} (atau saat chat)" } },
            { "src.probe",      new[] { "CLI probe", "probe CLI" } },
            { "src.statusline", new[] { "CLI statusline", "statusline CLI" } },
            { "scanning",       new[] { "Scanning Claude Code logs...", "Memindai log Claude Code..." } },
            { "today",          new[] { "Today  {0} tokens", "Hari ini  {0} token" } },
            { "week",           new[] { "7 days  {0} tokens", "7 hari  {0} token" } },
            { "admin.wait",     new[] { "Admin API: waiting...", "Admin API: menunggu..." } },
            { "admin.fail",     new[] { "Admin API failed: {0}", "Admin API gagal: {0}" } },
            { "org.month",      new[] { "Org this month", "Org bulan ini" } },
            { "org.fetched",    new[] { "{0} · fetched {1} (data delayed up to 24 h)", "{0} · diambil {1} (data tertunda s.d. 24 jam)" } },
            { "footer",         new[] { "≈ estimate at API list price, not a subscription bill", "≈ estimasi setara harga API, bukan tagihan langganan" } },
            { "stats",          new[] { "Unpriced: {0} · skipped lines: {1} · fast: {2}", "Tanpa harga: {0} · baris dilewati: {1} · fast: {2}" } },
            { "na",             new[] { "n/a", "n/a" } },
            { "reset.short",    new[] { "reset", "reset" } },
            { "unavailable",    new[] { "unavailable", "tidak tersedia" } },
            { "alreadyreset",   new[] { "already reset {0}", "sudah reset {0}" } },
            { "resetin.at",      new[] { "resets in {0} ({1})", "reset {0} ({1})" } },

            // ---- format angka & waktu ----
            { "unit.b",         new[] { "B", "M" } },     // miliar
            { "unit.m",         new[] { "M", "jt" } },    // juta
            { "unit.k",         new[] { "K", "rb" } },    // ribu
            { "time.now",       new[] { "now", "sekarang" } },
            { "time.dh",        new[] { "{0}d {1}h", "{0}h {1}j" } },
            { "time.hm",        new[] { "{0}h {1}m", "{0}j {1}m" } },
            { "time.m",         new[] { "{0}m", "{0}m" } },

            // ---- tray ----
            { "tray.show",         new[] { "Show widget", "Tampilkan widget" } },
            { "tray.dock",         new[] { "Dock to top edge (expands on hover)", "Tempel di tepi atas (lengkap saat kursor di atasnya)" } },
            { "tray.compact",      new[] { "Compact mode", "Mode ringkas" } },
            { "tray.clickthrough", new[] { "Click-through (widget can't be clicked)", "Klik-tembus (widget tak bisa diklik)" } },
            { "tray.refresh",      new[] { "Refresh now", "Refresh sekarang" } },
            { "tray.settings",     new[] { "Settings...", "Pengaturan..." } },
            { "tray.datafolder",   new[] { "Open data folder", "Buka folder data" } },
            { "tray.autostart",    new[] { "Start with Windows", "Jalankan saat Windows mulai" } },
            { "tray.openfolder",   new[] { "Open project folder ({0})", "Buka folder proyek ({0})" } },
            { "tray.openterminal", new[] { "Open terminal in project ({0})", "Buka terminal di proyek ({0})" } },
            { "tray.exit",         new[] { "Exit", "Keluar" } },
            { "tip.l5",            new[] { "5h {0} · resets in {1} ({2})", "5j {0} · reset {1} ({2})" } },
            { "tip.l5n",           new[] { "5h {0}", "5j {0}" } },
            { "tip.l7",            new[] { "7d {0} · resets in {1} ({2})", "7h {0} · reset {1} ({2})" } },
            { "tip.l7n",           new[] { "7d {0}", "7h {0}" } },
            { "tip.today",         new[] { "Today ≈{0}", "Hari ini ≈{0}" } },
            { "tip.noproviders",   new[] { "No active providers", "Tidak ada monitoring aktif" } },
            { "msg.autostartfail", new[] { "Failed to change autostart.", "Gagal mengubah autostart." } },
            { "probe.running",     new[] { "CLI probe running...", "probe CLI berjalan..." } },
            { "probe.failed",      new[] { "CLI probe failed: {0}", "probe CLI gagal: {0}" } },

            // ---- galat probe & Admin API ----
            { "perr.notfound",     new[] { "Claude Code not found (neither the CLI nor the desktop app bundle)", "Claude Code tidak ditemukan (CLI maupun bundel desktop app)" } },
            { "perr.start",        new[] { "failed to start the CLI ({0})", "gagal menjalankan CLI ({0})" } },
            { "perr.timeout",      new[] { "the CLI did not finish within 90 seconds", "CLI tidak selesai dalam 90 detik" } },
            { "perr.unrecognized", new[] { "unrecognized CLI output (version {0}; tested on 2.1.330)", "output CLI tidak dikenali (versi {0}; diuji pada 2.1.330)" } },
            { "perr.cli",          new[] { "CLI error: {0}", "CLI error: {0}" } },
            { "perr.auth",         new[] { "no limit data: signed in with {0}, not a Pro/Max/Ultra subscription", "tidak ada data limit: login memakai {0}, bukan langganan Pro/Max/Ultra" } },
            { "perr.nolimit",      new[] { "no limit data (needs a Pro/Max/Ultra subscription login; CLI version {0})", "tidak ada data limit (perlu login langganan Pro/Max/Ultra; versi CLI {0})" } },
            { "perr.nowindows",    new[] { "limit data without five_hour/seven_day", "data limit tanpa five_hour/seven_day" } },
            { "aerr.nokey",        new[] { "admin key not set", "admin key belum diisi" } },
            { "aerr.network",      new[] { "network ({0})", "jaringan ({0})" } },
            { "aerr.http",         new[] { "HTTP {0}", "HTTP {0}" } },
            { "aerr.badresponse",  new[] { "unrecognized response", "respons tidak dikenal" } },
            { "aerr.both",         new[] { "{0} / legacy: {1}", "{0} / legacy: {1}" } },

            // ---- pengaturan ----
            { "set.title",        new[] { "Settings - AI Usage Watcher", "Pengaturan - AI Usage Watcher" } },
            { "tab.display",      new[] { "Display", "Tampilan" } },
            { "tab.behavior",     new[] { "Behavior", "Perilaku" } },
            { "tab.alerts",       new[] { "Alerts", "Notifikasi" } },
            { "tab.data",         new[] { "Data", "Data" } },
            { "tab.probe",        new[] { "CLI probe", "Probe CLI" } },
            { "tab.admin",        new[] { "Admin API", "Admin API" } },
            { "set.opacity",      new[] { "Opacity ({0}%)", "Transparansi ({0}%)" } },
            { "set.showstart",    new[] { "Show the widget when the app starts", "Tampilkan widget saat aplikasi mulai" } },
            { "set.language",     new[] { "Language", "Bahasa" } },
            { "set.theme",        new[] { "Widget theme", "Tema widget" } },
            { "set.theme.dark",   new[] { "Dark", "Gelap" } },
            { "set.theme.light",  new[] { "Light", "Terang" } },
            { "set.ontop",        new[] { "Always on top of other windows (always on in top-edge mode)", "Selalu di atas jendela lain (selalu aktif di mode tepi atas)" } },
            { "set.hidefs",       new[] { "Hide when a full-screen app is on the same monitor", "Sembunyikan saat ada aplikasi layar penuh di monitor yang sama" } },
            { "set.dock",         new[] { "Dock to the top edge: compact, expands when the cursor is over it", "Tempel di tepi atas layar: tampil ringkas, lengkap saat kursor di atasnya" } },
            { "set.compact",      new[] { "Compact mode (one line; double-click the widget to toggle)", "Mode ringkas (satu baris; klik ganda widget untuk ganti)" } },
            { "set.click",        new[] { "Click-through (turn it off again from the tray icon menu)", "Klik-tembus (matikan lagi lewat menu ikon tray)" } },
            { "set.auto",         new[] { "Start with Windows (HKCU\\...\\Run)", "Jalankan saat Windows mulai (HKCU\\...\\Run)" } },
            { "set.probe.warn",   new[] {
                "Warning: the probe runs 'claude -p' automatically with your subscription. Anthropic's Consumer Terms prohibit automated access except via an API key or where explicitly permitted, and the Claude Code documentation does not mention this mode. Its compliance status is unconfirmed. Use at your own responsibility; confirm with Anthropic or Legal if in doubt.",
                "Peringatan: probe menjalankan 'claude -p' secara otomatis memakai langganan Anda. Ketentuan Anthropic (Consumer Terms) melarang akses otomatis kecuali lewat API key atau bila diizinkan eksplisit, dan dokumentasi Claude Code tidak menyebut mode ini. Status kepatuhannya belum dipastikan. Gunakan atas tanggung jawab sendiri; konfirmasi ke Anthropic atau Legal bila ragu." } },
            { "set.probe.enable", new[] { "Enable probe: refresh limits on new chats (for desktop app users)", "Aktifkan probe: perbarui limit saat ada chat baru (untuk pengguna desktop app)" } },
            { "set.probe.every",  new[] { "At most 1 probe every (minutes)", "Maksimal 1 probe tiap (menit)" } },
            { "set.probe.note",   new[] {
                "The desktop app does not run the statusline, so the widget runs 'claude -p ok' (Haiku, no hooks/MCP/tools, no saved session) to read the limits. Each probe uses a small amount of quota (about 7 thousand tokens, mostly cache). The probe data format is undocumented and may change.",
                "Desktop app tidak menjalankan statusline, jadi widget menjalankan 'claude -p ok' (Haiku, tanpa hook/MCP/tool, tanpa menyimpan sesi) untuk membaca limit. Tiap probe memakai sedikit kuota (sekitar 7 ribu token, sebagian besar cache). Format data probe tidak terdokumentasi resmi dan bisa berubah." } },
            { "set.mirror",       new[] { "Copy probe limits to another JSON file (e.g. the Windhawk mod status file)", "Salin limit hasil probe ke file JSON lain (mis. file status mod Windhawk)" } },
            { "set.mirror.path",  new[] { "Target file (must already exist)", "File tujuan (harus sudah ada)" } },
            { "set.browse",       new[] { "Browse...", "Pilih file..." } },
            { "set.admin.enable", new[] { "Enable (polls api.anthropic.com once an hour)", "Aktifkan (poll 1x/jam ke api.anthropic.com)" } },
            { "set.admin.key",    new[] { "Admin key (sk-ant-admin...)", "Admin key (sk-ant-admin...)" } },
            { "set.admin.nokey",  new[] { "No key stored.", "Belum ada key tersimpan." } },
            { "set.admin.stored", new[] { "Key stored encrypted (DPAPI). Leave the field empty to keep the existing key.", "Key tersimpan terenkripsi (DPAPI). Kosongkan kolom untuk tetap memakai key lama." } },
            { "set.admin.clear",  new[] { "Delete stored key", "Hapus key tersimpan" } },
            { "set.admin.cleared",new[] { "The key will be deleted when you click Save.", "Key akan dihapus saat Simpan." } },
            { "set.admin.keyids", new[] { "API key ID filter (optional, comma-separated)", "Filter API key ID (opsional, koma)" } },
            { "set.admin.wsids",  new[] { "Workspace ID filter (optional, comma-separated)", "Filter workspace ID (opsional, koma)" } },
            { "set.admin.note",   new[] {
                "Admin keys can only be created by an org Admin in the Claude Console. Use the filters to show only your own usage. Admin API data is delayed by up to 24 hours.",
                "Admin key hanya bisa dibuat Admin org di Claude Console. Gunakan filter agar hanya usage milik Anda yang tampil. Data Admin API tertunda hingga 24 jam." } },
            { "set.h.pricing",    new[] { "Pricing (estimate)", "Harga (estimasi)" } },
            { "set.pricing.open", new[] { "Open pricing.json", "Buka pricing.json" } },
            { "set.pricing.note", new[] { "Changes are applied automatically within 1 minute.", "Perubahan diterapkan otomatis dalam 1 menit." } },
            { "set.save",         new[] { "Save", "Simpan" } },
            { "set.cancel",       new[] { "Cancel", "Batal" } },
            { "msg.probe.title",  new[] { "Enable CLI probe", "Aktifkan probe CLI" } },
            { "msg.probe.body",   new[] {
                "The probe runs Claude Code ('claude -p') automatically with your subscription.\n\nAnthropic's Consumer Terms prohibit automated access except via an API key or where explicitly permitted, and the Claude Code documentation does not mention this mode, so its compliance status is unconfirmed. This feature is experimental and you use it at your own risk.\n\nEnable the probe?",
                "Probe menjalankan Claude Code ('claude -p') secara otomatis dengan langganan Anda.\n\nConsumer Terms Anthropic melarang akses otomatis kecuali lewat API key atau bila diizinkan eksplisit, dan dokumentasi Claude Code tidak menyebut mode ini, sehingga status kepatuhannya belum dipastikan. Fitur ini eksperimental dan risikonya Anda tanggung sendiri.\n\nAktifkan probe?" } },
            { "msg.key.prefix",   new[] { "The admin key must start with 'sk-ant-admin'. A regular API key cannot read cost reports.", "Admin key harus diawali 'sk-ant-admin'. Key API biasa tidak bisa membaca laporan biaya." } },
            { "msg.key.required", new[] { "Enter an admin key to enable the Admin API.", "Isi admin key untuk mengaktifkan Admin API." } },
            { "msg.mirror.missing", new[] { "The target file for the limit copy was not found. The widget does not create new files.", "File tujuan salinan limit tidak ditemukan. Widget tidak membuat file baru." } },

            // ---- pace, riwayat, alert, sisa ----
            { "limit.left",       new[] { "{0}  {1}% left", "{0}  {1}% sisa" } },
            { "pct.left",         new[] { "{0}% left", "{0}% sisa" } },
            { "pace.hit",         new[] { "At this pace you hit the limit in {0}", "Dengan laju ini limit tercapai dalam {0}" } },
            { "pace.now",         new[] { "At this pace the limit is about to be reached", "Dengan laju ini limit hampir tercapai" } },
            { "snapshot.stale",   new[] { " · older than your last chat", " · lebih lama dari chat terakhir" } },
            { "hist.title",       new[] { "Last {0} days  ≈{1}", "{0} hari terakhir  ≈{1}" } },
            { "hist.peak",        new[] { "peak ≈{0}", "puncak ≈{0}" } },
            { "alert.pct",        new[] { "{0} is at {1}% used", "{0} sudah terpakai {1}%" } },
            { "alert.pct.left",   new[] { "{0}: {1}% left", "{0}: sisa {1}%" } },
            { "alert.reset",      new[] { "{0} has reset", "{0} sudah reset" } },
            { "tray.remaining",   new[] { "Show remaining instead of used", "Tampilkan sisa, bukan terpakai" } },
            { "tip.error",         new[] { "! {0}", "! {0}" } },
            { "set.theme.system", new[] { "Follow Windows", "Ikuti Windows" } },
            { "set.remaining",    new[] { "Show remaining quota instead of used", "Tampilkan sisa kuota, bukan yang terpakai" } },
            { "set.hotkey.enable",new[] { "Global hotkey to show/hide the widget", "Hotkey global untuk tampil/sembunyikan widget" } },
            { "set.hotkey.keys",  new[] { "Hotkey (needs a modifier, e.g. Ctrl+Alt+U)", "Hotkey (wajib ada Ctrl/Alt/Shift/Win, mis. Ctrl+Alt+U)" } },
            { "set.alerts.enable",new[] { "Notify when a limit crosses a threshold", "Beri notifikasi saat limit melewati ambang" } },
            { "set.alerts.th",    new[] { "Thresholds, % used (e.g. 80,95)", "Ambang, % terpakai (mis. 80,95)" } },
            { "set.alerts.reset", new[] { "Notify when a limit resets (only after it crossed a threshold)", "Beri notifikasi saat limit reset (hanya bila sebelumnya melewati ambang)" } },
            { "set.alerts.note",  new[] {
                "Alerts are only as fresh as the latest limit snapshot, which updates when Claude Code runs (or when the probe is on).",
                "Notifikasi hanya semutakhir snapshot limit terakhir, yang diperbarui saat Claude Code dipakai (atau saat probe aktif)." } },
            { "set.wsl",          new[] { "Also scan logs of running WSL distros (never starts a stopped distro)", "Pindai juga log distro WSL yang sedang berjalan (tidak membangunkan distro yang mati)" } },
            { "set.extradirs",    new[] { "Extra Claude config folders (separate with ;)", "Folder konfigurasi Claude tambahan (pisahkan dengan ;)" } },
            { "set.sources.note", new[] {
                "The 5-hour and weekly limits still come only from the default folder's statusline or the probe.",
                "Limit 5 jam dan mingguan tetap hanya berasal dari statusline atau probe folder default." } },
            { "msg.thresholds.invalid", new[] { "Thresholds must be 1-4 whole numbers between 1 and 100, e.g. 80,95.", "Ambang harus 1-4 angka bulat antara 1 dan 100, mis. 80,95." } },
            { "msg.hotkey.invalid",     new[] { "Invalid hotkey. Use at least one modifier plus a key, e.g. Ctrl+Alt+U.", "Hotkey tidak valid. Pakai minimal satu pengubah plus satu tombol, mis. Ctrl+Alt+U." } },
            { "msg.hotkey.fail",        new[] { "The hotkey {0} could not be registered (another app may be using it).", "Hotkey {0} tidak bisa didaftarkan (mungkin dipakai aplikasi lain)." } },

            // ---- grafik riwayat, cache, anggaran, proyek ----
            { "hist.hover",       new[] { "{0}  ≈{1} · {2} tokens · {3} req", "{0}  ≈{1} · {2} token · {3} req" } },
            { "cache.row",        new[] { "Cache hit {0}% · saved ≈{1} today · ≈{2} in 7 days", "Cache hit {0}% · hemat ≈{1} hari ini · ≈{2} dalam 7 hari" } },
            { "budget.day",       new[] { "Daily budget  ≈{0} / ≈{1}  ({2}%)", "Anggaran harian  ≈{0} / ≈{1}  ({2}%)" } },
            { "budget.month",     new[] { "Monthly budget  ≈{0} / ≈{1}  ({2}%)", "Anggaran bulanan  ≈{0} / ≈{1}  ({2}%)" } },
            { "proj.title",       new[] { "Top projects (7 days)", "Proyek teratas (7 hari)" } },
            { "alert.budget.day", new[] { "Today's estimated cost ≈{0} reached {1}% of the ≈{2} daily budget", "Estimasi biaya hari ini ≈{0} mencapai {1}% dari anggaran harian ≈{2}" } },
            { "alert.budget.month", new[] { "This month's estimated cost ≈{0} reached {1}% of the ≈{2} monthly budget", "Estimasi biaya bulan ini ≈{0} mencapai {1}% dari anggaran bulanan ≈{2}" } },
            { "notify.stop",      new[] { "Claude finished ({0})", "Claude selesai ({0})" } },
            { "notify.stop.nodur",new[] { "Claude finished", "Claude selesai" } },
            { "notify.input",     new[] { "Claude needs your input", "Claude butuh input Anda" } },
            { "dur.s",            new[] { "{0}s", "{0} dtk" } },
            { "dur.ms",           new[] { "{0}m {1}s", "{0} mnt {1} dtk" } },
            { "dur.hm",           new[] { "{0}h {1}m", "{0} j {1} mnt" } },
            { "tray.exportcsv",   new[] { "Export history (CSV)...", "Ekspor riwayat (CSV)..." } },
            { "msg.csv.filter",   new[] { "CSV, comma separated (*.csv)|*.csv|CSV, semicolon separated for Excel (*.csv)|*.csv", "CSV, dipisah koma (*.csv)|*.csv|CSV, dipisah titik koma untuk Excel (*.csv)|*.csv" } },
            { "msg.csv.done",     new[] { "Saved: {0}", "Tersimpan: {0}" } },
            { "msg.csv.empty",    new[] { "There is no history to export yet.", "Belum ada riwayat untuk diekspor." } },

            // ---- pengaturan: fitur lanjutan ----
            { "set.histdays",     new[] { "History chart range", "Rentang grafik riwayat" } },
            { "set.days14",       new[] { "14 days", "14 hari" } },
            { "set.days30",       new[] { "30 days", "30 hari" } },
            { "set.dock.edge",    new[] { "Dock edge", "Tepi tempel" } },
            { "edge.top",         new[] { "Top", "Atas" } },
            { "edge.bottom",      new[] { "Bottom", "Bawah" } },
            { "edge.left",        new[] { "Left", "Kiri" } },
            { "edge.right",       new[] { "Right", "Kanan" } },
            { "set.dock.monitor", new[] { "Dock monitor", "Monitor tempel" } },
            { "monitor.primary",  new[] { "Primary monitor", "Monitor utama" } },
            { "monitor.item",     new[] { "{0} ({1}x{2})", "{0} ({1}x{2})" } },
            { "set.lang.export",  new[] { "Export translation template...", "Ekspor templat terjemahan..." } },
            { "set.lang.folder",  new[] { "Open language folder", "Buka folder bahasa" } },
            { "msg.lang.exported",new[] {
                "Template saved to:\n{0}\n\nTranslate the values, set \"_name\" and \"_culture\", then save it as <code>.json (for example ms.json) in the 'lang' folder. It appears in the Language list the next time you open Settings. Missing keys fall back to English.",
                "Templat tersimpan di:\n{0}\n\nTerjemahkan nilainya, isi \"_name\" dan \"_culture\", lalu simpan sebagai <kode>.json (misalnya ms.json) di folder 'lang'. Bahasa itu muncul di daftar Bahasa saat Pengaturan dibuka berikutnya. Kunci yang tidak ada memakai teks Inggris." } },
            { "set.budget.day",   new[] { "Daily budget, estimated USD (0 = off)", "Anggaran harian, estimasi USD (0 = mati)" } },
            { "set.budget.month", new[] { "Monthly budget, estimated USD (0 = off)", "Anggaran bulanan, estimasi USD (0 = mati)" } },
            { "set.budget.note",  new[] {
                "Budgets use the estimated cost at API list price, not your bill. Alerts fire at 80% and 100% of each budget.",
                "Anggaran memakai estimasi biaya setara harga API, bukan tagihan Anda. Notifikasi muncul pada 80% dan 100% tiap anggaran." } },
            { "set.hooks.stop",   new[] { "Notify when a long response finishes", "Beri notifikasi saat respons yang panjang selesai" } },
            { "set.hooks.secs",   new[] { "Counts as long after (seconds)", "Dianggap panjang setelah (detik)" } },
            { "set.hooks.input",  new[] { "Notify when Claude needs your input or permission", "Beri notifikasi saat Claude butuh input atau izin Anda" } },
            { "set.hooks.install",new[] { "Install hooks in Claude Code...", "Pasang hook di Claude Code..." } },
            { "set.hooks.remove", new[] { "Remove hooks", "Lepas hook" } },
            { "set.hooks.on",     new[] { "Hooks are installed.", "Hook terpasang." } },
            { "set.hooks.off",    new[] { "Hooks are not installed.", "Hook belum terpasang." } },
            { "set.hooks.note",   new[] {
                "Hooks run for every Claude Code surface (terminal, IDE, desktop app) and the widget must be running to show notifications. The hook only records the time and kind of event; it stores no conversation text.",
                "Hook berjalan di semua permukaan Claude Code (terminal, IDE, desktop app), dan widget harus berjalan agar notifikasi tampil. Hook hanya mencatat waktu dan jenis peristiwa; tidak ada teks percakapan yang disimpan." } },
            { "msg.hooks.confirm",new[] {
                "This adds three hooks (UserPromptSubmit, Stop, Notification) to Claude Code's settings.json so the widget can notify you. The file is backed up first, and every other setting is left untouched. The hooks only run this app's --notify command.\n\nInstall now?",
                "Ini menambahkan tiga hook (UserPromptSubmit, Stop, Notification) ke settings.json Claude Code agar widget bisa memberi notifikasi. File dicadangkan lebih dulu dan pengaturan lain tidak disentuh. Hook hanya menjalankan perintah --notify milik aplikasi ini.\n\nPasang sekarang?" } },
            { "msg.hooks.title",  new[] { "Install Claude Code hooks", "Pasang hook Claude Code" } },
            { "msg.hooks.done",   new[] { "Hooks installed. Restart running Claude Code sessions so they take effect.", "Hook terpasang. Mulai ulang sesi Claude Code yang sedang berjalan agar berlaku." } },
            { "msg.hooks.removed",new[] { "Hooks removed.", "Hook dilepas." } },
            { "msg.hooks.fail",   new[] { "Nothing was changed: {0}", "Tidak ada yang diubah: {0}" } },
            { "set.projects",     new[] { "Show top projects (names appear only on screen and are never saved)", "Tampilkan proyek teratas (nama hanya tampil di layar dan tidak pernah disimpan)" } },
            { "set.csv",          new[] { "Export history to CSV...", "Ekspor riwayat ke CSV..." } },
            { "set.diag",         new[] { "Copy diagnostics", "Salin diagnostik" } },
            { "msg.diag.copied",  new[] { "Diagnostics copied to the clipboard. It contains no keys, project names, or file contents; review it before sharing.", "Diagnostik disalin ke clipboard. Isinya tidak memuat key, nama proyek, atau isi file; periksa dulu sebelum dibagikan." } },
            { "set.portable.note",new[] { "Portable mode: settings are stored next to the app, and autostart is unavailable. The statusline wrapper still writes its snapshot to %LOCALAPPDATA%.", "Mode portabel: pengaturan disimpan di samping aplikasi dan autostart tidak tersedia. Pembungkus statusline tetap menulis snapshot-nya ke %LOCALAPPDATA%." } },

            // ---- fitur baru: mata uang, konteks, burn rate, audio, webhook, taskbar ----
            { "context.title",     new[] { "Last prompt context  {0}", "Konteks prompt terakhir  {0}" } },
            { "context.compact_hint", new[] { "Prompt context >75% · Consider running /compact", "Konteks prompt >75% · Pertimbangkan /compact" } },
            { "burnrate.row",      new[] { "Burn rate: {0} tokens in last 10m", "Laju pemakaian: {0} token dalam 10 mnt" } },
            { "alert.burnrate",    new[] { "High token burn rate: {0} tokens in 10 minutes", "Lonjakan pemakaian: {0} token dalam 10 menit" } },
            { "alert.context",     new[] { "Prompt context is large: {0} ({1}%) · Consider /compact", "Konteks prompt besar: {0} ({1}%) · Pertimbangkan /compact" } },
            { "set.h.currency",      new[] { "Local Currency", "Mata Uang Lokal" } },
            { "set.currency.enable", new[] { "Convert cost to local currency (e.g. IDR)", "Konversi biaya ke mata uang lokal (mis. IDR)" } },
            { "set.currency.symbol", new[] { "Currency symbol", "Simbol mata uang" } },
            { "set.currency.rate",   new[] { "Exchange rate (1 USD in local currency)", "Nilai kurs (1 USD dalam mata uang lokal)" } },
            { "set.currency.both",   new[] { "Show both USD & local currency", "Tampilkan USD & mata uang lokal bersamaan" } },
            { "set.currency.google", new[] { "Fetch from Google", "Ambil dari Google" } },
            { "set.currency.google_ok", new[] { "Rate updated from Google Finance: {0}", "Kurs berhasil diperbarui dari Google Finance: {0}" } },
            { "set.currency.google_fail", new[] { "Failed to fetch rate from Google Finance.", "Gagal mengambil kurs dari Google Finance." } },
            { "set.taskbar.show",    new[] { "Show button and limit progress in taskbar", "Tampilkan tombol dan progres limit di taskbar" } },
            { "set.audio.enable",    new[] { "Play sound effects for notifications and alerts", "Bunyikan efek suara untuk notifikasi dan alert" } },
            { "set.context.enable",  new[] { "Notify when session context is large (>75%)", "Beri notifikasi saat konteks sesi besar (>75%)" } },
            { "set.burnrate.enable", new[] { "Notify on sudden token consumption spikes", "Beri notifikasi saat lonjakan pemakaian token" } },
            { "set.burnrate.th",     new[] { "Spike threshold (tokens in 10 minutes)", "Ambang lonjakan (token dalam 10 menit)" } },
            { "set.webhook.enable",  new[] { "Send alerts to Webhook (Discord / Slack / Telegram)", "Kirim alert ke Webhook (Discord / Slack / Telegram)" } },
            { "set.webhook.url",     new[] { "Webhook URL", "URL Webhook" } },
            { "set.h.monitoring",    new[] { "Active Monitoring Providers", "Penyedia Monitoring Aktif" } },
            { "set.claude",          new[] { "Monitor Claude Code (local projects & CLI limits)", "Pantau Claude Code (proyek lokal & limit CLI)" } },
            { "set.gemini",          new[] { "Monitor Google Antigravity / Gemini (brain logs & limits)", "Pantau Google Antigravity / Gemini (log brain & limit)" } },
            { "set.claude_api",      new[] { "Monitor Claude API (monthly org cost via Admin API)", "Pantau Claude API (biaya org bulanan via Admin API)" } },
            { "set.noproviders",     new[] { "No monitoring providers enabled. Enable Claude or Gemini in Settings.", "Tidak ada monitoring aktif. Aktifkan Claude atau Gemini di Pengaturan." } },
            { "set.pricing.fetch",   new[] { "Update from Official Sources", "Ambil dari Sumber Resmi" } },
            { "set.pricing.fetching",new[] { "Updating...", "Mengambil..." } },
            { "set.pricing.fetch_ok",new[] { "Pricing updated from official sources ({0} models).", "Tabel harga berhasil diperbarui dari sumber resmi ({0} model)." } },
            { "set.pricing.fetch_fail", new[] { "Failed to fetch pricing from official sources. Existing pricing kept.", "Gagal mengambil harga dari sumber resmi. Tabel harga saat ini tetap digunakan." } },
            { "usage.claude",        new[] { "Claude  {0} tokens", "Claude  {0} token" } },
            { "usage.antigravity",   new[] { "Antigravity  {0} tokens", "Antigravity  {0} token" } },
            { "usage.claude.week",   new[] { "Claude 7d  {0} tokens", "Claude 7h  {0} token" } },
            { "usage.antigravity.week", new[] { "Antigravity 7d  {0} tokens", "Antigravity 7h  {0} token" } },
        };
    }
}
