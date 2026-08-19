# Remove-Top — Top Dj App

> **Nota para agentes:** Antes de comenzar cualquier tarea, lee los archivos en la carpeta `progress/`:
> - `progress/history.md` — bitácora de sesiones anteriores
> - `progress/feature_list.json` — lista de features completadas y pendientes
>
> Esto te dará contexto completo del estado del proyecto.

## Descripción

Aplicación WinUI 3 (Windows App SDK) para procesamiento de audio.

### Funcionalidades

| Módulo | Descripción |
|--------|-------------|
| **Normalización** (`NormalizationPage`) | Ajusta el nivel de pico de archivos de audio a un dBFS objetivo usando NAudio y exporta a WAV en subcarpeta `RemoveTop_Normalized`. Sobre el audio ya normalizado aplica una **masterización con 3 perfiles de intensidad** (selector en la página): `Ligera` (cadena original, conserva dinámica), `Hard Limiter` (limitador duro con lookahead tipo Adobe Audition: input boost + techo −0.3 dB, sube el RMS a ~−12 dB) y `Comercial EDM` (mayor boost, densidad de master comercial, RMS ~−9/−11 dB). Los 3 comparten el prefijo de ecualización (paso alto → EQ) y el techo final de −0.3 dB sin saturar. Al terminar cada resultado muestra **Pico y RMS reales** medidos en el archivo de salida. Al finalizar, corrige la ortografía de los nombres de salida (tildes) con un diccionario local (`SpanishNameCorrector`). Límite gratuito PUBLICADO de **50 archivos** (`AppLimits.NormalizationFreeLimitDisplay`, solo texto); el límite REAL de escaneo es de **1.000 archivos** (`AppLimits.NormalizationMaxFilesToScan`). El texto del InfoBar se monta en runtime desde `AppLimits`. Omite los que ya tienen una salida procesada válida (firma RIFF/WAVE) — reconoce tanto el nombre original como el corregido. Muestra un loader (`ProgressRing`) durante el procesamiento y, al terminar, el estado "Completado" con icono de estado (check verde si todo salió bien, advertencia ámbar si hubo errores). Si todo terminó correctamente, aparece un botón "Limpiar" (centrado) que borra los resultados y resetea la página. |
| **Renombrado Masivo** (`BatchRenamePage`) | Elimina texto específico de los nombres de archivos en una carpeta (audio, video, imagen, documentos). Opera directamente sobre los archivos originales. El escaneo es **recursivo (incluye subcarpetas)** con **máximo 1.000 archivos por ejecución** (`AppLimits.BatchRenameMaxFilesToScan`). Persiste hasta **20 patrones** (`AppLimits.BatchRenameMaxPatterns`) en `%LOCALAPPDATA%\Remove_Top\patterns.json`. Etiqueta "Versión Gratuita" (badge verde) junto a los mensajes de límite (patrones y archivos, generados desde `AppLimits`). Si el escaneo se truncó y el renombrado terminó, aparece la **tarjeta premium** ("Adquiere la versión premium", mismo patrón que DuplicateRemoval). Botón **"Limpiar"** al final: resetea ruta, resultados, vista previa, progreso y sugerencias IA **conservando los patrones**. Botón **"Cancelar"** centrado debajo del botón principal para resetear la página en cualquier momento. |
| **Edición Rápida** (`QuickRenamePage`) | Lista los `.mp3`/`.wav` de la carpeta principal y permite editar cada nombre en una caja de texto inline (nombre completo, incluida la extensión). Aplica los cambios con `File.Move` directamente sobre los originales. Tope de **200 archivos** (`AppLimits.QuickRenameMaxFilesToScan`, solo los primeros N). Badge **"Versión Gratuita"** + mensaje de límite junto a la carpeta de origen (generado desde `AppLimits.QuickRenameLimitMessage`). Marca `www.top-remix.com` centrado en la línea de "Nombres editables". Botón **"Limpiar"** al final (resetea ruta, lista y resultado). Al terminar muestra una etiqueta con cuántos archivos se renombraron y recarga la lista con los nombres nuevos. Sin barra de progreso ni lista de resultados. |
| **Extracción de Stems** (`VocalRemovalPage`) | Separa la voz del instrumental usando IA (modelo HT-Demucs FT en ONNX). Exporta vocal mono en subcarpeta `RemoveTop_Vocals`. Máximo **5 canciones** estéreo por lote (`AppLimits.VocalRemovalMaxFilesPerBatch`). |
| **Eliminación de Duplicados** (`DuplicateRemovalPage`) | Escanea una carpeta (recursivo, incluye subcarpetas, máx. **1.000 archivos** `AppLimits.DuplicatesMaxFilesToScan`). Pipeline de detección por prioridad: **nombre normalizado → nombre contenido (subconjunto) → hash → palabra clave**. La MISMA CANCIÓN por nombre normalizado (`SameName`) se clasifica como **exacta** y se marca por defecto; también se detectan nombres que difieren en **una sola letra** (falta ortográfica). El detector de **nombre contenido** (`SubsetNameDetector`) agrupa archivos donde todas las palabras del **título** (último bloque) del nombre más corto aparecen en el título del más largo (máx. 3 palabras de diferencia; tope de 6 miembros por cluster). Fix: `StripAllExtensions` elimina extensiones múltiples conocidas (`.mp3.vdjstems` → `.mp3`). Exactos por hash SHA-256 solo sobre los no reclamados por nombre con tamaño repetido (en paralelo). Los "posibles" por palabra clave se verifican por duración de audio. Eliminación con dos opciones: Papelera de Windows (recuperable) o borrado definitivo, ambas con confirmación. Detecta además archivos < **6 KB** (`AppLimits.DuplicatesMinValidFileSizeBytes`) como "dañados" en una 3.ª pestaña. **Previsualizador unificado**: botón en cada fila de las pestañas Exactos/Posibles (módulos `Features/AudioPreview/` y `Features/ImagePreview/`, no disponible en dañados) con icono según tipo (`Play` para audio, `Image` para imágenes) que muestra una **tarjeta con forma de onda + scrub** y transporte **Play/Pausa/Stop** para el audio, o la **imagen ajustada al espacio** (Stretch Uniform, sin zoom) en la tarjeta de imágenes. Un solo preview activo a la vez (abrir uno cierra el otro). Icono check verde cuando no hay duplicados. Botones de acciones centrados. Botón **"Limpiar"** al final de los resultados de eliminación (resetea ruta + resultados). |
| **Cuenta** (`AccountPage`) | Centro de perfil y actualizaciones. La página muestra: logo profesional vectorial (`Assets/BrandLogo.xaml`, gradiente + nota + forma de onda), sección **Perfil** (login con Google solo interfaz — `AuthService` stub; OAuth real documentado y pendiente de Client ID) y sección **Actualizaciones** (`UpdateChecker` en **modo simulado** `IsSimulated=true`; badge que se ilumina verde/ámbar; implementación real documentada en comentario — el repo GitHub es privado, fuente real pendiente). Ítem de menú "Cuenta" con icono `Person` y color teal `#00A88F`. Textos del encabezado centralizados en `AppLimits` (`AccountPageTitle/Subtitle`). |

