using FluentIcons.Common;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Remove_Top.Features.BatchRename
{
    /// <summary>
    /// Contrato de un proveedor que sugiere NUEVOS patrones a eliminar.
    /// Desacoplado de la UI para poder cambiar de proveedor
    /// (Groq, otra API, o un mock local de pruebas).
    /// </summary>
    public interface IPatternSuggestionProvider
    {
        /// <summary>
        /// Dado los patrones actuales y los nombres de los archivos afectados,
        /// devuelve sugerencias de patrones adicionales a agregar.
        /// No modifica nada: solo sugiere.
        /// </summary>
        Task<IReadOnlyList<PatternSuggestion>> SuggestPatternsAsync(
            IReadOnlyList<string> patterns,
            IReadOnlyList<string> fileNames,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Sugerencia de nuevo patrón a eliminar.
    /// Se muestra en la lista con un CheckBox para que el usuario apruebe
    /// su inclusión en la lista de patrones.
    /// </summary>
    public class PatternSuggestion : INotifyPropertyChanged
    {
        private bool _isApproved;

        /// <summary>Texto del patrón sugerido.</summary>
        public string Text { get; set; } = "";

        /// <summary>Nº de archivos que coincidirían si se agregara el patrón.</summary>
        public int Matches { get; set; }

        /// <summary>Texto informativo con el nº de coincidencias.</summary>
        public string MatchesDisplay => $"{Matches} archivo(s)";

        /// <summary>Indica si el usuario aprobó el patrón con el check.</summary>
        public bool IsApproved
        {
            get => _isApproved;
            set
            {
                if (_isApproved != value)
                {
                    _isApproved = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>Icono visual: sugerencia (chispa).</summary>
        public Icon StatusIcon => Icon.Sparkle;

        public override string ToString() => Text;

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
