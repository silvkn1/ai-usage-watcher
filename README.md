# AI Usage Watcher (Windows native)

Widget kecil untuk memantau pemakaian AI (Claude Code & Google Gemini Antigravity): **ikon angka di taskbar** + **jendela mengambang**.
C# WinForms di .NET Framework 4.8 (bawaan Windows 10/11): satu exe sekitar 200 KB, tanpa install runtime, tanpa NuGet.

| Sumber | Isi | Kapan update |
|---|---|---|
| Log lokal Claude Code (`~/.claude/projects/**/*.jsonl`) | Token hari ini & 7 hari per model, estimasi biaya Claude | Saat file log berubah, plus tiap 1 menit |
| Log lokal Gemini Antigravity (`~/.gemini/antigravity/brain/**/transcript.jsonl`) | Token hari ini & 7 hari model Gemini Flash/Pro, estimasi biaya | Real-time file watcher & saat berkas log berubah |
| Statusline Claude Code CLI (`rate_limits`) | Sisa limit 5 jam & mingguan + waktu reset (Pro/Max/Ultra) | Saat Claude Code CLI dipakai; jam snapshot ditampilkan |
| Probe CLI (opsional) | Limit yang sama, saat Anda memakai Claude Code **desktop** | Saat ada chat baru, maksimal 1x per 10 menit (bisa diatur) |
| Admin API (opsional, **mati secara default**) | Biaya org bulan ini per model | 1x/jam |

Ketiga sumber ditampilkan **terpisah dan tidak dijumlah**, karena token Claude Code yang memakai API key org bisa muncul di dua sumber.

## Pasang di komputer lain (per user, tanpa hak admin)

**Syarat**
- Windows 10 versi 1903 ke atas atau Windows 11 (.NET Framework 4.8 sudah bawaan).
- Claude Code / Google Antigravity: CLI, atau cukup Claude/Antigravity desktop app.
- Untuk angka limit Claude: login **langganan Pro/Max/Ultra**. Pengguna API key hanya mendapat token & estimasi biaya.

**Langkah**
1. Ambil `AIUsageWatcher-<versi>.zip` (dibuat dengan `build.ps1 -Package`), lalu ekstrak.
2. Jalankan:
   ```powershell
   powershell -NoProfile -ExecutionPolicy Bypass -File .\install.ps1 -Statusline
   ```
   Opsi: `-Autostart` (jalan saat Windows mulai), `-NoLaunch`, `-InstallDir <path>`.
   - Program dipasang ke `%LOCALAPPDATA%\Programs\AIUsageWatcher` dan diberi shortcut Start Menu.
   - `-Statusline` memasang pembungkus statusline (lihat di bawah). `settings.json` di-backup dulu.
3. Pengguna desktop app: buka *Pengaturan* lalu aktifkan **probe CLI** agar limit ikut update saat chat di desktop. Probe memakai sedikit kuota, jadi default-nya mati.
4. Melepas: `uninstall.ps1` (tambahkan `-RemoveData` untuk menghapus config & data).

**Default untuk pengguna baru**: probe CLI, salinan ke file lain (Windhawk), Admin API, pemindaian WSL, dan hotkey semuanya **mati**. Yang aktif: pemindaian log lokal Claude & Gemini, notifikasi ambang 80%/95%, selalu di atas, dan sembunyi saat layar penuh.

**Catatan IT**: exe tidak bertanda tangan digital, jadi SmartScreen/antivirus bisa memperingatkan saat zip diunduh. Jangan diakali (misalnya dengan `Unblock-File`). Perlu diketahui: perintah pasang memakai `-ExecutionPolicy Bypass` untuk proses itu saja, sehingga pemeriksaan file unduhan untuk skrip ikut terlewati. Karena itu, persetujuan tim IT atas paket ini yang menjadi pengaman sebenarnya. Minta tim IT menandatangani dengan sertifikat perusahaan atau mendistribusikannya lewat jalur resmi. Alternatifnya, build sendiri dari sumber dengan `build.ps1`. Kebijakan ExecutionPolicy dari GPO tetap berlaku.

