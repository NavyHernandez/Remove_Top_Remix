using NAudio.Wave;
using Remove_Top.Features.Normalization;

namespace Remove_Top.Features.FormatConverter
{
    /// <summary>
    /// Cadena de mejora enfocada en VOZ (no música): corta el rumble con un
    /// paso alto a 80 Hz, limpia el "encajonado" de 250 Hz, da presencia en
    /// 4 kHz, controla sibilancia en 6.5 kHz, atenúa el hiss sobre 15 kHz,
    /// nivela con compresor suave y limita a −1.0 dBTP.
    ///
    /// Difiere de <see cref="MasteringChain"/> (musical: HPF 30 Hz, brillo
    /// 10 kHz, −10 LUFS) en corte, EQ, dinámica y objetivo (−16 LUFS,
    /// estándar podcast estéreo). Reutiliza los providers de
    /// <c>MasteringDsp</c> sin modificarlos.
    /// </summary>
    public static class VoiceChain
    {
        /// <summary>Sonoridad objetivo de voz (podcast estéreo, −16 LUFS).</summary>
        public const double TargetLufs = -16.0;

        /// <summary>Techo true-peak de la cadena de voz (dBTP).</summary>
        public const double TruePeakCeilingDb = -1.0;

        /// <summary>
        /// Envuelve el origen (ya en estéreo 44.1 kHz) con la cadena de voz.
        /// Los filtros sobre Nyquist se omiten para no generar coeficientes inválidos.
        /// </summary>
        /// <param name="applyGate">Puerta de ruido managed (solo si no hubo afftdn).</param>
        public static ISampleProvider Build(ISampleProvider source, int channels, int sampleRate, bool applyGate)
        {
            ISampleProvider chain = new DcBlockerSampleProvider(source, channels, sampleRate, 10.0);

            // 1. Paso alto 80 Hz: rumble/HVAC/tráfico fuera antes de todo.
            chain = AddHighPass(chain, channels, sampleRate, 80.0, 0.707);
            // 2. Puerta de ruido (solo fallback sin ffmpeg): cierra en pausas.
            if (applyGate)
                chain = new NoiseGateSampleProvider(chain, channels, sampleRate, thresholdDb: -45.0, attackMs: 10.0, releaseMs: 200.0);
            // 3. EQ voz: quita caja (250 Hz), da presencia (4 kHz), doma S (6.5 kHz).
            chain = AddPeaking(chain, channels, sampleRate, 250.0, -3.0, 0.8);
            chain = AddPeaking(chain, channels, sampleRate, 4000.0, 2.0, 0.9);
            chain = AddPeaking(chain, channels, sampleRate, 6500.0, -3.0, 2.0);
            // 4. Hiss: estante que atenúa por encima de 15 kHz (los BiQuad del
            // proyecto no tienen low-pass; el shelf es la atenuación disponible).
            chain = AddHighShelf(chain, channels, sampleRate, 15000.0, -4.0);
            // 5. Compresor de voz: nivela susurros/gritos con makeup suave.
            chain = new CompressorSampleProvider(chain, channels, sampleRate,
                thresholdDb: -18.0, ratio: 3.0, attackMs: 12.0, releaseMs: 200.0, makeupDb: 3.0);
            // 6. Clipper suave + limitador true-peak a −1.0 dBTP (seguro para MP3).
            chain = new SoftClipperSampleProvider(chain, thresholdDb: -1.0, ceilingDb: 0.5);
            chain = new TruePeakLimiterSampleProvider(chain, channels, sampleRate,
                ceilingDb: TruePeakCeilingDb, lookaheadMs: 5.0, releaseMs: 50.0);
            return chain;
        }

        private static ISampleProvider AddHighPass(ISampleProvider chain, int channels, int sampleRate, double freq, double q)
        {
            if (freq >= sampleRate / 2.0) return chain;
            return new BiQuadSampleProvider(chain, channels, sampleRate, BiQuadType.HighPass, freq, 0.0, q, 0.0);
        }

        private static ISampleProvider AddPeaking(ISampleProvider chain, int channels, int sampleRate, double freq, double gainDb, double q)
        {
            if (freq >= sampleRate / 2.0) return chain;
            return new BiQuadSampleProvider(chain, channels, sampleRate, BiQuadType.Peaking, freq, gainDb, q, 0.0);
        }

        private static ISampleProvider AddHighShelf(ISampleProvider chain, int channels, int sampleRate, double freq, double gainDb)
        {
            if (freq >= sampleRate / 2.0) return chain;
            return new BiQuadSampleProvider(chain, channels, sampleRate, BiQuadType.HighShelf, freq, gainDb, 0.0, 0.5);
        }
    }

    /// <summary>
    /// Puerta de ruido estéreo-enlazada (fallback managed cuando no hay ffmpeg
    /// para el <c>afftdn</c>): por debajo del umbral cierra la ganancia de forma
    /// suave (attack/release), limpiando el fondo en las pausas de la voz sin
    /// recortar los inicios de palabra.
    /// </summary>
    public sealed class NoiseGateSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private readonly int _channels;
        private readonly float _thresholdLinear;
        private readonly float _attackCoeff;
        private readonly float _releaseCoeff;
        private float _gain = 1f;

        public NoiseGateSampleProvider(ISampleProvider source, int channels, int sampleRate, double thresholdDb, double attackMs, double releaseMs)
        {
            _source = source;
            _channels = channels;
            _thresholdLinear = (float)System.Math.Pow(10.0, thresholdDb / 20.0);
            _attackCoeff = (float)System.Math.Exp(-1.0 / (System.Math.Max(attackMs, 0.1) / 1000.0 * sampleRate));
            _releaseCoeff = (float)System.Math.Exp(-1.0 / (System.Math.Max(releaseMs, 0.1) / 1000.0 * sampleRate));
        }

        public WaveFormat WaveFormat => _source.WaveFormat;

        public int Read(float[] buffer, int offset, int count)
        {
            int samplesRead = _source.Read(buffer, offset, count);
            int frames = samplesRead / _channels;
            for (int f = 0; f < frames; f++)
            {
                int baseIdx = offset + f * _channels;
                float peak = 0f;
                for (int c = 0; c < _channels; c++)
                {
                    float a = System.Math.Abs(buffer[baseIdx + c]);
                    if (a > peak) peak = a;
                }

                // Por encima del umbral abre rápido; por debajo cierra lento.
                float coeff = peak > _thresholdLinear ? _attackCoeff : _releaseCoeff;
                float target = peak > _thresholdLinear ? 1f : 0f;
                _gain = coeff * _gain + (1f - coeff) * target;

                for (int c = 0; c < _channels; c++)
                    buffer[baseIdx + c] *= _gain;
            }
            return samplesRead;
        }
    }
}