## Stack Tecnológico

- **Framework:** .NET 8.0 + Windows App SDK 2.2.0
- **UI:** WinUI 3 (XAML)
- **Audio:** NAudio 2.3.0
- **IA:** Microsoft.ML.OnnxRuntime 1.21.0 (HT-Demucs FT)
- **Target:** Windows 10 build 19041+ (unpackaged)
- **Runtime:** Windows App SDK Runtime 1.6+

## Estructura del Proyecto

```
Remove_Top/
├── AGENTS.md
└── Remove_Top/
    ├── Remove_Top.slnx                  # Solución VS 2022 17.12+
    └── Remove_Top/
        ├── Remove_Top.csproj            # Configuración del proyecto WinUI 3
        ├── app.manifest                 # Manifiesto de aplicación (DPI awareness)
        ├── Package.appxmanifest         # Manifiesto MSIX (no usado en unpackaged)
        ├── App.xaml / App.xaml.cs       # Entry point + manejador global de excepciones + singleton VocalSeparator
        ├── MainWindow.xaml / .cs        # Ventana principal con NavigationView + caché de páginas
        ├── Features/                    # Cada feature agrupa su página y su lógica de negocio
        │   ├── Normalization/
        │   │   ├── NormalizationPage.xaml / .cs   # Normalización de audio (UI + ViewModel inline)
        │   │   ├── AudioNormalizer.cs             # Servicio de normalización con NAudio (MaxFilesToScan, FreeLimitDisplay)
        │   │   ├── SpanishNameCorrector.cs        # Corrección ortográfica de nombres (diccionario local de tildes)
        │   │   ├── MasteringDsp.cs                # DSP managed (BiQuad, compresor, limitador)
        │   │   └── MasteringChain.cs              # Cadena de masterización ligera (settings + build)
        │   ├── BatchRename/
        │   │   ├── BatchRenamePage.xaml / .cs     # Renombrado masivo (patrones, botón "Limpiar")
        │   │   ├── FileRenamer.cs                 # Servicio de renombrado en lote
        │   │   ├── PatternSuggestion.cs           # Interfaz IPatternSuggestionProvider + PatternSuggestion
        │   │   └── GroqPatternSuggester.cs        # Proveedor real de sugerencias (servidor Topremix)
        │   ├── QuickRename/
        │   │   ├── QuickRenamePage.xaml / .cs     # Edición rápida de nombres (.mp3/.wav)
        │   │   └── QuickRenamer.cs                # Servicio de edición rápida de nombres
        │   ├── VocalRemoval/
        │   │   ├── VocalRemovalPage.xaml / .cs    # Extracción de stems con IA
        │   │   ├── VocalSeparator.cs              # Separación de voz con modelo ONNX
        │   │   └── ModelDownloader.cs             # Descarga del modelo HT-Demucs desde HuggingFace
        │   ├── Account/
        │   │   ├── AccountPage.xaml / .cs         # Cuenta: perfil y actualizaciones
        │   │   ├── UpdateChecker.cs               # Verificador de actualizaciones (SIMULADO, real comentado)
        │   │   └── AuthService.cs                 # Stub de login con Google (OAuth real pendiente de Client ID)
        │   ├── AudioPreview/                      # Previsualizador de audio (reutilizable, sin dependencias extra)
        │   │   ├── AudioPreviewPlayer.cs          # Motor de reproducción NAudio (WaveOutEvent + MediaFoundationResampler)
        │   │   ├── WaveformPeaks.cs               # Extracción de picos min/max por columna (forma de onda)
        │   │   └── WaveformView.xaml / .cs        # Control de onda con playhead y scrub (Path/Line, sin Win2D)
        │   ├── ImagePreview/                      # Previsualizador de imágenes (reutilizable, nativo WinUI 3)
        │   │   ├── ImagePreviewSupport.cs         # IsImageFile + CreateSource (BitmapImage raster / SvgImageSource)
        │   │   └── ImagePreviewView.xaml / .cs    # Visor simple: imagen ajustada (Stretch Uniform), estados y eventos
        │   └── DuplicateRemoval/
        │       ├── DuplicateRemovalPage.xaml / .cs  # Eliminación de duplicados (UI + ViewModel inline, "Limpiar")
        │       ├── DuplicateScanner.cs              # Servicio: enumera y agrupa (nombre → hash → keyword) + verifica
        │       ├── DuplicateRemover.cs              # Servicio: envía confirmados a la Papelera / borrado definitivo
        │       ├── DuplicateGroup.cs                # Grupo de duplicados (keeper + duplicados)
        │       ├── DuplicateItem.cs                 # Ítem de duplicado (marcado, detalle de coincidencia)
        │       ├── DuplicateMatchKind.cs            # Tipos: Exact / SameName / ProbableByName / ProbableByKeyword / Damaged
        │       ├── DuplicateScanResult.cs           # Resultado del escaneo (exactos / posibles / dañados)
        │       ├── ScanProgress.cs                  # Progreso por fases del escaneo
        │       └── Detection/
        │           ├── IDuplicateDetector.cs        # Interfaz de detector
        │           ├── NormalizedNameDetector.cs    # Misma canción por nombre (exacto + difusa "1 letra")
        │           ├── ExactHashDetector.cs         # Exactos por hash SHA-256
        │           ├── KeywordDetector.cs           # Posibles por palabras clave del título
        │           ├── GroupBuilder.cs              # Construye grupos (keeper, marcado por tipo)
        │           ├── DurationVerifier.cs          # Verificación por duración de audio (NAudio)
        │           ├── NameNormalizer.cs            # Normalización + palabras (significativas / título / todas)
        │           ├── SubsetNameDetector.cs        # Nombre contenido: subconjunto de palabras
        │           ├── FileRecord.cs                # Registro con tamaño/hash/nombre/palabras precalculados
        │           └── DamagedFileDetector.cs       # Archivos < 6 KB ("dañados")
        ├── Helpers/
        │   ├── AppLimits.cs              # LÍMITES centralizados de la versión gratuita (cambiar aquí)
        │   ├── PremiumLinks.cs           # Enlace premium centralizado (UpgradeUrl, cambiar aquí)
        │   ├── UiHelpers.cs              # Iconos/contenido de botones con FluentIcons
        │   ├── FileTypeIconConverter.cs  # Icono según tipo de archivo (audio/video/imagen/documento)
        │   ├── RecycleBinHelper.cs       # Envía archivos a la Papelera de Windows (SHFileOperationW)
        │   └── TopRemixServerApiClient.cs  # Cliente HTTP compartido del servidor Topremix (endpoint, modelo, parseo)
        ├── Assets/                      # Iconos y recursos visuales (BrandLogo.xaml = logo vectorial)
        └── Properties/
            ├── launchSettings.json      # Perfiles de ejecución (Package/Unpackaged)
            └── PublishProfiles/         # Perfiles de publicación
```

