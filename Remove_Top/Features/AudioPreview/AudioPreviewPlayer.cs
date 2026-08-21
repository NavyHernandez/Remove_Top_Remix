using NAudio.Wave;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Remove_Top.Features.AudioPreview
{
    /// <summary>Estado de reproducción del previsualizador.</summary>
    public enum AudioPreviewState
    {
        /// <summary>Sin archivo cargado.</summary>
        Idle,

        /// <summary>Archivo cargado y listo (en el inicio o tras Stop).</summary>
        Loaded,

        /// <summary>Reproduciendo.</summary>
        Playing,

        /// <summary>Pausado (conserva la posición).</summary>
        Paused
    }

    /// <summary>
    /// Motor de reproducción de audio para el previsualizador, construido sobre
    /// NAudio (la misma librería de audio ya usada en el proyecto):
    ///
    ///   - <see cref="MediaFoundationReader"/>: lee los formatos ya soportados
    ///     por la app (mp3/wav/flac/m4a/aac/ogg/wma/aiff/wv...) y la familia
    ///     MPEG (mp1/mp2/mpa/mpeg/mpg/m1a/m2a), cuyos decodificadores los aporta
    ///     Windows Media Foundation.
    ///   - <see cref="MediaFoundationResampler"/>: convierte cualquier formato
    ///     de entrada a 44,1 kHz / 16 bits / estéreo PCM, el formato que
    ///     WaveOutEvent acepta siempre, para que todas las muestras suenen sin
    ///     necesidad de configurar la salida.
    ///   - <see cref="WaveOutEvent"/>: salida de audio del sistema.
    ///
    /// Soporta Play / Pause / Stop / Seek (por tiempo o fracción), expone la
    /// duración y la posición actual, y avisa cuando la reproducción llega al
    /// final (PlaybackEnded). El cierre y liberación del archivo (Close) es
    /// importante en Duplicados: el usuario puede borrar el archivo que estaba
    /// sonando y, si el lector sigue abierto, el borrado falla por el bloqueo.
    /// </summary>
    public class AudioPreviewPlayer : IDisposable
    {
        private MediaFoundationReader? _reader;
        private MediaFoundationResampler? _resampler;
        private WaveOutEvent? _output;
        private bool _disposed;

        /// <summary>Extensiones de audio que el previsualizador puede leer (MediaFoundation).</summary>
        private static readonly string[] AudioExtensions =
            [".mp3", ".wav", ".flac", ".m4a", ".aac", ".ogg", ".wma", ".aiff", ".aif", ".wv",
             ".mp1", ".mp2", ".mpa", ".mpeg", ".mpg", ".m1a", ".m2a"];

        /// <summary>Ruta del archivo cargado actualmente (vacío si no hay ninguno).</summary>
        public string CurrentFilePath { get; private set; } = "";

        /// <summary>True si hay un archivo cargado y listo para reproducir.</summary>
        public bool IsLoaded => _reader != null;

        /// <summary>Estado actual de la reproducción.</summary>
        public AudioPreviewState State { get; private set; } = AudioPreviewState.Idle;

        /// <summary>Duración total del archivo cargado.</summary>
        public TimeSpan Duration => _reader?.TotalTime ?? TimeSpan.Zero;

        /// <summary>Posición actual de reproducción.</summary>
        public TimeSpan Position
        {
            get
            {
                if (_reader == null) return TimeSpan.Zero;
                try { return _reader.CurrentTime; }
                catch { return TimeSpan.Zero; }
            }
        }

        /// <summary>Posición actual como fracción 0..1 (para el playhead de la onda).</summary>
        public double PositionFraction =>
            Duration.TotalSeconds > 0 ? Math.Clamp(Position.TotalSeconds / Duration.TotalSeconds, 0, 1) : 0;

        /// <summary>Se dispara cuando la reproducción llega al final de forma natural.</summary>
        public event Action? PlaybackEnded;

        /// <summary>Indica si un archivo es audio soportado por el previsualizador.</summary>
        public static bool IsSupportedAudio(string path)
        {
            var ext = Path.GetExtension(path)?.ToLowerInvariant();
            return ext != null && Array.IndexOf(AudioExtensions, ext) >= 0;
        }

        /// <summary>
        /// Carga un archivo de audio en segundo plano. Cierra cualquier archivo
        /// anterior (liberando su bloqueo) y deja el reproductor listo para
        /// reproducir (State = Loaded). Devuelve false si no se pudo leer.
        /// </summary>
        public Task<bool> LoadAsync(string path)
        {
            return Task.Run(() =>
            {
                try
                {
                    // Cierra el archivo anterior y libera su bloqueo.
                    Close();

                    _reader = new MediaFoundationReader(path);

                    // Formato de salida estándar: 44,1 kHz / 16 bits / estéreo PCM.
                    // Garantiza que WaveOutEvent reproduzca cualquier formato de entrada.
                    var outputFormat = new WaveFormat(44100, 16, 2);
                    _resampler = new MediaFoundationResampler(_reader, outputFormat)
                    {
                        ResamplerQuality = 60
                    };

                    _output = new WaveOutEvent();
                    _output.PlaybackStopped += Output_PlaybackStopped;
                    _output.Init(_resampler);

                    CurrentFilePath = path;
                    State = AudioPreviewState.Loaded;
                    return true;
                }
                catch
                {
                    // No se pudo abrir: libera todo y deja el estado limpio.
                    Close();
                    return false;
                }
            });
        }

        /// <summary>Reproduce desde la posición actual (o desde el inicio si ya se acabó).</summary>
        public void Play()
        {
            if (_output == null || State == AudioPreviewState.Idle) return;
            if (State == AudioPreviewState.Loaded && Position >= Duration) Seek(TimeSpan.Zero);
            _output.Play();
            State = AudioPreviewState.Playing;
        }

        /// <summary>Pausa conservando la posición.</summary>
        public void Pause()
        {
            if (_output == null) return;
            _output.Pause();
            State = AudioPreviewState.Paused;
        }

        /// <summary>Alterna entre reproducir y pausar.</summary>
        public void TogglePlay()
        {
            if (!IsLoaded) return;
            if (State == AudioPreviewState.Playing) Pause();
            else Play();
        }

        /// <summary>
        /// Detiene la reproducción y vuelve al inicio. Mantiene el archivo
        /// cargado para poder reproducirlo de nuevo sin reabrirlo.
        /// </summary>
        public void Stop()
        {
            StopInternal(dispose: false);
        }

        /// <summary>
        /// Cierra el archivo y libera su bloqueo para que pueda ser borrado.
        /// El reproductor queda reutilizable (State = Idle).
        /// </summary>
        public void Close()
        {
            StopInternal(dispose: true);
        }

        /// <summary>Mueve la reproducción a la posición indicada (recortada a los límites).</summary>
        public void Seek(TimeSpan position)
        {
            if (_reader == null) return;
            var target = position;
            if (target < TimeSpan.Zero) target = TimeSpan.Zero;
            if (target > Duration) target = Duration;
            try { _reader.CurrentTime = target; } catch { }
        }

        /// <summary>Mueve la reproducción a una fracción 0..1 de la duración.</summary>
        public void SeekToFraction(double fraction)
        {
            Seek(TimeSpan.FromSeconds(Duration.TotalSeconds * Math.Clamp(fraction, 0, 1)));
        }

        /// <summary>
        /// Núcleo del cierre: detiene la salida y, si se indica (dispose), libera
        /// el lector y el resampler. En el modo sin dispose (Stop) conserva el
        /// archivo y vuelve la posición al inicio.
        /// </summary>
        private void StopInternal(bool dispose)
        {
            if (_output != null)
            {
                // Se desuscribe antes de detener para no recibir el evento
                // PlaybackStopped de un Stop manual.
                _output.PlaybackStopped -= Output_PlaybackStopped;
                try { _output.Stop(); } catch { }
                if (dispose)
                {
                    try { _output.Dispose(); } catch { }
                }
                _output = null;
            }

            if (dispose)
            {
                DisposeChain();
                State = AudioPreviewState.Idle;
                CurrentFilePath = "";
            }
            else if (_reader != null)
            {
                try { _reader.Position = 0; } catch { }
                State = AudioPreviewState.Loaded;
            }
        }

        /// <summary>Libera lector y resampler (sin tocar la salida).</summary>
        private void DisposeChain()
        {
            try { _resampler?.Dispose(); } catch { }
            _resampler = null;
            try { _reader?.Dispose(); } catch { }
            _reader = null;
        }

        /// <summary>
        /// Al llegar al final de forma natural, WaveOutEvent dispara
        /// PlaybackStopped: se vuelve al estado "listo" y se avisa a la UI.
        /// </summary>
        private void Output_PlaybackStopped(object? sender, StoppedEventArgs e)
        {
            if (State == AudioPreviewState.Playing)
            {
                State = AudioPreviewState.Loaded;
                PlaybackEnded?.Invoke();
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Close();
            GC.SuppressFinalize(this);
        }
    }
}