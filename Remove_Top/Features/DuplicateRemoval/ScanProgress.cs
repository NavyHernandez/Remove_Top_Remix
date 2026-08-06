namespace Remove_Top.Features.DuplicateRemoval
{
    /// <summary>
    /// Progreso del escaneo: fase actual y avance por archivo analizado.
    /// Si Total es 0 la barra debe mostrarse indeterminada.
    /// </summary>
    public class ScanProgress
    {
        public string Phase { get; set; } = "";
        public int Current { get; set; }
        public int Total { get; set; }
        public bool IsIndeterminate => Total <= 0;
        public double Percentage => Total > 0 ? (double)Current / Total * 100.0 : 0;
    }
}
