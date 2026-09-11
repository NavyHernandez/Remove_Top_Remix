using NAudio.Wave;
using System;

namespace Remove_Top.Features.Normalization
{
    /// <summary>Tipo de filtro biquad soportado por la cadena de masterización.</summary>
    public enum BiQuadType
    {
        /// <summary>Paso alto: atenúa las frecuencias por debajo de la de corte.</summary>
        HighPass,

        /// <summary>EQ paramétrico en campana (peaking) para realzar o atenuar una banda.</summary>
        Peaking,

        /// <summary>Estante de agudos (high-shelf): realza o atenúa todo lo que está por encima de la frecuencia.</summary>
        HighShelf
    }

    /// <summary>
    /// Filtro biquad de segundo orden implementado con las fórmulas del
    /// "Audio EQ Cookbook" (RBJ) en Forma Directa II Transpuesta.
    /// Cada instancia mantiene su propio estado, por lo que debe crearse una
    /// por canal. Se usa para el paso alto, el EQ paramétrico y el estante.
    /// </summary>
    public sealed class BiQuad
    {
        // Coeficientes del filtro (se calculan una sola vez en el constructor)
        private readonly double _b0;
        private readonly double _b1;
        private readonly double _b2;
        private readonly double _a1;
        private readonly double _a2;

        // Estado del filtro (Forma Directa II Transpuesta)
        private double _s1;
        private double _s2;

        /// <summary>
        /// Crea un filtro biquad con los parámetros indicados.
        /// </summary>
        /// <param name="type">Tipo de filtro (paso alto, peaking o estante de agudos).</param>
        /// <param name="sampleRate">Frecuencia de muestreo de la señal (Hz).</param>
        /// <param name="freq">Frecuencia central o de corte (Hz).</param>
        /// <param name="gainDb">Ganancia en dB (solo usada por peaking y estantes).</param>
        /// <param name="q">Factor de calidad del filtro (paso alto y peaking).</param>
        /// <param name="shelfSlope">Pendiente del estante (solo HighShelf, rango 0.1-1).</param>
        public BiQuad(BiQuadType type, double sampleRate, double freq, double gainDb, double q, double shelfSlope)
        {
            double w0 = 2.0 * Math.PI * freq / sampleRate;
            double cosW0 = Math.Cos(w0);
            double sinW0 = Math.Sin(w0);

            double b0, b1, b2, a0, a1, a2;

            switch (type)
            {
                // Filtro paso alto: limpia sub-graves inaudibles (rumble).
                case BiQuadType.HighPass:
                    {
                        double alpha = sinW0 / (2.0 * Math.Max(q, 0.1));
                        b0 = (1.0 + cosW0) / 2.0;
                        b1 = -(1.0 + cosW0);
                        b2 = (1.0 + cosW0) / 2.0;
                        a0 = 1.0 + alpha;
                        a1 = -2.0 * cosW0;
                        a2 = 1.0 - alpha;
                        break;
                    }

                // EQ paramétrico en campana: realza o atenúa una banda central.
                case BiQuadType.Peaking:
                    {
                        double A = Math.Pow(10.0, gainDb / 40.0);
                        double alpha = sinW0 / (2.0 * Math.Max(q, 0.1));
                        b0 = 1.0 + alpha * A;
                        b1 = -2.0 * cosW0;
                        b2 = 1.0 - alpha * A;
                        a0 = 1.0 + alpha / A;
                        a1 = -2.0 * cosW0;
                        a2 = 1.0 - alpha / A;
                        break;
                    }

                // Estante de agudos: brillo y presencia por encima de la frecuencia.
                case BiQuadType.HighShelf:
                    {
                        double A = Math.Pow(10.0, gainDb / 40.0);
                        double rootA = Math.Sqrt(A);
                        double s = Math.Clamp(shelfSlope, 0.1, 1.0);
                        double alpha = sinW0 / 2.0 * Math.Sqrt((A + 1.0 / A) * (1.0 / s - 1.0) + 2.0);

                        b0 = A * ((A + 1.0) + (A - 1.0) * cosW0 + 2.0 * rootA * alpha);
                        b1 = -2.0 * A * ((A - 1.0) + (A + 1.0) * cosW0);
                        b2 = A * ((A + 1.0) + (A - 1.0) * cosW0 - 2.0 * rootA * alpha);
                        a0 = (A + 1.0) - (A - 1.0) * cosW0 + 2.0 * rootA * alpha;
                        a1 = 2.0 * ((A - 1.0) - (A + 1.0) * cosW0);
                        a2 = (A + 1.0) - (A - 1.0) * cosW0 - 2.0 * rootA * alpha;
                        break;
                    }

                default:
                    throw new ArgumentOutOfRangeException(nameof(type));
            }

            // Normaliza los coeficientes para que a0 = 1.
            _b0 = b0 / a0;
            _b1 = b1 / a0;
            _b2 = b2 / a0;
            _a1 = a1 / a0;
            _a2 = a2 / a0;
        }

