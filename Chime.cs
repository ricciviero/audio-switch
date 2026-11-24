using System.Media;

namespace CambiaAudio;

/// <summary>
/// Suono di conferma breve e morbido (due note, circa 0,2 secondi), generato in memoria:
/// nessun file audio esterno. Suona sull'uscita predefinita, cioè su quella appena scelta.
/// </summary>
sealed class Chime : IDisposable
{
    const int SampleRate = 44100;
    const double Volume = 0.22;          // volume basso: un avviso, non un allarme
    const double AttackSeconds = 0.005;  // attacco morbido, senza "clic"
    const double ReleaseSeconds = 0.012; // chiusura dolce di ogni nota

    // Mi (E5) e poi Si (B5): un intervallo di quinta, ascendente e delicato.
    static readonly (double Frequency, double Seconds)[] Notes = { (659.25, 0.075), (987.77, 0.14) };

    readonly MemoryStream wav;
    readonly SoundPlayer player;

    public Chime()
    {
        wav = BuildWav();
        player = new SoundPlayer(wav);
        player.Load();
    }

    /// <summary>Suona senza bloccare il programma.</summary>
    public void Play()
    {
        try { player.Play(); }
        catch { /* il suono non è essenziale */ }
    }

    static MemoryStream BuildWav()
    {
        var samples = new List<short>();
        foreach (var (frequency, seconds) in Notes)
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
