using System.Reflection;
using System.Runtime.InteropServices;

namespace CambiaAudio;

/// <summary>Icona vicino all'orologio, scorciatoia globale e menu.</summary>
sealed class TrayApp : ApplicationContext
{
    const uint MOD_ALT = 0x0001, MOD_CONTROL = 0x0002, MOD_NOREPEAT = 0x4000;
    const uint VK_A = 0x41;
    const string HotkeyText = "Ctrl+Alt+A";

    readonly AppSettings settings = AppSettings.Load();
    readonly ContextMenuStrip menu = new();
    readonly NotifyIcon tray;
    readonly HotkeyWindow hotkey = new();
    readonly Osd osd = new();
    DateTime lastTooltipUpdate = DateTime.MinValue;

    public TrayApp()
    {
        menu.Opening += (_, _) => BuildMenu();

        tray = new NotifyIcon
        {
            Icon = Glyphs.CreateTrayIcon(),
            ContextMenuStrip = menu,
            Text = "Cambia audio",
            Visible = true,
        };
        tray.MouseUp += (_, e) => { if (e.Button == MouseButtons.Left) ShowMenu(); };
        tray.MouseMove += (_, _) => UpdateTooltip();

        hotkey.Pressed += CycleOutput;
        if (!hotkey.Register(MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, VK_A))
            osd.ShowText($"{HotkeyText} è già usata da un altro programma", 5000);

        UpdateTooltip(force: true);
    }

    /// <summary>Passa all'uscita successiva tra quelle scelte per la scorciatoia.</summary>
    void CycleOutput()
    {
        try
        {
            var active = AudioDevices.GetActiveOutputs();
            if (active.Count == 0)
            {
                osd.ShowText("Nessuna uscita audio attiva");
                return;
            }

            var cycle = active.Where(d => settings.CycleDeviceIds.Contains(d.Id)).ToList();
            if (cycle.Count == 0) cycle = active;

            string? current = AudioDevices.GetDefaultOutputId();
            int index = cycle.FindIndex(d => d.Id == current);
            SwitchTo(cycle[(index + 1) % cycle.Count]);
        }
        catch (Exception ex)
        {
            osd.ShowText("Errore: " + ex.Message, 4000);
        }
    }

    void SwitchTo(AudioDevice device)
    {
        try
        {
            AudioDevices.SetDefaultOutput(device.Id);
            osd.ShowText(device.ShortName);
            UpdateTooltip(force: true);
        }
        catch (Exception ex)
        {
            osd.ShowText("Errore: " + ex.Message, 4000);
        }
    }

    void BuildMenu()
    {
        menu.Items.Clear();

        List<AudioDevice> devices;
        string? current;
        try
        {
            devices = AudioDevices.GetActiveOutputs();
            current = AudioDevices.GetDefaultOutputId();
        }
        catch (Exception ex)
        {
            menu.Items.Add(new ToolStripMenuItem("Errore: " + ex.Message) { Enabled = false });
            AddExit();
            return;
        }

        menu.Items.Add(new ToolStripMenuItem("Uscita audio") { Enabled = false });
        foreach (var device in devices)
        {
            var item = new ToolStripMenuItem(device.Name) { Checked = device.Id == current };
            item.Click += (_, _) => SwitchTo(device);
            menu.Items.Add(item);
        }

        menu.Items.Add(new ToolStripSeparator());

        var cycleMenu = new ToolStripMenuItem($"{HotkeyText} alterna tra");
        foreach (var device in devices)
        {
            var item = new ToolStripMenuItem(device.Name)
            {
                CheckOnClick = true,
                Checked = settings.CycleDeviceIds.Contains(device.Id),
            };
            item.CheckedChanged += (_, _) =>
            {
                settings.CycleDeviceIds.Remove(device.Id);
                if (item.Checked) settings.CycleDeviceIds.Add(device.Id);
                SaveSettings();
            };
            cycleMenu.DropDownItems.Add(item);
        }
        // Resta aperto mentre spunti più uscite.
        cycleMenu.DropDown.Closing += (_, e) =>
        {
            if (e.CloseReason == ToolStripDropDownCloseReason.ItemClicked) e.Cancel = true;
        };
        menu.Items.Add(cycleMenu);

        var autostart = new ToolStripMenuItem("Avvia con Windows") { Checked = Autostart.IsEnabled() };
        autostart.Click += (_, _) =>
        {
            try { Autostart.Set(!Autostart.IsEnabled()); }
            catch (Exception ex) { osd.ShowText("Errore: " + ex.Message, 4000); }
        };
        menu.Items.Add(autostart);

        menu.Items.Add(new ToolStripSeparator());
        AddExit();
    }

    void AddExit()
    {
        var exit = new ToolStripMenuItem("Esci");
        exit.Click += (_, _) => ExitThread();
        menu.Items.Add(exit);
    }

    void SaveSettings()
    {
        try { settings.Save(); }
        catch (Exception ex) { osd.ShowText("Impossibile salvare: " + ex.Message, 4000); }
    }

    void ShowMenu()
    {
        // Il clic sinistro apre lo stesso menu del clic destro.
        typeof(NotifyIcon).GetMethod("ShowContextMenu", BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(tray, null);
    }

    void UpdateTooltip(bool force = false)
    {
        if (!force && (DateTime.Now - lastTooltipUpdate).TotalSeconds < 1) return;
        lastTooltipUpdate = DateTime.Now;
        try
        {
            string? id = AudioDevices.GetDefaultOutputId();
            var device = AudioDevices.GetActiveOutputs().FirstOrDefault(d => d.Id == id);
            string tip = $"Uscita audio: {device?.ShortName ?? "nessuna"}\n{HotkeyText} per cambiare";
            tray.Text = tip.Length > 127 ? tip[..127] : tip;
        }
        catch
        {
            // Il tooltip non è essenziale.
        }
    }

    protected override void ExitThreadCore()
    {
        hotkey.Dispose();
        tray.Visible = false;
        tray.Dispose();
        osd.Dispose();
        base.ExitThreadCore();
    }
}

/// <summary>Finestra invisibile che riceve la scorciatoia globale.</summary>
sealed class HotkeyWindow : NativeWindow, IDisposable
{
    const int WM_HOTKEY = 0x0312;
    const int HotkeyId = 1;
    bool registered;

    public event Action? Pressed;

    // HWND_MESSAGE: finestra di soli messaggi, mai visibile.
    public HotkeyWindow() => CreateHandle(new CreateParams { Parent = new IntPtr(-3) });

    public bool Register(uint modifiers, uint key)
    {
        registered = RegisterHotKey(Handle, HotkeyId, modifiers, key);
        return registered;
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_HOTKEY && (int)m.WParam == HotkeyId) Pressed?.Invoke();
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        if (registered) UnregisterHotKey(Handle, HotkeyId);
        registered = false;
        DestroyHandle();
    }

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
