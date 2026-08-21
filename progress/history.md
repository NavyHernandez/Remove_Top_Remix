# Bitácora histórica (append-only)

Cada vez que se cierra una sesión, su resumen se añade aquí. No edites entradas anteriores. Solo añades al final.

---

## 2026-08-14 — Cierre: nuevas pendientes + PR

**Agente:** humano + opencode (deepseek-v4-flash-free)

### Cambios realizados
1. **`feature_list.json`**: agregadas 2 pendientes nuevas:
   - **id 11** `normalization_cancel_loading`: botón cancelar al cargar/analizar archivos en Normalización.
   - **id 12** `batch_rename_max_files`: máximo de 1000 canciones en Renombrado Masivo (límite centralizado en AppLimits).
2. **Repo**: todo el trabajo de la sesión (QuickRename, colores, botones "Limpiar", feature 8 branding) commiteado en `feat/app-improvements`, pusheado y PR creado.

### Verificación
- Build previo: 0 errores, 0 advertencias (Debug|x64). Salida `TopDjApp.exe`.

---

## 2026-08-14 — Feature 8: renombrado a "Top Dj App" + identidad centralizada en AppLimits

**Agente:** humano + opencode (deepseek-v4-flash-free)

### Cambios realizados
1. **`AppLimits.cs`** — nuevas constantes de identidad: `AppName = "Top Dj App"`, `AppSubtitle = "Mejorador de Audio"`, `AppBrandSite = "www.top-remix.com"` (sin cambiar el dominio) y `AppDataFolderName = "Remove_Top"`.
2. **`MainWindow`** — título de ventana y menú (brand + subtítulo) se montan desde `AppLimits.AppName`/`AppSubtitle`.
3. **Badges de marca de las 5 páginas** (`Normalization`, `BatchRename`, `QuickRename`, `VocalRemoval`, `DuplicateRemoval`): el `TextBlock` de marca (`BrandText`) se setea desde `AppLimits.AppName` en el constructor.
4. **www.top-remix.com** — centralizado en `AppLimits.AppBrandSite` (`SiteBrandText` en QuickRename, `BrandSiteRun` en DuplicateRemoval). Valor intacto.
5. **VocalRemoval** — `ModelInfoText` se construye con `AppLimits.AppName` (antes "Remove-Top").
6. **Ejecutable** — csproj agrega `<AssemblyName>TopDjApp</AssemblyName>`: la salida pasa a `TopDjApp.exe` (RootNamespace/namespaces siguen siendo `Remove_Top`).
7. **Carpeta de datos** — los 4 usos hardcodeados de `"Remove_Top"` (crash.log en App y MainWindow, patterns.json, models) se centralizan en `AppLimits.AppDataFolderName` (valor conservado → sin pérdida de patrones ni del modelo ONNX).

### Verificación
- Build: 0 errores, 0 advertencias (Debug|x64). Salida generada: `TopDjApp.exe`.

---

## 2026-08-14 — Feature 6 (iconos y texto): botones de reset → "Limpiar" con icono de escoba

**Agente:** humano + opencode (deepseek-v4-flash-free)

### Cambios realizados
Todos los botones de reinicio ("Iniciar de nuevo" / "Cancelar y empezar de nuevo") pasan a **"Limpiar"** con icono profesional `Icon.Broom` (escoba), conservando el color de cada feature. Se mantienen como "Cancelar" los botones de cancelación en curso (BatchRename y DuplicateRemoval) y el `StartButton` temporal.

1. **QuickRename** `RestartButton` → "Limpiar" + `Icon.Broom` (XAML + code-behind).
2. **BatchRename** `RestartButton` → "Limpiar" + `Icon.Broom`. `CancelButton` se mantiene "Cancelar".
3. **DuplicateRemoval** `RestartButton` → "Limpiar" + `Icon.Broom`. `CancelButton` se mantiene "Cancelar".
4. **Normalization**: `CancelButton` "Cancelar y empezar de nuevo" → "Limpiar" + `Icon.Broom` (borde ahora `#5B9BD5`, color de la feature); `ClearButton` "Limpiar" gana el icono `Icon.Broom`.
5. Comentarios y docs actualizados (AGENTS.md, feature_list.json).

VocalRemoval no tiene botón de reset; no se tocó.

### Verificación
- Build: 0 errores, 0 advertencias (Debug|x64).

---

## 2026-08-13 — Edición Rápida: sin progreso ni lista de resultados, solo etiqueta de resumen

**Agente:** humano + opencode (deepseek-v4-flash-free)

### Cambios realizados
1. **`QuickRenamePage.xaml`**: eliminadas las secciones `ProgressSection` (barra de progreso) y `ResultsSection` (lista de resultados por archivo). Nueva sección `ResultSection` con una sola etiqueta `ResultSummaryText` ("Se cambiaron X de Y archivo(s)."), el badge "✓ Completado" y el botón "Iniciar de nuevo".
2. **`QuickRenamer.cs`**: `ApplyRenamesAsync` ya no recibe `IProgress<QuickRenameProgress>`; ahora devuelve `int` con la cantidad de archivos renombrados correctamente. Eliminada la clase `QuickRenameProgress` y el icono de estado de `QuickRenameResult`.
3. **`QuickRenamePage.xaml.cs`**: flujo simplificado — sin colección `_results`, sin progreso; tras completar se muestra la etiqueta con el conteo y se recarga la lista con los nombres nuevos.

### Verificación
- Build: 0 errores, 0 advertencias (Debug|x64).

---

## 2026-08-13 — Edición Rápida: fix validación, tope 200, badge gratuito, top-remix.com e "Iniciar de nuevo"

**Agente:** humano + opencode (deepseek-v4-flash-free)

