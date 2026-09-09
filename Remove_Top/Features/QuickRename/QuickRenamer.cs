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
        private bool _isGuide;

        public string OriginalPath { get; set; } = "";
        public string OriginalName { get; set; } = "";

        /// <summary>
        /// Nombre tal como se cargó al inicio (sin renombrados aplicados).
        /// Se usa como plantilla de la canción guía en "Reordenar partes" y
        /// nunca se sobrescribe, para que la guía no arrastre posiciones
        /// previas al reabrir la funcionalidad.
        /// </summary>
        public string LoadedName { get; set; } = "";

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

        /// <summary>
        /// True si esta canción es la "guía" del reordenador de partes (la que
        /// define la plantilla de bloques). Solo una canción puede ser guía.
        /// </summary>
        public bool IsGuide
        {
            get => _isGuide;
            set
            {
                if (_isGuide != value)
                {
                    _isGuide = value;
                    OnPropertyChanged();
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

    /// <summary>
    /// Resultado del renombrado de un archivo.
    /// Lleva la referencia al <see cref="QuickRenameItem"/> del que proviene
    /// (Item) y las rutas original/destino para que la página pueda actualizar
    /// la lista en vivo tras aplicar los cambios.
    /// </summary>
    public class QuickRenameResult
    {
        /// <summary>Ítem que originó este resultado (para correlacionar sin arrays paralelos).</summary>
        public QuickRenameItem Item { get; set; } = null!;

        public string OriginalPath { get; set; } = "";
        public string OriginalName { get; set; } = "";
        public string NewName { get; set; } = "";
        public string NewPath { get; set; } = "";
        public bool Success { get; set; }
        public string Message { get; set; } = "";
    }

    /// <summary>
    /// Servicio de edición rápida de nombres.
    /// Lista los archivos .mp3/.wav de la carpeta principal y aplica
    /// los cambios de nombre directamente sobre los archivos originales.
    ///
    /// Responsabilidades:
    ///   - Validar nombres propuestos (<see cref="ValidateName"/>).
    ///   - Detectar conflictos pre-vuelo (<see cref="ValidateBatch"/>): destinos
    ///     duplicados dentro del lote y archivos que ya existen en la carpeta.
    ///   - Aplicar los cambios con File.Move sobre los originales, devolviendo
    ///     un <see cref="QuickRenameResult"/> por ítem con su estado real.
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
        /// Valida un nombre de archivo propuesto.
        /// Devuelve null si es válido, o un mensaje de error en caso contrario.
        /// </summary>
        public static string? ValidateName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "El nombre no puede estar vacío";

            if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                return "El nombre contiene caracteres no válidos";

            if (name.Contains('/') || name.Contains('\\') || name.Contains(':'))
                return "El nombre no puede contener rutas ni subdirectorios";

            return null;
        }

        /// <summary>
        /// Aplica los cambios de nombre a los archivos y devuelve un
        /// <see cref="QuickRenameResult"/> por cada ítem modificado (IsDirty),
        /// con su estado real (éxito/fallo + mensaje).
        ///
        /// Antes de renombrar ejecuta una validación pre-vuelo
        /// (<see cref="ValidateBatch"/>) para evitar colisiones de nombres dentro
        /// del propio lote y con archivos ya existentes. Soporta cancelación.
        /// </summary>
        public async Task<QuickRenameResult[]> ApplyRenamesAsync(
            IEnumerable<QuickRenameItem> items,
            CancellationToken cancellationToken = default)
        {
            var pending = items.Where(i => i.IsDirty).ToArray();
            if (pending.Length == 0) return [];

            // Validación pre-vuelo: marca como fallidos los conflictos sin tocar disco.
            var conflictPaths = ValidateBatch(pending);
            var results = new QuickRenameResult[pending.Length];

            for (int i = 0; i < pending.Length; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var item = pending[i];

                // Los conflictos detectados pre-vuelo se devuelven directamente
                // como fallo, sin intentar el File.Move.
                if (conflictPaths.TryGetValue(item, out var conflictError))
                {
                    results[i] = Fail(item, conflictError);
                    continue;
                }

                results[i] = RenameFile(item);
            }

            // Cede el hilo para mantener la UI receptiva si hubo muchos archivos.
            await Task.CompletedTask;
            return results;
        }

        /// <summary>
        /// Validación pre-vuelo del lote. Devuelve un diccionario que asocia los
        /// ítems en conflicto con el motivo del fallo. NO renombra ni toca disco.
        ///
        /// Detecta:
        ///   - Destinos duplicados dentro del lote (dos ítems → misma ruta final).
        ///   - Destinos que ya existen en la carpeta (y no son el propio original).
        /// </summary>
        private static Dictionary<QuickRenameItem, string> ValidateBatch(QuickRenameItem[] items)
        {
            var conflicts = new Dictionary<QuickRenameItem, string>();
            var taken = new Dictionary<string, QuickRenameItem>(StringComparer.OrdinalIgnoreCase);

            foreach (var item in items)
            {
                var dir = Path.GetDirectoryName(item.OriginalPath)!;

                // Nombres inválidos se detectan en RenameFile; aquí solo colisiones.
                var error = ValidateName(item.CurrentName);
                if (error != null) continue;

                var newPath = Path.Combine(dir, item.CurrentName);

                // Dos ítems distintos que apuntan al mismo destino final.
                if (taken.TryGetValue(newPath, out var other))
                {
                    conflicts[item] = "El nombre nuevo coincide con otro archivo de la lista";
                    conflicts[other] = "El nombre nuevo coincide con otro archivo de la lista";
                    continue;
                }

                // El destino ya existe en disco. Se excluye el caso del rename
                // solo-de-mayúsculas, donde el "otro archivo" es el propio original.
                if (File.Exists(newPath) &&
                    !string.Equals(newPath, item.OriginalPath, StringComparison.OrdinalIgnoreCase))
                {
                    conflicts[item] = "El archivo ya existe en la carpeta";
                    continue;
                }

                taken[newPath] = item;
            }

            return conflicts;
        }

        /// <summary>
        /// Renombra un único archivo.
        /// Valida el nuevo nombre y captura errores típicos de File.Move.
        /// </summary>
        private QuickRenameResult RenameFile(QuickRenameItem item)
        {
            var filePath = item.OriginalPath;
            var newName = item.CurrentName;
            var dir = Path.GetDirectoryName(filePath)!;
            var error = ValidateName(newName);
            if (error != null)
            {
                return Fail(item, error);
            }

            var newPath = Path.Combine(dir, newName);

            // Si el nombre no cambió en absoluto (misma cadena), no hay nada que hacer.
            // Nota: se usa Ordinal (no IgnoreCase) para que el rename SOLO de
            // mayúsculas (song.mp3 → Song.mp3) SÍ ejecute File.Move y Windows
            // aplique el cambio de case correctamente.
            if (string.Equals(newPath, filePath, StringComparison.Ordinal))
            {
                return new QuickRenameResult
                {
                    Item = item,
                    OriginalPath = filePath,
                    OriginalName = Path.GetFileName(filePath),
                    NewName = newName,
                    NewPath = filePath,
                    Success = true,
                    Message = "Sin cambios"
                };
            }

            try
            {
                if (!EnsureWritable(filePath))
                    return Fail(item, "El archivo es de solo lectura y no se pudo desbloquear");
                File.Move(filePath, newPath);
            }
            catch (UnauthorizedAccessException)
            {
                return Fail(item, "Sin permisos para renombrar el archivo");
            }
            catch (IOException)
            {
                // Distingue colisión real de bloqueo: ValidateBatch ya filtró los
                // conflictos conocidos, pero otro proceso pudo crear el destino
                // o bloquear el origen entre la validación y el File.Move.
                if (File.Exists(newPath) &&
                    !string.Equals(newPath, filePath, StringComparison.OrdinalIgnoreCase))
                    return Fail(item, "El nombre ya existe en la carpeta");
                return Fail(item, "El archivo está en uso por otra aplicación");
            }
            catch (Exception ex)
            {
                return Fail(item, $"ERROR: {ex.Message}");
            }

            return new QuickRenameResult
            {
                Item = item,
                OriginalPath = filePath,
                OriginalName = Path.GetFileName(filePath),
                NewName = newName,
                NewPath = newPath,
                Success = true,
                Message = $"Renombrado → {newName}"
            };
        }

        /// <summary>Crea un resultado de fallo reutilizado por validación y excepciones.</summary>
        private static QuickRenameResult Fail(QuickRenameItem item, string message)
        {
            return new QuickRenameResult
            {
                Item = item,
                OriginalPath = item.OriginalPath,
                OriginalName = Path.GetFileName(item.OriginalPath),
                NewName = item.CurrentName,
                NewPath = item.OriginalPath,
                Success = false,
                Message = message
            };
        }

        /// <summary>
        /// Quita el atributo de solo lectura si está presente (igual que
        /// TagService.EnsureWritable). Devuelve false si no se pudo desbloquear.
        /// </summary>
        private static bool EnsureWritable(string path)
        {
            try
            {
                var attrs = File.GetAttributes(path);
                if ((attrs & FileAttributes.ReadOnly) == FileAttributes.ReadOnly)
                    File.SetAttributes(path, attrs & ~FileAttributes.ReadOnly);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}