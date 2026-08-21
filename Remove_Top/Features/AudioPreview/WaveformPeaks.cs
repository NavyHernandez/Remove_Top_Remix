using NAudio.Wave;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Remove_Top.Features.AudioPreview
{
    /// <summary>Peaks (min/max por columna) que representan la forma de onda de un archivo.</summary>
    public class WaveformData
    {
        public float[] MinPeaks { get; init; } = [];
        public float[] MaxPeaks { get; init; } = [];
        public int Columns => MinPeaks.Length;
        public bool IsEmpty => Columns == 0;
    }

    /// <summary>
    /// Extrae la forma de onda de un archivo de audio en segundo plano.
    /// Reutiliza NAudio (MediaFoundationReader + ToSampleProvider) para leer la
    /// señal como floats y calcula, para cada columna de píxel, los valores
    /// mínimos y máximos de la mezcla a mono. Es el mismo patrón de lectura que
    /// usa AudioNormalizer.AnalyzeFile, pero resumido en columnas para dibujar
    /// una onda compacta sin cargar toda la señal en memoria.
    /// </summary>
    public static class WaveformPeaks
    {
        /// <summary>Calcula los peaks en un hilo de fondo.</summary>
        public static Task<WaveformData> ComputeAsync(
            string path,
            int columns,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(() => Compute(path, columns, cancellationToken), cancellationToken);
        }

        private static WaveformData Compute(string path, int columns, CancellationToken ct)
        {
            if (columns <= 0) columns = 1;

            // Se inicializan al valor opuesto para que el primer dato leído
            // actualice siempre el min/max de su columna.
            var mins = new float[columns];
            var maxs = new float[columns];
            Array.Fill(mins, 1f);
            Array.Fill(maxs, -1f);

            using var reader = new MediaFoundationReader(path);
            var sampleProvider = reader.ToSampleProvider();
            int channels = Math.Max(1, reader.WaveFormat.Channels);

            // Muestras totales (frames por canal) y muestras por columna de píxel.
            long totalSamples = (long)(reader.TotalTime.TotalSeconds * reader.WaveFormat.SampleRate);
            long samplesPerColumn = Math.Max(1, totalSamples / columns);

            // Buffer de 1 segundo de muestras (todas las muestras, interleaved).
            var buffer = new float[reader.WaveFormat.SampleRate * channels];
            long sampleIndex = 0;
            int samplesRead;

            while ((samplesRead = sampleProvider.Read(buffer, 0, buffer.Length)) > 0)
            {
                ct.ThrowIfCancellationRequested();

                // Recorre las muestras interleaved mezclando los canales a mono.
                for (int j = 0; j < samplesRead; j += channels)
                {
                    float mono = 0f;
                    for (int ch = 0; ch < channels; ch++)
                    {
                        int idx = j + ch;
                        if (idx < samplesRead) mono += buffer[idx];
                    }
                    mono /= channels;

                    int column = (int)(sampleIndex / samplesPerColumn);
                    if (column >= columns) column = columns - 1;
                    if (mono < mins[column]) mins[column] = mono;
                    if (mono > maxs[column]) maxs[column] = mono;
                    sampleIndex++;
                }
            }

            return new WaveformData { MinPeaks = mins, MaxPeaks = maxs };
        }
    }
}