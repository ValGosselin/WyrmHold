using System.IO;
using System.Media;
using Wyrmhold.Core;

namespace WyrmHold.App;

/// <summary>
/// Les sons des notifications de succès, fabriqués par le code (aucun fichier son emprunté :
/// pas de question de droits). Un son = une liste d'« échantillons » (44 100 par seconde),
/// chacun entre -1 et 1, qu'on calcule en additionnant des ondes.
/// Style « whoosh + accord » (choisi le 9 octobre 2026 parmi trois essais) : un souffle, un petit
/// impact grave et un accord chaleureux, avec une réverbération en stéréo. Pas de cloche ni de
/// mélodie aiguë (ça faisait « 8-bit » et « tintinnabulant »). Le 100 % a en plus une poussière
/// de paillettes (10 octobre 2026) : des éclats très courts, dispersés, fondus dans la réverbération.
/// </summary>
public static class AchievementSounds
{
    private const int SampleRate = 44100;

    // Volume maximal (1 = le plus fort possible) : discret, pour ne pas couvrir le jeu.
    private const double PeakVolume = 0.28;

    // Part du son « sec » (les notes) dans le mélange final ; la part réverbérée dépend du son.
    private const double DryLevel = 0.85;

    // Fabriqués une seule fois, au premier son joué (Lazy = « calculé à la première demande »).
    private static readonly Lazy<SoundPlayer> UnlockSound = new Lazy<SoundPlayer>(() => CreatePlayer(BuildUnlock()));
    private static readonly Lazy<SoundPlayer> CompletedSound = new Lazy<SoundPlayer>(() => CreatePlayer(BuildCompleted()));

    public static void PlayUnlock() => Play(UnlockSound);

    public static void PlayCompleted() => Play(CompletedSound);

    private static void Play(Lazy<SoundPlayer> sound)
    {
        try
        {
            // Play() joue « à côté » : Wyrmhold et le jeu ne sont pas bloqués pendant le son.
            sound.Value.Play();
        }
        catch (Exception ex)
        {
            Logger.Log($"Son de succès impossible à jouer : {ex.Message}");
        }
    }

    // ----- Les deux sons -----

    /// <summary>
    /// Succès : un court souffle, puis un accord de mi majeur qui « se pose » avec un petit impact,
    /// et une note qui glisse vers le haut. Environ 1,9 s, réverbération comprise.
    /// </summary>
    private static Stereo BuildUnlock()
    {
        float[] notes = new float[(int)(1.1 * SampleRate)];

        AddSwoosh(notes, start: 0.00, duration: 0.25, volume: 0.30, fromHz: 300, toHz: 2500);
        AddThump(notes, start: 0.20, volume: 0.35);

        foreach (double frequency in new[] { 329.63, 415.30, 493.88 })   // mi, sol#, si
        {
            AddPad(notes, start: 0.20, frequency: frequency, volume: 0.20, attack: 0.02, decay: 0.55);
        }

        AddSine(notes, start: 0.20, frequency: 493.88, volume: 0.35, attack: 0.01, decay: 0.35,
            glideTo: 659.25, glideTime: 0.08);

        return Finish(notes, wetLevel: 0.25, tailSeconds: 0.8);
    }

    /// <summary>
    /// 100 % : un souffle plus long avec une note qui monte, un impact, puis un grand accord
    /// (do majeur 9, sur deux octaves). Raccourci à la demande : environ 2,9 s au lieu de 4,2.
    /// </summary>
    private static Stereo BuildCompleted()
    {
        float[] notes = new float[(int)(2.0 * SampleRate)];

        AddSwoosh(notes, start: 0.00, duration: 0.45, volume: 0.30, fromHz: 150, toHz: 3500);
        AddSine(notes, start: 0.10, frequency: 392.00, volume: 0.25, attack: 0.15, decay: 0.40,
            glideTo: 783.99, glideTime: 0.30);
        AddThump(notes, start: 0.45, volume: 0.55);

        foreach (double frequency in new[] { 130.81, 261.63, 329.63, 392.00, 493.88, 587.33 })   // do, do, mi, sol, si, ré
        {
            AddPad(notes, start: 0.45, frequency: frequency, volume: 0.16, attack: 0.03, decay: 1.2);
        }

        AddSine(notes, start: 0.45, frequency: 783.99, volume: 0.30, attack: 0.02, decay: 0.6);

        // Paillettes (ajoutées le 10 octobre 2026, demande de Val : « un peu pailleté ou brillant »).
        float[] sparkleLeft = new float[notes.Length];
        float[] sparkleRight = new float[notes.Length];
        AddSparkles(sparkleLeft, sparkleRight, start: 0.45, duration: 1.4, count: 70, volume: 0.07);

        return Finish(notes, wetLevel: 0.30, tailSeconds: 0.9, sparkleLeft, sparkleRight);
    }