## Arquitectura

```
App.xaml.cs (Application)
  └── MainWindow (NavigationView)
        ├── NormalizationPage → AudioNormalizer (NAudio) + SpanishNameCorrector
        ├── BatchRenamePage   → FileRenamer
        ├── QuickRenamePage   → QuickRenamer
        ├── VocalRemovalPage  → VocalSeparator (ONNX) + ModelDownloader
        ├── DuplicateRemovalPage → DuplicateScanner + DuplicateRemover + RecycleBinHelper + AudioPreview (AudioPreviewPlayer + WaveformView)
        └── AccountPage → UpdateChecker (simulado) + AuthService (stub)
```

- **Features/<Feature>/:** Cada feature es un módulo autocontenido que agrupa su página (con ViewModel inline en el code-behind) y su lógica de negocio. Los servicios se comunican con la UI via `IProgress<T>` y `CancellationToken`.
- **Helpers/:** Utilidades compartidas entre features (iconos Fluent y conversores).
- **App.xaml.cs:** Manejador global de excepciones escribe en `%LOCALAPPDATA%\Remove_Top\crash.log`. Expone el singleton estático `App.VocalSeparator` que mantiene el modelo ONNX cargado entre navegaciones.
- **MainWindow.xaml.cs:** Mantiene una caché `Dictionary<Type, Page>` para reutilizar las páginas al navegar.

