using System;
using System.Collections.Generic;
using System.Linq;

namespace Remove_Top.Features.DuplicateRemoval.Detection
{
    /// <summary>
    /// Detecta duplicados por palabra clave: archivos que comparten un PAR de
    /// consecutivas significativas del BLOQUE DE TÍTULO de su nombre
    /// (p. ej. "ROSITA FLORES - AGUAS DEL RIO" y "ROSITA FLORES FEAT VERONICA
    /// - AGUAS DEL RIO" comparten el par (aguas, rio) del título). El bloque
    /// del artista no participa, así que dos canciones DISTINTAS del mismo
    /// intérprete ("... - AGUAS DEL RIO" vs "... - BUSCANDO OLVIDO") ya no se
    /// agrupan por el par (rosita, flores).
    ///
    /// Cada archivo se agrupa por su par MÁS específico (el que comparte con
    /// menos archivos) y cae en un único grupo: así se evita que la
    /// transitividad encadene grupos (p. ej. "Track One Love", "One Love
    /// Special" y "Love Special Edition" no quedan todos unidos, porque cada
    /// par se procesa de forma independiente). Se requieren más de 4 palabras
    /// significativas en el título: los títulos más cortos quedan fuera de la
    /// coincidencia difusa por palabra clave (los de una sola palabra ya los
    /// agrupa el detector por nombre normalizado). Estos grupos son difusos y
    /// siempre quedan desmarcados por defecto.
    /// </summary>
    internal sealed class KeywordDetector : IDuplicateDetector
    {
        public IReadOnlyList<DuplicateGroup> Detect(IReadOnlyList<FileRecord> records)
        {
            var active = records.Where(r => r.Words.Length > 4).ToArray();
            if (active.Length < 2) return [];

            // par -> índices de los archivos que contienen ese par consecutivo.
            var pairToRecords = new Dictionary<string, List<int>>(StringComparer.Ordinal);
            for (int i = 0; i < active.Length; i++)
            {
                var words = active[i].Words;
                for (int w = 0; w < words.Length - 1; w++)
                {
                    string pair = words[w] + "\u0001" + words[w + 1];
                    if (!pairToRecords.TryGetValue(pair, out var list))
                        pairToRecords[pair] = list = [];
                    list.Add(i);
                }
            }

            // Se procesan primero los pares más raros (menos archivos): son la
            // señal más específica. Cada archivo se asigna a un único par, lo
            // que elimina los encadenamientos por transitividad.
            var assigned = new bool[active.Length];
            var groups = new List<DuplicateGroup>();
            foreach (var pair in pairToRecords.OrderBy(p => p.Value.Count))
            {
                var members = pair.Value.Where(i => !assigned[i]).ToArray();
                if (members.Length < 2) continue;

                foreach (int i in members) assigned[i] = true;
                var memberRecords = members.Select(i => active[i]).ToArray();
                groups.Add(GroupBuilder.Build(memberRecords, DuplicateMatchKind.ProbableByKeyword, keepLargest: true));
            }

            return groups;
        }
    }
}