    // ----- Les instruments -----

    /// <summary>
    /// Une onde pure qui monte en « attack » secondes puis s'éteint doucement.
    /// Avec glideTo, sa hauteur glisse de frequency à glideTo pendant glideTime secondes.
    /// </summary>
    private static void AddSine(float[] samples, double start, double frequency, double volume,
        double attack, double decay, double? glideTo = null, double glideTime = 0, double maxSeconds = double.MaxValue)
    {
        int first = (int)(start * SampleRate);
        double phase = 0;

        // maxSeconds : on arrête le calcul quand la note est devenue inaudible (utile pour les paillettes, très courtes).
        int last = (int)Math.Min(samples.Length, first + maxSeconds * SampleRate);

        for (int i = first; i < last; i++)
        {
            double t = (double)(i - first) / SampleRate;   // secondes depuis le début de la note

            double currentFrequency = glideTo is double target && t < glideTime
                ? frequency + (target - frequency) * (t / glideTime)
                : glideTo ?? frequency;

            // On avance la « phase » pas à pas : c'est ce qui permet à la hauteur de changer sans craquement.
            phase += 2 * Math.PI * currentFrequency / SampleRate;

            double envelope = Math.Min(1, t / attack) * Math.Exp(-t / decay);
            samples[i] += (float)(volume * envelope * Math.Sin(phase));
        }
    }

    /// <summary>
    /// Une nappe : deux sinus presque à la même hauteur (0,4 % d'écart). Leur léger battement
    /// donne un son chaud et vivant (effet « chorus »).
    /// </summary>
    private static void AddPad(float[] samples, double start, double frequency, double volume, double attack, double decay)
    {
        AddSine(samples, start, frequency, volume * 0.5, attack, decay);
        AddSine(samples, start, frequency * 1.004, volume * 0.5, attack, decay);
    }

    /// <summary>
    /// Un petit impact grave : un sinus qui descend très vite de 90 à 45 Hz. Il donne du « poids » au son.
    /// </summary>
    private static void AddThump(float[] samples, double start, double volume)
    {
        AddSine(samples, start, 90, volume, attack: 0.003, decay: 0.08, glideTo: 45, glideTime: 0.08);
    }

    /// <summary>
    /// Un souffle (« whoosh ») : du bruit (des valeurs au hasard) passé dans un filtre qui laisse
    /// passer de plus en plus d'aigus, avec un volume qui gonfle puis retombe.
    /// </summary>
    private static void AddSwoosh(float[] samples, double start, double duration, double volume, double fromHz, double toHz)
    {
        // Random(7) : toujours les mêmes « hasards », donc le son est identique à chaque fois.
        Random random = new Random(7);
        int first = (int)(start * SampleRate);
        int count = (int)(duration * SampleRate);
        double filtered1 = 0;
        double filtered2 = 0;

        for (int n = 0; n < count && first + n < samples.Length; n++)
        {
            double progress = (double)n / count;                              // 0 → 1
            double cutoff = fromHz + (toHz - fromHz) * progress * progress;    // de grave à aigu
            double smoothing = 1 - Math.Exp(-2 * Math.PI * cutoff / SampleRate);
            double noise = random.NextDouble() * 2 - 1;

            // Deux filtres « passe-bas » à la suite : un souffle plus doux qu'avec un seul.
            filtered1 += smoothing * (noise - filtered1);
            filtered2 += smoothing * (filtered1 - filtered2);

            double swell = Math.Pow(Math.Sin(Math.PI * Math.Pow(progress, 1.5)), 2);
            samples[first + n] += (float)(volume * 2 * swell * filtered2);
        }
    }

