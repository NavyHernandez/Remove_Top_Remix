using FluentIcons.Common;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Remove_Top.Features.QuickRename
{
    /// <summary>
    /// Contrato de un proveedor de corrección de nombres por IA.
    /// Desacoplado de la UI para poder intercambiar el proveedor
    /// (Groq, otra API, o un mock local de pruebas).
    /// </summary>
    public interface INameCorrectionProvider
    {
        /// <summary>
        /// Corrige una lista de nombres de archivo y devuelve las sugerencias
        /// en el mismo orden. No renombra nada: solo sugiere.
        /// </summary>
        Task<IReadOnlyList<CorrectionSuggestion>> CorrectNamesAsync(
            IReadOnlyList<string> fileNames,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Sugerencia de corrección para un archivo.
    /// Se muestra en la lista con un CheckBox para que el usuario apruebe
    /// el cambio (individualmente o todos a la vez).
    /// </summary>
    public class CorrectionSuggestion : INotifyPropertyChanged
    {
        private bool _isApproved;

        /// <summary>Nombre original (incluye extensión).</summary>
        public string OriginalFull { get; set; } = "";

        /// <summary>Nombre corregido propuesto (incluye extensión).</summary>
        public string SuggestedFull { get; set; } = "";

        /// <summary>Indica si el usuario aprobó el cambio con el check.</summary>
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

        /// <summary>Indica si el nombre propuesto difiere del original.</summary>
        public bool Changed => !string.Equals(OriginalFull, SuggestedFull, System.StringComparison.Ordinal);

        /// <summary>Icono visual: edición sugerida (lápiz) o sin cambios (check).</summary>
        public Icon StatusIcon => Changed ? Icon.Edit : Icon.Checkmark;

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
