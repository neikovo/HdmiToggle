// HdmiToggle - малко приложение в системния трей, което спира и пуска
// картината към външния HDMI изход на Windows 11.
//
// Компилиране: build.cmd  (ползва вградения в Windows csc.exe, без допълнителни инсталации)
// Команден ред: HdmiToggle.exe on | off | toggle | status   (без аргумент -> икона в трея)

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

enum HdmiState { NotConnected, Off, On }

static class Display
{
    [StructLayout(LayoutKind.Sequential)]
    struct LUID { public uint LowPart; public int HighPart; }

    [StructLayout(LayoutKind.Sequential)]
    struct PathSource
    {
        public LUID adapterId;
        public uint id;
        public uint modeInfoIdx;
        public uint statusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct PathTarget
    {
        public LUID adapterId;
        public uint id;
        public uint modeInfoIdx;
        public uint outputTechnology;
        public uint rotation;
        public uint scaling;
        public uint refreshNumerator;
        public uint refreshDenominator;
        public uint scanLineOrdering;
        public uint targetAvailable;
        public uint statusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct PathInfo
    {
        public PathSource source;
        public PathTarget target;
        public uint flags;
    }

    // 64 байта; съдържанието на union-а не ни трябва, само го връщаме обратно на Windows.
    [StructLayout(LayoutKind.Sequential)]
    struct ModeInfo
    {
        public uint infoType;
        public uint id;
        public LUID adapterId;
        public ulong u0, u1, u2, u3, u4, u5;
    }

    [DllImport("user32.dll")]
    static extern int GetDisplayConfigBufferSizes(uint flags, out uint numPaths, out uint numModes);

    [DllImport("user32.dll")]
    static extern int QueryDisplayConfig(uint flags, ref uint numPaths, [In, Out] PathInfo[] paths,
        ref uint numModes, [In, Out] ModeInfo[] modes, IntPtr currentTopologyId);

    [DllImport("user32.dll")]
    static extern int SetDisplayConfig(uint numPaths, [In] PathInfo[] paths,
        uint numModes, [In] ModeInfo[] modes, uint flags);

    const uint QDC_ALL_PATHS = 1;
    const uint QDC_ONLY_ACTIVE_PATHS = 2;

    const uint SDC_TOPOLOGY_INTERNAL = 0x01;
    const uint SDC_TOPOLOGY_CLONE = 0x02;
    const uint SDC_TOPOLOGY_EXTEND = 0x04;
    const uint SDC_USE_SUPPLIED_DISPLAY_CONFIG = 0x20;
    const uint SDC_APPLY = 0x80;
    const uint SDC_SAVE_TO_DATABASE = 0x200;
    const uint SDC_ALLOW_CHANGES = 0x400;

    const uint PATH_ACTIVE = 1;
    const int ERROR_INSUFFICIENT_BUFFER = 122;

    const uint TECH_HDMI = 5;

    static bool IsOtherExternal(uint tech)
    {
        // VGA, DVI, външен DisplayPort, външен UDI, USB/докинг (indirect wired)
        return tech == 0 || tech == 4 || tech == 10 || tech == 12 || tech == 16;
    }

    static bool Query(uint flags, out PathInfo[] paths, out ModeInfo[] modes)
    {
        paths = new PathInfo[0];
        modes = new ModeInfo[0];
        for (int attempt = 0; attempt < 5; attempt++)
        {
            uint np, nm;
            if (GetDisplayConfigBufferSizes(flags, out np, out nm) != 0) return false;
            PathInfo[] p = new PathInfo[np];
            ModeInfo[] m = new ModeInfo[nm];
            int r = QueryDisplayConfig(flags, ref np, p, ref nm, m, IntPtr.Zero);
            if (r == ERROR_INSUFFICIENT_BUFFER) continue;
            if (r != 0) return false;
            Array.Resize(ref p, (int)np);
            Array.Resize(ref m, (int)nm);
            paths = p;
            modes = m;
            return true;
        }
        return false;
    }

    // Ако има свързан HDMI монитор, работим само с него. Ако няма HDMI,
    // но има друг външен монитор (DisplayPort/USB-C док), работим с него.
    static bool HdmiPresent(PathInfo[] all)
    {
        foreach (PathInfo p in all)
            if (p.target.targetAvailable != 0 && p.target.outputTechnology == TECH_HDMI) return true;
        return false;
    }

    static bool IsTarget(uint tech, bool hdmiPresent)
    {
        return hdmiPresent ? tech == TECH_HDMI : IsOtherExternal(tech);
    }

    public static HdmiState GetState()
    {
        PathInfo[] all; ModeInfo[] modes;
        if (!Query(QDC_ALL_PATHS, out all, out modes)) return HdmiState.NotConnected;
        bool hdmi = HdmiPresent(all);
        bool connected = false, active = false;
        foreach (PathInfo p in all)
        {
            if (p.target.targetAvailable == 0 || !IsTarget(p.target.outputTechnology, hdmi)) continue;
            connected = true;
            if ((p.flags & PATH_ACTIVE) != 0) active = true;
        }
        if (!connected) return HdmiState.NotConnected;
        return active ? HdmiState.On : HdmiState.Off;
    }

    public static bool TurnOff()
    {
        PathInfo[] all; ModeInfo[] allModes;
        if (!Query(QDC_ALL_PATHS, out all, out allModes)) return false;
        bool hdmi = HdmiPresent(all);

        PathInfo[] paths; ModeInfo[] modes;
        if (!Query(QDC_ONLY_ACTIVE_PATHS, out paths, out modes)) return false;

        int changed = 0, remaining = 0;
        for (int i = 0; i < paths.Length; i++)
        {
            if (IsTarget(paths[i].target.outputTechnology, hdmi))
            {
                paths[i].flags &= ~PATH_ACTIVE;
                changed++;
            }
            else remaining++;
        }
        if (changed == 0) return true; // вече е спрян

        int r = -1;
        if (remaining > 0)
        {
            r = SetDisplayConfig((uint)paths.Length, paths, (uint)modes.Length, modes,
                SDC_APPLY | SDC_USE_SUPPLIED_DISPLAY_CONFIG | SDC_ALLOW_CHANGES | SDC_SAVE_TO_DATABASE);
        }
        if (r != 0)
        {
            // Резервен вариант (или когато HDMI е единственият активен екран):
            // "само екранът на компютъра".
            r = SetDisplayConfig(0, null, 0, null, SDC_APPLY | SDC_TOPOLOGY_INTERNAL);
        }
        return r == 0;
    }

    public static bool TurnOn(bool clone)
    {
        int r = SetDisplayConfig(0, null, 0, null,
            SDC_APPLY | (clone ? SDC_TOPOLOGY_CLONE : SDC_TOPOLOGY_EXTEND));
        return r == 0;
    }
}

static class Settings
{
    const string KeyPath = @"Software\HdmiToggle";
    const string RunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string RunName = "HdmiToggle";