## Build

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 -Package
```
Hasil: `dist\AIUsageWatcher.exe`, skrip, zip paket, dan `SHA256SUMS.txt` (hash zip dan exe untuk dicantumkan di catatan rilis; ini checksum, bukan tanda tangan penerbit). Memakai `csc.exe` bawaan `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\`. Manifest winget baru bisa dibuat setelah ada URL rilis publik.

## Statusline (limit dari Claude Code CLI)

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File install-statusline.ps1 [-Shell auto|bash|powershell] [-Uninstall]
```
- Wrapper dipilih seperti aturan Claude Code di Windows: **Git Bash** bila terpasang (`cuw-statusline.sh`, sekitar 0,8 detik), selain itu **PowerShell** (`cuw-statusline.ps1`, sekitar 2,5 detik).
- Statusline lama **tetap jalan**. Perintahnya disimpan di `%LOCALAPPDATA%\AIUsageWatcher\statusline-chain.txt` dan diberi input yang sama persis (diuji byte demi byte, termasuk teks non-ASCII).
- Yang disimpan hanya objek `rate_limits` dan waktu tangkap.

**Ikon selalu terlihat (Win 11):** Settings → Personalization → Taskbar → *Other system tray icons* → aktifkan **AIUsageWatcher**.

## Pemakaian

