using Microsoft.Web.WebView2.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;

namespace Remove_Top.Features.DocumentPreview
{
    /// <summary>
    /// Clasificación de un documento para su previsualización en Duplicados.
    /// Solo los tipos con lector nativo tienen preview: texto plano directo,
    /// Office moderno (OOXML) con extracción de texto incluida, y PDF vía
    /// WebView2. Los binarios legacy (.doc/.xls/.ppt) no tienen lector en
    /// .NET y quedan como Unsupported (botón EyeOff, igual que el video).
    /// </summary>
    public enum DocumentKind
    {
        Unsupported,
        PlainText,
        OfficeOpenXml,
        Pdf
    }

    /// <summary>Texto extraído de un documento, siempre capado (ver AppLimits).</summary>
    public sealed class DocumentTextResult
    {
        public string Text { get; set; } = "";
        public int LinesShown { get; set; }
        public bool Truncated { get; set; }
    }

    /// <summary>
    /// Soporte del previsualizador de documentos: detección por extensión y
    /// extracción de texto (plano + Office moderno) sin librerías externas.
    /// El PDF se renderiza con WebView2 (ya usado por el login de YouTube).
    /// </summary>
    public static class DocumentPreviewSupport
    {
        private static readonly string[] PlainTextExtensions = [".txt", ".csv", ".log", ".md"];
        private static readonly string[] OfficeExtensions = [".docx", ".xlsx", ".pptx"];

        /// <summary>Lector XML seguro: sin DTD ni resolvedor externo.</summary>
        private static readonly XmlReaderSettings SecureXmlSettings = new()
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null
        };

        /// <summary>Tope de filas de cálculo por hoja (la vista es un vistazo, no el libro completo).</summary>
        private const int MaxSheetRows = 500;

        /// <summary>
        /// UTF-8 estricto: lanza DecoderFallbackException ante bytes inválidos
        /// (se evita el constructor con argumentos nombrados).
        /// </summary>
        private static readonly Encoding StrictUtf8 = Encoding.GetEncoding(
            "utf-8", EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);

        /// <summary>Clasifica un archivo según su extensión (insensible a mayúsculas).</summary>
        public static DocumentKind Classify(string path)
        {
            var ext = Path.GetExtension(path)?.ToLowerInvariant();
            if (ext == null) return DocumentKind.Unsupported;
            if (ext == ".pdf") return DocumentKind.Pdf;
            if (Array.IndexOf(OfficeExtensions, ext) >= 0) return DocumentKind.OfficeOpenXml;
            if (Array.IndexOf(PlainTextExtensions, ext) >= 0) return DocumentKind.PlainText;
            return DocumentKind.Unsupported;
        }

        /// <summary>Indica si el archivo es un documento previsualizable (texto, Office moderno o PDF).</summary>
        public static bool IsDocumentFile(string path) => Classify(path) != DocumentKind.Unsupported;

        /// <summary>Etiqueta corta del tipo para el pie de la tarjeta.</summary>
        public static string DescribeKind(DocumentKind kind) => kind switch
        {
            DocumentKind.PlainText => "Texto",
            DocumentKind.OfficeOpenXml => "Office",
            DocumentKind.Pdf => "PDF",
            _ => "Documento"
        };

