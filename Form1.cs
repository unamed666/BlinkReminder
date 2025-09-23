using BlinkReminder.Properties;
using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BlinkReminder
{
    public partial class Form1 : Form
    {
        private NotifyIcon trayIcon;
        private SettingsManager settings;
        // === P/Invoke untuk drag (tanpa title bar) ===
        [DllImport("user32.dll")] private static extern bool ReleaseCapture();
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);
        private const int WM_NCLBUTTONDOWN = 0xA1;
        private const int HTCAPTION = 2;
        private ToolStripMenuItem _toggleItemTray;
        private ToolStripMenuItem _toggleItemCtx;
        int blinkspeed = 2;
        int visiblespeed = 3;
        bool aktif = false;
        string kedip = "Not Blinking";
        // === Toggle Mode ===
        private bool _mode = false; // ganti ke false jika ingin mulai dalam kondisi non-aktif
        public bool Mode
        {
            get => _mode;
            set
            {
                if (_mode == value) return;
                _mode = value;

                // Persist langsung
                settings.Mode = value;
                settings.SaveSettings();

                UpdateMode();
            }
        }

        // Context menu selalu aktif
        private ContextMenuStrip _ctx;
        private ToolStripMenuItem _resetPosItem;
        private ToolStripMenuItem _resetSizeItem;
        private ToolStripMenuItem _ExitItem;


        // Tipe kontrol yang dikecualikan dari drag
        private static readonly Type[] DragExclusions = new[]
        {
            typeof(TextBoxBase), typeof(ComboBox), typeof(NumericUpDown),
            typeof(ListBox), typeof(CheckedListBox), typeof(DataGridView),
            typeof(TreeView), typeof(ListView), typeof(RichTextBox), typeof(WebBrowser)
        };

        // Handler drag yang bisa dipasang ke banyak kontrol
        private MouseEventHandler _dragMouseDownHandler;
        private readonly Timer _blinkTimer = new Timer();
        private bool _phaseVisible = true;

        public Form1()
        {
            InitializeComponent();
            // INISIALISASI TIMER KEDIP — gantikan BlinkLoop
            _blinkTimer.Interval = 500; // fase tampil 0.5s
            _blinkTimer.Tick += (s, e) =>
            {
                if (!Mode) { _blinkTimer.Stop(); this.Opacity = 1.0; return; } // 1.0 bukan 100 ya nyaaa~
                if (_phaseVisible)
                {
                    this.Opacity = 0.0;
                    _blinkTimer.Interval = blinkspeed; // fase sembunyi 1s
                    _phaseVisible = false;
                }
                else
                {
                    this.Opacity = 1.0;
                    _blinkTimer.Interval = visiblespeed;  // fase tampil 0.5s
                    _phaseVisible = true;
                }
            };

            // === NotifyIcon (system tray) ===
            trayIcon = new NotifyIcon();
            trayIcon.Icon = SystemIcons.Application;   // bisa diganti .ico custom
            trayIcon.Visible = true;
            trayIcon.Text = "BlinkReminder";

            // Buat menu system tray
            ContextMenuStrip trayMenu = new ContextMenuStrip();

            // Item tampilkan
            // ✅ CONTEXT MENU — gunakan _ctx
            _ctx = new ContextMenuStrip();

            _resetPosItem = new ToolStripMenuItem("Reset Posisi ke Tengah", null, (_, __) => { if (_mode) ResetPositionCenter(); });
            _resetSizeItem = new ToolStripMenuItem("Reset Ukuran ke 300x300", null, (_, __) => { if (_mode) ResetSize300(); });
            _ExitItem = new ToolStripMenuItem("EXIT", null, (_, __) => { Application.Exit(); });

            _ctx.Items.AddRange(new ToolStripItem[] { _resetPosItem, _resetSizeItem, _ExitItem });
            _ctx.Items.Add(new ToolStripSeparator());

            // Toggle khusus CONTEXT MENU
            _toggleItemCtx = new ToolStripMenuItem("Switch Menu");
            _toggleItemCtx.Click += (_, __) =>
            {
                Mode = !Mode;
                if (!Mode)
                    this.Size = new Size(413, 205);
                else if (settings.WinWidth > 0 && settings.WinHeight > 0)
                    this.Size = new Size(settings.WinWidth, settings.WinHeight);

                hidepanel();
                settings.SaveSettings();
            };
            _ctx.Items.Add(_toggleItemCtx);

            // Pasang ke form & sebar ke child controls
            this.ContextMenuStrip = _ctx;
            AttachContextMenuRecursively(this, _ctx);



            // Item keluar
            trayMenu.Items.Add("Keluar", null, (s, e) => { Application.Exit(); });
            trayMenu.Items.Add(kedip, null, (s, e) => 
            { 
                aktif = !aktif;
                kedip = aktif ? "Blinking" : "Not Blinking";
                label4.Text = kedip;
                ((ToolStripMenuItem)s).Text = kedip; // update nama item di tray
                
            });

            // Pasang menu ke tray
            trayIcon.ContextMenuStrip = trayMenu;

            // Sembunyikan dari taskbar
            this.ShowInTaskbar = false;

            // buat manager setting dan load dari file
            settings = new SettingsManager();
            settings.LoadSettings();            
            // Sinkron awal ke variabel & UI, nyaa~
            blinkspeed = settings.BlinkSpeed;
            visiblespeed = settings.VisibleSpeed;

            if (txtBlink != null) txtBlink.Text = blinkspeed.ToString();
            if (txtVisible != null) txtVisible.Text = visiblespeed.ToString();

            // Sinkron awal dari file ke field
            _mode = settings.Mode;  // langsung ke field agar tidak Save di constructor
            hidepanel();            // opsional
            UpdateMode();           // ini yang start/stop timer sesuai Mode
            // Simpan bounds saat form dipindah atau diresize
            this.Move += (s, e) => PersistWindowBounds();
            this.ResizeEnd += (s, e) => PersistWindowBounds();
            
            if (settings.WinLeft >= 0 && settings.WinTop >= 0)
            {
                this.StartPosition = FormStartPosition.Manual;
                this.Location = new Point(settings.WinLeft, settings.WinTop);
            }

            if (!Mode)
                this.Size = new Size(413, 205);
            else if (settings.WinWidth > 0 && settings.WinHeight > 0)
                this.Size = new Size(settings.WinWidth, settings.WinHeight);
            btnColor.Text = settings.Color;

            // contoh: update UI sesuai data
            this.BackColor = ColorTranslator.FromHtml(settings.Color); // sekarang aman
            this.Text = $"Mode = {settings.Mode}";



            // Selalu borderless agar saat Mode=false tidak ada resize/move bawaan OS
            this.FormBorderStyle = FormBorderStyle.None;
            this.ControlBox = false;
            this.DoubleBuffered = true;
            this.StartPosition = FormStartPosition.CenterScreen;




            // ✅ Toggle khusus TRAY
            _toggleItemTray = new ToolStripMenuItem("Switch Menu");
            _toggleItemTray.Click += (_, __) =>
            {
                Mode = !Mode;
                if (!Mode)
                    this.Size = new Size(413, 205);
                else if (settings.WinWidth > 0 && settings.WinHeight > 0)
                    this.Size = new Size(settings.WinWidth, settings.WinHeight);

                hidepanel();
                settings.SaveSettings();
            };
            trayMenu.Items.Add(_toggleItemTray);


            // 6) Pasang ke form & ke seluruh kontrol
            this.ContextMenuStrip = _ctx;
            AttachContextMenuRecursively(this, _ctx);

            // Drag handler kiri: hanya jalan ketika Mode=true
            _dragMouseDownHandler = (s, e) =>
            {
                if (!_mode) return;
                if (e.Button == MouseButtons.Left)
                {                    
                    ReleaseCapture();
                    SendMessage(this.Handle, WM_NCLBUTTONDOWN, HTCAPTION, 0);
                    aktif = !aktif;
                    kedip = aktif ? "Blinking" : "Not Blinking";
                    label4.Text = kedip;
                    trayMenu.Items[1].Text = aktif ? "Blinking" : "Not Blinking";
                    UpdateMode();
                }
            };
            AttachDragHandlersRecursively(this);

            // Pastikan kontrol baru juga dapat context menu dan handler drag
            this.ControlAdded += (s, e) =>
            {
                if (e.Control == null) return;
                if (e.Control.ContextMenuStrip == null) e.Control.ContextMenuStrip = _ctx;
                AttachContextMenuRecursively(e.Control, _ctx);
                AttachDragHandlersRecursively(e.Control);
                e.Control.ControlAdded += (s2, e2) =>
                {
                    if (e2.Control == null) return;
                    if (e2.Control.ContextMenuStrip == null) e2.Control.ContextMenuStrip = _ctx;
                    AttachContextMenuRecursively(e2.Control, _ctx);
                    AttachDragHandlersRecursively(e2.Control);
                };
            };

            

            // Sinkronkan UI awal
            UpdateMode();
        }



        // === MODE SWITCHER ===
        private void UpdateMode()
        {
            _resetPosItem.Enabled = _mode;
            _resetSizeItem.Enabled = _mode;

            hidepanel();

            if (_mode && aktif)
            {
                _phaseVisible = true;
                this.Opacity = 1.0;
                _blinkTimer.Interval = 500;
                _blinkTimer.Start();
            }
            else
            {
                _blinkTimer.Stop();
                this.Opacity = 1.0;
            }

            this.Text = $"Mode = {settings.Mode}";
            this.Invalidate();
        }


        // === Helper context menu ===
        private void AttachContextMenuRecursively(Control root, ContextMenuStrip menu)
        {
            foreach (Control c in root.Controls)
            {
                if (c.ContextMenuStrip == null) c.ContextMenuStrip = menu;
                if (c.HasChildren) AttachContextMenuRecursively(c, menu);
            }
        }

        // === Helper drag handlers ===
        private bool IsExcluded(Control c) => DragExclusions.Any(t => t.IsInstanceOfType(c));

        private void AttachDragHandlersRecursively(Control root)
        {
            if (!IsExcluded(root))
                root.MouseDown += _dragMouseDownHandler;

            foreach (Control child in root.Controls)
                AttachDragHandlersRecursively(child);
        }

        // === Aksi menu (hanya efektif saat Mode=true, dicek di handler) ===
        private void ResetPositionCenter()
        {
            if (this.WindowState == FormWindowState.Maximized)
                this.WindowState = FormWindowState.Normal;
            this.CenterToScreen();
            PersistWindowBounds();
        }

        private void ResetSize300()
        {
            if (this.WindowState == FormWindowState.Maximized)
                this.WindowState = FormWindowState.Normal;
            this.Size = new Size(300, 300);
            PersistWindowBounds();
        }

        // === Resize tepi hanya saat Mode=true ===
        protected override void WndProc(ref Message m)
        {
            const int WM_NCHITTEST = 0x84;
            const int HTCLIENT = 1;
            const int HTLEFT = 10;
            const int HTRIGHT = 11;
            const int HTTOP = 12;
            const int HTTOPLEFT = 13;
            const int HTTOPRIGHT = 14;
            const int HTBOTTOM = 15;
            const int HTBOTTOMLEFT = 16;
            const int HTBOTTOMRIGHT = 17;

            if (_mode && m.Msg == WM_NCHITTEST)
            {
                base.WndProc(ref m);
                if ((int)m.Result == HTCLIENT)
                {
                    Point pt = PointToClient(Cursor.Position);
                    int grip = 10;

                    bool left = pt.X <= grip;
                    bool right = pt.X >= Width - grip;
                    bool top = pt.Y <= grip;
                    bool bottom = pt.Y >= Height - grip;

                    if (left && top) { m.Result = (IntPtr)HTTOPLEFT; return; }
                    if (right && top) { m.Result = (IntPtr)HTTOPRIGHT; return; }
                    if (left && bottom) { m.Result = (IntPtr)HTBOTTOMLEFT; return; }
                    if (right && bottom) { m.Result = (IntPtr)HTBOTTOMRIGHT; return; }
                    if (left) { m.Result = (IntPtr)HTLEFT; return; }
                    if (right) { m.Result = (IntPtr)HTRIGHT; return; }
                    if (top) { m.Result = (IntPtr)HTTOP; return; }
                    if (bottom) { m.Result = (IntPtr)HTBOTTOM; return; }

                    // Di area klien biasa jangan set HTCAPTION, karena drag ditangani via MouseDown kiri agar klik kanan tetap bekerja
                    return;
                }
                return;
            }

            base.WndProc(ref m);
        }

        private void btnColor_Click(object sender, EventArgs e)
        {
            using (ColorDialog dlg = new ColorDialog())
            {
                dlg.AllowFullOpen = true;    // izinkan custom color
                dlg.FullOpen = true;         // langsung buka palet penuh
                dlg.Color = this.BackColor;  // warna awal

                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    // Pakai HEX #RRGGBB agar konsisten, hindari nama warna seperti "Red" nyaaa~
                    string hex = $"#{dlg.Color.R:X2}{dlg.Color.G:X2}{dlg.Color.B:X2}";

                    this.BackColor = dlg.Color;      // terapkan ke UI langsung nyaaa~
                    settings.Color = hex;            // simpan ke setting nyaaa~
                    settings.SaveSettings();         // tulis ke settings.txt nyaaa~
                    btnColor.Text = hex;             // tampilkan di tombol nyaaa~
                }
            }
        }
        private void PersistWindowBounds()
        {
            if (this.WindowState == FormWindowState.Normal)
            {
                settings.WinLeft = this.Left;
                settings.WinTop = this.Top;
                settings.WinWidth = this.Width;
                settings.WinHeight = this.Height;
                settings.SaveSettings();   // inilah tempat SaveSettings dipanggil
            }
        }


        void hidepanel()
        {
            if (Mode)
            {
                panel1.Visible = false;
                label4.Visible = false;
            }
            else
            {
                panel1.Visible = true;
                label4.Visible = true;
            }
        }

        private void txtBlink_TextChanged(object sender, EventArgs e)
        {
            if (int.TryParse(txtBlink.Text, out var val))
            {
                blinkspeed = Math.Max(0, val);          // jaga-jaga, nyaa~
                settings.BlinkSpeed = blinkspeed;
                settings.SaveSettings();
                // kalau sedang fase sembunyi/tampil, interval akan diset di tick berikutnya, nyaa~
            }
        }

        private void txtVisible_TextChanged(object sender, EventArgs e)
        {
            if (int.TryParse(txtVisible.Text, out var val))
    {
        visiblespeed = Math.Max(0, val);        // jaga-jaga, nyaa~
        settings.VisibleSpeed = visiblespeed;
        settings.SaveSettings();
    }
        }

        private void panel1_MouseClick(object sender, MouseEventArgs e)
        {
            aktif = !aktif;
            kedip = aktif ? "Blinking" : "Not Blinking";
            label4.Text = kedip;
            trayIcon.ContextMenuStrip.Items[1].Text = aktif ? "Blinking" : "Not Blinking";
        }

        private void label4_MouseClick(object sender, MouseEventArgs e)
        {
            aktif = !aktif;
            kedip = aktif ? "Blinking" : "Not Blinking";
            label4.Text = kedip;
            trayIcon.ContextMenuStrip.Items[1].Text = aktif ? "Blinking" : "Not Blinking";
        }
    }
    public class SettingsManager
    {
        public int BlinkSpeed { get; set; } = 2000;   // default ms
        public int VisibleSpeed { get; set; } = 1000;
        public int WinLeft { get; set; } = -1; // -1 artinya belum terset
        public int WinTop { get; set; } = -1;
        public int WinWidth { get; set; } = 800;
        public int WinHeight { get; set; } = 600;

        private string _filePath = "settings.txt";

        public string Color { get; set; } = "#FFFFFF";
        
        public bool Mode { get; set; } = false;

        public void SaveSettings()
        {
            using (StreamWriter writer = new StreamWriter(_filePath, false))
            {
                writer.WriteLine($"color={Color}");
                writer.WriteLine($"mode={Mode.ToString().ToLower()}");
                writer.WriteLine($"left={WinLeft}");
                writer.WriteLine($"top={WinTop}");
                writer.WriteLine($"width={WinWidth}");
                writer.WriteLine($"height={WinHeight}");
                writer.WriteLine($"blink={BlinkSpeed}");      // NEW, nyaa~
                writer.WriteLine($"visible={VisibleSpeed}");  // NEW, nyaa~
            }
        }

        public void LoadSettings()
        {
            if (!File.Exists(_filePath))
            {
                SaveSettings(); // buat file default
                return;
            }

            foreach (var line in File.ReadAllLines(_filePath))
            {
                var parts = line.Split('=');
                if (parts.Length != 2) continue;

                string key = parts[0].Trim().ToLower();
                string value = parts[1].Trim();

                if (key == "color")
                {
                    var v = value.Trim();
                    if (!v.StartsWith("#")) v = "#" + v; // normalisasi
                    Color = v;
                }

                if (key == "mode" && bool.TryParse(value, out bool result))
                    this.Mode = result;           // kalau mau langsung sinkron ke UI
                       

                if (key == "left" && int.TryParse(value, out var l)) WinLeft = l;
                if (key == "top" && int.TryParse(value, out var t)) WinTop = t;
                if (key == "width" && int.TryParse(value, out var w)) WinWidth = w;
                if (key == "height" && int.TryParse(value, out var h)) WinHeight = h;
                if (key == "blink" && int.TryParse(value, out var bs)) BlinkSpeed = bs;       
                if (key == "visible" && int.TryParse(value, out var vs)) VisibleSpeed = vs;  

            }
        }
    }
}