- **Ikon tray**: satu angka besar, yaitu persen limit **5 jam**, memenuhi lencana berwarna (hijau <50, kuning 50-79, merah >=80; abu-abu `--` = belum ada data atau jendela sudah reset). Bila limit **mingguan sudah penuh** (100%), ikon menampilkan `100` mingguan, ditandai garis putih di bawah angka. Dalam mode "sisa", angka menampilkan persen tersisa (warna tetap menurut persen terpakai). Font: Arial Narrow Bold bila ada (cadangan Arial Bold), dirender tanpa anti-alias di ukuran kecil agar tajam di 16 px.
- **Tooltip tray**: tiga baris. 5h dan 7d masing-masing dengan persen, sisa waktu, dan **jam reset** (mis. "resets in 4h 4m (22 Nov 01:47)"; tanggal ikut tampil bila reset jatuh di hari lain), lalu galat probe terakhir atau biaya hari ini. Batas 63 karakter bawaan Windows Forms diperluas hingga 127 lewat field privat NotifyIcon; bila gagal, kembali ke 63 karakter.
- **Klik kiri ikon tray**: membuka **jendela detail tepat di atas ikon yang diklik** (dipusatkan pada ikon, menempel di atas taskbar; taskbar di sisi lain menyesuaikan). Jendela ini tidak menyentuh widget yang ter-pin di tepi. Tertutup bila klik di luar, tekan Esc, atau klik ikon lagi. Klik kanan: menu (tampilkan widget, tempel tepi atas, ringkas, sisa, klik-tembus, refresh, pengaturan, folder data, autostart, keluar).
- **Jam reset**: di widget, jendela detail, notifikasi, dan tooltip, sisa waktu reset disertai jam sebenarnya (mis. "resets in 4h 5m (22 Nov 01:50)").
- **Widget**: seret untuk memindah (posisi diingat), klik ganda untuk mode ringkas/detail, klik kanan untuk menu.
- **Klik-tembus**: widget tidak bisa diklik. Matikan lagi lewat menu ikon tray.
- **Bahasa** (Pengaturan → Language): English (default) atau Bahasa Indonesia. Berlaku untuk widget, menu tray, tooltip, dan dialog Pengaturan; berganti seketika setelah Simpan. Format angka, satuan (K/M/B atau rb/jt/M), dan durasi mengikuti bahasa. Pesan galat lama baru diganti setelah refresh berikutnya. Teks skrip pemasang dan README belum dilokalisasi.
- **Tema** (Pengaturan): gelap (default), terang, atau **Ikuti Windows** (mengikuti pengaturan mode aplikasi Windows, diperiksa tiap detik). Tema berlaku untuk **widget, dialog Pengaturan (termasuk title bar dan scrollbar), dan menu klik kanan** (di widget maupun ikon tray). Kotak pesan Windows (konfirmasi dan peringatan) tidak bisa ditema dan tetap bergaya sistem.
- **Penanda laju (pace)**: garis putih di tiap bar menandai seberapa jauh jendela waktu berjalan. Bila isi bar melewati garis, pemakaian lebih cepat dari waktu. Bila laju saat ini diperkirakan menghabiskan limit sebelum reset, muncul baris "Dengan laju ini limit tercapai dalam …". Prakiraan dihitung dari saat snapshot ditangkap, dan disembunyikan bila data masih sangat awal (<10% jendela), pemakaian <5%, atau snapshot basi.
- **Snapshot basi**: bila ada chat setelah snapshot limit ditangkap (lebih dari 5 menit), bar dipudarkan dan diberi catatan "lebih lama dari chat terakhir". Malam hari tanpa chat tidak dianggap basi.
- **Terpakai / tersisa**: menu tray atau Pengaturan. Isi bar, teks, tooltip, dan angka di ikon tray ikut berganti; warna tetap menurut persen terpakai.
- **Notifikasi** (default aktif pada 80% dan 95%): satu notifikasi per lompatan ambang, per jendela limit. Saat app mulai, ambang yang sudah terlewati tidak dinotifikasi. Notifikasi reset (default mati) hanya muncul untuk jendela yang sebelumnya melewati ambang. Notifikasi hanya semutakhir snapshot limit terakhir. Windows dapat menahannya bila Focus Assist aktif.
- **Riwayat 14 hari**: grafik batang biaya harian di widget. Data harian (tanggal, token, biaya, jumlah request) disimpan di `history.json` agar bertahan walau log Claude Code dihapus. Hasil pindai menimpa (bukan menambah) hari yang sama, dan biaya hari yang tersimpan memakai harga saat itu. Jendela pindai log: 30 hari.
- **Sumber data tambahan** (Pengaturan): folder konfigurasi Claude tambahan (dipisah `;`), dan opsi memindai log distro **WSL yang sedang berjalan** (tidak membangunkan distro yang mati; dicek tiap 10 menit; hanya pindai berkala 1 menit, tanpa pemantau file). Limit 5 jam/mingguan tetap hanya dari statusline atau probe folder default.
- **Hotkey global** (default mati): mis. `Ctrl+Alt+U` untuk tampil/sembunyi. Wajib ada minimal satu pengubah. Bila kombinasinya dipakai aplikasi lain, muncul peringatan saat menyimpan.
- **Selalu di atas** (default aktif): widget berada di atas jendela biasa. Posisinya ditegaskan ulang setiap kali jendela aktif berganti, bukan dengan timer tetap, agar tidak berebut dengan jendela topmost lain.
  - Batasan Windows: Start menu, Task View, pusat notifikasi, layar UAC/kunci, dan game layar penuh eksklusif tetap berada di atas widget. Mengatasinya butuh `uiAccess`, yang mensyaratkan exe bertanda tangan di Program Files, jadi tidak dipakai.
- **Sembunyi saat layar penuh** (default aktif): widget disembunyikan bila jendela aktif menutupi seluruh monitor tempat widget berada, lalu muncul lagi setelahnya.
  - Contohnya browser F11, video layar penuh, game borderless, atau mode presentasi.
  - Desktop, taskbar, dan jendela maximize biasa tidak memicunya.
  - Bila Anda memilih *Tampilkan widget* saat aplikasi layar penuh aktif, aplikasi itu dikecualikan sampai fokus berpindah.
- **Tempel di tepi atas** (menu tray atau Pengaturan): widget menempel di tepi atas layar sebagai satu baris ringkas.
  - Saat kursor diarahkan ke situ, widget terbuka ke versi lengkap. Sekitar 0,4 detik setelah kursor pergi, widget kembali ringkas.
  - Seret ke kiri/kanan untuk memindahkan; posisinya diingat.
  - Di mode ini, klik-tembus dan klik ganda dinonaktifkan karena mode ini bergantung pada hover.