        /// <summary>Indica si el Runtime de WebView2 está disponible (necesario para el PDF).</summary>
        public static bool IsPdfViewerAvailable()
        {
            try
            {
                CoreWebView2Environment.GetAvailableBrowserVersionString();
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Extrae el texto mostrable de un documento de texto u Office en un
        /// hilo de fondo. maxBytes limita la lectura (plano) o los caracteres
        /// extraídos (Office); maxLines limita las líneas. Lanza excepción si
        /// el archivo no se puede leer (el visor muestra el estado de error).
        /// </summary>
        public static Task<DocumentTextResult> ExtractTextAsync(string path, DocumentKind kind, int maxBytes, int maxLines)
            => Task.Run(() => ExtractText(path, kind, maxBytes, maxLines));

        private static DocumentTextResult ExtractText(string path, DocumentKind kind, int maxBytes, int maxLines)
        {
            string text;
            bool truncatedBySource;
            if (kind == DocumentKind.OfficeOpenXml)
                text = ExtractOfficeText(path, Path.GetExtension(path)?.ToLowerInvariant() ?? "", maxBytes, out truncatedBySource);
            else
                text = ReadPlainText(path, maxBytes, out truncatedBySource);

            var lines = text.Split('\n');
            bool truncated = truncatedBySource || lines.Length > maxLines;
            var shown = lines.Select(l => l.TrimEnd('\r')).Take(maxLines).ToArray();
            return new DocumentTextResult
            {
                Text = string.Join('\n', shown).TrimEnd('\r', '\n'),
                LinesShown = shown.Length,
                Truncated = truncated
            };
        }

        /// <summary>
        /// Lee un archivo de texto plano con FileShare.Read (no bloquea el
        /// borrado) y lo decodifica como UTF-8 con fallback a Latin-1.
        /// </summary>
        private static string ReadPlainText(string path, int maxBytes, out bool truncated)
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            truncated = fs.Length > maxBytes;
            int count = (int)Math.Min(fs.Length, maxBytes);
            byte[] buffer = new byte[count];
            int read = 0;
            while (read < count)
            {
                int n = fs.Read(buffer, read, count - read);
                if (n == 0) break;
                read += n;
            }
            return DecodeWithFallback(buffer, read);
        }

        /// <summary>
        /// Decodifica como UTF-8 estricto y, si falla (p. ej. ANSI con
        /// tildes), reintenta como Latin-1: para el rango del español
        /// (0xA0-0xFF) es idéntico a Windows-1252 y no requiere el paquete
        /// CodePages.
        /// </summary>
        private static string DecodeWithFallback(byte[] buffer, int count)
        {
            try
            {
                string text = StrictUtf8.GetString(buffer, 0, count);
                return text.TrimStart('﻿');
            }
            catch (DecoderFallbackException)
            {
                return Encoding.Latin1.GetString(buffer, 0, count).TrimStart('﻿');
            }
        }

        /// <summary>
        /// Extrae el texto de un Office moderno (docx/xlsx/pptx son ZIPs con
        /// XML). charCap limita los caracteres extraídos.
        /// </summary>
        private static string ExtractOfficeText(string path, string ext, int charCap, out bool truncated)
        {
            truncated = false;
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var zip = new ZipArchive(fs, ZipArchiveMode.Read);
            var sb = new StringBuilder();
            if (ext == ".docx")
                ExtractDocx(zip, sb, charCap, ref truncated);
            else if (ext == ".xlsx")
                ExtractXlsx(zip, sb, charCap, ref truncated);
            else
                ExtractPptx(zip, sb, charCap, ref truncated);
            return sb.ToString();
        }

        /// <summary>Devuelve true si el constructor alcanzó el tope (y lo marca).</summary>
        private static bool Capped(StringBuilder sb, int charCap, ref bool truncated)
        {
            if (sb.Length < charCap) return false;
            truncated = true;
            return true;
        }

        /// <summary>Texto de word/document.xml: párrafos como líneas.</summary>
        private static void ExtractDocx(ZipArchive zip, StringBuilder sb, int charCap, ref bool truncated)
        {
            var entry = zip.GetEntry("word/document.xml")
                ?? throw new InvalidDataException("El .docx no contiene word/document.xml.");
            using var stream = entry.Open();
            using var reader = XmlReader.Create(stream, SecureXmlSettings);
            bool inText = false;
            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.Element)
                {
                    switch (reader.LocalName)
                    {
                        case "t": inText = !reader.IsEmptyElement; break;
                        case "tab": sb.Append('\t'); break;
                        case "br":
                        case "cr": sb.Append('\n'); break;
                    }
                }
                else if (reader.NodeType == XmlNodeType.Text && inText)
                {
                    sb.Append(reader.Value);
                }
                else if (reader.NodeType == XmlNodeType.EndElement)
                {
                    if (reader.LocalName == "t") inText = false;
                    else if (reader.LocalName == "p") sb.Append('\n');
                }
                if (Capped(sb, charCap, ref truncated)) break;
            }
        }

        /// <summary>
        /// Texto de un libro: cadenas compartidas + valores de cada hoja
        /// (celdas separadas por tabulación, filas por línea). Las fechas
        /// seriales se muestran como número: es un vistazo, no fidelidad total.
        /// </summary>
        private static void ExtractXlsx(ZipArchive zip, StringBuilder sb, int charCap, ref bool truncated)
        {
            var shared = ReadSharedStrings(zip);
            var sheets = zip.Entries
                .Where(e => e.FullName.StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase)
                    && e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                .OrderBy(e => e.FullName)
                .ToArray();
            if (sheets.Length == 0)
                throw new InvalidDataException("El .xlsx no contiene hojas de cálculo.");

            int sheetNumber = 0;
            foreach (var sheet in sheets)
            {
                sheetNumber++;
                sb.AppendLine($"── Hoja {sheetNumber} ──");
                using var stream = sheet.Open();
                using var reader = XmlReader.Create(stream, SecureXmlSettings);
                bool inRow = false, inValue = false, inInlineText = false;
                string? cellType = null;
                var cell = new StringBuilder();
                var rowCells = new List<string>();
                int rows = 0;
                while (reader.Read() && rows < MaxSheetRows)
                {
                    if (reader.NodeType == XmlNodeType.Element)
                    {
                        switch (reader.LocalName)
                        {
                            case "row": inRow = true; rowCells.Clear(); break;
                            case "c": cellType = reader.GetAttribute("t"); cell.Clear(); break;
                            case "v": inValue = !reader.IsEmptyElement; break;
                            case "t": inInlineText = !reader.IsEmptyElement; break;
                        }
                    }
                    else if (reader.NodeType == XmlNodeType.Text && (inValue || inInlineText))
                    {
                        cell.Append(reader.Value);
                    }
                    else if (reader.NodeType == XmlNodeType.EndElement)
                    {
                        if (reader.LocalName == "v") inValue = false;
                        else if (reader.LocalName == "t") inInlineText = false;
                        else if (reader.LocalName == "c")
                        {
                            string value = cell.ToString();
                            if (cellType == "s" && int.TryParse(value, out int index)
                                && index >= 0 && index < shared.Count)
                                value = shared[index];
                            rowCells.Add(value);
                        }
                        else if (reader.LocalName == "row" && inRow)
                        {
                            inRow = false;
                            rows++;
                            sb.AppendLine(string.Join('\t', rowCells));
                        }
                    }
                    if (Capped(sb, charCap, ref truncated)) return;
                }
                if (rows >= MaxSheetRows) truncated = true;
            }
        }

        /// <summary>Cadenas compartidas (xl/sharedStrings.xml) de un libro.</summary>
        private static List<string> ReadSharedStrings(ZipArchive zip)
        {
            var shared = new List<string>();
            var entry = zip.GetEntry("xl/sharedStrings.xml");
            if (entry == null) return shared;
            using var stream = entry.Open();
            using var reader = XmlReader.Create(stream, SecureXmlSettings);
            var item = new StringBuilder();
            bool inItem = false, inText = false;
            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.Element)
                {
                    if (reader.LocalName == "si") { inItem = true; item.Clear(); }
                    else if (inItem && reader.LocalName == "t") inText = !reader.IsEmptyElement;
                }
                else if (reader.NodeType == XmlNodeType.Text && inItem && inText)
                {
                    item.Append(reader.Value);
                }
                else if (reader.NodeType == XmlNodeType.EndElement)
                {
                    if (reader.LocalName == "t") inText = false;
                    else if (reader.LocalName == "si") { inItem = false; shared.Add(item.ToString()); }
                }
            }
            return shared;
        }

        /// <summary>Texto de cada diapositiva (ppt/slides/slide*.xml).</summary>
        private static void ExtractPptx(ZipArchive zip, StringBuilder sb, int charCap, ref bool truncated)
        {
            var slides = zip.Entries
                .Where(e => e.FullName.StartsWith("ppt/slides/slide", StringComparison.OrdinalIgnoreCase)
                    && e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                .OrderBy(e => e.FullName)
                .ToArray();
            if (slides.Length == 0)
                throw new InvalidDataException("El .pptx no contiene diapositivas.");

            int slideNumber = 0;
            foreach (var slide in slides)
            {
                slideNumber++;
                sb.AppendLine($"── Diapositiva {slideNumber} ──");
                using var stream = slide.Open();
                using var reader = XmlReader.Create(stream, SecureXmlSettings);
                bool inText = false;
                while (reader.Read())
                {
                    if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "t")
                        inText = !reader.IsEmptyElement;
                    else if (reader.NodeType == XmlNodeType.Text && inText)
                        sb.Append(reader.Value);
                    else if (reader.NodeType == XmlNodeType.EndElement)
                    {
                        if (reader.LocalName == "t") inText = false;
                        else if (reader.LocalName == "p") sb.Append('\n');
                    }
                    if (Capped(sb, charCap, ref truncated)) return;
                }
            }
        }
    }
}