        /// <summary>Procesa una única muestra de un canal.</summary>
        public float Process(float input)
        {
            // Forma Directa II Transpuesta: y[n] = b0·x[n] + s1, etc.
            double output = _b0 * input + _s1;
            _s1 = _b1 * input - _a1 * output + _s2;
            _s2 = _b2 * input - _a2 * output;
            return (float)output;
        }

        /// <summary>Reinicia el estado interno del filtro.</summary>
        public void Reset()
        {
            _s1 = 0.0;
            _s2 = 0.0;
        }
    }

    /// <summary>
    /// ISampleProvider que aplica un filtro biquad a una señal interleaved,
    /// manteniendo una instancia de filtro independiente por canal.
    /// </summary>
    public sealed class BiQuadSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private readonly BiQuad[] _filters;

        /// <summary>
        /// Envuelve el origen con un filtro biquad por canal.
        /// </summary>
        public BiQuadSampleProvider(
            ISampleProvider source,
            int channels,
            int sampleRate,
            BiQuadType type,
            double freq,
            double gainDb,
            double q,
            double shelfSlope)
        {
            _source = source;
            _filters = new BiQuad[channels];
            for (int i = 0; i < channels; i++)
                _filters[i] = new BiQuad(type, sampleRate, freq, gainDb, q, shelfSlope);
        }

        public WaveFormat WaveFormat => _source.WaveFormat;

