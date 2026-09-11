using NAudio.Wave;

namespace Remove_Top.Features.Normalization
{
    /// <summary>
    /// Parámetros de la cadena de masterización.
    /// Todos los valores tienen ajustes por defecto; se pueden calibrar aquí
    /// sin tocar el resto del código.
    /// </summary>
    public sealed class MasteringSettings
    {
        // --- Filtro paso alto: limpia sub-graves inaudibles ---
        public double HighPassFreqHz = 30.0;
        public double HighPassQ = 0.707;

        // --- EQ paramétrico: graves con cuerpo ---
        public double BassFreqHz = 80.0;
        public double BassGainDb = 1.5;
        public double BassQ = 0.7;

        // --- EQ paramétrico: atenuación de medios-bajos "encajonados" ---
        public double MidFreqHz = 315.0;
        public double MidGainDb = -1.0;
        public double MidQ = 0.7;

        // --- Estante de agudos: brillo y presencia ---
        public double TrebleShelfFreqHz = 10000.0;
        public double TrebleShelfGainDb = 2.0;
        public double TrebleShelfSlope = 0.5;
    }

    /// <summary>
    /// Niveles de intensidad de la masterización aplicada tras la normalización.
    /// </summary>
    public enum MasteringIntensity
    {
        /// <summary>Cadena original suave ("glue"): preserva la dinámica (normalización por pico).</summary>
        Ligera,

        /// <summary>Limitador duro tipo Adobe Audition: rellena la onda y sube la sonoridad (~ -10 LUFS) sin saturar.</summary>
        HardLimiter,

        /// <summary>Densidad de master comercial/EDM: ~ -8.5 LUFS con techo true-peak -1.0 dBTP.</summary>
        ComercialEdm
    }

    /// <summary>
    /// Construye la cadena de masterización como una pila de ISampleProvider.
    ///
    /// Cadena de todos los perfiles:
    ///   DC Blocker → HPF 30 Hz → EQ (graves/medios/agudos)
    /// Los perfiles Loudness (HardLimiter / ComercialEdm) continúan con:
    ///   Compresor estéreo-enlazado → Soft Clipper → TruePeakLimiter (-1.0 dBTP)
    /// y luego, fuera de esta clase, TPDF Dither al escribir.
    /// Ligera conserva el compresor suave + limitador de pico clásico.
    /// </summary>
    public static class MasteringChain
    {
        /// <summary>Techo true-peak de los perfiles Loudness (dBTP).</summary>
        public const double TruePeakCeilingDb = -1.0;

        /// <summary>Techo del limitador clásico del perfil Ligera (dBFS).</summary>
        public const double LigeraCeilingDb = -0.3;

        /// <summary>Lookahead del limitador true-peak por perfil (ms).</summary>
        public const double HardLimiterLookaheadMs = 6.0;
        public const double ComercialEdmLookaheadMs = 5.0;

        /// <summary>
        /// Sonoridad objetivo (LUFS Integrated) de los perfiles Loudness.
        /// Devuelve NaN para Ligera (que se normaliza por pico, no por loudness).
        /// </summary>
        public static double TargetLufs(MasteringIntensity intensity) => intensity switch
        {
            MasteringIntensity.HardLimiter => -10.0,
            MasteringIntensity.ComercialEdm => -8.5,
            _ => double.NaN
        };

        /// <summary>
        /// Nombre legible de cada perfil de intensidad (usado en la UI y en los mensajes).
        /// </summary>
        public static string DisplayName(MasteringIntensity intensity) => intensity switch
        {
            MasteringIntensity.HardLimiter => "Hard Limiter",
            MasteringIntensity.ComercialEdm => "Comercial EDM",
            _ => "Ligera"
        };

        /// <summary>
        /// Envuelve el origen con la cadena de masterización del perfil indicado.
        /// Los filtros cuya frecuencia supere Nyquist se omiten para no generar
        /// coeficientes inválidos.
        /// </summary>
        public static ISampleProvider Build(ISampleProvider source, WaveFormat format, MasteringIntensity intensity)
        {
            int channels = format.Channels;
            int sampleRate = format.SampleRate;

            // Front-end común: DC blocker + HPF 30 Hz + EQ.
            ISampleProvider chain = BuildInputConditioning(source, format);
            chain = BuildEq(chain, format);

            return intensity switch
            {
                MasteringIntensity.HardLimiter => BuildLoudnessProfile(
                    chain, channels, sampleRate,
                    compThresholdDb: -16.0, compRatio: 2.5, compAttackMs: 15.0, compReleaseMs: 90.0,
                    clipThresholdDb: -1.0, clipCeilingDb: 1.0,
                    lookaheadMs: HardLimiterLookaheadMs, limiterReleaseMs: 60.0),

                MasteringIntensity.ComercialEdm => BuildLoudnessProfile(
                    chain, channels, sampleRate,
                    compThresholdDb: -22.0, compRatio: 3.5, compAttackMs: 22.0, compReleaseMs: 60.0,
                    clipThresholdDb: -2.0, clipCeilingDb: 0.0,
                    lookaheadMs: ComercialEdmLookaheadMs, limiterReleaseMs: 50.0),

                _ => BuildLigera(chain, channels, sampleRate)
            };
        }