## Detección de duplicados (detalle)

Pipeline de `DuplicateScanner.ScanAsync` por prioridad (optimizado para bibliotecas musicales):

1. **Misma canción por nombre normalizado (`SameName`)** → pestaña "Exacto", marcada por defecto. La normalización ignora mayúsculas, acentos, guiones, espacios y guiones iniciales.
2. **Nombre contenido (`SubsetMatch`)** → pestaña "Exacto", marcado por defecto. Detecta archivos donde todas las palabras del nombre más corto aparecen en el más largo (máx. 3 palabras de diferencia). Clustering transitivo con union-find.
3. **Exactos por hash SHA-256** → solo sobre archivos NO reclamados por nombre y con tamaño repetido (un tamaño único no puede tener duplicado idéntico); en paralelo.
4. **Posibles por palabras clave (`ProbableByKeyword`)** → entre lo restante; se verifican por duración y los falsos positivos se descartan.

### Coincidencia difusa "1 letra de diferencia" (`NormalizedNameDetector`)

Además del nombre exacto, detecta nombres "casi idénticos" (falta ortográfica):

- `NearNameMatches`: mismo número de palabras, **exactamente una** palabra distinta en la misma posición, con **≥ 5 letras** (`MinFuzzyWordLength`) y a una única edición de letra (sustitución / inserción / eliminación).
- Se excluyen diferencias de **dígitos** ("mosaico 1" vs "mosaico 2" nunca coinciden).
- Guarda `MinFuzzyNameLength = 6` (longitud del nombre normalizado).
- Clustering **transitivo con union-find**; el grupo se clasifica `SameName` con `NameNearMatch = true` (detalle en UI: "mismo nombre · 1 letra distinta").

### Nombre contenido (`SubsetNameDetector`)

Detecta archivos donde el título (último bloque) del nombre más corto es subconjunto de palabras del título del más largo. **El artista NO participa** (bloque previo al último guion), para que un archivo con solo el artista no actúe como "hub" que agrupa transitivamente TODAS las canciones del mismo intérprete (fix del `×N` inflado).

