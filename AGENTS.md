# Remove-Top — Mejorador de Audio

## Descripción

Aplicación WinUI 3 (Windows App SDK) para procesamiento de audio.

### Funcionalidades

| Módulo | Descripción |
|--------|-------------|
| **Normalización** (`NormalizationPage`) | Ajusta el nivel de pico de archivos de audio a un dBFS objetivo usando NAudio y exporta a WAV en subcarpeta `RemoveTop_Normalized`. Sobre el audio ya normalizado aplica una masterización ligera (paso alto → EQ → compresor → limitador a −0.3 dB). Límite gratuito PUBLICADO de **50 archivos** (`AudioNormalizer.FreeLimitDisplay`, solo texto); el límite REAL de escaneo es de **1.000 archivos** (`MaxFilesToScan`). El texto del InfoBar se monta en runtime con ambos valores. Omite los que ya tienen una salida procesada válida (firma RIFF/WAVE). Muestra un loader (`ProgressRing`) durante el procesamiento y, al terminar, el estado "Completado" con icono de estado (check verde si todo salió bien, advertencia ámbar si hubo errores). Si todo terminó correctamente, aparece un botón "Limpiar" (centrado) que borra los resultados y resetea la página. |
| **Renombrado Masivo** (`BatchRenamePage`) | Elimina texto específico de los nombres de archivos en una carpeta (audio, video, imagen, documentos). Opera directamente sobre los archivos originales. Persiste hasta **20 patrones** (`MaxPatterns`) en `%LOCALAPPDATA%\Remove_Top\patterns.json`. Etiqueta "Versión Gratuita" (badge verde) junto al mensaje de límite. Botón **"Iniciar de nuevo"** al final: resetea ruta, resultados, vista previa, progreso y sugerencias IA **conservando los patrones**. |
| **Edición Rápida** (`QuickRenamePage`) | Lista los `.mp3`/`.wav` de la carpeta principal y permite editar cada nombre en una caja de texto inline (nombre completo, incluida la extensión). Aplica los cambios con `File.Move` directamente sobre los originales. Incluye corrección de nombres con IA vía proveedor desacoplado (`INameCorrectionProvider`): Mock local (pruebas) o Groq API (real, requiere API Key). |
| **Extracción de Stems** (`VocalRemovalPage`) | Separa la voz del instrumental usando IA (modelo HT-Demucs FT en ONNX). Exporta vocal mono en subcarpeta `RemoveTop_Vocals`. Máximo 5 canciones estéreo por lote. |
| **Eliminación de Duplicados** (`DuplicateRemovalPage`) | Escanea una carpeta (recursivo, incluye subcarpetas, máx. 1.000 archivos `DuplicateScanner.MaxFilesToScan`). Pipeline de detección por prioridad: **nombre normalizado → hash → palabra clave**. La MISMA CANCIÓN por nombre normalizado (`SameName`) se clasifica como **exacta** y se marca por defecto; también se detectan nombres que difieren en **una sola letra** (falta ortográfica). Exactos por hash SHA-256 solo sobre los no reclamados por nombre con tamaño repetido (en paralelo). Los "posibles" por palabra clave se verifican por duración de audio. Eliminación con dos opciones: Papelera de Windows (recuperable) o borrado definitivo, ambas con confirmación. Detecta además archivos < 6 KB como "dañados" en una 3.ª pestaña. Botón **"Iniciar de nuevo"** al final de los resultados de eliminación (resetea ruta + resultados). |

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
        │   │   ├── MasteringDsp.cs                # DSP managed (BiQuad, compresor, limitador)
        │   │   └── MasteringChain.cs              # Cadena de masterización ligera (settings + build)
        │   ├── BatchRename/
        │   │   ├── BatchRenamePage.xaml / .cs     # Renombrado masivo (patrones, botón "Iniciar de nuevo")
        │   │   ├── FileRenamer.cs                 # Servicio de renombrado en lote
        │   │   ├── PatternSuggestion.cs           # Interfaz IPatternSuggestionProvider + PatternSuggestion
        │   │   ├── MockPatternSuggester.cs        # Proveedor local de sugerencias (pruebas, sin red)
        │   │   └── GroqPatternSuggester.cs        # Proveedor real de sugerencias (Groq API, requiere key)
        │   ├── QuickRename/
        │   │   ├── QuickRenamePage.xaml / .cs     # Edición rápida de nombres (.mp3/.wav)
        │   │   ├── QuickRenamer.cs                # Servicio de edición rápida de nombres
        │   │   ├── NameCorrection.cs              # Interfaz INameCorrectionProvider + CorrectionSuggestion
        │   │   ├── MockNameCorrector.cs           # Proveedor de corrección local (pruebas, sin red)
        │   │   └── GroqNameCorrector.cs           # Proveedor de corrección real (Groq API, requiere key)
        │   ├── VocalRemoval/
        │   │   ├── VocalRemovalPage.xaml / .cs    # Extracción de stems con IA
        │   │   ├── VocalSeparator.cs              # Separación de voz con modelo ONNX
        │   │   └── ModelDownloader.cs             # Descarga del modelo HT-Demucs desde HuggingFace
        │   └── DuplicateRemoval/
        │       ├── DuplicateRemovalPage.xaml / .cs  # Eliminación de duplicados (UI + ViewModel inline, "Iniciar de nuevo")
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
        │           ├── FileRecord.cs                # Registro con tamaño/hash/nombre/palabras precalculados
        │           └── DamagedFileDetector.cs       # Archivos < 6 KB ("dañados")
        ├── Helpers/
        │   ├── UiHelpers.cs              # Iconos/contenido de botones con FluentIcons
        │   ├── FileTypeIconConverter.cs  # Icono según tipo de archivo (audio/video/imagen/documento)
        │   ├── RecycleBinHelper.cs       # Envía archivos a la Papelera de Windows (SHFileOperationW)
        │   └── GroqApiClient.cs          # Cliente HTTP compartido de Groq (endpoint, modelo, parseo)
        ├── Assets/                      # Iconos y recursos visuales
        └── Properties/
            ├── launchSettings.json      # Perfiles de ejecución (Package/Unpackaged)
            └── PublishProfiles/         # Perfiles de publicación
