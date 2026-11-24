using System.Reflection;
using System.Runtime.InteropServices;

namespace CambiaAudio;

/// <summary>Icona vicino all'orologio, scorciatoie globali e menu.</summary>
sealed class TrayApp : ApplicationContext
{
    const uint MOD_ALT = 0x0001, MOD_CONTROL = 0x0002, MOD_NOREPEAT = 0x4000;
    const uint VK_A = 0x41, VK_M = 0x4D;
    const int OutputHotkeyId = 1, MicHotkeyId = 2;
    const string OutputHotkeyText = "Ctrl+Alt+A";
    const string MicHotkeyText = "Ctrl+Alt+M";
    static readonly Color MutedBackground = Color.FromArgb(140, 28, 32);

    readonly AppSettings settings = AppSettings.Load();
    readonly ContextMenuStrip menu = new();
    readonly NotifyIcon tray;
    readonly HotkeyWindow hotkeys = new();
    readonly Osd osd = new();
    readonly Chime outputChime = Chime.OutputChanged();
    readonly Chime micOnChime = Chime.MicOn();
    readonly Chime micOffChime = Chime.MicOff();
    readonly System.Windows.Forms.Timer micWatch = new() { Interval = 2000 };
    bool? micMuted;   // stato mostrato ora nell'icona (null = nessun microfono)
    Icon? trayIcon;
    DateTime lastTooltipUpdate = DateTime.MinValue;

    public TrayApp()
    {
        menu.Opening += (_, _) => BuildMenu();

        tray = new NotifyIcon
        {
            ContextMenuStrip = menu,
            Text = "Cambia audio",
            Visible = true,
        };
        tray.MouseUp += (_, e) => { if (e.Button == MouseButtons.Left) ShowMenu(); };
        tray.MouseMove += (_, _) => UpdateTooltip();
        RefreshMicState(force: true);

        hotkeys.Pressed += id =>
        {
            if (id == OutputHotkeyId) CycleOutput();
            else if (id == MicHotkeyId) ToggleMic();
        };
        var busy = new List<string>();
        if (!hotkeys.Register(OutputHotkeyId, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, VK_A)) busy.Add(OutputHotkeyText);
        if (!hotkeys.Register(MicHotkeyId, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, VK_M)) busy.Add(MicHotkeyText);
        if (busy.Count > 0)
            osd.ShowText($"{string.Join(" e ", busy)} già in uso da un altro programma", 5000);

        // Il muto può cambiare anche da Windows o da altri programmi: l'icona si aggiorna da sola.
        micWatch.Tick += (_, _) => RefreshMicState();
        micWatch.Start();
    }

    // ---- Uscita audio ----

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
            outputChime.Play(); // suona già sulla nuova uscita
            osd.ShowText(device.ShortName);
            UpdateTooltip(force: true);
        }
        catch (Exception ex)
        {
            osd.ShowText("Errore: " + ex.Message, 4000);
        }
    }

    // ---- Microfono ----

    /// <summary>Mette o toglie il muto al microfono per tutti i programmi.</summary>
    void ToggleMic()
    {
        try
        {
            bool? muted = AudioDevices.ToggleMicMute();
            if (muted == null)
            {
                osd.ShowText("Nessun microfono attivo", 2000, Glyphs.Microphone);
                return;
            }
            (muted.Value ? micOffChime : micOnChime).Play();
            osd.ShowText(muted.Value ? "Microfono disattivato" : "Microfono attivo", 1500, Glyphs.Microphone,
                muted.Value ? MutedBackground : null);
            RefreshMicState(force: true);
        }
        catch (Exception ex)
        {
            osd.ShowText("Errore: " + ex.Message, 4000);
        }
    }

    /// <summary>Aggiorna l'icona (pallino rosso = microfono in muto) se lo stato è cambiato.</summary>
    void RefreshMicState(bool force = false)
    {
        bool? state;
        try { state = AudioDevices.IsMicMuted(); }
        catch { return; }

        if (!force && state == micMuted && trayIcon != null) return;
        micMuted = state;

        var previous = trayIcon;
        trayIcon = Glyphs.CreateTrayIcon(micMuted == true);
        tray.Icon = trayIcon;
        previous?.Dispose();
        UpdateTooltip(force: true);
    }

    // ---- Menu e tooltip ----

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

        var mic = new ToolStripMenuItem($"Microfono in muto ({MicHotkeyText})")
        {
            Checked = micMuted == true,
            Enabled = micMuted != null,
        };
        mic.Click += (_, _) => ToggleMic();
        menu.Items.Add(mic);

        menu.Items.Add(new ToolStripSeparator());

        var cycleMenu = new ToolStripMenuItem($"{OutputHotkeyText} alterna tra");
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
            string micText = micMuted switch { true => "in muto", false => "attivo", null => "assente" };
            string tip = $"Uscita: {device?.ShortName ?? "nessuna"}\nMicrofono: {micText}\n{OutputHotkeyText} uscita, {MicHotkeyText} microfono";
            tray.Text = tip.Length > 127 ? tip[..127] : tip;
        }
        catch
        {
            // Il tooltip non è essenziale.
        }
    }

    protected override void ExitThreadCore()
    {
        micWatch.Stop();
        micWatch.Dispose();
        hotkeys.Dispose();
        tray.Visible = false;
        tray.Dispose();
        trayIcon?.Dispose();
        osd.Dispose();
        outputChime.Dispose();
        micOnChime.Dispose();
        micOffChime.Dispose();
        base.ExitThreadCore();
    }
}

/// <summary>Finestra invisibile che riceve le scorciatoie globali.</summary>
sealed class HotkeyWindow : NativeWindow, IDisposable
{
    const int WM_HOTKEY = 0x0312;
    readonly List<int> registered = new();

    /// <summary>Scatta con l'id della scorciatoia premuta.</summary>
    public event Action<int>? Pressed;

    // HWND_MESSAGE: finestra di soli messaggi, mai visibile.
    public HotkeyWindow() => CreateHandle(new CreateParams { Parent = new IntPtr(-3) });

    public bool Register(int id, uint modifiers, uint key)
    {
        if (!RegisterHotKey(Handle, id, modifiers, key)) return false;
        registered.Add(id);
        return true;
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_HOTKEY) Pressed?.Invoke((int)m.WParam);
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        foreach (int id in registered) UnregisterHotKey(Handle, id);
        registered.Clear();
        DestroyHandle();
    }

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