- Requisitos: diferencia de 1 a 3 palabras del título, todas las palabras del título más corto en el más largo, nombre normalizado del más corto contenido en el más largo (el artista sí participa aquí como salvaguarda).
- Clustering transitivo con union-find (A ⊂ B y B ⊂ C → {A, B, C}) con **tope de 6 miembros** (`MaxGroupSize`): los clusters mayores se descartan (cadena de falsos positivos).
- Se clasifica `SubsetMatch` con `NameNearMatch = true`.
- Detalle en UI: "nombre contenido · misma duración" o "nombre contenido · mismo tamaño".

### Fix: extensiones múltiples (`NameNormalizer.StripAllExtensions`)

Elimina TODAS las extensiones conocidas del final del nombre (no solo la última):

- `song.mp3.vdjstems` → `song` (antes quedaba `song.mp3`)
- `track.flac.zip` → `track`
- Set de ~30 extensiones: audio (.mp3, .wav, .flac...), DJ (.vdjstems, .stems...), contenedores (.mp4, .zip...)

### Verificación por duración (`DurationVerifier`)

- `SameName`: salvaguarda — si la duración difiere > **2×** (`SameNameMaxDurationRatio`) el ítem se desmarca (posible título idéntico de otra canción).
- `SubsetMatch` ("nombre contenido"): la coincidencia de palabras NO basta. El duplicado solo se confirma si comparte **tamaño exacto** (`SameSize`) o si la **duración es prácticamente igual** (tolerancia estricta `SubsetMatchDurationTolerance = 0.10`). Si ninguna se cumple (p. ej. 4:10 vs 3:31 = 16%) son canciones distintas que comparten palabras y el miembro se **elimina del grupo** (falso positivo).
- `ProbableByKeyword`: si las duraciones no coinciden (tolerancia `DurationTolerance = 0.30`) el miembro se elimina del grupo (falso positivo).

### Marcado por defecto (`GroupBuilder`)

- `Exact`, `SameName` y `SubsetMatch` → siempre marcados.
- `ProbableByName` (legacy) → marcado si comparte tamaño.
- Keeper: ruta más superficial y, en empate, más corta (`keepLargest: false`).

## Renombrado masivo (detalle)

