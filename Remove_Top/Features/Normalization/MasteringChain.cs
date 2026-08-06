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

        // --- Compresor de rango dinámico ---
        public double CompressorThresholdDb = -15.0;
        public double CompressorRatio = 2.0;
        public double CompressorAttackMs = 35.0;
        public double CompressorReleaseMs = 100.0;
        public double CompressorMakeupDb = 2.0;

        // --- Limitador de picos / maximizador ---
        public double LimiterCeilingDb = -0.3;
        public double LimiterBoostDb = 2.0;
    }

    /// <summary>
    /// Construye la cadena de masterización ligera como una pila de
    /// ISampleProvider en el orden recomendado:
    /// paso alto → EQ paramétrico (graves/medios/agudos) → compresor → limitador.
    /// Se aplica DESPUÉS de la ganancia de normalización definida por el usuario.
    /// </summary>
    public static class MasteringChain
    {
        /// <summary>
        /// Envuelve el origen con todos los efectos de la cadena.
        /// Los filtros cuya frecuencia supere la mitad de la frecuencia de
        /// muestreo (Nyquist) se omiten para no generar coeficientes inválidos.
        /// </summary>
        /// <param name="source">SampleProvider al que se le aplica la cadena.</param>
        /// <param name="format">Formato de la señal (canales y frecuencia de muestreo).</param>
        /// <param name="settings">Parámetros de la masterización.</param>
        public static ISampleProvider Build(ISampleProvider source, WaveFormat format, MasteringSettings settings)
        {
            int channels = format.Channels;
            int sampleRate = format.SampleRate;

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

            // Compresor: controla el rango dinámico y aporta cuerpo.
            chain = new CompressorSampleProvider(chain, channels, sampleRate,
                settings.CompressorThresholdDb, settings.CompressorRatio,
                settings.CompressorAttackMs, settings.CompressorReleaseMs,
                settings.CompressorMakeupDb);

            // Limitador de picos: techo a -0.3 dB y boost de sonoridad, sin distorsión.
            chain = new PeakLimiterSampleProvider(chain, channels, sampleRate,
                settings.LimiterCeilingDb, settings.LimiterBoostDb);

            return chain;
        }
    }
}
