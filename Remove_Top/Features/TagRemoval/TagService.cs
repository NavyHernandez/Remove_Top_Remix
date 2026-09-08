using Remove_Top.Helpers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TagLib;

namespace Remove_Top.Features.TagRemoval
{
    /// <summary>
    /// Resultado de la recolección de archivos (con el límite de escaneo aplicado).
    /// </summary>
    public class FileScanResult
    {
        /// <summary>Archivos que se van a procesar (hasta <see cref="AppLimits.TagsMaxFilesToScan"/>).</summary>
        public List<string> Files { get; set; } = [];

        /// <summary>Total de archivos de audio encontrados (antes de aplicar el límite).</summary>
        public int TotalFound { get; set; }

        /// <summary>Indica si el escaneo se truncó (la carpeta tenía más archivos del límite).</summary>
        public bool Truncated { get; set; }
    }

    /// <summary>
    /// Lógica de negocio de Eliminar/Reemplazar Etiquetas con TagLib#:
    /// recolección recursiva de archivos, lectura de tags (análisis),
    /// borrado total de tags (incluida la portada) y reescritura de tags con
    /// portada opcional. Se comunica con la UI mediante <c>IProgress&lt;T&gt;</c>
    /// y soporta cancelación.
    /// </summary>
    public class TagService
    {
        private const int MaxFiles = AppLimits.TagsMaxFilesToScan;

        /// <summary>
        /// Extensiones de audio soportadas por TagLib# (se excluye .aac suelto,
        /// que la librería no puede leer/escribir).
        /// </summary>
        private static readonly string[] AudioExtensions =
            [".mp3", ".flac", ".m4a", ".mp4", ".wma", ".asf", ".ogg", ".oga",
             ".opus", ".aiff", ".aif", ".wav", ".ape", ".wv", ".mka", ".tta", ".dsf"];

        /// <summary>
        /// Nombres base de las portadas externas que se eliminan junto con las
        /// etiquetas en el modo Eliminar (p. ej. folder.jpg, cover.png...).
        /// </summary>
        private static readonly string[] ExternalCoverNames =
            ["folder", "cover", "front", "back", "album", "artwork"];

        /// <summary>Extensiones de las portadas externas consideradas.</summary>
        private static readonly string[] ExternalCoverExtensions =
            [".jpg", ".jpeg", ".png", ".webp", ".bmp", ".gif"];

        /// <summary>Verifica si la extensión del archivo corresponde a un formato que TagLib soporta.</summary>
        public static bool IsSupportedFile(string path)
        {
            var ext = Path.GetExtension(path)?.ToLowerInvariant();
            return ext != null && AudioExtensions.Contains(ext);
        }

        /// <summary>
        /// Enumera los archivos de audio a procesar: si una ruta es una carpeta
        /// se escanea de forma recursiva (incluye subcarpetas); si es un archivo
        /// se usa directamente. Aplica el límite de <see cref="AppLimits.TagsMaxFilesToScan"/>
        /// e indica si el escaneo se truncó.
        /// </summary>
        public static FileScanResult CollectFiles(IEnumerable<string> paths)
        {
            var files = new List<string>();
            int total = 0;

            foreach (var path in paths)
            {
                if (Directory.Exists(path))
                {
                    try
                    {
                        foreach (var file in Directory.EnumerateFiles(
                            path, "*.*", SearchOption.AllDirectories).Where(IsSupportedFile))
                        {
                            total++;
                            if (files.Count < MaxFiles) files.Add(file);
                        }
                    }
                    catch
                    {
                        // Carpeta sin permisos de lectura: se omite y se sigue con el resto.
                    }
                }
                else if (System.IO.File.Exists(path) && IsSupportedFile(path))
                {
                    total++;
                    if (files.Count < MaxFiles) files.Add(path);
                }
            }

            return new FileScanResult
            {
                Files = files,
                TotalFound = total,
                Truncated = total > MaxFiles
            };
        }

        /// <summary>
        /// Lee las tags actuales de cada archivo (análisis) en segundo plano y
        /// reporta progreso por archivo. Devuelve los ítems para el listado.
        /// </summary>
        public async Task<List<TagFileItem>> AnalyzeAsync(
            IReadOnlyList<string> files,
            IProgress<TagProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var items = new List<TagFileItem>(files.Count);
            for (int i = 0; i < files.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var file = files[i];
                var item = await Task.Run(() => ReadItem(file), cancellationToken);
                items.Add(item);
                progress?.Report(new TagProgress
                {
                    CurrentIndex = i + 1,
                    TotalCount = files.Count,
                    CurrentFile = item.FileName
                });
            }
            return items;
        }

        /// <summary>
        /// Lee el título, artista, álbum y presencia de portada de un archivo.
        /// Nunca lanza: los archivos ilegibles se reportan sin tags.
        /// </summary>
        private static TagFileItem ReadItem(string path)
        {
            var item = new TagFileItem { FilePath = path, FileName = Path.GetFileName(path) };
            try
            {
                using var file = TagLib.File.Create(path);
                item.Title = file.Tag.Title ?? "";
                item.Artist = string.Join(", ", file.Tag.Performers ?? []);
                item.Album = file.Tag.Album ?? "";
                item.HasCover = file.Tag.Pictures is { Length: > 0 };
            }
            catch
            {
                // Archivo ilegible: se muestra sin tags.
            }
            return item;
        }

