using FluentIcons.Common;
using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;

namespace Remove_Top.Features.DuplicateRemoval
{
    /// <summary>
    /// Representa un archivo repetido candidato a eliminación.
    /// Se muestra en la UI con un CheckBox de confirmación, un badge "×N"
    /// con el total de copias del grupo y una etiqueta de tipo de coincidencia.
    /// </summary>
    public class DuplicateItem : INotifyPropertyChanged
    {
        private bool _isMarkedForDeletion;

        /// <summary>Ruta completa del archivo.</summary>
        public string FilePath { get; set; } = "";

        /// <summary>Tamaño del archivo en bytes.</summary>
        public long Size { get; set; }

        /// <summary>Total de copias que existen del grupo (incluida la conservada).</summary>
        public int RepeatCount { get; set; }

        /// <summary>Tipo de coincidencia (exacto por hash, posible por nombre o por palabra).</summary>
        public DuplicateMatchKind MatchKind { get; set; }

        /// <summary>
        /// Indica si el archivo comparte su tamaño exacto con otro miembro del
        /// grupo. Los "posibles por nombre" con tamaño idéntico a otro miembro
        /// son casi seguros duplicados y se marcan por defecto; los de tamaño
        /// distinto quedan desmarcados salvo que la duración los confirme.
        /// Los "posibles por palabra" siempre quedan desmarcados.
        /// </summary>
        public bool SameSize { get; set; }

        /// <summary>
        /// Indica que la coincidencia por nombre es "casi idéntica": los nombres
        /// normalizados difieren en una sola letra (falta ortográfica). Se muestra
        /// en el detalle del ítem para explicar por qué quedó marcado.
        /// </summary>
        public bool NameNearMatch { get; set; }

        /// <summary>Duración del archivo en segundos, si es audio y se pudo leer; null en otro caso.</summary>
        public double? DurationSeconds { get; set; }

        /// <summary>Duración de referencia del grupo (para comparar la coincidencia).</summary>
        public double? ReferenceDurationSeconds { get; set; }

        /// <summary>
        /// Indica si la duración del archivo coincide (dentro de la tolerancia)
        /// con la de referencia del grupo: señal de que es la misma canción
        /// aunque tenga otro tamaño/codificación.
        /// </summary>
        public bool DurationMatches { get; set; }

        /// <summary>Nombre del archivo (sin ruta).</summary>
        public string FileName => Path.GetFileName(FilePath);

        /// <summary>Carpeta donde se encuentra el archivo.</summary>
        public string FolderPath => Path.GetDirectoryName(FilePath) ?? "";

        /// <summary>Tamaño formateado para mostrar (KB/MB/GB).</summary>
        public string SizeDisplay => FormatSize(Size);

        /// <summary>Duración formateada (m:ss o h:mm:ss), o vacío si no está disponible.</summary>
        public string DurationDisplay => DurationSeconds is double d ? FormatDuration(d) : "";

        /// <summary>Badge "×N" con el total de copias del grupo.</summary>
        public string RepeatDisplay => $"×{RepeatCount}";

        /// <summary>Etiqueta de tipo de coincidencia.</summary>
        public string MatchDisplay => MatchKind switch
        {
            DuplicateMatchKind.Exact => "Exacto",
            DuplicateMatchKind.SameName => "Exacto",
            DuplicateMatchKind.ProbableByName or DuplicateMatchKind.ProbableByKeyword => "Posible",
            _ => "Dañado"
        };

        /// <summary>
        /// Detalle adicional del tipo de coincidencia: nombre (y duración cuando
        /// está disponible) para los "misma canción por nombre", tamaño para los
        /// "posibles por nombre" y palabra clave + duración para los "posibles
        /// por palabra".
        /// </summary>
        public string MatchDetailDisplay => MatchKind switch
        {
            DuplicateMatchKind.SameName => BuildSameNameDetail(),
            DuplicateMatchKind.ProbableByName => BuildNameDetail(),
            DuplicateMatchKind.ProbableByKeyword => BuildKeywordDetail(),
            _ => ""
        };

        private string BuildSameNameDetail()
        {
            if (NameNearMatch) return "mismo nombre · 1 letra distinta";
            if (SameSize) return "mismo nombre · mismo tamaño";
            if (DurationMatches) return $"mismo nombre · dura similar ({DurationDisplay})";
            if (DurationSeconds is double d)
                return $"mismo nombre · duración muy distinta ({FormatDuration(d)})";
            return "mismo nombre";
        }

        private string BuildNameDetail()
        {
            if (SameSize) return "mismo tamaño";
            if (DurationMatches) return $"tamaño distinto · dura similar ({DurationDisplay})";
            return "tamaño distinto";
        }

        private string BuildKeywordDetail()
        {
            if (DurationSeconds is double d) return $"palabras clave · {FormatDuration(d)}";
            return "palabras clave";
        }

        /// <summary>Icono visual del tipo de coincidencia.</summary>
        public Icon MatchIcon => MatchKind switch
        {
            DuplicateMatchKind.Exact or DuplicateMatchKind.SameName => Icon.CheckmarkCircle,
            DuplicateMatchKind.ProbableByName or DuplicateMatchKind.ProbableByKeyword => Icon.Warning,
            _ => Icon.ErrorCircle
        };

        /// <summary>Indica si el usuario confirmó la eliminación con el check.</summary>
        public bool IsMarkedForDeletion
        {
            get => _isMarkedForDeletion;
            set
            {
                if (_isMarkedForDeletion != value)
                {
                    _isMarkedForDeletion = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>Formatea un tamaño en bytes a una cadena legible.</summary>
        public static string FormatSize(long bytes)
        {
            if (bytes >= 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
            if (bytes >= 1024L * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
            if (bytes >= 1024L) return $"{bytes / 1024.0:F0} KB";
            return $"{bytes} B";
        }

        /// <summary>Formatea una duración en segundos a m:ss (u h:mm:ss).</summary>
        public static string FormatDuration(double seconds)
        {
            if (seconds < 0) return "";
            var ts = TimeSpan.FromSeconds(seconds);
            return ts.TotalHours >= 1
                ? $"{(int)ts.TotalHours}:{ts.Minutes:D2}:{ts.Seconds:D2}"
                : $"{(int)ts.TotalMinutes}:{ts.Seconds:D2}";
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