    // Les notes des paillettes : l'accord du 100 % (do, mi, sol, si, ré) trois octaves plus haut,
    // pour que chaque éclat « tombe juste » avec l'accord au lieu de sonner comme un bruit parasite.
    private static readonly double[] SparkleNotes = { 2093.0, 2637.0, 3136.0, 3951.1, 4698.6, 5274.0 };

    /// <summary>
    /// Une poussière de paillettes : beaucoup d'éclats très courts (20 à 60 ms), à des hauteurs et des
    /// places (gauche / droite) tirées au hasard, de plus en plus espacés. Trop brefs pour former une
    /// mélodie : on entend un scintillement, pas des clochettes. Ils passent dans la réverbération,
    /// qui les fond en un voile brillant.
    /// </summary>
    private static void AddSparkles(float[] left, float[] right, double start, double duration, int count, double volume)
    {
        // Random(42) : toujours les mêmes « hasards », donc le son est identique à chaque fois.
        Random random = new Random(42);

        for (int k = 0; k < count; k++)
        {
            // Beaucoup d'éclats au début, puis de moins en moins (la puissance 2 les tasse vers le début).
            double when = start + duration * Math.Pow(random.NextDouble(), 2);

            // Une note de l'accord, très légèrement désaccordée (± 0,3 %) pour un rendu moins « électronique ».
            double frequency = SparkleNotes[random.Next(SparkleNotes.Length)] * (1 + (random.NextDouble() - 0.5) * 0.006);

            // Les éclats tardifs sont plus faibles : la poussière retombe.
            double fade = 1 - (when - start) / duration;
            double grainVolume = volume * (0.4 + 0.6 * random.NextDouble()) * (0.3 + 0.7 * fade);
            double decay = 0.02 + 0.04 * random.NextDouble();

            // Place dans l'espace : 0 = tout à gauche, 1 = tout à droite (loi « à puissance constante » :
            // le volume perçu reste le même où que soit l'éclat).
            double pan = random.NextDouble();
            double leftGain = Math.Cos(pan * Math.PI / 2);
            double rightGain = Math.Sin(pan * Math.PI / 2);

            AddSine(left, when, frequency, grainVolume * leftGain, attack: 0.002, decay: decay, maxSeconds: decay * 6);
            AddSine(right, when, frequency, grainVolume * rightGain, attack: 0.002, decay: decay, maxSeconds: decay * 6);
        }
    }

    // ----- La réverbération et le mélange -----

    private record Stereo(float[] Left, float[] Right);

    /// <summary>
    /// Ajoute la réverbération (en stéréo), mélange avec le son d'origine et règle le volume.
    /// extraLeft / extraRight : des sons déjà placés à gauche et à droite (les paillettes), facultatifs.
    /// </summary>
    private static Stereo Finish(float[] notes, double wetLevel, double tailSeconds,
        float[]? extraLeft = null, float[]? extraRight = null)
    {
        // On rallonge le son pour laisser la réverbération s'éteindre.
        int length = notes.Length + (int)(tailSeconds * SampleRate);
        float[] dryLeft = new float[length];
        float[] dryRight = new float[length];

        for (int i = 0; i < notes.Length; i++)
        {
            dryLeft[i] = notes[i] + (extraLeft?[i] ?? 0);
            dryRight[i] = notes[i] + (extraRight?[i] ?? 0);
        }

        // Des réglages un peu différents à gauche et à droite : c'est ce qui donne la largeur.
        float[] wetLeft = Reverb(dryLeft, new[] { 29.7, 37.1, 41.1, 43.7 });
        float[] wetRight = Reverb(dryRight, new[] { 31.3, 36.7, 40.3, 44.9 });

        float[] left = new float[length];
        float[] right = new float[length];

        for (int i = 0; i < length; i++)
        {
            left[i] = (float)(DryLevel * dryLeft[i] + wetLevel * wetLeft[i]);
            right[i] = (float)(DryLevel * dryRight[i] + wetLevel * wetRight[i]);
        }

        // Le point le plus fort (des deux côtés) est ramené à PeakVolume : jamais de saturation.
        float peak = Math.Max(left.Max(Math.Abs), right.Max(Math.Abs));
        float factor = peak > 0 ? (float)(PeakVolume / peak) : 1;

        for (int i = 0; i < length; i++)
        {
            left[i] *= factor;
            right[i] *= factor;
        }

        return new Stereo(left, right);
    }

