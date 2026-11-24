namespace CambiaAudio;

static class Program
{
    [STAThread]
    static void Main()
    {
        // Una sola copia alla volta: una seconda non potrebbe registrare la scorciatoia.
        using var mutex = new Mutex(true, @"Local\CambiaAudio", out bool firstInstance);
        if (!firstInstance) return;

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApp());
    }
}