- **Pengaturan** (bertab): **Tampilan** (bahasa, tema, transparansi, terpakai/sisa, tempel tepi atas, ringkas, klik-tembus), **Perilaku** (tampil saat mulai, autostart, selalu di atas, sembunyi saat layar penuh, hotkey), **Notifikasi** (ambang, notifikasi reset), **Data** (WSL, folder tambahan, harga), **Probe CLI** (peringatan, interval, salin ke file lain), dan **Admin API**. Bila ada isian yang tidak valid, dialog berpindah ke tab yang bersangkutan.

## Fitur lanjutan

- **Grafik riwayat**: arahkan kursor ke batang untuk melihat tanggal, biaya, token, dan jumlah request hari itu (batang disorot; label berubah tanpa mengubah ukuran widget). Rentang 14 atau 30 hari (Pengaturan → Tampilan).
- **Anggaran biaya** (Pengaturan → Notifikasi): anggaran harian dan bulanan dalam USD, **estimasi setara harga API, bukan tagihan** (untuk pelanggan langganan hanya sebagai ukuran pemakaian). Notifikasi pada 80% dan 100%, sekali per hari/bulan; yang sudah terlewati saat app mulai tidak dinotifikasi. Progresnya tampil sebagai baris di widget. 0 = mati. Total bulan berjalan dihitung dari `history.json`.
- **Efisiensi cache**: porsi token sisi input yang dilayani dari cache, dan **estimasi penghematan bersih** (hemat membaca cache dikurangi biaya tambahan menulis cache dibanding harga input biasa). Dicocokkan dengan hitungan PowerShell terpisah (dedup `id|requestId`).
- **Proyek teratas** (opsional, default mati): lima proyek paling mahal dalam 7 hari, dari nama folder terakhir `cwd`. Nama hanya ada di memori dan layar; tidak ditulis ke `history.json`, log, CSV, maupun diagnostik.
- **Ekspor CSV** (menu tray atau Pengaturan → Data): riwayat harian (tanggal, token, estimasi biaya, request). Dua pilihan: koma dengan titik desimal, atau **titik koma dengan koma desimal** untuk Excel yang pengaturan regionalnya memakai `;` (mis. Indonesia). Berkas berenkode UTF-8 dengan BOM.
- **Tepi tempel dan monitor**: atas, bawah, kiri, atau kanan, pada monitor yang dipilih. Widget menempel rata pada tepi area kerja dan tumbuh menjauhi tepi saat kursor di atasnya. Diuji pada satu monitor; beberapa monitor belum diuji.
- **Salin diagnostik** (Pengaturan → Data): ringkasan untuk laporan masalah. Memuat versi, OS, pengaturan non-rahasia, usia snapshot, galat terakhir, dan 25 baris log. **Tidak** memuat admin key, nama proyek, isi file, maupun path pengguna (SID, nama pengguna, dan folder profil disamarkan).
- **Mode portabel**: letakkan berkas kosong `portable.flag` di samping exe. Pengaturan, riwayat, dan log disimpan di `.\data`; autostart dinonaktifkan dan registri tidak ditulis. Batasan: pembungkus statusline tetap menulis snapshot-nya ke `%LOCALAPPDATA%\ClaudeUsageWidget` (widget membaca yang terbaru dari kedua lokasi), dan admin key terenkripsi DPAPI tidak bisa dibuka di PC atau akun lain.
- **Mata uang lokal (mis. IDR / Rupiah)** (Pengaturan → Tampilan): konversi estimasi biaya USD ke mata uang lokal dengan kurs dan simbol kustom (default kurs `16.200` dengan simbol `Rp`). Pilihan menampilkan kedua nilai `$ (Rp)` atau hanya mata uang lokal.
- **Pemantauan konteks sesi & peringatan `/compact`**: melacak ukuran konteks input percakapan terakhir terhadap batas jendela 200k token. Menampilkan bar persentase dan peringatan saat konteks melebihi 75% (150k token) agar pengguna tahu kapan waktu tepat menjalankan `/compact`.
- **Deteksi lonjakan pemakaian (Burn rate)** (Pengaturan → Notifikasi): menghitung pemakaian token dalam 10 menit terakhir dan memicu notifikasi jika konsumsi melonjak di atas ambang (default 100k token/10 menit).
- **Efek suara (Audio alerts)** (Pengaturan → Notifikasi): memainkan suara sistem Windows saat ada peringatan limit, budget terlampaui, Claude butuh izin/input, atau respons panjang selesai.
- **Integrasi Webhook (Discord/Slack/Telegram)** (Pengaturan → Notifikasi): mengirimkan notifikasi secara otomatis ke URL webhook saat limit mendekati habis, budget tercapai, atau lonjakan token terdeteksi.
- **Navigasi cepat proyek (Quick jump)** (Menu tray): opsi klik untuk langsung membuka folder kerja proyek Claude aktif di Windows File Explorer atau membuka Command Prompt / Terminal tepat di direktori proyek tersebut.
- **Progres pada tombol taskbar Windows** (Pengaturan → Tampilan): menampilkan progres kuota 5 jam dan warna status (hijau <50%, kuning 50-79%, merah >=80%) langsung pada tombol taskbar Windows via `ITaskbarList3`.