### Bug corregido (crítico)
`QuickRenamer.ValidateName` rechazaba cualquier nombre con `.`, y como la edición es sobre el nombre completo con extensión (`.mp3`/`.wav`), **todo renombrado fallaba** con "El nombre no puede contener rutas ni subdirectorios". Se eliminó `.` del chequeo; `:`, `/`, `\` ya los cubre `Path.GetInvalidFileNameChars()`.

### Cambios realizados
1. **`AppLimits.cs`**: nueva constante `QuickRenameMaxFilesToScan = 200` + propiedad `QuickRenameLimitMessage` ("Se muestran los primeros 200 archivos .mp3/.wav de la carpeta.").
2. **`QuickRenamer.cs`**: `GetAudioFiles(folderPath, int? maxFiles)` aplica `.Take(n)`; fix de `ValidateName`.
3. **`QuickRenamePage.xaml`**:
   - Badge "Versión Gratuita" (#70AD47) + mensaje de límite en la sección "Carpeta de origen".
   - Marca `www.top-remix.com` centrada en la línea de "Nombres editables" (grid `Auto,*,Auto,Auto`).
   - Botón **"Iniciar de nuevo"** al final de la sección de resultados (patrón BatchRename).
4. **`QuickRenamePage.xaml.cs`**:
   - `LoadFiles` refactorizado en `LoadFiles` + `PopulateItems` (solo reconstruye la lista).
   - Tope de 200 en la carga (`PopulateItems`).
   - Fix de flujo: al completar ya NO se borran resultados (antes `LoadFiles` los limpiaba al instante); se recarga la lista con los nombres nuevos conservando resultados, badge "✓ Completado" y botón "Iniciar de nuevo".
   - `RestartButton_Click`: resetea ruta, lista, resultados y secciones.

### Verificación
- Build: 0 errores, 0 advertencias (Debug|x64).
- Renombrado con extensión (p. ej. `song.mp3` → `Song.mp3`) ya valida y aplica `File.Move`.

---

## 2026-08-13 — Duplicados: "nombre contenido" exige confirmación por tamaño o duración

**Agente:** humano + opencode (deepseek-v4-flash-free)

### Problema reportado
En `F:\Musik\chicha\QUE CHUCHAQUI\MUJERES`, `ANITA SOTALIN - CORAZÓN CORAZÓN.mp3` (4:10, 10,0 MB) se agrupaba con `Anita Sotalin - corazon.mp3` (3:31, 3,4 MB) como duplicado marcado, pese a ser canciones distintas (duración y tamaño diferentes).

### Causa
La coincidencia de palabras del `SubsetNameDetector` no basta para declarar "la misma canción": solo se desmarcaba si la duración difería > 2× (`SameNameMaxDurationRatio`). Una diferencia del 16% (4:10 vs 3:31) se consideraba válida.

### Cambios realizados (`DurationVerifier.cs`)
- Se separó la rama `SubsetMatch` de `SameName`.
- Para "nombre contenido", el duplicado se confirma SOLO si:
  - comparte **tamaño exacto** (`SameSize`), o
  - la **duración es prácticamente igual** (nueva `SubsetMatchDurationTolerance = 0.10`, más estricta que `DurationTolerance`).
- Si ninguna se cumple, el miembro se **elimina del grupo** (falso positivo, igual que en `ProbableByKeyword`); si el grupo queda vacío se descarta.
- `SameName` conserva su salvaguarda de 2× (título idéntico).

### Verificación
- `AMOR DE POBRES` ↔ `AMOR DE POBRES (1)`: duración idéntica (163,1 s ambas) → siguen agrupando y marcadas. ✓
- `ANITA SOTALIN - CORAZÓN CORAZÓN` (250,5 s) vs `Anita Sotalin - corazon` (210,8 s): 15,9 % > 10 % y tamaños distintos → se elimina del grupo; deja de aparecer como duplicado. ✓
- Build: 0 errores, 0 advertencias (Debug|x64).

---

## 2026-08-13 — Duplicados: fix del "×N" inflado (hub de artista en SubsetNameDetector)

**Agente:** humano + opencode (deepseek-v4-flash-free)

### Problema reportado
En `F:\Musik\chicha\QUE CHUCHAQUI\MUJERES`, `Hipatia Balseca - AMOR DE POBRES.mp3` se agrupaba con `×10` pese a existir solo 2 copias reales (2 archivos; difieren en 4 bytes).

### Causa raíz
`SubsetNameDetector` comparaba TODAS las palabras del nombre (`GetAllNameWords`) con clustering transitivo (union-find). `hipatia balseca.mp3` (solo el artista, 2 palabras) actuaba como "hub": sus 2 palabras son subconjunto de cualquier canción de Hipatia (diferencia ≤ `MaxWordDifference=3`), el nombre normalizado del hub es prefijo del nombre normalizado completo, y la unión transitiva fusionaba las 10 canciones distintas en un solo cluster marcado para eliminar.

### Cambios realizados
1. **`NameNormalizer`**:
   - Nuevo `GetTitleWordsAll()`: extrae TODAS las palabras del BLOQUE DE TÍTULO (último bloque tras separadores), conservando dígitos y stop-words.
   - Refactor de `GetAllNameWords()` → `ExtractAllWords()` (misma lógica).
2. **`SubsetNameDetector`**:
   - La comparación de subconjunto ahora usa SOLO el TÍTULO (`GetTitleWordsAll`); el artista ya no participa y deja de ser un hub. La contención por nombre normalizado (con artista) se mantiene como salvaguarda.
   - Nuevo tope `MaxGroupSize = 6`: clusters mayores se descartan (cadena de falsos positivos).
   - Doc actualizada (clasificación real `SubsetMatch`, ya no decía `SameName`).
3. **`FileRecord.cs`**: comentario "(hasta 4)" → "(hasta 8)" (coincide con `GetTitleWords` default).
4. **`DuplicateRemovalPage.xaml.cs`**:
   - Texto hardcodeado "< 6 KB" → `DuplicateItem.FormatSize(AppLimits.DuplicatesMinValidFileSizeBytes)`.
   - Fix borrado: solo se quitan de la UI los archivos que se eliminaron correctamente (los fallidos permanecen marcados para reintento).
5. **`DuplicateRemover.cs`**: `DeletionResult` ahora incluye `FilePath`.
6. **`AGENTS.md`**: descripción del "nombre contenido" actualizada (título, no nombre completo; tope 6 miembros).

### Verificación
- Simulación del nuevo algoritmo sobre los 12 archivos de Hipatia Balseca: queda UN solo grupo de 2 miembros (las 2 copias reales de "AMOR DE POBRES"); los otros 10 ya no se agrupan.
- Casos legítimos verificados que siguen agrupando: `AMOR DE POBRES` ↔ `AMOR DE POBRES (1)`; `CORAZON DE AJI` ↔ `Corazon de aji morado`. El caso 147 (espacios, sin separador) no cambia: se detecta igual por keyword como "Posible".
- Build: 0 errores, 0 advertencias (Debug|x64).

---

## 2026-08-13 — Sesión de mejoras múltiples

**Agente:** humano + opencode (mimo-v2.5-free)

### Cambios realizados

1. **BatchRename — Simplificación de card de IA**
   - Eliminados `ProviderComboBox`, `ApiKeyBox` y texto "Modo de pruebas" del card "Mejorar patrones con IA"
   - Renombrado `GroqApiClient.cs` → `TopRemixServerApiClient.cs` (cliente HTTP unificado)
   - `GroqPatternSuggester.cs` actualizado: sin parámetro apiKey, `MaxFileNames=10`
   - Eliminado `MockPatternSuggester.cs`

2. **QuickRename — Eliminación total de IA**
   - Eliminados bloques `AiSection` y `SuggestionsSection` del XAML
   - Eliminado todo el código de corrección con IA del code-behind
   - Eliminados archivos: `NameCorrection.cs`, `MockNameCorrector.cs`, `GroqNameCorrector.cs`

3. **Normalization — Corrección ortográfica automática**
   - Nuevo `SpanishNameCorrector.cs` con diccionario local de ~400 palabras con tilde
   - `AudioNormalizer.cs`: nuevos métodos `GetExpectedOutputPath`, `GetCorrectedOutputPath`, `HasProcessedOutput` (dual-check), `IsValidWav`, `CorrectOutputNames`
   - `NormalizationPage.xaml.cs`: llamada a `CorrectOutputNames` tras `ProcessFilesAsync`

4. **Normalization — Limpieza de nombres de salida**
   - Nuevo `CleanOutputName` en `AudioNormalizer.cs`:
     - Elimina paréntesis `()` y corchetes `[]` con su contenido
     - Elimina palabras "audio", "video", "oficial" (cualquier caso)
     - Convierte a Title Case (primera letra mayúscula, resto minúscula)
   - Pipeline: `CleanOutputName` → `SpanishNameCorrector.CorrectTitle`
   - Ejemplo: `JESSI URIBE - DULCE PECADO.wav` → `Jessi Uribe - Dulce Pecado.wav`

5. **Normalization — Botón Cancelar**
   - Nuevo `CancelButton` ("Cancelar y empezar de nuevo") debajo de `StartButton`
   - Visible tras el análisis, oculto durante procesamiento
   - Resetea toda la página al estado inicial

6. **Helpers centralizados**
   - Nuevo `AppLimits.cs` (límites de la versión gratuita)
   - Nuevo `PremiumLinks.cs` (enlace premium)

7. **Otros**
   - Mejoras en `DuplicateRemoval` y `VocalRemoval`
   - `AGENTS.md` actualizado con todos los cambios

### Archivos modificados (24)
- `AGENTS.md`
- `Features/BatchRename/BatchRenamePage.xaml`, `.cs`, `GroqPatternSuggester.cs`
- `Features/DuplicateRemoval/DuplicateRemovalPage.xaml`, `.cs`, `DuplicateRemover.cs`, `DuplicateScanner.cs`
- `Features/Normalization/AudioNormalizer.cs`, `NormalizationPage.xaml`, `.cs`
- `Features/QuickRename/QuickRenamePage.xaml`, `.cs`
- `Features/VocalRemoval/VocalRemovalPage.xaml`, `.cs`, `VocalSeparator.cs`
- `Helpers/TopRemixServerApiClient.cs` (nuevo, reemplaza GroqApiClient)

### Archivos eliminados (5)
- `Features/BatchRename/MockPatternSuggester.cs`
- `Features/QuickRename/GroqNameCorrector.cs`, `MockNameCorrector.cs`, `NameCorrection.cs`
- `Helpers/GroqApiClient.cs`

### Archivos nuevos (3)
- `Features/Normalization/SpanishNameCorrector.cs`
- `Helpers/AppLimits.cs`
- `Helpers/PremiumLinks.cs`

### Resultado
Build exitoso: 0 errores, 0 advertencias. Branch `feat/app-improvements` push a `origin`. PR creado en GitHub.

---

## 2026-08-13 — Continuación: Cancelar, limpieza de nombres y estructura progress

**Agente:** humano + opencode (mimo-v2.5-free)

### Cambios realizados

1. **Normalization — Botón "Cancelar y empezar de nuevo"**
   - Nuevo `CancelButton` en `NormalizationPage.xaml` debajo de `StartButton`
   - Visible tras el análisis de archivos, oculto durante procesamiento
   - Handler `CancelButton_Click` que resetea toda la página al estado inicial
   - Visibilidad controlada en `AnalyzeFilesAsync`, `StartButton_Click` y `ClearButton_Click`

2. **Normalization — Limpieza automática de nombres de salida**
   - Nuevo método `CleanOutputName` en `AudioNormalizer.cs`:
     - Elimina contenido entre paréntesis `()` y corchetes `[]` con Regex
     - Elimina palabras "audio", "video", "oficial" (case-insensitive)
     - Convierte a Title Case (primera letra mayúscula, resto minúscula)
   - Pipeline actualizado: `CleanOutputName` → `SpanishNameCorrector.CorrectTitle`
   - `GetCorrectedOutputPath` actualizado para usar el nuevo pipeline
   - Ejemplo: `JESSI URIBE - DULCE PECADO (Official Audio).wav` → `Jessi Uribe - Dulce Pecado.wav`

3. **Carpeta `progress/` — Estructura de agentes**
   - Nueva carpeta `progress/` en la raíz del proyecto
   - `progress/.gitignore`: excluye todo excepto `history.md` y `feature_list.json`
   - `progress/history.md`: bitácora histórica (append-only) con sesiones registradas
   - `progress/feature_list.json`: 10 features (5 done, 5 pending) en formato estructurado
   - `progress/` agregado al `.gitignore` raíz

4. **AGENTS.md — Nota para agentes**
   - Agregada nota al inicio: los agentes deben leer `progress/history.md` y `progress/feature_list.json` antes de trabajar
   - Título actualizado a "Top Dj App"

### Archivos modificados (2)
- `AGENTS.md` — nota para agentes + título actualizado
- `.gitignore` — agregado `progress/`

### Archivos nuevos (3)
- `progress/.gitignore`
- `progress/history.md`
- `progress/feature_list.json`

### Features pendientes registradas
- Iconos en botones y mejora de texto
- Funcionalidad Uso (Métricas de uso)
- Cambiar nombre a "Top Dj App" en todas partes
- Iconos de la aplicación (logo, taskbar, title bar)
- Conexión al servidor de IA (TopRemix)

### Resultado
Build exitoso: 0 errores, 0 advertencias. Estructura `progress/` lista para uso de agentes.

---

## 2026-08-13 — Mejoras en Duplicados y Renombrado Masivo

**Agente:** humano + opencode (mimo-v2.5-free)

### Cambios realizados

1. **Duplicados — Nuevo detector de contención de palabras (`SubsetNameDetector`)**
   - Detecta archivos donde el nombre más corto es subconjunto de palabras del más largo
   - Ej.: `147 Lolita Echeverria Cuchara De Palo Extenced Intro Simple 2K24 147 BPM.mp3` ↔ `147 Lolita Echeverria Cuchara de Palo Extenced Intro 2k24.mp3`
   - Clustering transitivo con union-find, diferencia máxima de 3 palabras
   - Clasificado como `SubsetMatch` con badge "Exacto" en la UI
   - Detalle: "nombre contenido · misma duración" o "nombre contenido · mismo tamaño"

2. **Duplicados — Fix: extensiones múltiples en nombres**
   - `NameNormalizer`: nuevo método `StripAllExtensions()` con set de ~30 extensiones conocidas (audio, DJ, contenedores)
   - `Normalize()`, `GetSignificantWords()`, `GetTitleWords()`, `GetAllNameWords()` ahora usan `StripAllExtensions()` en vez de `GetFileNameWithoutExtension()`
   - Fix: `song.mp3.vdjstems` y `song.mp3` ahora se normalizan igual (antes `.mp3` quedaba como parte del nombre)

3. **Duplicados — Icono "sin duplicados"**
   - Nuevo `NoDuplicatesIcon` (CheckmarkCircleFilled verde #2ECC71) junto al texto "No se encontraron duplicados."
   - Se oculta al iniciar escaneo, se muestra solo cuando no hay resultados

4. **Duplicados — Botones de acciones centrados**
   - Grid de acciones: `HorizontalAlignment="Right"` → `HorizontalAlignment="Center"`

5. **Renombrado Masivo — Botón "Cancelar"**
   - Nuevo `CancelButton` debajo del botón "Eliminar patrones", centrado, estilo outline verde
   - Icono `Dismiss` (✕), resetea ruta/resultados/vista previa/progreso/sugerencias
   - Conserva los patrones (persistencia)
   - Oculto cuando hay resultados mostrados

6. **Renombrado Masivo — Botones se ocultan tras resultados**
   - Tanto `StartButton` como `CancelButton` se ocultan después de completar el renombrado
   - Reaparecen al hacer clic en "Iniciar de nuevo"

### Pipeline de Duplicados actualizado
```
1. NormalizedNameDetector (exacto + fuzzy "1 letra")
2. SubsetNameDetector (contención de palabras) ← NUEVO
3. ExactHashDetector (SHA-256)
4. KeywordDetector (palabras clave)
5. DurationVerifier (verifica todo incluido SubsetMatch)
```

### Archivos nuevos (1)
- `Features/DuplicateRemoval/Detection/SubsetNameDetector.cs`

### Archivos modificados (8)
- `Features/DuplicateRemoval/Detection/NameNormalizer.cs` — `StripAllExtensions()`, fix extensiones múltiples
- `Features/DuplicateRemoval/Detection/SubsetNameDetector.cs` — nuevo detector
- `Features/DuplicateRemoval/DuplicateMatchKind.cs` — nuevo valor `SubsetMatch`
- `Features/DuplicateRemoval/Detection/GroupBuilder.cs` — manejo de `SubsetMatch`
- `Features/DuplicateRemoval/DuplicateScanner.cs` — integración de `SubsetNameDetector` en pipeline
- `Features/DuplicateRemoval/Detection/DurationVerifier.cs` — verificación para `SubsetMatch`
- `Features/DuplicateRemoval/DuplicateItem.cs` — display de `SubsetMatch` (badge + detalle + icono)
- `Features/DuplicateRemoval/DuplicateRemovalPage.xaml` + `.cs` — icono sin duplicados, botones centrados, botón Cancelar en BatchRename

### Resultado
Build exitoso: 0 errores, 0 advertencias.
---

## 2026-08-16 — Cierre: commit + push y nueva pendiente (icono en encabezados)

**Agente:** humano + opencode (deepseek-v4-flash-free)

### Cambios realizados
1. **Nueva pendiente id 22** `header_icon_per_feature` agregada a `feature_list.json`: "Icono en el encabezado de cada funcionalidad" (icono representativo junto al título/badge, coherente con el icono del menú).
2. **Repo actualizado**: commit `dffb008` pusheado a `origin/feat/app-improvements` (17 archivos, +615/-26). Incluye: funcionalidad Cuenta (perfil + actualizaciones simuladas), badge de marca a todo lo ancho con Bahnschrift, menú glass (Acrílico) y botón "Marcar todos".

### Verificación
- Build previo Debug|x64: 0 errores, 0 advertencias.
- JSON de feature_list validado (22 features).
- Working tree limpio tras el push.

---

## 2026-08-19 — Feature 24: previsualizador de imágenes en Duplicados

**Agente:** humano + opencode (deepseek-v4-flash-free)

### Cambios realizados
1. **`Features/ImagePreview/`** (nuevo módulo reutilizable, solo API nativa WinUI 3):
   - `ImagePreviewSupport.cs` — `IsImageFile(path)` (extensiones alineadas con `FileTypeIconConverter`: .jpg/.jpeg/.png/.gif/.bmp/.tiff/.tif/.webp/.ico/.jfif/.svg) y `CreateSource(path)` → `BitmapImage` (raster, `DecodePixelWidth = 1600`) o `SvgImageSource` (SVG).
   - `ImagePreviewView.xaml/.cs` — visor simple: `Image` con `Stretch="Uniform"` (ajustada al espacio, **sin zoom/pan**), estados vacío/cargando/error, `Load(path)`, `Clear()` (libera `Source`), `CurrentPath` y eventos `ImageLoaded(int w, int h)` / `ImageLoadFailed`. Para SVG usa `Opened`/`OpenFailed` (0×0 en el pie).
2. **`DuplicateItem.cs`** — `IsImage`, `IsPreviewable` (audio o imagen) y `PreviewIcon` (`Play` audio / `Image` imagen).
3. **`DuplicateRemovalPage.xaml`** — botón de preview **unificado** en las filas de Exactos/Posibles: `Visibility="{Binding IsPreviewable}"`, `Icon="{Binding PreviewIcon}"` (dañados sin botón). Nueva tarjeta `ImagePreviewSection` (header icono+nombre+cerrar, visor de 300 px, pie con dimensiones + tamaño en disco).
4. **`DuplicateRemovalPage.xaml.cs`** — `PreviewButton_Click` despacha por tipo (audio → `BeginPreviewAsync`, imagen → `BeginImagePreview`). Nuevos `BeginImagePreview`, `ClearImagePreview`, `ImagePreviewClose_Click`, `ImagePreviewViewer_ImageLoaded`/`ImageLoadFailed` y `StopAllPreviews()` (cierra audio + imagen). `StopAllPreviews` sustituye a `StopPreviewCore(closeFile: true)` en `ResetResults`, `RunDeletionAsync`, `BeginPreviewAsync` y `Page_Unloaded` (un solo preview activo a la vez, archivo liberado antes de resetear/borrar).
5. **AGENTS.md** — estructura (`Features/ImagePreview/`) y sección "Previsualizador de imágenes (detalle)".
6. **`feature_list.json`** — feature **24** `image_previewer` `done`.

### Verificación
- Build final Debug|x64: **0 errores, 0 advertencias**.
- JSON de feature_list validado (24 features).

---

## 2026-08-19 — Cierre: 4 pendientes nuevas + push

**Agente:** humano + opencode (deepseek-v4-flash-free)

### Cambios realizados
1. **`feature_list.json`**: agregadas 4 pendientes nuevas:
   - **id 25** `duplicates_scanned_count`: mostrar la cantidad de archivos examinados en Duplicados (resumen del escaneo).
   - **id 26** `account_suggestions_box`: cuadro de sugerencias en Cuenta, visible solo cuando el usuario está logueado (AuthService.IsLoggedIn), con envío al servidor Topremix.
   - **id 27** `strip_metadata`: funcionalidad para quitar metadatos y etiquetas (ID3) de los archivos de audio en lote sin re-codificar.
   - **id 28** `batch_rename_premium_compose_tags`: premium en Renombrado Masivo — "Componer etiquetas": agregar etiquetas al nombre de la canción e incrustar una mini imagen (carátula) en los archivos de audio.
2. **Repo actualizado**: commit + push (imagen preview feature 24 + pendientes 25-28).

### Verificación
- JSON de feature_list validado (28 features).

---

## 2026-08-19 — Features 22 y 25: icono de encabezado por página + archivos examinados en Duplicados

**Agente:** humano + opencode (deepseek-v4-flash-free)

### Cambios realizados
1. **Feature 22 `header_icon_per_feature`** — el icono del badge de marca (junto a "Top Dj App") de cada página pasa a ser coherente con el icono del ítem del menú (antes todas usaban `MusicNote2`):
   - Normalization `MusicNote2` → `Speaker2`, BatchRename → `Edit`, QuickRename → `Rename`, VocalRemoval → `Mic`, DuplicateRemoval → `Copy`. Account ya usaba `Person` (coherente), no se tocó.
2. **Feature 25 `duplicates_scanned_count`** — el encabezado de la sección Resultados de Duplicados ahora muestra cuántos archivos se examinaron:
   - `DuplicateRemovalPage.xaml`: nuevo `ScannedFilesText` (fila adicional bajo el título "Resultados", fuente 12, color secundario).
   - `DuplicateRemovalPage.xaml.cs` (`UpdateTabHeaders`): `"N archivo(s) examinado(s)"`, o `"Se examinaron los primeros N de M archivos"` cuando el escaneo se truncó (`_totalFound > _scannedFiles`). Se resetea con `ResetResults` (`_scannedFiles = 0`).
3. **`feature_list.json`**: features 22 y 25 → `done` (descripciones actualizadas).

### Verificación
- Build final Debug|x64: **0 errores, 0 advertencias**.
- Nota: hubo que cerrar `TopDjApp.exe` en ejecución (bloqueaba la copia del binario) antes de compilar.

---

## 2026-08-19 — Feature 11: botón cancelar durante el análisis de Normalización

**Agente:** humano + opencode (deepseek-v4-flash-free)

### Cambios realizados
1. **`NormalizationPage.xaml.cs`** — el `CancelButton` ahora actúa como "Cancelar" (icono `Dismiss`) **mientras se analizan los archivos** y vuelve a "Limpiar" (icono `Broom`) al terminar:
   - `AnalyzeFilesAsync` crea un `CancellationTokenSource` y lo pasa a `AudioNormalizer.AnalyzeFilesAsync(files, progress, _cts.Token)` (ya lo soportaba). Muestra el botón "Cancelar" y deshabilita `BrowseButton` durante el análisis.
   - `catch (OperationCanceledException)` → `ResetPageState()` + mensaje "Análisis cancelado. Selecciona una carpeta para volver a empezar." (visible bajo el selector de carpeta).
   - `CancelButton_Click`: si `_isAnalyzing`, cancela (`_cts.Cancel()`); en cualquier otro caso resetea la página (comportamiento anterior).
   - `ClearButton_Click` refactorizado en `ResetPageState()` (reutilizado por el cancel del análisis).
   - `finally`: restaura `_isAnalyzing`, habilita `BrowseButton`, devuelve el contenido "Limpiar" del botón y libera el `_cts`.
2. **`feature_list.json`**: feature 11 → `done`.

### Verificación
- Build final Debug|x64: **0 errores, 0 advertencias**.

---

## 2026-08-19 — Feature 19: autenticación Firebase (Email/Password) en Cuenta

**Agente:** humano + opencode (deepseek-v4-flash-free)

### Decisión de diseño (corrige lo registrado previamente)
La feature 19 estaba documentada como `google_login` (stub de Google OAuth, pendiente de Client ID). El usuario la **descartó** y pidió en su lugar autenticación real con **Firebase Email/Password**: "No quiero login con Google ni OAuth... usando el paquete NuGet FirebaseAuthentication.net". La feature pasa a `firebase_email_password_auth` (done).

### Cómo se implementó
1. **`Remove_Top.csproj`** — nuevo `PackageReference` de **FirebaseAuthentication.net 4.1.0** (trae Newtonsoft.Json transitivo). No hay SDK nativo de Firebase para .NET/WinUI; este paquete (.NET Standard 2.0) es compatible con `net8.0-windows10.0.19041.0`.
2. **`Features/Account/FirebaseConfig.cs`** (nuevo) — `ApiKey` (`AIzaSyD4bnxoglAdLlAcUyGwb8Iet6LJwoAjpE8`) y `AuthDomain` (`top-remix-e53ca.firebaseapp.com`), app web de Firebase del proyecto. La API key es pública por diseño (config web); documentado en el archivo.
3. **`Features/Account/SecureUserRepository.cs`** (nuevo) — implementa `Firebase.Auth.Repository.IUserRepository` sobre el **Windows Credential Locker** (`PasswordVault`): guarda `{ UserInfo, FirebaseCredential }` serializado (Newtonsoft, `StringEnumConverter`) en el campo "password" de un credential con recurso `Remove_Top_FirebaseAuth`/usuario `firebase_user`. `ReplaceCredential` hace remove-then-add (PasswordVault.Add lanza si el credential ya existe); `UserExists` tolera `ELEMENT_NOT_FOUND`. El token de refresco queda cifrado por el SO (alternativa al `FileUserRepository` del paquete, que escribe `firebase.json` en texto plano). La contraseña nunca se almacena.
4. **`Features/Account/AuthService.cs`** (reescrito) — mantiene el singleton `Instance`, `CurrentUser`/`IsLoggedIn` y ahora:
   - `LoginAsync(email, password)` → `FirebaseAuthClient.SignInWithEmailAndPasswordAsync`.
   - `RegisterAsync(email, password, displayName?)` → `CreateUserWithEmailAndPasswordAsync` (nombre opcional).
   - `SignOut()` → `client.SignOut()` (borra el token del Locker).
   - Cliente lazy configurado con `EmailProvider` + `SecureUserRepository`; al crearlo, `FirebaseAuthClient` rehidrata `User` desde el repositorio → **sesión restaurada automáticamente** al iniciar la app.
   - `GetAuthErrorMessage(Exception)` mapea `FirebaseAuthException.Reason` (`EmailExists`, `WeakPassword`, `WrongPassword`, `UnknownEmailAddress`, `UserDisabled`, `TooManyAttemptsTryLater`, etc.) a mensajes amigables en español. Nunca expone `ResponseData` cruda.
5. **`Features/Account/AccountPage.xaml`** — sección "Perfil" rediseñada en dos paneles:
   - `LoggedInPanel` (con sesión): avatar + nombre/correo + botón "Cerrar sesión".
   - `LoggedOutPanel` (sin sesión): formulario con `EmailBox`, `PasswordBox` (reveal Peek), `DisplayNameBox` (solo en registro), botón `AuthSubmitButton` (contenido por `UiHelpers.Content`: `PersonLock` login / `PersonAdd` registro), `ProgressRing`, `AuthToggleButton` (HyperlinkButton login↔registro) y `AuthErrorText` (rojo). La sección "Actualizaciones" no cambió.
6. **`Features/Account/AccountPage.xaml.cs`** — `RefreshProfile()` alterna paneles según `IsLoggedIn`; `AuthSubmitButton_Click` envía login/registro async (valida campos vacíos, bloquea el formulario con `SetAuthBusy` durante la red y muestra error amigable); `AuthField_KeyDown` envía con **Enter**; `LogoutButton_Click` cierra sesión y limpia el formulario.
7. **AGENTS.md** — tabla de funcionalidades, estructura del proyecto (nuevos `FirebaseConfig.cs`/`SecureUserRepository.cs`) y arquitectura (`AccountPage → AuthService (Firebase Email/Password + PasswordVault)`).
8. **`feature_list.json`** — feature 19 renombrada a `firebase_email_password_auth` con acceptance actualizada y `done`.

### Verificación
- Build final Debug|x64: **0 errores, 0 advertencias**.
- JSON de feature_list validado (28 features).
- Sin referencias residuales a `LoginWithGoogle`, `LoginButton` ni `LoginNoteText` (grep limpio).

---

## 2026-08-19 — Features 26 y 29: sugerencias en Firestore + gate de verificación de correo

### Contexto
- El usuario descartó usar `TopRemixServerApiClient` para las sugerencias y pidió **Firestore**: "Utiliza firestore e implementa lo que se necesario para conectarse i guardar las sugerencias".
- Autorización del usuario: implementar el gate de verificación **sin** el texto "Solo se admiten cuentas con correo verificado" (solo implementar).

### Cambios
1. **`FirebaseConfig.cs`** — añadidos `ProjectId = "top-remix-e53ca"` y `SuggestionsCollection = "suggestions"`.
2. **`FirebaseRestApi.cs` (nuevo)** — REST API de Firebase sin paquetes nuevos:
   - `SendVerificationEmailAsync(idToken)` → `accounts:sendOobCode` con `requestType: VERIFY_EMAIL` (el paquete v4 no lo expone).
   - `AddSuggestionAsync(uid, email, message, idToken)` → Firestore `createDocument` en la colección `suggestions` con `Authorization: Bearer <idToken>`, campos `uid/email/message/createdAt` (timestampValue).
   - `FirebaseApiException` + mapeo de errores (`MapErrorCode`/`MapErrorStatus`/`MapHttpStatus`) a mensajes amigables.
3. **`AuthService.cs`** — reescrito con tipos de resultado:
   - `LoginResult` (`RequiresEmailVerification`): login de cuenta sin verificar cierra la sesión y borra el token.
   - `RegisterResult` (`EmailVerificationSent`): crea la cuenta, envía el enlace y NO deja sesión.
   - `SendVerificationEmailAsync(email, password)`: re-sign-in → envío → sign-out.
   - `SubmitSuggestionAsync(message)`: idToken fresco → Firestore.
   - `RestoreSession`: hace sign-out si el usuario restaurado está sin verificar.
   - `GetAuthErrorMessage` también cubre `FirebaseApiException`.
4. **`AppLimits.cs`** — `SuggestionsMaxLength = 1000`, `SuggestionsTitle`, `SuggestionsSubtitle`.
5. **`AccountPage.xaml` / `.cs`** — paneles de verificación (ámbar con "Reenviar enlace de verificación", verde de éxito), tarjeta `SuggestionsSection` (visible solo con sesión): TextBox multiline con contador `n/1000`, botón teal `Icon.Send`, ProgressRing e InfoBar de feedback. Sin textos publicitarios de límite.

### Verificación
- Build final Debug|x64: **0 errores, 0 advertencias**.
- JSON de feature_list validado (**29 features**, 26 y 29 en `done`).
- Queda pendiente del lado del usuario (manual en Firebase Console): crear la base de datos Firestore y las reglas de seguridad (`allow create: if request.auth != null && request.auth.token.email_verified == true;`).

---

## 2026-08-19 — Fix: mensaje "revisa internet" al iniciar sesión + icono de ojo en la contraseña

### Contexto
- El usuario reportó que al iniciar sesión con un correo válido la UI decía "no se puede conectar con el servidor, revise el internet".
- Diagnóstico (read-only):
  1. `curl` al endpoint `signInWithPassword` con la Web API key responde `400 INVALID_LOGIN_CREDENTIALS` en ~0.5s → red y API key correctas.
  2. Google devuelve UN solo código `INVALID_LOGIN_CREDENTIALS` tanto para correo inexistente como para contraseña incorrecta (anti-enumeración de cuentas).
  3. El parser del SDK v4 (`FirebaseFailureParser.GetFailureReason`) NO mapea ese código → `AuthErrorReason.Unknown` → el `switch` de `GetAuthErrorMessage` caía en el default *"No se pudo conectar con el servidor..."*.
- Conclusión: no era un problema de red/configuración; era un mapeo de errores incompleto.

### Cambios
1. **`App.xaml.cs`** — se expone `App.Log(source, message, stackTrace)` (wrapper interno de `WriteCrashLog`) para registrar errores de autenticación en `%LOCALAPPDATA%\Remove_Top\crash.log` desde cualquier módulo.
2. **`AuthService.GetAuthErrorMessage`** — reescrito:
   - `FirebaseAuthHttpException` se procesa ANTES que `FirebaseAuthException` (es su subclase) y se lee `ResponseData` (JSON `error.message`) con `MapHttpErrorMessage`:
     - `INVALID_LOGIN_CREDENTIALS` → "Correo o contraseña incorrectos."
     - `INVALID_EMAIL`, `MISSING_EMAIL`, `MISSING_PASSWORD`, `EMAIL_EXISTS`, `EMAIL_NOT_FOUND`/`USER_NOT_FOUND`, `USER_DISABLED`, `OPERATION_NOT_ALLOWED`, `CREDENTIAL_TOO_OLD_LOGIN_AGAIN`, API key no válida, prefijos `WEAK_PASSWORD` y `TOO_MANY_ATTEMPTS_TRY_LATER`.
   - Cuerpo sin mapear → `App.Log` con Reason/Url/Response para diagnóstico.
   - Sin cuerpo JSON (= fallo de red real) → "No se pudo conectar con el servidor. Revisa tu internet e inténtalo de nuevo."
   - `AuthErrorReason.Undefined` → mensaje de conexión; default → "No se pudo iniciar sesión. Inténtalo de nuevo."
3. **`AccountPage.xaml`** — el campo de contraseña pasa a etiqueta + `Grid` con `PasswordBox` (`PasswordRevealMode="Hidden"`, padding derecho 40) y **botón ojo** a la derecha (FluentIcon `Eye`, tooltip "Mostrar contraseña", transparente).
4. **`AccountPage.xaml.cs`** — `TogglePasswordButton_Click` alterna `Hidden`↔`Visible` e intercambia `Eye`↔`EyeOff` + tooltip; `SetAuthBusy` también deshabilita el botón ojo.

### Verificación
- Build final Debug|x64: **0 errores, 0 advertencias**.
- Recordatorio al usuario: en su caso el error era de credenciales (correo inexistente o contraseña incorrecta); la cuenta debe existir en Firebase Console → Authentication → Users.

---

## 2026-08-19 — Auto-login tras confirmar el correo (sin deep link)

### Contexto
- El usuario pidió auto-login luego de confirmar el correo, SIN deep linking ni esquema custom: flujo estándar de Firebase (el usuario vuelve a la app manualmente tras confirmar en el navegador).

### Cambios
1. **`AuthService.cs`** — el refresh token de cuentas sin verificar ya NO se borra: el login sin verificar, el registro, el reenvío y la restauración lo conservan como "pendiente" (`HasPendingVerification`). Nuevo `CompleteVerificationAsync()`: refresca el idToken con el token guardado (sin contraseña), consulta `getAccountInfo`, y si `emailVerified` es true marca `user.Info.IsEmailVerified = true` (setter público del SDK), la persiste (GetIdTokenAsync → UserManager) y abre la sesión.
2. **`FirebaseRestApi.cs`** — nuevo `IsEmailVerifiedAsync(idToken)` (REST `accounts:getAccountInfo`, nunca lanza).
3. **`AccountPage.xaml/.cs`** — polling de auto-login con `DispatcherTimer` de 5 s (tope ~10 min): se inicia al mostrar el panel de verificación (login sin verificar), tras el registro y tras reenviar el enlace, y al cargar la página si hay verificación pendiente; se detiene al loguearse, al hacer logout o al salir de la página. Al detectar la confirmación, refresca la UI (perfil + sugerencias). Textos actualizados ("entraremos a tu cuenta automáticamente").

### Verificación
- Build final Debug|x64: **0 errores, 0 advertencias**.
- JSON de feature_list validado (29 features; feature 29 actualizada con auto-login, `done`).

---

## 2026-08-20 — Auto-creación de cuenta desde el formulario de login (primer uso)

### Contexto
- El usuario reportó que al ingresar sus credenciales por primera vez (login) la app decía "Correo o contraseña incorrectos." en vez de crear la cuenta: Google devuelve `INVALID_LOGIN_CREDENTIALS` para cuentas inexistentes y el formulario de login solo intentaba iniciar sesión.

### Cambios
1. **`AuthService.cs`** — nuevo `LoginOrRegisterAsync(email, password, displayName)`: intenta login; si falla con `INVALID_LOGIN_CREDENTIALS`, crea la cuenta automáticamente (`RegisterAsync`) y devuelve `LoginResult.AccountCreated = true` + `EmailVerificationSent`. Si el registro falla con `EMAIL_EXISTS`, la cuenta ya existía → la contraseña era incorrecta → relanza el error original. Otros errores (red, etc.) se propagan sin crear nada. Nuevos helpers `IsUnknownCredentials`/`IsEmailExists`/`GetErrorCode` (refactor: `MapHttpErrorMessage` reutiliza el parseo de `error.message`).
2. **`LoginResult`** — nuevas propiedades `AccountCreated` y `EmailVerificationSent`.
3. **`AccountPage.xaml.cs`** — el modo login llama a `LoginOrRegisterAsync`; en `AccountCreated` muestra el panel verde "Correo de verificación enviado" y arranca el polling de auto-login.

### Verificación
- Build Debug|x64: **0 errores, 0 advertencias**.

---

## 2026-08-20 — Velopack: auto-update integrado con GitHub Releases

**Agente:** humano + opencode (mimo-v2.5-free)

### Cambios realizados
Integración completa de Velopack para auto-updates desde GitHub Releases.

1. **`Remove_Top.csproj`** — nuevo `PackageReference` Velopack v0.0.1298.
2. **`App.xaml.cs`** — `VelopackApp.Build().SetAutoApplyOnStartup(true).Run()` en `OnLaunched` antes de crear MainWindow. Aplica actualizaciones pendientes al reiniciar.
3. **`UpdateChecker.cs`** — reescrito completamente: elimina modo simulado, usa `UpdateManager(GitHubRepoUrl)` para `CheckForUpdatesAsync`, `DownloadUpdatesAsync` (con progreso) y `ApplyUpdatesAndRestart`. Version leída del assembly.
4. **`AccountPage.xaml`** — nuevo `DownloadUpdateButton` (oculto por defecto, aparece si hay update), `DownloadProgressRing` con progreso, texto de nota actualizado.
5. **`AccountPage.xaml.cs`** — `DownloadUpdateButton_Click` con descarga en background + progreso + ApplyUpdate al terminar.

### Workflow de branches
- Creada rama `staging` desde `feat/app-improvements` y push a origin.
- Workflow: feature branches → staging → main (producción Velopack).

### Verificación
- Build Debug|x64: **0 errores, 0 advertencias**.

---

## 2026-08-20 — Renombrado a "One Dj App" + iconos personalizados

**Agente:** humano + opencode (mimo-v2.5-free)

### Cambios realizados
Renombrado completo de "Top Dj App" → "One Dj App" y configuración de iconos reales desde `iconos/`.

1. **`iconos/` → `Assets/`** — copiados 6 archivos: `AppIcon.ico`, `SplashScreen` (512x512), `StoreLogo` (256), `Square44x44` (48), `Square150x150` (32), `LockScreenLogo` (16).
2. **`Remove_Top.csproj`** — `AssemblyName = OneDjApp` (antes TopDjApp) + `ApplicationIcon = Assets\AppIcon.ico`.
3. **`AppLimits.cs`** — `AppName = "One Dj App"`, texto de sugerencias actualizado.
4. **`MainWindow.xaml`** — `Title="One Dj App"`.
5. **`MainWindow.xaml.cs`** — `Win32Helper.SetWindowIcon(this)` para icono de ventana WinUI 3 unpackaged.
6. **`Helpers/Win32Helper.cs`** — nuevo: P/Invoke `LoadImageW` + `SendMessageW(WM_SETICON)` para icono de barra de título y taskbar.
7. **`AccountPage.xaml`** — texto hardcoded "Top Dj App" → "One Dj App".
8. **`AccountPage.xaml.cs`** — "Usuario Top Dj App" → "Usuario One Dj App".

### Verificación
- Build Debug|x64: **0 errores, 0 advertencias**. Salida: `OneDjApp.exe`.

---

## 2026-08-20 — DuplicateRemoval: botón "Cancelar" → "Limpiar" con icono Broom

**Agente:** humano + opencode (mimo-v2.5-free)

### Cambios realizados
El botón "Cancelar" (columna 3 de la fila de acciones tras el escaneo) se reemplaza por **"Limpiar"** con icono `Icon.Broom` (escoba), alineado con el patrón de "Limpiar" del resto de features.

1. **`DuplicateRemovalPage.xaml`** — `CancelButton` → `CleanButton` (x:Name, Click).
2. **`DuplicateRemovalPage.xaml.cs`** — constructor: `Icon.Dismiss, "Cancelar"` → `Icon.Broom, "Limpiar"`. Referencias `CancelButton` → `CleanButton` en UpdateUI y ClickHandler. Comentarios actualizados.

### Verificación
- Build Debug|x64: **0 errores, 0 advertencias**.

---

## 2026-08-20 — AccountPage: icono One Dj en círculo blanco + regla de no auto-push

**Agente:** humano + opencode (mimo-v2.5-free)

### Cambios realizados
1. **`AccountPage.xaml`** — `assets:BrandLogo` (cuadrado azul) reemplazado por un `Border` blanco circular (`CornerRadius="60"`) con `Image` del icono One Dj (`Assets/StoreLogo.png`, 80x80 centrado).
2. **`AGENTS.md`** — agregada regla explícita: **NO hacer git push a menos que el usuario lo solicite directamente**.

### Regla de repositorio
> **IMPORTANTE:** NO actualices el repositorio (git push) a menos que el usuario te lo solicite explícitamente. Solo haz commit y push cuando el usuario lo pida directamente.

### Verificación
- Build Debug|x64: **0 errores, 0 advertencias**.