    public static bool Clone
    {
        get
        {
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(KeyPath))
                return k != null && Convert.ToInt32(k.GetValue("Clone", 0)) != 0;
        }
        set
        {
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(KeyPath))
                k.SetValue("Clone", value ? 1 : 0, RegistryValueKind.DWord);
        }
    }

    public static bool Autostart
    {
        get
        {
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunPath))
                return k != null && k.GetValue(RunName) != null;
        }
        set
        {
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(RunPath))
            {
                if (value) k.SetValue(RunName, "\"" + Application.ExecutablePath + "\"");
                else k.DeleteValue(RunName, false);
            }
        }
    }
}

class HotkeyWindow : NativeWindow, IDisposable
{
    [DllImport("user32.dll")]
    static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint vk);
    [DllImport("user32.dll")]
    static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    const int WM_HOTKEY = 0x0312;
    const uint MOD_ALT = 1, MOD_CONTROL = 2, MOD_NOREPEAT = 0x4000;

    public event EventHandler Pressed;
    public bool Registered;

    public HotkeyWindow()
    {
        CreateHandle(new CreateParams());
        Registered = RegisterHotKey(Handle, 1, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, (uint)Keys.H);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_HOTKEY && Pressed != null) Pressed(this, EventArgs.Empty);
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        if (Registered) UnregisterHotKey(Handle, 1);
        DestroyHandle();
    }
}

class TrayApp : ApplicationContext
{
    [DllImport("user32.dll")]
    static extern bool DestroyIcon(IntPtr handle);

    readonly NotifyIcon tray = new NotifyIcon();
    readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
    readonly HotkeyWindow hotkey = new HotkeyWindow();
    readonly ToolStripMenuItem miToggle, miExtend, miClone, miAutostart;
    HdmiState state = (HdmiState)(-1);
    IntPtr iconHandle = IntPtr.Zero;

    public TrayApp()
    {
        ContextMenuStrip menu = new ContextMenuStrip();
        miToggle = new ToolStripMenuItem("", null, delegate { Toggle(); });
        miToggle.Font = new Font(miToggle.Font, FontStyle.Bold);
        miExtend = new ToolStripMenuItem("При включване: разшири работния плот", null,
            delegate { Settings.Clone = false; UpdateChecks(); });
        miClone = new ToolStripMenuItem("При включване: дублирай екрана", null,
            delegate { Settings.Clone = true; UpdateChecks(); });
        miAutostart = new ToolStripMenuItem("Стартирай с Windows", null,
            delegate { Settings.Autostart = !Settings.Autostart; UpdateChecks(); });

        menu.Items.Add(miToggle);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(miExtend);
        menu.Items.Add(miClone);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(miAutostart);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Изход", null, delegate { ExitThread(); }));

        tray.ContextMenuStrip = menu;
        tray.MouseClick += delegate(object s, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) Toggle();
        };
        tray.Visible = true;