        public int Read(float[] buffer, int offset, int count)
        {
            int samplesRead = _source.Read(buffer, offset, count);
            int channels = _filters.Length;

            for (int i = 0; i < samplesRead; i++)
                buffer[offset + i] = _filters[i % channels].Process(buffer[offset + i]);

            return samplesRead;
        }
    }

    /// <summary>
    /// Compresor de rango dinámico feed-forward con detector de envolvente por
    /// canal, tiempos de attack/release configurables y makeup gain.
    /// Suaviza los picos y aporta cuerpo antes del limitador final.
    /// </summary>
    public sealed class CompressorSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private readonly int _channels;
        private readonly float _thresholdDb;
        private readonly float _ratio;
        private readonly float _attackCoeff;
        private readonly float _releaseCoeff;
        private readonly float _makeupLinear;

        // Envolvente (nivel detectado) por canal
        private readonly float[] _envelope;

        /// <summary>
        /// Crea un compresor con detección de pico y envolvente attack/release.
        /// </summary>
        /// <param name="thresholdDb">Umbral en dBFS por encima del cual se comprime (negativo).</param>
        /// <param name="ratio">Relación de compresión (p.ej. 2.0 = 2:1).</param>
        /// <param name="attackMs">Tiempo de ataque en milisegundos.</param>
        /// <param name="releaseMs">Tiempo de liberación en milisegundos.</param>
        /// <param name="makeupDb">Ganancia de compensación (makeup) en dB, se aplica siempre.</param>
        public CompressorSampleProvider(
            ISampleProvider source,
            int channels,
            int sampleRate,
            double thresholdDb,
            double ratio,
            double attackMs,
            double releaseMs,
            double makeupDb)
        {
            _source = source;
            _channels = channels;
            _thresholdDb = (float)thresholdDb;
            _ratio = (float)Math.Max(ratio, 1.0);

            // Coeficientes de suavizado: 1 - exp(-1 / (tiempo * sampleRate))
            _attackCoeff = (float)Math.Exp(-1.0 / (Math.Max(attackMs, 0.1) / 1000.0 * sampleRate));
            _releaseCoeff = (float)Math.Exp(-1.0 / (Math.Max(releaseMs, 0.1) / 1000.0 * sampleRate));
            _makeupLinear = (float)Math.Pow(10.0, makeupDb / 20.0);

            _envelope = new float[channels];
        }

        public WaveFormat WaveFormat => _source.WaveFormat;

        public int Read(float[] buffer, int offset, int count)
        {
            int samplesRead = _source.Read(buffer, offset, count);
            int frames = samplesRead / _channels;
            int tailStart = frames * _channels;

            // Detección estéreo-enlazada: un solo detector con el máximo de los
            // canales, para no desplazar la imagen estéreo al comprimir.
            for (int f = 0; f < frames; f++)
            {
                int baseIdx = offset + f * _channels;

                float peak = 0f;
                for (int c = 0; c < _channels; c++)
                {
                    float a = Math.Abs(buffer[baseIdx + c]);
                    if (a > peak) peak = a;
                }

                float envelope = _envelope[0];
                float coeff = peak > envelope ? _attackCoeff : _releaseCoeff;
                envelope = coeff * envelope + (1f - coeff) * peak;
                _envelope[0] = envelope;

                float envelopeDb = 20f * (float)Math.Log10(envelope + 1e-12f);
                float overDb = envelopeDb - _thresholdDb;
                float reductionDb = overDb > 0f ? overDb * (1f - 1f / _ratio) : 0f;
                float gain = _makeupLinear * (float)Math.Pow(10.0, -reductionDb / 20.0);

                for (int c = 0; c < _channels; c++)
                    buffer[baseIdx + c] *= gain;
            }

            // Remanente (no ocurre con NAudio, que entrega múltiplos de canales).
            for (int i = tailStart; i < samplesRead; i++)
                buffer[offset + i] *= _makeupLinear;

            return samplesRead;
        }
    }

    /// <summary>
    /// Limitador de picos / maximizador.
    /// Aplica un boost (makeup) y controla con una envolvente de ataque/release
    /// cualquier pico que supere el techo configurado, reduciendo la ganancia de
    /// forma suave (sin clipeo duro) para evitar distorsión digital.
    /// </summary>
    public sealed class PeakLimiterSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private readonly int _channels;
        private readonly float _boostLinear;
        private readonly float _ceilingLinear;
        private readonly float _attackCoeff;
        private readonly float _releaseCoeff;

        // Envolvente del pico y ganancia de reducción suavizada por canal
        private readonly float[] _envelope;
        private readonly float[] _gainReduction;

        /// <summary>
        /// Crea un limitador de picos.
        /// </summary>
        /// <param name="ceilingDb">Techo de salida en dBFS (negativo, p.ej. -0.3).</param>
        /// <param name="boostDb">Boost de sonoridad (makeup) en dB.</param>
        public PeakLimiterSampleProvider(ISampleProvider source, int channels, int sampleRate, double ceilingDb, double boostDb)
        {
            _source = source;
            _channels = channels;
            _boostLinear = (float)Math.Pow(10.0, boostDb / 20.0);
            _ceilingLinear = (float)Math.Pow(10.0, ceilingDb / 20.0);

            // Ataque muy rápido (~0.5 ms) y liberación suave (~50 ms).
            _attackCoeff = (float)Math.Exp(-1.0 / (0.0005 * sampleRate));
            _releaseCoeff = (float)Math.Exp(-1.0 / (0.050 * sampleRate));

            _envelope = new float[channels];
            _gainReduction = new float[channels];
            Array.Fill(_gainReduction, 1f);
        }

        public WaveFormat WaveFormat => _source.WaveFormat;

        public int Read(float[] buffer, int offset, int count)
        {
            int samplesRead = _source.Read(buffer, offset, count);

            for (int i = 0; i < samplesRead; i++)
            {
                int channel = i % _channels;
                float input = buffer[offset + i] * _boostLinear;
                float abs = Math.Abs(input);

                // Envolvente del pico con ataque/release por canal.
                float envelope = _envelope[channel];
                float envCoeff = abs > envelope ? _attackCoeff : _releaseCoeff;
                envelope = envCoeff * envelope + (1f - envCoeff) * abs;
                _envelope[channel] = envelope;

                // Ganancia objetivo: solo reduce si la envolvente supera el techo.
                float targetGain = envelope > _ceilingLinear ? _ceilingLinear / envelope : 1f;

                // Suaviza la ganancia para evitar artefactos auditivos.
                float gain = _gainReduction[channel];
                float gainCoeff = targetGain < gain ? _attackCoeff : _releaseCoeff;
                gain = gainCoeff * gain + (1f - gainCoeff) * targetGain;
                _gainReduction[channel] = gain;

                buffer[offset + i] = input * gain;
            }

            return samplesRead;
        }
    }

    /// <summary>
    /// Limitador duro tipo "Hard Limiter" (estilo Adobe Audition).
    /// Combina pre-amplificación (Input Boost), lookahead y techo de salida
    /// (ceiling). El lookahead permite anticipar los picos antes de que se
    /// escuchen, por lo que se puede subir mucha más sonoridad (RMS) sin
    /// saturar: es el paso profesional que rellena la forma de onda hasta
    /// cerca del techo (-0.3 dB) como en un master comercial.
    /// La ganancia es estéreo-enlazada para no desplazar la imagen estéreo.
    /// </summary>
    public sealed class HardLimiterSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private readonly int _channels;
        private readonly int _lookaheadFrames;
        private readonly float _boostLinear;
        private readonly float _ceilingLinear;
        private readonly float _releaseCoeff;

        // Anillo de retardo: mantiene la señal preamplificada durante el lookahead.
        // La muestra que sale del anillo es la que se emite (retardada); el resto
        // del anillo es su "futuro", usado para calcular la reducción de ganancia.
        private readonly float[] _ring;
        private int _ringPos;

        // Ganancia actual suavizada con release (estéreo-enlazada).
        private float _currentGain = 1f;

        private bool _eof;
        private int _flushedFrames;

        private readonly float[] _frame;
        private readonly float[] _outFrame;

        /// <summary>
        /// Crea el limitador duro.
        /// </summary>
        /// <param name="ceilingDb">Techo de salida en dBFS (negativo, p.ej. -0.3).</param>
        /// <param name="boostDb">Pre-amplificación (input boost) en dB antes de limitar. Es lo que eleva el nivel promedio.</param>
        /// <param name="lookaheadMs">Tiempo de anticipación en ms (Adobe recomienda al menos 5 ms para evitar distorsión audible).</param>
        /// <param name="releaseMs">Tiempo de liberación en ms (~100 ms preserva los graves).</param>
        public HardLimiterSampleProvider(
            ISampleProvider source,
            int channels,
            int sampleRate,
            double ceilingDb,
            double boostDb,
            double lookaheadMs,
            double releaseMs)
        {
            _source = source;
            _channels = channels;
            _lookaheadFrames = Math.Max(1, (int)Math.Round(lookaheadMs / 1000.0 * sampleRate));
            _boostLinear = (float)Math.Pow(10.0, boostDb / 20.0);
            _ceilingLinear = (float)Math.Pow(10.0, ceilingDb / 20.0);
            _releaseCoeff = (float)Math.Exp(-1.0 / (Math.Max(releaseMs, 0.1) / 1000.0 * sampleRate));

            _ring = new float[_lookaheadFrames * channels];
            _frame = new float[channels];
            _outFrame = new float[channels];
        }

        public WaveFormat WaveFormat => _source.WaveFormat;

        public int Read(float[] buffer, int offset, int count)
        {
            int frames = count / _channels;
            if (frames == 0)
                return 0;

            int written = 0;

            for (int f = 0; f < frames; f++)
            {
                if (!_eof)
                {
                    int n = _source.Read(_frame, 0, _channels);
                    if (n < _channels)
                    {
                        // Fin de la señal: completa con silencio y vacía la cola de retardo.
                        for (int c = n; c < _channels; c++)
                            _frame[c] = 0f;
                        _eof = true;
                        _flushedFrames = 0;
                    }

                    // Pre-amplificación (Input Boost) antes de limitar.
                    for (int c = 0; c < _channels; c++)
                        _frame[c] *= _boostLinear;
                }
                else
                {
                    Array.Clear(_frame, 0, _channels);
                    _flushedFrames++;
                    if (_flushedFrames >= _lookaheadFrames)
                        break; // cola de retardo vaciada
                }

                // Muestra retardada que sale del anillo (es la que se va a emitir).
                int pos = _ringPos * _channels;
                for (int c = 0; c < _channels; c++)
                    _outFrame[c] = _ring[pos + c];

                // Pico en la ventana de lookahead: la muestra de salida más sus
                // sucesoras (aún sin sobrescribir el hueco que se va a liberar).
                float peak = 0f;
                for (int i = 0; i < _ring.Length; i++)
                {
                    float abs = Math.Abs(_ring[i]);
                    if (abs > peak) peak = abs;
                }

                float targetGain = peak > _ceilingLinear ? _ceilingLinear / peak : 1f;

                // Ataque instantáneo (gracias al lookahead) y recuperación con release.
                if (targetGain < _currentGain)
                    _currentGain = targetGain;
                else
                    _currentGain = _releaseCoeff * _currentGain + (1f - _releaseCoeff) * targetGain;

                // Emite la muestra retardada con la ganancia aplicada (estéreo-enlazada).
                for (int c = 0; c < _channels; c++)
                    buffer[offset + written + c] = _outFrame[c] * _currentGain;

                // Guarda la nueva muestra preamplificada en el hueco liberado.
                for (int c = 0; c < _channels; c++)
                    _ring[pos + c] = _frame[c];

                _ringPos = (_ringPos + 1) % _lookaheadFrames;
                written += _channels;
            }

            return written;
        }
    }

    /// <summary>
    /// Eliminador de DC offset (one-pole high-pass a ~10 Hz).
    /// Evita que un sesgo continuo se coma headroom y que el Soft Clipper
    /// distorsione de forma asimétrica.
    /// </summary>
    public sealed class DcBlockerSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private readonly int _channels;
        private readonly float _r;
        private readonly float[] _x1;
        private readonly float[] _y1;

        public DcBlockerSampleProvider(ISampleProvider source, int channels, int sampleRate, double cutoffHz = 10.0)
        {
            _source = source;
            _channels = channels;
            // R tal que la frecuencia de corte sea cutoffHz.
            _r = (float)(1.0 - 2.0 * Math.PI * cutoffHz / sampleRate);
            if (_r < 0.9f) _r = 0.9f;
            if (_r > 0.99999f) _r = 0.99999f;
            _x1 = new float[channels];
            _y1 = new float[channels];
        }

        public WaveFormat WaveFormat => _source.WaveFormat;

        public int Read(float[] buffer, int offset, int count)
        {
            int samplesRead = _source.Read(buffer, offset, count);
            for (int i = 0; i < samplesRead; i++)
            {
                int c = i % _channels;
                float x = buffer[offset + i];
                float y = x - _x1[c] + _r * _y1[c];
                _x1[c] = x;
                _y1[c] = y;
                buffer[offset + i] = y;
            }
            return samplesRead;
        }
    }

    /// <summary>
    /// Soft Clipper (rodilla suave tipo tanh). Por encima del umbral comprime
    /// el pico de forma progresiva hacia el techo en vez de recortarlo en seco.
    /// Se coloca antes del limitador true-peak: al suavizar los transientes,
    /// el limitador trabaja menos y se reduce el bombeo (limitación en etapas).
    /// </summary>
    public sealed class SoftClipperSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private readonly float _threshold;
        private readonly float _ceiling;

        public SoftClipperSampleProvider(ISampleProvider source, double thresholdDb, double ceilingDb)
        {
            _source = source;
            _threshold = (float)Math.Pow(10.0, thresholdDb / 20.0);
            _ceiling = (float)Math.Pow(10.0, ceilingDb / 20.0);
            if (_ceiling <= _threshold)
                _ceiling = _threshold * 1.0001f;
        }

        public WaveFormat WaveFormat => _source.WaveFormat;

        public int Read(float[] buffer, int offset, int count)
        {
            int samplesRead = _source.Read(buffer, offset, count);
            float t = _threshold;
            float range = _ceiling - _threshold;

            for (int i = 0; i < samplesRead; i++)
            {
                float x = buffer[offset + i];
                float a = Math.Abs(x);
                if (a > t)
                {
                    float over = a - t;
                    float shaped = t + range * (float)Math.Tanh(over / range);
                    buffer[offset + i] = x < 0f ? -shaped : shaped;
                }
            }
            return samplesRead;
        }
    }

    /// <summary>
    /// Limitador brickwall true-peak con lookahead.
    ///
    ///   - Lookahead: la señal se retarda y la reducción de ganancia se calcula
    ///     sobre el pico de la ventana, de modo que la atenuación ya está activa
    ///     cuando llega el transiente.
    ///   - Detección true-peak: por cada muestra se estima el pico del intervalo
    ///     inter-muestra con interpolación cúbica (Catmull-Rom) a 4×, de modo que
    ///     el techo se respeta también en los picos inter-muestra.
    ///   - Estéreo-enlazado: un solo detector (máximo de canales) para no mover
    ///     la imagen estéreo.
    ///   - Release adaptativo: cuanto mayor es la reducción, más rápido libera.
    /// </summary>
    public sealed class TruePeakLimiterSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private readonly int _channels;
        private readonly int _lookaheadFrames;
        private readonly float _ceilingLinear;
        private readonly double _sampleRate;
        private readonly double _baseReleaseMs;

        private readonly float[] _ring;      // frames retardados
        private readonly float[] _tpRing;    // true peak por frame
        private int _ringPos;

        // Historial de las últimas 4 muestras por canal (interpolación cúbica).
        private readonly float[][] _hist;

        private float _currentGain = 1f;

        private bool _eof;
        private int _flushedFrames;

        private readonly float[] _frame;
        private readonly float[] _outFrame;

        public TruePeakLimiterSampleProvider(
            ISampleProvider source,
            int channels,
            int sampleRate,
            double ceilingDb,
            double lookaheadMs,
            double releaseMs)
        {
            _source = source;
            _channels = channels;
            _sampleRate = sampleRate;
            _lookaheadFrames = Math.Max(1, (int)Math.Round(lookaheadMs / 1000.0 * sampleRate));
            _ceilingLinear = (float)Math.Pow(10.0, ceilingDb / 20.0);
            _baseReleaseMs = Math.Max(1.0, releaseMs);

            _ring = new float[_lookaheadFrames * _channels];
            _tpRing = new float[_lookaheadFrames];
            _frame = new float[_channels];
            _outFrame = new float[_channels];

            _hist = new float[_channels][];
            for (int c = 0; c < _channels; c++)
                _hist[c] = new float[4];
        }

        public WaveFormat WaveFormat => _source.WaveFormat;

        public int Read(float[] buffer, int offset, int count)
        {
            int frames = count / _channels;
            if (frames == 0)
                return 0;

            int written = 0;

            for (int f = 0; f < frames; f++)
            {
                if (!_eof)
                {
                    int n = _source.Read(_frame, 0, _channels);
                    if (n < _channels)
                    {
                        for (int c = n; c < _channels; c++)
                            _frame[c] = 0f;
                        _eof = true;
                        _flushedFrames = 0;
                    }
                }
                else
                {
                    Array.Clear(_frame, 0, _channels);
                    _flushedFrames++;
                    if (_flushedFrames >= _lookaheadFrames)
                        break;
                }

                // True peak del frame (máximo entre canales, interpolado 4×).
                float truePeak = 0f;
                for (int c = 0; c < _channels; c++)
                {
                    var h = _hist[c];
                    h[0] = h[1];
                    h[1] = h[2];
                    h[2] = h[3];
                    h[3] = _frame[c];

                    float p = CatmullRomPeak(h);
                    if (p > truePeak) truePeak = p;
                }

                // Muestra retardada que se emite.
                int pos = _ringPos * _channels;
                for (int c = 0; c < _channels; c++)
                    _outFrame[c] = _ring[pos + c];

                // Pico de la ventana de lookahead.
                float peak = 0f;
                for (int i = 0; i < _tpRing.Length; i++)
                {
                    float a = _tpRing[i];
                    if (a > peak) peak = a;
                }

                float targetGain = peak > _ceilingLinear ? _ceilingLinear / peak : 1f;

                if (targetGain < _currentGain)
                {
                    // Ataque instantáneo (el lookahead ya anticipó el pico).
                    _currentGain = targetGain;
                }
                else
                {
                    // Release adaptativo: más rápido cuanto mayor sea la reducción.
                    float grDb = -20f * (float)Math.Log10(Math.Max(_currentGain, 1e-6f));
                    double relMs = _baseReleaseMs + grDb;
                    if (relMs < _baseReleaseMs) relMs = _baseReleaseMs;
                    if (relMs > _baseReleaseMs * 3.0) relMs = _baseReleaseMs * 3.0;
                    float coeff = (float)Math.Exp(-1.0 / (relMs / 1000.0 * _sampleRate));
                    _currentGain = coeff * _currentGain + (1f - coeff) * targetGain;
                }

                for (int c = 0; c < _channels; c++)
                    buffer[offset + written + c] = _outFrame[c] * _currentGain;

                for (int c = 0; c < _channels; c++)
                    _ring[pos + c] = _frame[c];
                _tpRing[_ringPos] = truePeak;

                _ringPos = (_ringPos + 1) % _lookaheadFrames;
                written += _channels;
            }

            return written;
        }

        /// <summary>
        /// Estima el pico verdadero del intervalo entre h[1] y h[2] con
        /// interpolación cúbica de Catmull-Rom evaluada a 4× (t = 0.25/0.5/0.75)
        /// más los extremos. Incluye el posible sobre-pico inter-muestra.
        /// </summary>
        private static float CatmullRomPeak(float[] h)
        {
            float p0 = h[0], p1 = h[1], p2 = h[2], p3 = h[3];
            float peak = Math.Max(Math.Abs(p1), Math.Abs(p2));

            for (int k = 1; k <= 3; k++)
            {
                float t = k * 0.25f;
                float t2 = t * t;
                float t3 = t2 * t;
                float y = 0.5f * (
                    (2f * p1) +
                    (-p0 + p2) * t +
                    (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
                    (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
                float a = Math.Abs(y);
                if (a > peak) peak = a;
            }

            return peak;
        }
    }

    /// <summary>
    /// Dither TPDF (Triangular Probability Density Function), último paso antes
    /// de escribir. Suma ruido triangular de 1 LSB de la profundidad destino
    /// para que la cuantización a 16/24 bits no introduzca distorsión de
    /// correlación. La escala se adapta a la profundidad del formato de salida.
    /// </summary>
    public sealed class TpdfDitherSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private readonly float _lsb;
        private readonly Random _rng = new();

        public TpdfDitherSampleProvider(ISampleProvider source, int bitsPerSample)
        {
            _source = source;
            int bits = bitsPerSample > 0 ? bitsPerSample : 16;
            // Rango completo ±1 = 2.0 → 1 LSB = 2 / 2^bits.
            _lsb = (float)(2.0 / Math.Pow(2.0, bits));
        }

        public WaveFormat WaveFormat => _source.WaveFormat;

        public int Read(float[] buffer, int offset, int count)
        {
            int samplesRead = _source.Read(buffer, offset, count);
            for (int i = 0; i < samplesRead; i++)
            {
                float noise = (float)(_rng.NextDouble() - _rng.NextDouble()) * _lsb;
                buffer[offset + i] += noise;
            }
            return samplesRead;
        }
    }
}