```

## Arquitectura

```
App.xaml.cs (Application)
  └── MainWindow (NavigationView)
        ├── NormalizationPage → AudioNormalizer (NAudio)
        ├── BatchRenamePage   → FileRenamer
        ├── QuickRenamePage   → QuickRenamer + INameCorrectionProvider
        ├── VocalRemovalPage  → VocalSeparator (ONNX) + ModelDownloader
        └── DuplicateRemovalPage → DuplicateScanner + DuplicateRemover + RecycleBinHelper
```

- **Features/<Feature>/:** Cada feature es un módulo autocontenido que agrupa su página (con ViewModel inline en el code-behind) y su lógica de negocio. Los servicios se comunican con la UI via `IProgress<T>` y `CancellationToken`.
- **Helpers/:** Utilidades compartidas entre features (iconos Fluent y conversores).
- **App.xaml.cs:** Manejador global de excepciones escribe en `%LOCALAPPDATA%\Remove_Top\crash.log`. Expone el singleton estático `App.VocalSeparator` que mantiene el modelo ONNX cargado entre navegaciones.
- **MainWindow.xaml.cs:** Mantiene una caché `Dictionary<Type, Page>` para reutilizar las páginas al navegar.

## Detección de duplicados (detalle)

Pipeline de `DuplicateScanner.ScanAsync` por prioridad (optimizado para bibliotecas musicales):

1. **Misma canción por nombre normalizado (`SameName`)** → pestaña "Exacto", marcada por defecto. La normalización ignora mayúsculas, acentos, guiones, espacios y guiones iniciales.
2. **Exactos por hash SHA-256** → solo sobre archivos NO reclamados por nombre y con tamaño repetido (un tamaño único no puede tener duplicado idéntico); en paralelo.
3. **Posibles por palabras clave (`ProbableByKeyword`)** → entre lo restante; se verifican por duración y los falsos positivos se descartan.

### Coincidencia difusa "1 letra de diferencia" (`NormalizedNameDetector`)

Además del nombre exacto, detecta nombres "casi idénticos" (falta ortográfica):

- `NearNameMatches`: mismo número de palabras, **exactamente una** palabra distinta en la misma posición, con **≥ 5 letras** (`MinFuzzyWordLength`) y a una única edición de letra (sustitución / inserción / eliminación).
- Se excluyen diferencias de **dígitos** ("mosaico 1" vs "mosaico 2" nunca coinciden).
- Guarda `MinFuzzyNameLength = 6` (longitud del nombre normalizado).
- Clustering **transitivo con union-find**; el grupo se clasifica `SameName` con `NameNearMatch = true` (detalle en UI: "mismo nombre · 1 letra distinta").

### Verificación por duración (`DurationVerifier`)

- `SameName`: salvaguarda — si la duración difiere > **2×** (`SameNameMaxDurationRatio`) el ítem se desmarca (posible título idéntico de otra canción).
- `ProbableByKeyword`: si las duraciones no coinciden (tolerancia `DurationTolerance = 0.30`) el miembro se elimina del grupo (falso positivo).

### Marcado por defecto (`GroupBuilder`)

- `Exact` y `SameName` → siempre marcados.
- `ProbableByName` (legacy) → marcado si comparte tamaño.
- Keeper: ruta más superficial y, en empate, más corta (`keepLargest: false`).

## Renombrado masivo (detalle)

- Máximo **20 patrones** (`MaxPatterns`), persistidos en `%LOCALAPPDATA%\Remove_Top\patterns.json`.
- Etiqueta **"Versión Gratuita"** (badge #70AD47) junto al mensaje "Máximo 20 patrones. La búsqueda no distingue mayúsculas/minúsculas."
- Botón **"Iniciar de nuevo"** (`RestartButton`) al final de los resultados: resetea ruta, resultados, vista previa, progreso, badge y sugerencias IA, pero **CONSERVA los patrones**.

## Normalización (límite gratuito)

- `AudioNormalizer.MaxFilesToScan = 1000` → límite REAL de archivos analizados.
- `AudioNormalizer.FreeLimitDisplay = 50` → límite PUBLICADO en la UI (solo texto de marketing; el escaneo real sigue el límite real).
- El `InfoBar` de la página se construye en runtime (`NormalizationPage` constructor) usando ambos valores.

## Proveedores de corrección de nombres (IA)

- `QuickRenamePage` usa `INameCorrectionProvider` (interfaz en `Features/QuickRename/NameCorrection.cs`), desacoplada de la UI para poder cambiar de proveedor sin tocar la página.
- **`MockNameCorrector`** (predeterminado): simula correcciones localmente (separa palabras unidas, capitaliza, conserva extensión). Sin red ni API Key. Útil para pruebas.
- **`GroqNameCorrector`**: llama a `https://api.groq.com/openai/v1/chat/completions` (modelo `llama-3.3-70b-versatile`). Queda deshabilitado en la UI hasta que el usuario ingrese su API Key en el campo correspondiente.
- El flujo: la página envía la lista de nombres → el proveedor devuelve `CorrectionSuggestion` (original vs. sugerido) → el usuario aprueba con CheckBox (individual o "Aprobar todos") → "Aplicar aprobados" rellena las cajas de texto editables → "Aplicar cambios" renombra.