        hotkey.Pressed += delegate { Toggle(); };
        SystemEvents.DisplaySettingsChanged += OnDisplayChanged;

        timer.Interval = 2000;
        timer.Tick += delegate { Refresh(); };
        timer.Start();

        UpdateChecks();
        Refresh();
    }

    void OnDisplayChanged(object sender, EventArgs e) { Refresh(); }

    void UpdateChecks()
    {
        bool clone = Settings.Clone;
        miExtend.Checked = !clone;
        miClone.Checked = clone;
        miAutostart.Checked = Settings.Autostart;
    }

    void Toggle()
    {
        HdmiState s = Display.GetState();
        if (s == HdmiState.NotConnected)
        {
            tray.ShowBalloonTip(3000, "HDMI", "Няма свързан външен монитор.", ToolTipIcon.Warning);
            return;
        }
        bool ok = s == HdmiState.On ? Display.TurnOff() : Display.TurnOn(Settings.Clone);
        if (!ok)
            tray.ShowBalloonTip(3000, "HDMI", "Windows отказа смяната на екраните.", ToolTipIcon.Error);
        Refresh();
    }

    void Refresh()
    {
        HdmiState s = Display.GetState();
        if (s == state) return;
        state = s;

        string hk = hotkey.Registered ? " (Ctrl+Alt+H)" : "";
        if (s == HdmiState.On)
        {
            tray.Text = "HDMI: включен" + hk;
            miToggle.Text = "Спри картината към HDMI";
            miToggle.Enabled = true;
        }
        else if (s == HdmiState.Off)
        {
            tray.Text = "HDMI: спрян" + hk;
            miToggle.Text = "Пусни картината към HDMI";
            miToggle.Enabled = true;
        }
        else
        {
            tray.Text = "HDMI: няма свързан монитор";
            miToggle.Text = "Няма свързан монитор";
            miToggle.Enabled = false;
        }

        IntPtr old = iconHandle;
        using (Bitmap bmp = DrawIcon(s))
        {
            iconHandle = bmp.GetHicon();
            tray.Icon = Icon.FromHandle(iconHandle);
        }
        if (old != IntPtr.Zero) DestroyIcon(old);
    }

    static Bitmap DrawIcon(HdmiState s)
    {
        Bitmap bmp = new Bitmap(32, 32);
        using (Graphics g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Color screen = s == HdmiState.On ? Color.FromArgb(40, 190, 90)
                         : s == HdmiState.Off ? Color.FromArgb(70, 70, 70)
                         : Color.FromArgb(150, 150, 150);
            using (Brush frame = new SolidBrush(Color.FromArgb(235, 235, 235)))
            using (Brush fill = new SolidBrush(screen))
            {
                g.FillRectangle(frame, 1, 4, 30, 20);   // рамка
                g.FillRectangle(fill, 4, 7, 24, 14);    // екран
                g.FillRectangle(frame, 14, 24, 4, 3);   // стойка
                g.FillRectangle(frame, 8, 27, 16, 3);   // основа
            }
            if (s != HdmiState.On)
            {
                using (Pen p = new Pen(Color.FromArgb(230, 60, 60), 3f))
                    g.DrawLine(p, 6, 9, 26, 19);
            }
        }
        return bmp;
    }

    protected override void ExitThreadCore()
    {
        timer.Stop();
        SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
        tray.Visible = false;
        tray.Dispose();
        hotkey.Dispose();
        base.ExitThreadCore();
    }
}

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        if (args.Length > 0)
        {
            string cmd = args[0].ToLowerInvariant().TrimStart('/', '-');
            HdmiState s = Display.GetState();
            switch (cmd)
            {
                case "on": return Display.TurnOn(Settings.Clone) ? 0 : 1;
                case "off": return Display.TurnOff() ? 0 : 1;
                case "toggle":
                    if (s == HdmiState.NotConnected) return 2;
                    return (s == HdmiState.On ? Display.TurnOff() : Display.TurnOn(Settings.Clone)) ? 0 : 1;
                case "status":
                    // код на изход: 0 = включен, 1 = спрян, 2 = няма монитор
                    return s == HdmiState.On ? 0 : s == HdmiState.Off ? 1 : 2;
                default:
                    MessageBox.Show("Употреба: HdmiToggle.exe [on | off | toggle | status]", "HdmiToggle");
                    return 64;
            }
        }

        bool created;
        using (Mutex mutex = new Mutex(true, "HdmiToggle.SingleInstance", out created))
        {
            if (!created) return 0; // вече работи
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new TrayApp());
        }
        return 0;
    }
}
