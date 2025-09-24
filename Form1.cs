using BlinkReminder.Properties;
using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Policy;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BlinkReminder
{
    public partial class Form1 : Form
    {
        private NotifyIcon trayIcon;
        private SettingsManager settings;

        // === P/Invoke for window dragging (no title bar) ===
        [DllImport("user32.dll")] private static extern bool ReleaseCapture();
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);
        private const int WM_NCLBUTTONDOWN = 0xA1;
        private const int HTCAPTION = 2;

        private ToolStripMenuItem _toggleItemTray;
        private ToolStripMenuItem _toggleItemCtx;

        int blinkspeed = 2000;
        int visiblespeed = 3000;
        bool aktif = false;
        string kedip = "Status: Not Blinking";

        // === Toggle Mode ===
        private bool _mode = false; // set to false to start in non-active state
        public bool Mode
        {
            get => _mode;
            set
            {
                if (_mode == value) return;
                _mode = value;

                // Persist immediately
                settings.Mode = value;
                settings.SaveSettings();

                UpdateMode();
            }
        }

        // Context menu is always active
        private ContextMenuStrip _ctx;
        private ToolStripMenuItem _resetPosItem;
        private ToolStripMenuItem _resetSizeItem;
        private ToolStripMenuItem _ExitItem;

        // Control types excluded from dragging
        private static readonly Type[] DragExclusions = new[]
        {
            typeof(TextBoxBase), typeof(ComboBox), typeof(NumericUpDown),
            typeof(ListBox), typeof(CheckedListBox), typeof(DataGridView),
            typeof(TreeView), typeof(ListView), typeof(RichTextBox), typeof(WebBrowser)
        };

        // Drag handler that can be attached to many controls
        private MouseEventHandler _dragMouseDownHandler;
        private readonly Timer _blinkTimer = new Timer();
        private bool _phaseVisible = true;


        protected override CreateParams CreateParams
        {
            get
            {
                const int WS_EX_TOOLWINDOW = 0x00000080;  // jangan tampil di Alt+Tab
                const int WS_EX_APPWINDOW = 0x00040000;  // paksa tampil di Alt+Tab

                var cp = base.CreateParams;
                cp.ExStyle |= WS_EX_TOOLWINDOW;   // tambahkan TOOLWINDOW
                cp.ExStyle &= ~WS_EX_APPWINDOW;   // hilangkan APPWINDOW
                return cp;
            }
        }

        public Form1()
        {
            InitializeComponent();
            AttachDigitsDotOnly(txtBlink);
            AttachDigitsDotOnly(txtVisible);

            // Create settings manager and load from file
            settings = new SettingsManager();
            settings.LoadSettings();

            // Initialize blink timer — replaces BlinkLoop
            _blinkTimer.Interval = 500; // visible phase 0.5s
            _blinkTimer.Tick += (s, e) =>
            {
                if (!Mode)
                {
                    _blinkTimer.Stop();
                    this.Opacity = 1.0;
                    return; // 1.0 (not 100)
                }

                if (_phaseVisible)
                {
                    this.Opacity = 0.0;
                    _blinkTimer.Interval = blinkspeed; // hidden phase 1s
                    _phaseVisible = false;
                }
                else
                {
                    this.Opacity = 1.0;
                    _blinkTimer.Interval = visiblespeed;  // visible phase 0.5s
                    _phaseVisible = true;
                }
            };

            

            // === NotifyIcon (system tray) ===
            trayIcon = new NotifyIcon();
            var exeIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            if (exeIcon != null)
                trayIcon.Icon = new Icon(exeIcon, SystemInformation.SmallIconSize); // 16x16 for tray
                                                                                    // can be replaced with a custom .ico
            trayIcon.Visible = true;
            trayIcon.Text = "Blink Reminder";

            // Create system tray menu
            ContextMenuStrip trayMenu = new ContextMenuStrip();

            // Menu items
            // Context menu — use _ctx
            _ctx = new ContextMenuStrip();

            _resetPosItem = new ToolStripMenuItem("Reset Posisi ke Tengah", null, (_, __) => { if (_mode) ResetPositionCenter(); });
            _resetSizeItem = new ToolStripMenuItem("Reset Ukuran ke 300x300", null, (_, __) => { if (_mode) ResetSize300(); });
            _ExitItem = new ToolStripMenuItem("EXIT", null, (_, __) => { Application.Exit(); });

            _ctx.Items.AddRange(new ToolStripItem[] { _resetPosItem, _resetSizeItem, _ExitItem });
            _ctx.Items.Add(new ToolStripSeparator());

            // Context menu toggle
            _toggleItemCtx = new ToolStripMenuItem("Switch Menu");
            _toggleItemCtx.Click += (_, __) =>
            {
                Mode = !Mode;
                if (!Mode)
                {
                    this.Size = new Size(430, 277);
                }
                else if (settings.WinWidth > 0 && settings.WinHeight > 0)
                {
                    this.Size = new Size(settings.WinWidth, settings.WinHeight);
                }

                hidepanel();
                settings.SaveSettings();
            };
            _ctx.Items.Add(_toggleItemCtx);

            // Attach to form and propagate to child controls
            this.ContextMenuStrip = _ctx;
            AttachContextMenuRecursively(this, _ctx);

            // Exit menu item
            trayMenu.Items.Add("EXIT", null, (s, e) => { Application.Exit(); });
            trayMenu.Items.Add(kedip, null, (s, e) =>
            {
                aktif = !aktif;
                kedip = aktif ? "Status: Blinking" : "Status: Not Blinking";
                label4.Text = kedip;
                ((ToolStripMenuItem)s).Text = kedip; // update tray item text
                UpdateMode();
            });

            // Assign menu to tray icon
            trayIcon.ContextMenuStrip = trayMenu;

            // Hide from taskbar
            this.ShowInTaskbar = false;

            
            label4.Text = kedip;

            // Initial sync to variables and UI
            blinkspeed = settings.BlinkSpeed;
            visiblespeed = settings.VisibleSpeed;

            if (txtBlink != null) txtBlink.Text = (blinkspeed / 1000.0).ToString("0.###", CultureInfo.InvariantCulture);
            if (txtVisible != null) txtVisible.Text = (visiblespeed / 1000.0).ToString("0.###", CultureInfo.InvariantCulture);

            // Initial sync from file to fields
            _mode = settings.Mode;  // set field directly to avoid saving in constructor
            hidepanel();            // optional
            UpdateMode();           // starts/stops timer according to Mode


            if (settings.WinLeft >= 0 && settings.WinTop >= 0)
            {
                this.StartPosition = FormStartPosition.Manual;
                this.Location = new Point(settings.WinLeft, settings.WinTop);
            }

            if (!Mode)
            {
                this.Size = new Size(430, 277);
            }
            else if (settings.WinWidth > 0 && settings.WinHeight > 0)
            {
                this.Size = new Size(settings.WinWidth, settings.WinHeight);
            }

            btnColor.Text = settings.Color;

            // Persist bounds when moved or resized
            this.Move += (s, e) => PersistWindowBounds();
            this.ResizeEnd += (s, e) => PersistWindowBounds();

            // Example: update UI based on settings
            this.BackColor = ColorTranslator.FromHtml(settings.Color); // safe now
            this.Text = $"Mode = {settings.Mode}";

            // Always borderless so that when Mode=false there is no default OS resize/move
            this.FormBorderStyle = FormBorderStyle.None;
            this.ControlBox = false;
            this.DoubleBuffered = true;

            // Tray toggle
            _toggleItemTray = new ToolStripMenuItem("Switch Menu");
            _toggleItemTray.Click += (_, __) =>
            {
                Mode = !Mode;
                if (!Mode)
                {
                    this.Size = new Size(430, 277);
                }
                else if (settings.WinWidth > 0 && settings.WinHeight > 0)
                {
                    this.Size = new Size(settings.WinWidth, settings.WinHeight);
                }

                hidepanel();
                settings.SaveSettings();
            };
            trayMenu.Items.Add(_toggleItemTray);

            // Attach to form and all child controls
            this.ContextMenuStrip = _ctx;
            AttachContextMenuRecursively(this, _ctx);

            // Drag handler (left mouse): active only when Mode=true
            _dragMouseDownHandler = (s, e) =>
            {
                if (!_mode) return;
                if (e.Button == MouseButtons.Left)
                {
                    ReleaseCapture();
                    SendMessage(this.Handle, WM_NCLBUTTONDOWN, HTCAPTION, 0);
                    aktif = !aktif;
                    kedip = aktif ? "Status: Blinking" : "Status: Not Blinking";
                    label4.Text = kedip;
                    trayMenu.Items[1].Text = kedip;
                    UpdateMode();
                }
            };
            AttachDragHandlersRecursively(this);

            // Ensure newly added controls also receive context menu and drag handler
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

            // Finalize initial UI sync
            UpdateMode();
        }

        public static void AttachDigitsDotOnly(TextBox textBox)
{
    if (textBox == null) return;

    // Batasi input: hanya digit dan '.' (karakter lain ditolak)
    textBox.KeyPress += (s, e) =>
    {
        if (char.IsControl(e.KeyChar)) return;

        if (char.IsDigit(e.KeyChar)) return;

        if (e.KeyChar == '.')
        {
            // Tolak jika sudah ada '.' dan seleksi tidak sedang menimpa titik yang ada
            bool selectionHasDot = textBox.SelectedText?.IndexOf('.') >= 0;
            if (textBox.Text.IndexOf('.') >= 0 && !selectionHasDot)
            {
                e.Handled = true;
                return;
            }
            return;
        }

        e.Handled = true;
    };

    // Normalisasi setelah setiap perubahan teks (termasuk paste atau sisip di depan)
    textBox.TextChanged += (s, e) =>
    {
        string t = textBox.Text ?? string.Empty;
        int caret = textBox.SelectionStart;

        // Saring hanya digit dan satu titik
        var sb = new System.Text.StringBuilder(t.Length);
        bool dotSeen = false;
        for (int i = 0; i < t.Length; i++)
        {
            char c = t[i];
            if (char.IsDigit(c)) { sb.Append(c); continue; }
            if (c == '.')
            {
                if (!dotSeen) { sb.Append('.'); dotSeen = true; }
                continue;
            }
            // karakter lain diabaikan
        }
        string cleaned = sb.ToString();

        // Jika dimulai dengan '.' → jadikan "0." + sisa
        if (cleaned.StartsWith("."))
            cleaned = "0" + cleaned;

        // Jika digit pertama '0' dan belum ada titik sesudahnya → sisipkan titik setelah '0'
        if (cleaned.Length >= 1 && cleaned[0] == '0' && (cleaned.Length == 1 || cleaned[1] != '.'))
            cleaned = cleaned.Insert(1, ".");

        // Opsional: hindari "0.00..." (nol berulang tepat setelah titik)
        if (cleaned.Length >= 3 && cleaned.StartsWith("0."))
        {
            int i = 2;
            while (i < cleaned.Length && cleaned[i] == '0') i++;
            if (i > 2) cleaned = "0." + cleaned.Substring(i);
        }

        if (cleaned != t)
        {
            int delta = cleaned.Length - t.Length;
            textBox.Text = cleaned;
            textBox.SelectionStart = Math.Max(0, Math.Min(cleaned.Length, caret + delta));
        }
    };
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

            this.Text = $"Blink Reminder";
            this.Invalidate();
        }

        // === Context menu helper ===
        private void AttachContextMenuRecursively(Control root, ContextMenuStrip menu)
        {
            foreach (Control c in root.Controls)
            {
                if (c.ContextMenuStrip == null) c.ContextMenuStrip = menu;
                if (c.HasChildren) AttachContextMenuRecursively(c, menu);
            }
        }

        // === Drag handlers helper ===
        private bool IsExcluded(Control c) => DragExclusions.Any(t => t.IsInstanceOfType(c));

        private void AttachDragHandlersRecursively(Control root)
        {
            if (!IsExcluded(root))
                root.MouseDown += _dragMouseDownHandler;

            foreach (Control child in root.Controls)
                AttachDragHandlersRecursively(child);
        }

        // === Menu actions (effective only when Mode=true; checked in handler) ===
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

        // === Edge resize only when Mode=true ===
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

                    // In the client area do not set HTCAPTION; dragging is handled via left MouseDown so the right-click still works
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
                dlg.AllowFullOpen = true;    // allow custom colors
                dlg.FullOpen = true;         // open full palette immediately
                dlg.Color = this.BackColor;  // initial color

                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    // Use HEX #RRGGBB for consistency; avoid color names such as "Red"
                    string hex = $"#{dlg.Color.R:X2}{dlg.Color.G:X2}{dlg.Color.B:X2}";

                    this.BackColor = dlg.Color;      // apply to UI immediately
                    settings.Color = hex;            // save to settings
                    settings.SaveSettings();         // write to BlinkReminder.txt
                    btnColor.Text = hex;             // display on the button
                }
            }
        }

        private void PersistWindowBounds()
        {
            if (this.WindowState == FormWindowState.Normal)
            {
                settings.WinLeft = this.Left;
                settings.WinTop = this.Top;
                if (Mode)
                {
                    settings.WinWidth = this.Width;
                    settings.WinHeight = this.Height;
                }
                settings.SaveSettings();   // location where SaveSettings is called
            }
        }

        void hidepanel()
        {
            if (Mode)
            {
                panel1.Visible = false;
                label4.Visible = false;
                this.FormBorderStyle = FormBorderStyle.None;
            }
            else
            {
                panel1.Visible = true;
                label4.Visible = true;
                this.FormBorderStyle = FormBorderStyle.Sizable;
                this.ShowIcon = true;
            }
        }

        private void txtBlink_TextChanged(object sender, EventArgs e)
        {           
          

            if (double.TryParse(txtBlink.Text, out var val))
            {                
                double B = Math.Max(0, val);
                blinkspeed = (int)(B * 1000); // convert to milliseconds
                settings.BlinkSpeed = blinkspeed;
                settings.SaveSettings();

            }
        }

        private void txtVisible_TextChanged(object sender, EventArgs e)
        {         

            if (double.TryParse(txtVisible.Text, out var val))
            {
                double B = Math.Max(0, val);
                visiblespeed = (int)(B * 1000); // convert to milliseconds
                settings.VisibleSpeed = visiblespeed;
                settings.SaveSettings();
            }

        }

        private void panel1_MouseClick(object sender, MouseEventArgs e)
        {
            aktif = !aktif;
            kedip = aktif ? "Status: Blinking" : "Status: Not Blinking";
            label4.Text = kedip;
            trayIcon.ContextMenuStrip.Items[1].Text = kedip;
            UpdateMode();
        }

        private void label4_MouseClick(object sender, MouseEventArgs e)
        {
            aktif = !aktif;
            kedip = aktif ? "Status: Blinking" : "Status: Not Blinking";
            label4.Text = kedip;
            trayIcon.ContextMenuStrip.Items[1].Text = kedip;
            UpdateMode();
        }

        private void linkLabel1_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            System.Diagnostics.Process.Start("https://github.com/unamed666/BlinkReminder");
        }
    }

    public class SettingsManager
    {
        public int BlinkSpeed { get; set; } = 2000;   // default milliseconds
        public int VisibleSpeed { get; set; } = 1000;
        public int WinLeft { get; set; } = -1; // -1 means not set
        public int WinTop { get; set; } = -1;
        public int WinWidth { get; set; } = 300;
        public int WinHeight { get; set; } = 300;

        private string _filePath = "BlinkReminder.txt";

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
                writer.WriteLine($"blink={BlinkSpeed}");      // NEW
                writer.WriteLine($"visible={VisibleSpeed}");  // NEW
            }
        }

        public void LoadSettings()
        {
            if (!File.Exists(_filePath))
            {
                SaveSettings(); // create default file
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
                    if (!v.StartsWith("#")) v = "#" + v; // normalize
                    Color = v;
                }

                if (key == "mode" && bool.TryParse(value, out bool result))
                    this.Mode = result;           // to immediately synchronize with UI

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
