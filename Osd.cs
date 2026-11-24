using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace CambiaAudio;

/// <summary>Riquadro che compare per un attimo in basso al centro con il nome della nuova uscita.</summary>
sealed class Osd : Form
{
    static readonly Color DefaultBackground = Color.FromArgb(36, 36, 36);

    readonly FlowLayoutPanel row;
    readonly Label icon;
    readonly Label text;
    readonly System.Windows.Forms.Timer hideTimer = new();

    public Osd()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = DefaultBackground;
        Padding = new Padding(18, 12, 22, 12);

        row = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = Padding.Empty,
            BackColor = Color.Transparent,
        };
        icon = new Label
        {
            AutoSize = true,
            Text = Glyphs.Volume,
            Font = new Font(Glyphs.FontName, 16f),
            ForeColor = Color.White,
            Margin = new Padding(0, 5, 12, 0),
        };
        text = new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI", 13f),
            ForeColor = Color.White,
            Margin = Padding.Empty,
        };
        row.Controls.Add(icon);
        row.Controls.Add(text);
        Controls.Add(row);

        hideTimer.Tick += (_, _) => { hideTimer.Stop(); Hide(); };
    }

    // Non ruba il focus al gioco o alla finestra in uso.
    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            const int WS_EX_TOPMOST = 0x00000008, WS_EX_TOOLWINDOW = 0x00000080, WS_EX_NOACTIVATE = 0x08000000;
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        int round = 2; // DWMWCP_ROUND: angoli arrotondati di Windows 11
        DwmSetWindowAttribute(Handle, 33, ref round, sizeof(int));
    }

    /// <param name="glyph">Icona a sinistra del testo (predefinita: altoparlante).</param>
    /// <param name="background">Colore di sfondo (predefinito: grigio scuro).</param>
    public void ShowText(string message, int milliseconds = 1500, string glyph = Glyphs.Volume, Color? background = null)
    {
        text.Text = message;
        icon.Text = glyph;
        BackColor = background ?? DefaultBackground;
        row.PerformLayout();
        var content = row.PreferredSize;
        row.Location = new Point(Padding.Left, Padding.Top);
        ClientSize = new Size(content.Width + Padding.Horizontal, content.Height + Padding.Vertical);

        var area = Screen.FromPoint(Cursor.Position).WorkingArea;
        Location = new Point(area.Left + (area.Width - Width) / 2, area.Bottom - Height - 48);

        if (!Visible) Show();
        hideTimer.Stop();
        hideTimer.Interval = milliseconds;
        hideTimer.Start();
    }

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}

/// <summary>Icona dell'altoparlante presa dai font di sistema di Windows.</summary>
static class Glyphs
{
    public const string Volume = "";
    public const string Microphone = "";

    public static string FontName { get; } = FontExists("Segoe Fluent Icons") ? "Segoe Fluent Icons" : "Segoe MDL2 Assets";

    /// <param name="micMuted">Se true aggiunge un pallino rosso: microfono in muto.</param>
    public static Icon CreateTrayIcon(bool micMuted = false)
    {
        bool lightTaskbar = Registry.CurrentUser
            .OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize")
            ?.GetValue("SystemUsesLightTheme") is int v && v == 1;

        using var bitmap = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bitmap))
        using (var font = new Font(FontName, 22f, GraphicsUnit.Pixel))
        using (var brush = new SolidBrush(lightTaskbar ? Color.Black : Color.White))
        using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
        {
            g.Clear(Color.Transparent);
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            g.DrawString(Volume, font, brush, new RectangleF(0, 1, 32, 32), format);

            if (micMuted)
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using var red = new SolidBrush(Color.FromArgb(232, 17, 35));
                using var ring = new Pen(lightTaskbar ? Color.White : Color.FromArgb(32, 32, 32), 2f);
                g.FillEllipse(red, 18, 18, 13, 13);
                g.DrawEllipse(ring, 18, 18, 13, 13);
            }
        }

        // Copia che possiede il proprio handle, così l'icona vecchia si può liberare davvero.
        IntPtr handle = bitmap.GetHicon();
        try
        {
            using var temp = Icon.FromHandle(handle);
            return (Icon)temp.Clone();
        }
        finally { DestroyIcon(handle); }
    }

    [DllImport("user32.dll")]
    static extern bool DestroyIcon(IntPtr handle);

    static bool FontExists(string name)
    {
        using var font = new Font(name, 10f);
        return string.Equals(font.Name, name, StringComparison.OrdinalIgnoreCase);
    }
}
