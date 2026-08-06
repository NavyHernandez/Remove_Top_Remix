using FluentIcons.Common;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Remove_Top.Features.QuickRename
{
    /// <summary>
    /// Fila editable de la lista de renombrado rápido.
    /// CurrentName se enlaza TwoWay al TextBox de la UI y notifica
    /// cambios para actualizar el icono y el contador en vivo.
    /// </summary>
    public class QuickRenameItem : INotifyPropertyChanged
    {
        private string _currentName;

        public string OriginalPath { get; set; } = "";
        public string OriginalName { get; set; } = "";

        public string CurrentName
        {
            get => _currentName;
            set
            {
                if (_currentName != value)
                {
                    _currentName = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsDirty));
                    OnPropertyChanged(nameof(Icon));
                }
            }
        }

        public bool IsDirty => !string.Equals(CurrentName, OriginalName, StringComparison.Ordinal);
        public Icon Icon => IsDirty ? Icon.Edit : Icon.Document;

        public QuickRenameItem()
        {
            _currentName = "";
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>Resultado del renombrado de un archivo.</summary>
    public class QuickRenameResult
    {
        public string OriginalName { get; set; } = "";
        public string NewName { get; set; } = "";
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public Icon StatusIcon => Success ? Icon.CheckmarkCircle : Icon.DismissCircle;
    }

    /// <summary>Progreso del proceso de renombrado para la UI.</summary>
    public class QuickRenameProgress
    {
        public int CurrentIndex { get; set; }
        public int TotalCount { get; set; }
        public string CurrentFile { get; set; } = "";
        public QuickRenameResult? Result { get; set; }
        public double Percentage => TotalCount > 0 ? (double)CurrentIndex / TotalCount * 100.0 : 0;
    }

    /// <summary>
    /// Servicio de edición rápida de nombres.
    /// Lista los archivos .mp3/.wav de la carpeta principal y aplica
    /// los cambios de nombre directamente sobre los archivos originales.
    /// </summary>
    public class QuickRenamer
    {
        private static readonly string[] SupportedExtensions = [".mp3", ".wav"];

        public static bool IsSupportedFile(string path)
        {
            var ext = Path.GetExtension(path)?.ToLowerInvariant();
            return ext != null && SupportedExtensions.Contains(ext);
        }

        /// <summary>
        /// Busca archivos .mp3/.wav en la carpeta principal (sin recursión).
        /// Devuelve un array vacío si la carpeta no existe o hay error de permisos.
        /// </summary>
        public static string[] GetAudioFiles(string folderPath)
        {
            if (!Directory.Exists(folderPath))
                return [];

            try
            {
                return Directory.EnumerateFiles(folderPath, "*.*", SearchOption.TopDirectoryOnly)
                    .Where(IsSupportedFile)
                    .ToArray();
            }
            catch
            {
                return [];
            }
        }

        /// <summary>
        /// Valida un nombre de archivo propuesto.
        /// Devuelve null si es válido, o un mensaje de error en caso contrario.
        /// </summary>
        public static string? ValidateName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "El nombre no puede estar vacío";

            if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                return "El nombre contiene caracteres no válidos";

            if (name.Contains('/') || name.Contains('\\') || name.Contains(':') || name.Contains('.'))
                return "El nombre no puede contener rutas ni subdirectorios";

            return null;
        }

        /// <summary>
        /// Aplica los cambios de nombre a los archivos.
        /// Reporta cada resultado mediante IProgress y soporta cancelación.
        /// Solo se procesan los ítems cuyo nombre cambió (IsDirty).
        /// </summary>
        public async Task ApplyRenamesAsync(
            IEnumerable<QuickRenameItem> items,
            IProgress<QuickRenameProgress> progress,
            CancellationToken cancellationToken = default)
        {
            var pending = items.Where(i => i.IsDirty).ToArray();
            int total = pending.Length;

            for (int i = 0; i < total; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var item = pending[i];
                var result = RenameFile(item.OriginalPath, item.CurrentName);

                progress.Report(new QuickRenameProgress
                {
                    CurrentIndex = i + 1,
                    TotalCount = total,
                    CurrentFile = item.OriginalName,
                    Result = result
                });
            }
        }

        /// <summary>
        /// Renombra un único archivo.
        /// Valida el nuevo nombre y captura errores típicos de File.Move.
        /// </summary>
        private QuickRenameResult RenameFile(string filePath, string newName)
        {
            var dir = Path.GetDirectoryName(filePath)!;
            var error = ValidateName(newName);
            if (error != null)
            {
                return new QuickRenameResult
                {
                    OriginalName = Path.GetFileName(filePath),
                    Success = false,
                    Message = error
                };
            }

            var newPath = Path.Combine(dir, newName);

            if (string.Equals(newPath, filePath, StringComparison.OrdinalIgnoreCase))
            {
                return new QuickRenameResult
                {
                    OriginalName = Path.GetFileName(filePath),
                    NewName = newName,
                    Success = true,
                    Message = "Sin cambios"
                };
            }

            try
            {
                File.Move(filePath, newPath);
            }
            catch (UnauthorizedAccessException)
            {
                return new QuickRenameResult
                {
                    OriginalName = Path.GetFileName(filePath),
                    Success = false,
                    Message = "Sin permisos para renombrar el archivo"
                };
            }
            catch (IOException)
            {
                return new QuickRenameResult
                {
                    OriginalName = Path.GetFileName(filePath),
                    Success = false,
                    Message = "El nombre ya existe o el archivo está en uso"
                };
            }
            catch (Exception ex)
            {
                return new QuickRenameResult
                {
                    OriginalName = Path.GetFileName(filePath),
                    Success = false,
                    Message = $"ERROR: {ex.Message}"
                };
            }

            return new QuickRenameResult
            {
                OriginalName = Path.GetFileName(filePath),
                NewName = newName,
                Success = true,
                Message = $"Renombrado → {newName}"
            };
        }
    }
}
