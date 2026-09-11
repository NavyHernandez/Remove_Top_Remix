using System;
using System.Collections.Generic;

namespace Remove_Top.Features.Normalization
{
    /// <summary>
    /// Medidor de sonoridad percibida EBU R128 / ITU-R BS.1770-4 (LUFS Integrated).
    ///
    /// Aplica el filtro K-weighting (pre-filtro high-shelf + RLB high-pass) a la
    /// señal y agrega bloques de 400 ms con 75 % de solape (hop 100 ms). El valor
    /// integrado se obtiene con doble gating: absoluto (−70 LUFS) y relativo
    /// (−10 LU respecto a la media ya gateada).
    ///
    /// Solo mide: no modifica la señal del llamador (internamente filtra en su
    /// propio estado). Se usa para calcular la ganancia de masterización por
    /// sonoridad percibida en vez de RMS.
    /// </summary>
    public sealed class LoudnessMeter
    {
        private const double AbsoluteGateLufs = -70.0;
        private const double RelativeGateLu = -10.0;
        private const double LoudnessOffset = -0.691;

        private readonly int _channels;
        private readonly int _blockSamples;   // 400 ms
        private readonly int _hopSamples;     // 100 ms

        // Filtro K-weighting por canal: pre-filtro + RLB en serie.
        private readonly BiquadStage[][] _stages;

        // Ventana deslizante de energía (muestras al cuadrado) por canal.
        private readonly double[][] _ring;
        private readonly double[] _ringSum;
        private int _ringPos;
        private int _samplesSinceHop;
        private long _totalFrames;
        private int _channelCursor;

        // Bloques medidos (energía combinada de canales y su loudness).
        private readonly List<double> _blockEnergy = new();
        private readonly List<double> _blockLoudness = new();

        public LoudnessMeter(int channels, int sampleRate)
        {
            _channels = Math.Max(1, channels);
            _blockSamples = Math.Max(1, (int)Math.Round(0.400 * sampleRate));
            _hopSamples = Math.Max(1, (int)Math.Round(0.100 * sampleRate));

            var pre = BiquadStage.CreateKWeightingPreFilter(sampleRate);
            var rlb = BiquadStage.CreateKWeightingHighPass(sampleRate);

            _stages = new BiquadStage[_channels][];
            for (int c = 0; c < _channels; c++)
                _stages[c] = new[] { pre.CreateCopy(), rlb.CreateCopy() };

            _ring = new double[_channels][];
            for (int c = 0; c < _channels; c++)
                _ring[c] = new double[_blockSamples];
            _ringSum = new double[_channels];
        }

        /// <summary>
        /// Procesa un bloque de muestras interleaved (float −1..1). No modifica
        /// el búfer: el filtrado se guarda en el estado interno del medidor.
        /// </summary>
        public void AddSamples(float[] buffer, int offset, int count)
        {
            for (int i = 0; i < count; i++)
            {
                int ch = _channelCursor;
                _channelCursor++;
                if (_channelCursor >= _channels)
                    _channelCursor = 0;

                double x = buffer[offset + i];
                var stages = _stages[ch];
                x = stages[0].Process(x);
                x = stages[1].Process(x);

                double sq = x * x;
                double old = _ring[ch][_ringPos];
                _ring[ch][_ringPos] = sq;
                _ringSum[ch] += sq - old;

                if (ch == _channels - 1)
                {
                    _ringPos++;
                    if (_ringPos >= _blockSamples)
                        _ringPos = 0;

                    _totalFrames++;
                    _samplesSinceHop++;

                    // El primer bloque se captura cuando la ventana ya está llena.
                    if (_totalFrames >= _blockSamples && _samplesSinceHop >= _hopSamples)
                    {
                        _samplesSinceHop = 0;
                        CaptureBlock();
                    }
                }
            }
        }