### Paket bahasa (terjemahan komunitas)
Selain English dan Bahasa Indonesia, bahasa lain bisa ditambahkan tanpa mengubah kode. *Pengaturan → Tampilan → Ekspor templat terjemahan* menulis `lang\template.json` (semua kunci dengan teks Inggris). Terjemahkan nilainya, isi `_name` dan `_culture` (mis. `ms-MY`, untuk format angka dan tanggal), lalu simpan sebagai `<kode>.json` (mis. `ms.json`) di folder `lang` di samping exe atau di folder data. Bahasa itu muncul di daftar Bahasa saat Pengaturan dibuka lagi. Kunci yang hilang memakai teks Inggris, dan string dengan placeholder `{n}` yang salah otomatis memakai teks Inggris (tidak membuat widget mogok). `en` dan `id` tidak bisa ditimpa; berkas dibatasi 256 KB.

### Notifikasi dari hook Claude Code (opsional)
Widget bisa memberi notifikasi saat **respons panjang selesai** atau **Claude butuh input/izin**, lewat tiga hook (`UserPromptSubmit`, `Stop`, `Notification`). Dipasang **hanya** lewat tombol *Pasang hook di Claude Code...* (Pengaturan → Notifikasi) dengan konfirmasi.
- **Cara kerja**: tiap hook menjalankan `ClaudeUsageWidget.exe --notify <jenis>` (bentuk exec: `command` + `args`, tanpa shell sehingga tidak ada masalah quoting). Lama respons dihitung dari cap waktu prompt per sesi; **transkrip percakapan tidak dibaca**. Peristiwa ditulis ke `notify.json` (hanya jenis, lama, cap waktu), lalu widget yang berjalan menampilkan notifikasi. Perintah `--notify` selalu keluar dengan kode 0 dan tanpa keluaran, sehingga tidak pernah memblokir Claude.
- **Keamanan**: `settings.json` dicadangkan lebih dulu, hook milik pihak lain tidak disentuh, dan hasilnya diverifikasi (semua kunci lain harus identik). Pelepasan (tombol *Lepas hook*, atau otomatis oleh `uninstall.ps1`) hanya membuang entri milik widget.
- **Cakupan**: menurut dokumentasi Claude Code, hook yang sama berjalan di terminal, ekstensi IDE, dan desktop app. Widget harus berjalan agar notifikasi tampil. Sesi yang sedang berjalan perlu dimulai ulang setelah hook dipasang.
- **Pengaturan**: ambang durasi "panjang" (default 30 detik), notifikasi input, dan peredaman 15 detik untuk peristiwa sejenis. Peristiwa yang lebih tua dari 90 detik diabaikan.

## Probe limit via CLI (eksperimental, opsional)

