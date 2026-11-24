using System.Media;

namespace CambiaAudio;

/// <summary>
/// Suoni di conferma brevi e morbidi (due note, circa 0,2 secondi), generati in memoria:
/// nessun file audio esterno. Suonano sull'uscita predefinita.
/// </summary>
sealed class Chime : IDisposable
{
    const int SampleRate = 44100;
    const double Volume = 0.22;          // volume basso: un avviso, non un allarme
    const double AttackSeconds = 0.005;  // attacco morbido, senza "clic"
    const double ReleaseSeconds = 0.012; // chiusura dolce di ogni nota

    /// <summary>Cambio di uscita: Mi → Si, sale.</summary>
    public static Chime OutputChanged() => new((659.25, 0.075), (987.77, 0.14));

    /// <summary>Microfono riattivato: La → Mi acuto, sale veloce.</summary>
    public static Chime MicOn() => new((880.00, 0.06), (1318.51, 0.11));

    /// <summary>Microfono in muto: Mi → La grave, scende.</summary>
    public static Chime MicOff() => new((659.25, 0.07), (440.00, 0.13));

    readonly MemoryStream wav;
    readonly SoundPlayer player;

    Chime(params (double Frequency, double Seconds)[] notes)
    {
        wav = BuildWav(notes);
        player = new SoundPlayer(wav);
        player.Load();
    }

    /// <summary>Suona senza bloccare il programma.</summary>
    public void Play()
    {
        try { player.Play(); }
        catch { /* il suono non è essenziale */ }
    }

    static MemoryStream BuildWav((double Frequency, double Seconds)[] notes)
    {
        var samples = new List<short>();
        foreach (var (frequency, seconds) in notes)
        {
            int count = (int)(seconds * SampleRate);
            for (int i = 0; i < count; i++)
            {
                double t = (double)i / SampleRate;
                double attack = Math.Min(1.0, t / AttackSeconds);
                double release = Math.Min(1.0, (seconds - t) / ReleaseSeconds);
                double decay = Math.Exp(-t * 16);
                // Fondamentale più un'armonica leggera: suono rotondo, tipo campanella.
                double wave = (Math.Sin(2 * Math.PI * frequency * t) + 0.25 * Math.Sin(4 * Math.PI * frequency * t)) / 1.25;
                samples.Add((short)(wave * attack * release * decay * Volume * short.MaxValue));
            }
        }

        var stream = new MemoryStream();
        using (var w = new BinaryWriter(stream, System.Text.Encoding.ASCII, leaveOpen: true))
        {
            int dataBytes = samples.Count * sizeof(short);
            w.Write("RIFF"u8); w.Write(36 + dataBytes); w.Write("WAVE"u8);
            w.Write("fmt "u8); w.Write(16);
            w.Write((short)1);              // PCM
            w.Write((short)1);              // mono
            w.Write(SampleRate);
            w.Write(SampleRate * sizeof(short));
            w.Write((short)sizeof(short));  // byte per campione
            w.Write((short)16);             // bit per campione
            w.Write("data"u8); w.Write(dataBytes);
            foreach (short s in samples) w.Write(s);
        }
        stream.Position = 0;
        return stream;
    }

    public void Dispose()
    {
        player.Dispose();
        wav.Dispose();
    }
}
