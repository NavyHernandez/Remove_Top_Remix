using FluentIcons.Common;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Remove_Top.Features.BatchRename
{
    public class RenameResult
    {
        public string OriginalName { get; set; } = "";
        public string NewName { get; set; } = "";
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public Icon StatusIcon => Success ? Icon.CheckmarkCircle : Icon.DismissCircle;
    }

    public class RenameProgress
    {
        public int CurrentIndex { get; set; }
        public int TotalCount { get; set; }
        public string CurrentFile { get; set; } = "";
        public RenameResult? Result { get; set; }
        public double Percentage => TotalCount > 0 ? (double)CurrentIndex / TotalCount * 100.0 : 0;
    }

    /// <summary>Representa un patrón para eliminar, usado en la UI con chips.</summary>
    public class RenamePattern
    {
        public string Text { get; set; } = "";
        public override string ToString() => Text;
    }

    /// <summary>
    /// Servicio de renombrado masivo.
    /// Aplica múltiples patrones de búsqueda (case-insensitive) a los nombres
    /// de archivos dentro de una carpeta. Soporta extensiones de audio, video,
    /// imagen y documentos. Los archivos no soportados se saltan con mensaje.
    /// </summary>
    public class FileRenamer
    {
        private static readonly string[] AudioExtensions =
        [
            ".mp3", ".wav", ".flac", ".aac", ".m4a",
            ".ogg", ".wma", ".aiff", ".aif", ".wv"
        ];

        private static readonly string[] ImageExtensions =
        [
            ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".tiff", ".tif", ".webp", ".svg"
        ];

        private static readonly string[] VideoExtensions =
        [
            ".mp4", ".avi", ".mkv", ".mov", ".wmv", ".flv", ".webm", ".m4v", ".ts"
        ];

        private static readonly string[] DocumentExtensions =
        [
            ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".txt", ".csv"
        ];

        private static readonly string[] AllExtensions =
            AudioExtensions.Concat(ImageExtensions).Concat(VideoExtensions).Concat(DocumentExtensions).ToArray();

        public static bool IsSupportedFile(string path)
        {
            var ext = Path.GetExtension(path)?.ToLowerInvariant();
            return ext != null && AllExtensions.Contains(ext);
        }

        /// <summary>
        /// Busca archivos que contengan ALGUNO de los patrones en su nombre.
        /// </summary>
        public static string[] GetAffectedFiles(string folderPath, string[] patterns)
        {
            if (!Directory.Exists(folderPath) || patterns.Length == 0)
                return [];

            try
            {
                return Directory.EnumerateFiles(folderPath, "*.*", SearchOption.AllDirectories)
                    .Where(IsSupportedFile)
                    .Where(f =>
                    {
                        var name = Path.GetFileNameWithoutExtension(f);
                        return patterns.Any(p =>
                            name.Contains(p, StringComparison.OrdinalIgnoreCase));
                    })
                    .ToArray();
            }
            catch
            {
                return [];
            }
        }

        /// <summary>
        /// Procesa los archivos aplicando todos los patrones a cada nombre.
        /// Reporta progreso mediante IProgress y soporta cancelación.
        /// Los archivos no soportados se omiten con mensaje informativo.
        /// </summary>
        public async Task ProcessFolderAsync(
            string folderPath,
            string[] patterns,
            IProgress<RenameProgress> progress,
            CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(folderPath))
                throw new DirectoryNotFoundException($"No se encontró la carpeta: {folderPath}");

            if (patterns.Length == 0)
                throw new ArgumentException("Debe agregar al menos un patrón.");

            var files = GetAffectedFiles(folderPath, patterns);
            int total = files.Length;

            for (int i = 0; i < total; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var file = files[i];
                RenameResult result;

                try
                {
                    result = await Task.Run(() => RenameFile(file, patterns), cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    result = new RenameResult
                    {
                        OriginalName = Path.GetFileName(file),
                        NewName = "",
                        Success = false,
                        Message = $"ERROR: {ex.Message}"
                    };
                }

                progress.Report(new RenameProgress
                {
                    CurrentIndex = i + 1,
                    TotalCount = total,
                    CurrentFile = Path.GetFileName(file),
                    Result = result
                });
            }
        }

        /// <summary>
        /// Renombra un archivo eliminando TODOS los patrones de su nombre (sin extensión).
        /// Si el nuevo nombre ya existe, agrega un contador "(1)", "(2)", etc.
        /// Si no hay cambios en el nombre, retorna Success=false sin mover el archivo.
        /// </summary>
        private RenameResult RenameFile(string filePath, string[] patterns)
        {
            var dir = Path.GetDirectoryName(filePath)!;
            var nameWithoutExt = Path.GetFileNameWithoutExtension(filePath);
            var ext = Path.GetExtension(filePath);

            var newNameWithoutExt = nameWithoutExt;
            foreach (var pattern in patterns)
            {
                newNameWithoutExt = newNameWithoutExt.Replace(pattern, "", StringComparison.OrdinalIgnoreCase);
            }
            newNameWithoutExt = newNameWithoutExt.Trim();
            newNameWithoutExt = System.Text.RegularExpressions.Regex.Replace(newNameWithoutExt, @"\s+", " ");

            if (string.IsNullOrWhiteSpace(newNameWithoutExt))
            {
                return new RenameResult
                {
                    OriginalName = Path.GetFileName(filePath),
                    Success = false,
                    Message = "El nombre resultante quedaría vacío"
                };
            }

            if (newNameWithoutExt.Equals(nameWithoutExt, StringComparison.Ordinal))
            {
                return new RenameResult
                {
                    OriginalName = Path.GetFileName(filePath),
                    Success = false,
                    Message = "Ningún patrón coincide en el nombre"
                };
            }

            var newPath = Path.Combine(dir, newNameWithoutExt + ext);
            if (File.Exists(newPath))
            {
                var baseName = newNameWithoutExt;
                int counter = 1;
                while (File.Exists(Path.Combine(dir, baseName + ext)))
                {
                    baseName = $"{newNameWithoutExt} ({counter})";
                    counter++;
                }
                newPath = Path.Combine(dir, baseName + ext);
            }

            try
            {
                File.Move(filePath, newPath);
            }
            catch (UnauthorizedAccessException)
            {
                return new RenameResult
                {
                    OriginalName = Path.GetFileName(filePath),
                    Success = false,
                    Message = "Sin permisos para renombrar el archivo"
                };
            }
            catch (IOException)
            {
                return new RenameResult
                {
                    OriginalName = Path.GetFileName(filePath),
                    Success = false,
                    Message = "El archivo está en uso por otro programa"
                };
            }

            return new RenameResult
            {
                OriginalName = Path.GetFileName(filePath),
                NewName = Path.GetFileName(newPath),
                Success = true,
                Message = $"Renombrado → {Path.GetFileName(newPath)}"
            };
        }
    }
}