> **Peringatan ketentuan.** Probe menjalankan Claude Code (`claude -p`) secara otomatis dengan langganan Anda. [Consumer Terms](https://www.anthropic.com/legal/consumer-terms) Anthropic melarang akses "otomatis atau non-manusia" kecuali lewat API key atau bila diizinkan eksplisit. [Dokumentasi legal Claude Code](https://code.claude.com/docs/en/legal-and-compliance) tidak menyebut mode `-p`. Status kepatuhannya **belum dipastikan**. Fitur ini mati secara default, meminta konfirmasi setiap kali dinyalakan, dan digunakan atas tanggung jawab pengguna. Konfirmasi ke Anthropic atau Legal bila ragu. Bagian lain widget (log lokal, statusline, Admin API) tidak memakai mekanisme ini.

Desktop app **tidak menjalankan statusline**, dan log percakapan tidak memuat data limit. Karena itu ada opsi terpisah *Pengaturan → Probe limit via CLI*.

- **Cara kerja**: saat log Claude Code bertambah (ada chat), widget menjalankan
  `claude -p ok --model claude-haiku-5-5 --output-format stream-json --verbose --no-session-persistence --strict-mcp-config --tools "" --settings {"disableAllHooks":true}`.
  Dari output-nya, widget membaca event `rate_limit_event` (`unifiedWindows.five_hour/seven_day`).
- **Pengaman**:
  - Hanya terpicu bila ada chat baru.
  - Maksimal 1 probe per N menit (default 10), dan dilewati bila snapshot masih lebih baru dari N menit.
  - Probe tidak menyimpan sesi (tidak menulis JSONL), jadi tidak memicu dirinya sendiri.
  - Hook, MCP, dan tool dimatikan, sehingga plugin seperti claude-mem tidak ikut berjalan.
- **Biaya**: tiap probe memakai sedikit kuota langganan, terukur sekitar 7 ribu token (sebagian besar cache) dan sekitar 8 detik.
- **Risiko**: `rate_limit_event` **tidak terdokumentasi resmi** dan bisa berubah saat Claude Code diperbarui. Bila gagal, widget menampilkan "probe CLI gagal: ..." dan tetap memakai snapshot terakhir.
- `--bare` tidak dipakai, karena mode itu tidak memakai login langganan.
- **Lokasi CLI** dicari berurutan: `~\.local\bin\claude.exe`, lalu `claude.exe`/`claude.cmd` di PATH, lalu npm (`%APPDATA%\npm\claude.cmd`), lalu Claude Code yang dibundel desktop app (versi terbaru).
- **Pesan gagal dibuat spesifik**, misalnya "login bukan langganan", "CLI tidak ditemukan", atau "output tidak dikenali (versi …)".

### Salin limit ke file lain (mis. mod Windhawk)
*Pengaturan → Salin limit hasil probe ke file JSON lain.* Setiap probe berhasil, widget mengganti **hanya** nilai `"rate_limits"` di file tersebut. Isi lainnya tetap identik byte demi byte.
- File ditukar secara atomik.
- File tidak pernah dibuat baru.
- Penulisan dilewati bila file sedang ditulis program lain (JSON tidak valid).

Default mati.

## Biaya = estimasi

Biaya dihitung dari token × tabel `%LOCALAPPDATA%\ClaudeUsageWidget\pricing.json`, **setara harga API (list price)**. Bila Anda memakai langganan Pro/Max, angka ini **bukan uang yang keluar**.
- Nilai berlabel `verifikasi` di file diturunkan dari kelipatan standar, bukan dibaca langsung. Cocokkan dengan https://platform.claude.com/docs/en/about-claude/pricing. Perubahan file diterapkan otomatis dalam 1 menit.
- Yang tidak dimodelkan: mode *fast* (jumlahnya ditampilkan), tier >100K prompt Haiku 5.5, dan diskon batch.

## Admin API (opsional)

- Butuh admin key `sk-ant-admin...`, yang hanya bisa dibuat **Admin org** di Claude Console. Akun individu tidak punya.
- Key disimpan **terenkripsi DPAPI** (CurrentUser) di `config.json`, tidak pernah plaintext.
- Endpoint utama: `GET /v1/organizations/analytics/cost` (beta `analytics-usage-cost-2026-08-24`). Cadangannya `GET /v1/organizations/cost_report` (legacy). `amount` dibaca sebagai sen USD.
- Isi filter *API key ID* / *workspace ID* agar hanya usage Anda yang tampil. Nama parameter filter untuk endpoint analytics belum diverifikasi terhadap dokumentasi; bila hasilnya tidak terfilter, cek halaman usage & cost API.
- Data Admin API tertunda 4 sampai 24 jam.
- Org Claude Enterprise (claude.ai) memakai key `read:analytics` dari claude.ai, bukan `sk-ant-admin`.

## Verifikasi

| Cek | Cara |
|---|---|
| Token harian | `ClaudeUsageWidget.exe --dump hasil.txt`, lalu bandingkan dengan `npx ccusage daily` |
| % limit | Bandingkan dengan `/usage` di Claude Code |
| Admin API | Halaman Usage/Cost di Claude Console |
| Sumber daya | Terukur (Win 11, 443 file log / 371 MB): scan awal ~1,3 dtk; setelah 5 menit dengan sesi Claude Code aktif, memori privat ~46 MB, CPU ~2,7 dtk per 5 menit (<1% satu core). Masih di atas target awal <40 MB |

Dedup memakai `message.id + requestId` lintas file (seperti ccusage), dan hari dihitung dengan **waktu lokal**.

## Keamanan & privasi

- Tidak memproses data peserta. Semua data tetap di PC. App hanya **membaca** log lokal.
- **PDP**: log Claude Code bisa berisi apa pun yang ditempel pengguna, termasuk data pribadi. Widget hanya mengambil field `usage`, `model`, dan `timestamp`. Isi pesan dibuang sebelum diproses dan tidak pernah disimpan. Probe hanya mengirim teks "ok".
- Koneksi keluar hanya HTTPS ke `api.anthropic.com`, dan hanya bila Admin API diaktifkan.
- Snapshot statusline hanya menyimpan objek `rate_limits` dan waktu tangkap, bukan cwd, path, atau isi sesi.
- Log `widget.log` hanya berisi jenis kejadian/eksepsi, tanpa isi prompt atau path proyek. Maksimal 256 KB.
- Lokasi data: `%LOCALAPPDATA%\ClaudeUsageWidget\` (`config.json`, `pricing.json`, `ratelimits.json`, `history.json`, `widget.log`, skrip statusline).
- **Backup** tidak diperlukan: data dibangun ulang dari JSONL & Admin API.
- **Penghapusan total**: `uninstall.ps1 -RemoveData`. Skrip ini melepas statusline, autostart, shortcut, file program, dan seluruh data di atas.
- **Lock-in**: tidak ada. Kode sumber terbuka di repo ini dan bisa di-build ulang dengan `csc` bawaan Windows.
- Exe tanpa tanda tangan digital bisa diperingatkan SmartScreen/antivirus kantor. **Koordinasikan dengan tim IT sebelum dibagikan ke orang lain.**

## Struktur

```
src/Common.cs        path, log minimal, helper JSON & format
src/Config.cs        config.json, DPAPI, autostart
src/Usage.cs         tabel harga + pemindai JSONL inkremental
src/Sources.cs       pembaca rate_limits + Admin API
src/Probe.cs         probe CLI, tulis atomik, salinan ke file lain
install.ps1          pasang per user (+ shortcut, autostart, statusline)
uninstall.ps1        lepas semuanya (opsional hapus data)
src/FloatingForm.cs  jendela mengambang (custom paint, DPI-aware)
src/TrayApp.cs       ikon tray dinamis, timer, watcher
src/SettingsForm.cs  dialog pengaturan
statusline/          pembungkus statusline (bash & PowerShell) + installer
pricing.json         tabel harga bawaan (di-embed ke exe)
```