        /// <summary>
        /// Sonoridad integrada (LUFS-I) de todo lo procesado. Devuelve
        /// <see cref="double.NegativeInfinity"/> si no hubo bloques válidos.
        /// </summary>
        public double IntegratedLufs
        {
            get
            {
                if (_blockEnergy.Count == 0)
                    return double.NegativeInfinity;

                // Gating absoluto.
                double sumAbs = 0.0;
                int nAbs = 0;
                for (int i = 0; i < _blockEnergy.Count; i++)
                {
                    if (_blockLoudness[i] > AbsoluteGateLufs)
                    {
                        sumAbs += _blockEnergy[i];
                        nAbs++;
                    }
                }
                if (nAbs == 0)
                    return double.NegativeInfinity;

                double relativeThreshold =
                    LoudnessOffset + 10.0 * Math.Log10(sumAbs / nAbs) + RelativeGateLu;

                // Gating relativo sobre los bloques que pasaron el absoluto.
                double sumRel = 0.0;
                int nRel = 0;
                for (int i = 0; i < _blockEnergy.Count; i++)
                {
                    if (_blockLoudness[i] > AbsoluteGateLufs &&
                        _blockLoudness[i] > relativeThreshold)
                    {
                        sumRel += _blockEnergy[i];
                        nRel++;
                    }
                }

                if (nRel == 0)
                {
                    sumRel = sumAbs;
                    nRel = nAbs;
                }

                return LoudnessOffset + 10.0 * Math.Log10(sumRel / nRel);
            }
        }

        private void CaptureBlock()
        {
            // Energía combinada de canales del bloque (z_j / blockSamples).
            double energy = 0.0;
            for (int c = 0; c < _channels; c++)
                energy += _ringSum[c] / _blockSamples;

            double loudness = LoudnessOffset + 10.0 * Math.Log10(energy + 1e-12);
            _blockEnergy.Add(energy);
            _blockLoudness.Add(loudness);
        }

        /// <summary>
        /// Biquad de Transposed Direct Form II con coeficientes explícitos.
        /// Se usa para el K-weighting (coeficientes de pyloudnorm / BS.1770-4).
        /// </summary>
        private struct BiquadStage
        {
            private double _b0, _b1, _b2, _a1, _a2;
            private double _s1, _s2;

            public double Process(double x)
            {
                double y = _b0 * x + _s1;
                _s1 = _b1 * x - _a1 * y + _s2;
                _s2 = _b2 * x - _a2 * y;
                return y;
            }

            public BiquadStage CreateCopy() => this;

            /// <summary>Pre-filtro K-weighting (high-shelf).</summary>
            public static BiquadStage CreateKWeightingPreFilter(int sampleRate)
            {
                const double G = 3.999843853973347;
                const double Q = 0.7071752369554196;
                const double Fc = 1681.974450955533;

                double k = Math.Tan(Math.PI * Fc / sampleRate);
                double vh = Math.Pow(10.0, G / 20.0);
                double vb = Math.Pow(vh, 0.4996667741545416);

                double a0 = 1.0 + k / Q + k * k;
                double b0 = (vh + vb * k / Q + k * k) / a0;
                double b1 = 2.0 * (k * k - vh) / a0;
                double b2 = (vh - vb * k / Q + k * k) / a0;
                double a1 = 2.0 * (k * k - 1.0) / a0;
                double a2 = (1.0 - k / Q + k * k) / a0;

                return new BiquadStage { _b0 = b0, _b1 = b1, _b2 = b2, _a1 = a1, _a2 = a2 };
            }

            /// <summary>RLB high-pass del K-weighting.</summary>
            public static BiquadStage CreateKWeightingHighPass(int sampleRate)
            {
                const double Q = 0.5;
                const double Fc = 38.13547087602444;

                double k = Math.Tan(Math.PI * Fc / sampleRate);
                double a0 = 1.0 + k / Q + k * k;
                double b0 = 1.0 / a0;
                double b1 = -2.0 / a0;
                double b2 = 1.0 / a0;
                double a1 = 2.0 * (k * k - 1.0) / a0;
                double a2 = (1.0 - k / Q + k * k) / a0;

                return new BiquadStage { _b0 = b0, _b1 = b1, _b2 = b2, _a1 = a1, _a2 = a2 };
            }
        }
    }
}