    /// <summary>
    /// Une réverbération classique (modèle de Schroeder) : le son est répété par quatre « échos »
    /// qui se mélangent et s'éteignent (filtres en peigne), puis brouillé par deux filtres
    /// « passe-tout » pour ne plus entendre d'échos séparés, seulement une pièce qui résonne.
    /// </summary>
    private static float[] Reverb(float[] input, double[] combDelaysMs)
    {
        const double Feedback = 0.80;   // longueur de la résonance
        const double Damping = 0.40;    // les aigus s'éteignent plus vite, comme dans une vraie pièce

        float[] output = new float[input.Length];

        foreach (double delayMs in combDelaysMs)
        {
            int delay = (int)(delayMs / 1000 * SampleRate);
            double[] buffer = new double[delay];
            double damped = 0;

            for (int i = 0; i < input.Length; i++)
            {
                double delayed = buffer[i % delay];
                damped = delayed * (1 - Damping) + damped * Damping;
                buffer[i % delay] = input[i] + damped * Feedback;
                output[i] += (float)(delayed / combDelaysMs.Length);
            }
        }

        foreach (double delayMs in new[] { 5.0, 1.7 })
        {
            output = AllPass(output, (int)(delayMs / 1000 * SampleRate), 0.5);
        }

        return output;
    }

    private static float[] AllPass(float[] input, int delay, double gain)
    {
        float[] output = new float[input.Length];
        double[] buffer = new double[delay];

        for (int i = 0; i < input.Length; i++)
        {
            double delayed = buffer[i % delay];
            double value = -gain * input[i] + delayed;
            buffer[i % delay] = input[i] + gain * value;
            output[i] = (float)value;
        }

        return output;
    }

    // ----- Le fichier WAV -----

    /// <summary>
    /// Range le son au format WAV stéréo (en-tête + nombres de 16 bits, gauche puis droite), en mémoire.
    /// </summary>
    private static SoundPlayer CreatePlayer(Stereo sound)
    {
        const short Channels = 2;
        const short BytesPerSample = sizeof(short);

        MemoryStream stream = new MemoryStream();
        BinaryWriter writer = new BinaryWriter(stream);
        int dataLength = sound.Left.Length * Channels * BytesPerSample;

        writer.Write("RIFF"u8);
        writer.Write(36 + dataLength);
        writer.Write("WAVE"u8);

        writer.Write("fmt "u8);
        writer.Write(16);                                          // taille de ce bloc
        writer.Write((short)1);                                    // format PCM (non compressé)
        writer.Write(Channels);                                    // stéréo
        writer.Write(SampleRate);
        writer.Write(SampleRate * Channels * BytesPerSample);      // octets par seconde
        writer.Write((short)(Channels * BytesPerSample));          // octets par instant (gauche + droite)
        writer.Write((short)16);                                   // bits par échantillon

        writer.Write("data"u8);
        writer.Write(dataLength);

        for (int i = 0; i < sound.Left.Length; i++)
        {
            writer.Write(ToSample(sound.Left[i]));
            writer.Write(ToSample(sound.Right[i]));
        }

        writer.Flush();
        stream.Position = 0;

        SoundPlayer player = new SoundPlayer(stream);
        player.Load();   // lit le son une fois pour toutes
        return player;
    }

    private static short ToSample(float value)
    {
        return (short)(Math.Clamp(value, -1f, 1f) * short.MaxValue);
    }
}
