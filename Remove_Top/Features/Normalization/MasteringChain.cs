using NAudio.Wave;

namespace Remove_Top.Features.Normalization
{
    /// <summary>
    /// Parámetros de la cadena de masterización ligera.
    /// Todos los valores tienen ajustes por defecto pensados para un efecto
    /// sutil ("glue"); se pueden calibrar aquí sin tocar el resto del código.
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
        /// <summary>Cadena original suave ("glue"): preserva la dinámica.</summary>
        Ligera,

        /// <summary>Limitador duro tipo Adobe Audition: rellena la onda y sube el RMS (~ -12 dB) sin saturar.</summary>
        HardLimiter,

        /// <summary>Densidad de master comercial/EDM: RMS ~ -9/-11 dB con techo -0.3 dB.</summary>
        ComercialEdm
    }

    /// <summary>
    /// Construye la cadena de masterización como una pila de ISampleProvider.
    /// El prefijo de ecualización (paso alto → EQ) es común a todos los perfiles;
    /// la etapa de dinámica (compresor + limitador) cambia según la intensidad:
    ///   - Ligera: compresor suave + limitador de pico clásico a -0.3 dB.
    ///   - HardLimiter / ComercialEdm: compresor + limitador duro con lookahead
    ///     (input boost) que eleva el nivel promedio hasta el techo -0.3 dB.
    /// Se aplica DESPUÉS de la ganancia de normalización definida por el usuario.
    /// </summary>
    public static class MasteringChain
    {
        /// <summary>Techo de salida de todos los limitadores (dBFS).</summary>
        public const double LimiterCeilingDb = -0.3;

        /// <summary>Lookahead del limitador duro en ms (mínimo recomendado por Adobe).</summary>
        public const double HardLimiterLookaheadMs = 5.0;

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
        /// Los filtros cuya frecuencia supere la mitad de la frecuencia de
        /// muestreo (Nyquist) se omiten para no generar coeficientes inválidos.
        /// </summary>
        /// <param name="source">SampleProvider al que se le aplica la cadena.</param>
        /// <param name="format">Formato de la señal (canales y frecuencia de muestreo).</param>
        /// <param name="intensity">Perfil de intensidad de la masterización.</param>
        public static ISampleProvider Build(ISampleProvider source, WaveFormat format, MasteringIntensity intensity)
        {
            return intensity switch
            {
                MasteringIntensity.HardLimiter => BuildWithHardLimiter(
                    source, format,
                    compThresholdDb: -14.0, compRatio: 2.0, compAttackMs: 30.0, compReleaseMs: 90.0, compMakeupDb: 1.5,
                    limiterBoostDb: 5.0, limiterReleaseMs: 100.0),

                MasteringIntensity.ComercialEdm => BuildWithHardLimiter(
                    source, format,
                    compThresholdDb: -20.0, compRatio: 3.0, compAttackMs: 25.0, compReleaseMs: 80.0, compMakeupDb: 2.5,
                    limiterBoostDb: 9.0, limiterReleaseMs: 90.0),

                _ => BuildLigera(source, format)
            };
        }

        /// <summary>
        /// Perfil "Ligera": compresor suave + limitador de pico clásico (comportamiento original).
        /// </summary>
        private static ISampleProvider BuildLigera(ISampleProvider source, WaveFormat format)
        {
            int channels = format.Channels;
            int sampleRate = format.SampleRate;

            var chain = BuildEqPrefix(source, format);

            // Compresor: controla el rango dinámico y aporta cuerpo.
            chain = new CompressorSampleProvider(chain, channels, sampleRate,
                -15.0, 2.0, 35.0, 100.0, 2.0);

            // Limitador de picos: techo a -0.3 dB y boost de sonoridad, sin distorsión.
            chain = new PeakLimiterSampleProvider(chain, channels, sampleRate,
                LimiterCeilingDb, 2.0);

            return chain;
        }

        /// <summary>
        /// Perfiles con limitador duro: compresor + HardLimiter con lookahead.
        /// El input boost del limitador eleva el nivel promedio (RMS) hasta el techo.
        /// </summary>
        private static ISampleProvider BuildWithHardLimiter(
            ISampleProvider source,
            WaveFormat format,
            double compThresholdDb,
            double compRatio,
            double compAttackMs,
            double compReleaseMs,
            double compMakeupDb,
            double limiterBoostDb,
            double limiterReleaseMs)
        {
            int channels = format.Channels;
            int sampleRate = format.SampleRate;

            var chain = BuildEqPrefix(source, format);

            // Compresor: reduce el rango dinámico antes de que el limitador haga el "paso final".
            chain = new CompressorSampleProvider(chain, channels, sampleRate,
                compThresholdDb, compRatio, compAttackMs, compReleaseMs, compMakeupDb);

            // Limitador duro con lookahead: preamplifica (boost) y controla el techo.
            chain = new HardLimiterSampleProvider(chain, channels, sampleRate,
                LimiterCeilingDb, limiterBoostDb, HardLimiterLookaheadMs, limiterReleaseMs);

            return chain;
        }

        /// <summary>
        /// Prefijo de ecualización común: paso alto → graves → medios → agudos.
        /// </summary>
        private static ISampleProvider BuildEqPrefix(ISampleProvider source, WaveFormat format)
        {
            int channels = format.Channels;
            int sampleRate = format.SampleRate;
            var settings = new MasteringSettings();

            ISampleProvider chain = source;

            // Paso alto: elimina frecuencias inaudibles por debajo de 30 Hz.
            if (settings.HighPassFreqHz < sampleRate / 2.0)
                chain = new BiQuadSampleProvider(chain, channels, sampleRate, BiQuadType.HighPass,
                    settings.HighPassFreqHz, 0.0, settings.HighPassQ, 0.0);

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