        /// <summary>
        /// Procesa el lote de archivos según el modo elegido. Los errores por
        /// archivo no detienen el lote: se reportan como <see cref="TagResult"/>
        /// fallido y se continúa con el siguiente.
        /// </summary>
        public async Task ProcessAsync(
            TagMode mode,
            IReadOnlyList<string> files,
            TagValues values,
            IProgress<TagProgress> progress,
            CancellationToken cancellationToken = default)
        {
            for (int i = 0; i < files.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var file = files[i];
                var name = Path.GetFileName(file);
                try
                {
                    var result = await Task.Run(() => ProcessFile(mode, file, values), cancellationToken);
                    result.FileName = name;
                    progress.Report(new TagProgress
                    {
                        CurrentIndex = i + 1,
                        TotalCount = files.Count,
                        CurrentFile = name,
                        Result = result
                    });
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    progress.Report(new TagProgress
                    {
                        CurrentIndex = i + 1,
                        TotalCount = files.Count,
                        CurrentFile = name,
                        Result = new TagResult
                        {
                            FileName = name,
                            Success = false,
                            Message = $"ERROR: {ex.Message}"
                        }
                    });
                }
            }
        }

        /// <summary>
        /// Ejecuta la operación sobre un único archivo.
        ///
        /// <see cref="TagMode.Clear"/>: elimina TODAS las tags (incluida la
        /// portada incrustada) mediante <c>RemoveTags(AllTags)</c> y borra
        /// también las portadas externas de la carpeta (folder.jpg, cover.jpg...).
        ///
        /// <see cref="TagMode.Replace"/>: primero vacía las tags (encoge el
        /// archivo) y después escribe los valores nuevos. Si no se indicó una
        /// portada nueva, conserva la existente; si se indicó, la reemplaza.
        ///
        /// Antes de escribir se garantiza que el archivo sea grabable: si tenía
        /// el atributo de Solo lectura, se le quita (el archivo queda grabable).
        /// </summary>
        private static TagResult ProcessFile(TagMode mode, string path, TagValues values)
        {
            if (!EnsureWritable(path))
            {
                return new TagResult
                {
                    Success = false,
                    Message = "Archivo de solo lectura: no se pudieron escribir las etiquetas."
                };
            }

            if (mode == TagMode.Clear)
            {
                using (var file = TagLib.File.Create(path))
                {
                    file.RemoveTags(TagTypes.AllTags);
                    file.Save();
                }
                return new TagResult
                {
                    Success = true,
                    Message = "Etiquetas eliminadas" + DeleteExternalCovers(path)
                };
            }

            // Reemplazo: conservar la portada actual si no hay una nueva.
            IPicture[]? existingPictures = null;
            if (values.CoverPath == null)
            {
                using (var read = TagLib.File.Create(path))
                    existingPictures = read.Tag.Pictures;
            }

            // Vaciar tags (liberar y encoger el archivo) antes de reescribir.
            using (var file = TagLib.File.Create(path))
            {
                file.RemoveTags(TagTypes.AllTags);
                file.Save();
            }

            using (var file = TagLib.File.Create(path))
            {
                file.Tag.Title = values.Title;
                file.Tag.Performers = Split(values.Artist);
                file.Tag.AlbumArtists = Split(values.AlbumArtist);
                file.Tag.Album = values.Album;
                file.Tag.Genres = Split(values.Genre);
                file.Tag.Year = (uint)(ushort.TryParse(values.Year, out var year) ? year : 0);
                file.Tag.Comment = values.Comment;

                if (values.CoverPath != null)
                {
                    var cover = new Picture(values.CoverPath) { Type = PictureType.FrontCover };
                    file.Tag.Pictures = [cover];
                }
                else if (existingPictures is { Length: > 0 })
                {
                    file.Tag.Pictures = existingPictures;
                }

                file.Save();
            }

            return new TagResult
            {
                Success = true,
                Message = values.CoverPath != null
                    ? "Etiquetas reemplazadas y portada actualizada"
                    : "Etiquetas reemplazadas"
            };
        }

        /// <summary>Convierte un texto en un arreglo de un elemento (vacío si el texto es blanco).</summary>
        private static string[] Split(string value) =>
            string.IsNullOrWhiteSpace(value) ? [] : [value.Trim()];

        /// <summary>
        /// Garantiza que el archivo sea grabable quitando el atributo de Solo
        /// lectura si lo tiene (el archivo queda grabable, sin restaurar).
        /// Devuelve false si no se pudo comprobar/quitar el atributo.
        /// </summary>
        private static bool EnsureWritable(string path)
        {
            try
            {
                var attrs = System.IO.File.GetAttributes(path);
                if ((attrs & FileAttributes.ReadOnly) == FileAttributes.ReadOnly)
                {
                    System.IO.File.SetAttributes(path, attrs & ~FileAttributes.ReadOnly);
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Elimina las portadas externas (folder.jpg, cover.jpg, front.jpg,
        /// back.jpg, album.*, artwork.*) que haya en la misma carpeta del
        /// archivo. Es idempotente: si un archivo ya no existe, se ignora.
        /// Devuelve una nota con los archivos eliminados (o cadena vacía).
        /// </summary>
        private static string DeleteExternalCovers(string path)
        {
            var dir = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(dir)) return "";

            var deleted = new List<string>();
            foreach (var name in ExternalCoverNames)
            {
                foreach (var ext in ExternalCoverExtensions)
                {
                    var candidate = Path.Combine(dir, name + ext);
                    try
                    {
                        if (System.IO.File.Exists(candidate))
                        {
                            System.IO.File.Delete(candidate);
                            deleted.Add(name + ext);
                        }
                    }
                    catch
                    {
                        // Portada bloqueada o sin permisos: se omite sin romper el lote.
                    }
                }
            }

            return deleted.Count > 0
                ? $" \u00b7 portada externa eliminada ({string.Join(", ", deleted)})"
                : "";
        }
    }
}