- Máximo **20 patrones** (`AppLimits.BatchRenameMaxPatterns`), persistidos en `%LOCALAPPDATA%\Remove_Top\patterns.json`.
- Etiqueta **"Versión Gratuita"** (badge #70AD47) junto al mensaje "Máximo 20 patrones. La búsqueda no distingue mayúsculas/minúsculas." (texto generado desde `AppLimits.BatchRenameLimitMessage`).
- **Máximo 1.000 archivos por ejecución** (`AppLimits.BatchRenameMaxFilesToScan` → `FileRenamer.MaxFilesToScan`). El escaneo es **recursivo e incluye subcarpetas** (`SearchOption.AllDirectories`). Aviso de límite junto al badge (generado desde `AppLimits.BatchRenameFilesLimitMessage`).
- **Tarjeta premium** (`PremiumSection`): si el escaneo se truncó (la carpeta tenía más de 1.000 archivos afectados) y el renombrado terminó, aparece el botón **"Adquiere la versión premium"** (mismo patrón que DuplicateRemoval; enlace en `PremiumLinks.UpgradeUrl`).
- Botón **"Limpiar"** (`RestartButton`) al final de los resultados: resetea ruta, resultados, vista previa, progreso, badge y sugerencias IA, pero **CONSERVA los patrones**.
- Botón **"Cancelar"** (`CancelButton`) centrado debajo del botón principal: resetea la página en cualquier momento (conserva patrones). Se oculta tras mostrar resultados.

## Previsualizador de audio (detalle)

Módulo reutilizable `Features/AudioPreview/` (sin dependencias nuevas; usa la NAudio ya referenciada) integrado en la pestaña de Duplicados para **verificar por audio** antes de borrar.

- **`AudioPreviewPlayer`** — motor de reproducción: `MediaFoundationReader` lee los formatos ya soportados y `MediaFoundationResampler` convierte a 44,1 kHz/16-bit/estéreo PCM (formato que `WaveOutEvent` acepta siempre). Expone `Play/Pause/Stop/Seek/SeekToFraction`, `Position`, `Duration`, estado y el evento `PlaybackEnded`. `Close()` libera el archivo (imprescindible para poder borrar el que estaba sonando).
- **`WaveformPeaks`** — extrae en segundo plano los picos min/max por columna (mezcla a mono) desde `MediaFoundationReader.ToSampleProvider()`.
- **`WaveformView`** — control visual sin Win2D: la onda es una `Path` (un segmento vertical por columna), el playhead una `Line`, y el tramo futuro se atenúa con un `Border` translúcido anclado a la derecha. Soporta **scrub** (arrastrar para adelantar/atrasar): durante el arrastre mueve el playhead y al soltar dispara `SeekRequested(fracción)`.
- **Integración**: botón play (icono `Play`, tooltip "Previsualizar") en cada fila de audio de las pestañas **Exactos/Posibles**, visible solo si `DuplicateItem.IsAudio` (extensión de audio). La pestaña **Dañados no ofrece preview**. La tarjeta `PreviewSection` (entre Resultados y Acciones) muestra nombre, onda de 88 px, Play/Pausa, Stop y reloj `pos / dur`.
- **Ciclo de vida**: un `DispatcherTimer` de 100 ms actualiza playhead y reloj. La reproducción se detiene y el archivo se libera al: cambiar de carpeta (`ResetResults`), eliminar (antes de borrar), cerrar la tarjeta, salir de la página (`Unloaded`) o cambiar de archivo.

## Previsualizador de imágenes (detalle)

Módulo reutilizable `Features/ImagePreview/` (solo API nativa de WinUI 3, sin dependencias nuevas) que complementa al de audio: el botón de preview de las filas ahora es **unificado** — icono `Play` para audio, `Image` para imágenes (`DuplicateItem.IsAudio` / `IsImage` / `IsPreviewable` / `PreviewIcon`).

- **`ImagePreviewSupport`** — `IsImageFile(path)` (extensiones `.jpg/.jpeg/.png/.gif/.bmp/.tiff/.tif/.webp/.ico/.jfif/.svg`, alineadas con `FileTypeIconConverter`) y `CreateSource(path)` → `BitmapImage` (raster, con `DecodePixelWidth = 1600` para limitar memoria) o `SvgImageSource` (SVG).
- **`ImagePreviewView`** — visor simple: imagen con `Stretch="Uniform"` (se ajusta al espacio, **sin zoom/pan**). Estados **vacío / cargando / error** y la imagen. `Load(path)`, `Clear()` (libera la fuente), `CurrentPath` y eventos `ImageLoaded(int w, int h)` / `ImageLoadFailed`. En SVG las dimensiones no se exponen y se notifica 0×0 (pie: "Vectorial (SVG)").
- **Integración**: tarjeta `ImagePreviewSection` (entre la tarjeta de audio y las acciones) con header (icono `Image` + nombre + cerrar), el visor de 300 px de alto y un pie con **dimensiones reales + tamaño en disco**.
- **Un solo preview activo**: `StopAllPreviews()` (cierra audio con `StopPreviewCore(closeFile: true)` y libera la imagen con `ClearImagePreview()`) se invoca al cambiar de preview, de carpeta (`ResetResults`), antes de borrar (`RunDeletionAsync`) y al salir de la página (`Unloaded`). `ClearImagePreview()` pone `Source = null` para liberar memoria y el posible bloqueo del archivo antes de resetear/borrar.
- **Pestaña Dañados**: sin botón de preview (sin cambios en su template).

## Normalización (límite gratuito)

- `AppLimits.NormalizationMaxFilesToScan = 1000` → límite REAL de archivos analizados.
- `AppLimits.NormalizationFreeLimitDisplay = 50` → límite PUBLICADO en la UI (solo texto de marketing; el escaneo real sigue el límite real).
- El `InfoBar` de la página se construye en runtime (`NormalizationPage` constructor) usando `AppLimits.NormalizationInfoBarTitle/Message`.

## Normalización (masterización por intensidad)

Selector `IntensityComboBox` en `NormalizationPage` con 3 perfiles (`MasteringIntensity` en `MasteringChain.cs`), por defecto **Hard Limiter**:

1. **Ligera** — cadena original: paso alto → EQ → compresor (−15 dB, 2:1) → limitador de pico clásico (−0.3 dB, +2 dB). Conserva la dinámica (RMS ~ −16 dB).
2. **Hard Limiter** — paso alto → EQ → compresor (−14 dB, 2:1) → **`HardLimiterSampleProvider`** (lookahead 5 ms, input boost +5 dB, techo −0.3 dB, release 100 ms, estéreo-enlazado). Rellena la onda: RMS ~ −12 dB sin saturar (parámetros equivalentes al Hard Limiter de Adobe Audition).
3. **Comercial EDM** — paso alto → EQ → compresor (−20 dB, 3:1) → **`HardLimiterSampleProvider`** (lookahead 5 ms, input boost +9 dB, techo −0.3 dB). Densidad de master comercial: RMS ~ −9/−11 dB.

- `HardLimiterSampleProvider` (en `MasteringDsp.cs`) usa un anillo de retardo de lookahead para anticipar los picos: preamplifica (input boost) y aplica reducción de ganancia estéreo-enlazada con ataque instantáneo y release suave; el techo nunca se supera.
- Los 3 perfiles comparten el prefijo de ecualización (`MasteringChain.BuildEqPrefix`) y el techo final `MasteringChain.LimiterCeilingDb = -0.3`.
- `AudioNormalizer.NormalizeFile` mide el **pico y RMS reales** del archivo de salida y los muestra en el mensaje de cada resultado (p. ej. `Pico −0.3 dB · RMS −9.5 dB`), para verificar la mejora sin abrir un DAW.

## Límites de la versión gratuita (componente central)

**Todos los límites de las funcionalidades se definen en `Helpers/AppLimits.cs`** y se cambian manualmente ahí:

| Funcionalidad | Constante | Valor |
|---------------|-----------|-------|
| Normalización | `NormalizationFreeLimitDisplay` (publicado) · `NormalizationMaxFilesToScan` (real) | 50 · 1.000 |
| Renombrado masivo | `BatchRenameMaxPatterns` | 20 |
| Stems | `VocalRemovalMaxFilesPerBatch` | 5 |
| Duplicados | `DuplicatesMaxFilesToScan` · `DuplicatesMaxDeletionsPerRun` · `DuplicatesMinValidFileSizeBytes` | 1.000 · 1.000 · 6 KB |

Las funcionalidades **consumen la lógica y los textos de UI desde `AppLimits`**: los servicios los usan en sus `Take(n)`/topes reales y las páginas montan los InfoBars, contadores y descripciones en runtime desde las propiedades de texto (`AppLimits.NormalizationInfoBar*`, `DuplicatesInfoBar*`, `BatchRenameLimitMessage`, `VocalRemovalPageDescription`). Así los textos nunca se desincronizan de los límites reales. Para cambiar un límite, editar el valor aquí y recompilar.

Además de los límites, `AppLimits` centraliza los **textos del encabezado de cada página** (título, subtítulo) y el badge **"Versión Gratuita"** (`AppLimits.FreeBadgeText`): `NormalizationPageTitle/Subtitle`, `BatchRenamePageTitle/Subtitle`, `QuickRenamePageTitle/Subtitle`, `VocalRemovalPageTitle/Subtitle`, `DuplicatesPageTitle/Subtitle`, `AccountPageTitle/Subtitle`. Cada página los monta en su constructor desde estas propiedades, por lo que todos los textos de las funcionalidades se cambian en un solo lugar.

### Identidad de la aplicación (branding)

También en `AppLimits` se centraliza la identidad de la app, para cambiar el nombre y textos de marca en un solo lugar:

| Constante | Valor | Dónde se usa |
|-----------|-------|--------------|
| `AppName` | `Top Dj App` | Título de ventana, nombre del menú (`BrandNameText`), badges de marca de las 5 páginas (`BrandText`) |
| `AppSubtitle` | `Mejorador de Audio` | Subtítulo del menú (`BrandSubtitleText`) |
| `AppBrandSite` | `www.top-remix.com` | `SiteBrandText` (QuickRename) y `BrandSiteRun` (DuplicateRemoval). **No cambiar el dominio** |
| `AppDataFolderName` | `Remove_Top` | Carpeta de datos en `%LOCALAPPDATA%` (crash.log, patterns.json, models). Conservar para no perder datos |

El ejecutable se genera como `TopDjApp.exe` (AssemblyName en el csproj); el `RootNamespace` sigue siendo `Remove_Top`.

## Sugerencia de patrones con IA (BatchRename)

- `BatchRenamePage` usa `IPatternSuggestionProvider` (interfaz en `Features/BatchRename/PatternSuggestion.cs`): dado los patrones actuales + los primeros 10 nombres de archivos afectados, sugiere NUEVOS patrones a eliminar.
- **`GroqPatternSuggester`**: envía `{ patrones, archivos }` (solo los primeros 10 nombres base sin extensión de los archivos afectados) y pide hasta 10 patrones nuevos. La API key se configura en el cliente compartido.
- El proveedor usa el cliente HTTP `Helpers/TopRemixServerApiClient.cs` (endpoint/modelo/apiKey/configuración de conexión ahí).
- El flujo: la página envía patrones + primeros 10 nombres → el proveedor devuelve `PatternSuggestion` → el usuario aprueba con CheckBox → "Agregar aprobados" los incorpora a los patrones (persistiendo y recalculando la vista previa).

## Cómo ejecutar

### Requisitos

- Visual Studio 2022 **17.12+** (para formato `.slnx`)
- .NET 8 SDK
- Windows App SDK Runtime (instalar con `winget install "Windows App SDK Runtime"`)
- Windows 10 build 19041+ (recomendado Windows 11)

### Desde Visual Studio

1. Abrir `Remove_Top.slnx`
2. Seleccionar plataforma **x64** en el combo "Solution Platform"
3. Seleccionar perfil **"Remove_Top (Unpackaged)"** en el menú de depuración
4. **F5** para compilar y ejecutar

### Desde terminal

```bash
cd Remove_Top\Remove_Top
dotnet build -c Debug -p:Platform=x64
bin\x64\Debug\net8.0-windows10.0.19041.0\win-x64\TopDjApp.exe
```

## Solución de problemas

### La ventana se abre y cierra inmediatamente

1. Revisar `%LOCALAPPDATA%\Remove_Top\crash.log`
2. Verificar que el perfil correcto sea "Unpackaged" (no "Package")
3. Verificar que la plataforma sea "x64" (no "x86")
4. Verificar que el Windows App SDK Runtime esté instalado:
   ```powershell
   winget list "Windows App Runtime"
   ```

### Los cambios de XAML no se reflejan al ejecutar

- Existen **dos salidas de build**: `bin/Debug/...` (AnyCPU / `dotnet build`) y `bin/x64/Debug/net8.0-windows10.0.19041.0/win-x64/` (Debug|x64). El usuario lanza con **F5 en Visual Studio (Debug|x64)**: para ver los cambios hay que compilar `-p:Platform=x64`.
- El csproj tiene `<DisableFastUpToDateCheck>true</DisableFastUpToDateCheck>` para forzar la recompilación XAML en cada F5.
- **Cerrar instancias de `TopDjApp.exe` en ejecución antes de compilar**: el ejecutable queda bloqueado y la compilación falla.

### Recursos XAML no encontrados

Los recursos visuales de WinUI 3 deben estar en el diccionario `XamlControlsResources`.
Usar siempre recursos estándar como:
- `ControlElevationBorderBrush` (bordes)
- `LayerFillColorDefaultBrush` (fondos)
- `TextFillColorSecondaryBrush` (texto secundario)
- `SystemAccentColorBrush` (acento)

NO usar recursos como `CardBorderBrush` o `CardBackgroundFillColorDefaultBrush`
que no existen en todas las versiones de WinUI 3.

## Pruebas manuales

- Bibliotecas de prueba usadas: `F:\Musik\Corridos`, `F:\Musik\rokola\SEGUNDO ROSERO II`.
- Casos de referencia de la detección de duplicados:
  - **SameName por normalización:** `Jessi Uribe Sobreviviré` ↔ `JESSI URIBE - SOBREVIVIRE`.
  - **Difusa "1 letra":** `Segundo Rosero Incomprencion.wav` ↔ `Segundo rosero Incomprension.wav`; `Ni perdono ni olvido` ↔ `Ni Perdón Ni Olvido`.
  - **NO deben agruparse:** la serie "mosaico 1 / mosaico 2 / mosaico" (dígitos).

## Cómo agregar una nueva página

1. Crear una carpeta `Features/<Feature>/`
2. Crear archivos `NuevaPagina.xaml` y `NuevaPagina.xaml.cs` en esa carpeta (namespace `Remove_Top.Features.<Feature>`)
3. Agregar un `NavigationViewItem` en `MainWindow.xaml` con un Tag único
4. Agregar el case correspondiente en `MainWindow.xaml.cs` → `NavView_ItemInvoked`

## Cómo agregar un nuevo servicio

1. Crear archivo en la carpeta de su feature (`Features/<Feature>/`)
2. Definir clases de resultado y progreso (similar a `NormalizationResult` + `NormalizationProgress`)
3. Usar `IProgress<T>` para comunicación con la UI
4. Soporte de `CancellationToken` para cancelación

## Notas de compilación

- El proyecto usa `<WindowsPackageType>None</WindowsPackageType>` para modo unpackaged
- No requiere proyecto `.wapproj` separado
- Los binarios se generan en `bin\$(Platform)\$(Configuration)\`
- La compilación requiere .NET 8 SDK y Windows SDK 10.0.19041+