## Sugerencia de patrones con IA (BatchRename)

- `BatchRenamePage` usa `IPatternSuggestionProvider` (interfaz en `Features/BatchRename/PatternSuggestion.cs`): dado los patrones actuales + los nombres de archivos afectados, sugiere NUEVOS patrones a eliminar.
- **`MockPatternSuggester`** (predeterminado): heurísticas locales (variantes de separadores, tokens recurrentes). Sin red ni API Key. Para pruebas.
- **`GroqPatternSuggester`**: envía `{ patrones, archivos }` (solo nombres base sin extensión de los archivos afectados, truncados y con tope para ser ligeros) y pide hasta 10 patrones nuevos. Requiere API Key por sesión.
- Ambos proveedores (y `GroqNameCorrector`) comparten el cliente HTTP `Helpers/GroqApiClient.cs` (endpoint/modelo/configuración de conexión ahí).
- El flujo: la página envía patrones + nombres → el proveedor devuelve `PatternSuggestion` → el usuario aprueba con CheckBox → "Agregar aprobados" los incorpora a los patrones (persistiendo y recalculando la vista previa).

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
bin\x64\Debug\net8.0-windows10.0.19041.0\win-x64\Remove_Top.exe
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
- **Cerrar instancias de `Remove_Top.exe` en ejecución antes de compilar**: el ejecutable queda bloqueado y la compilación falla.

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