        /// <summary>
        /// Front-end común: elimina DC offset y filtra sub-graves (HPF 30 Hz)
        /// antes de cualquier otra etapa, para no perder headroom ni asimetrar
        /// el clipper.
        /// </summary>
        private static ISampleProvider BuildInputConditioning(ISampleProvider source, WaveFormat format)
        {
            int channels = format.Channels;
            int sampleRate = format.SampleRate;

            ISampleProvider chain = new DcBlockerSampleProvider(source, channels, sampleRate, 10.0);

            var settings = new MasteringSettings();
            if (settings.HighPassFreqHz < sampleRate / 2.0)
                chain = new BiQuadSampleProvider(chain, channels, sampleRate, BiQuadType.HighPass,
                    settings.HighPassFreqHz, 0.0, settings.HighPassQ, 0.0);

            return chain;
        }

        /// <summary>
        /// Perfil "Ligera": compresor suave + limitador de pico clásico.
        /// </summary>
        private static ISampleProvider BuildLigera(ISampleProvider source, int channels, int sampleRate)
        {
            ISampleProvider chain = source;

            chain = new CompressorSampleProvider(chain, channels, sampleRate,
                -15.0, 2.0, 35.0, 100.0, 2.0);

            chain = new PeakLimiterSampleProvider(chain, channels, sampleRate,
                LigeraCeilingDb, 2.0);

            return chain;
        }

        /// <summary>
        /// Perfiles Loudness: compresor estéreo-enlazado + Soft Clipper +
        /// limitador true-peak con lookahead. La sonoridad la fija la
        /// normalización por LUFS externa (2 pasadas), por eso el compresor no
        /// lleva makeup.
        /// </summary>
        private static ISampleProvider BuildLoudnessProfile(
            ISampleProvider source,
            int channels,
            int sampleRate,
            double compThresholdDb,
            double compRatio,
            double compAttackMs,
            double compReleaseMs,
            double clipThresholdDb,
            double clipCeilingDb,
            double lookaheadMs,
            double limiterReleaseMs)
        {
            ISampleProvider chain = source;

            chain = new CompressorSampleProvider(chain, channels, sampleRate,
                compThresholdDb, compRatio, compAttackMs, compReleaseMs, 0.0);

            chain = new SoftClipperSampleProvider(chain, clipThresholdDb, clipCeilingDb);

            chain = new TruePeakLimiterSampleProvider(chain, channels, sampleRate,
                TruePeakCeilingDb, lookaheadMs, limiterReleaseMs);

            return chain;
        }

        /// <summary>
        /// EQ común (sin el paso alto, que ya se aplica en el front-end):
        /// graves con cuerpo, medios-bajos atenuados y estante de agudos.
        /// </summary>
        private static ISampleProvider BuildEq(ISampleProvider source, WaveFormat format)
        {
            int channels = format.Channels;
            int sampleRate = format.SampleRate;
            var settings = new MasteringSettings();

            ISampleProvider chain = source;

            // EQ paramétrico: realce suave de graves (+1.5 dB @ 80 Hz).
            if (settings.BassFreqHz < sampleRate / 2.0)
                chain = new BiQuadSampleProvider(chain, channels, sampleRate, BiQuadType.Peaking,
                    settings.BassFreqHz, settings.BassGainDb, settings.BassQ, 0.0);

            // EQ paramétrico: atenuación ligera de medios-bajos (-1.0 dB @ 315 Hz).
            if (settings.MidFreqHz < sampleRate / 2.0)
                chain = new BiQuadSampleProvider(chain, channels, sampleRate, BiQuadType.Peaking,
                    settings.MidFreqHz, settings.MidGainDb, settings.MidQ, 0.0);

            // Estante de agudos: brillo y presencia (+2.0 dB @ 10 kHz).
            if (settings.TrebleShelfFreqHz < sampleRate / 2.0)
                chain = new BiQuadSampleProvider(chain, channels, sampleRate, BiQuadType.HighShelf,
                    settings.TrebleShelfFreqHz, settings.TrebleShelfGainDb, 0.0, settings.TrebleShelfSlope);

            return chain;
        }
    }
}
