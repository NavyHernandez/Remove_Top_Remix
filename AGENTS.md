# Remove-Top — One Dj App

> **Nota para agentes:** Antes de comenzar cualquier tarea, lee los archivos en la carpeta `progress/`:
> - `progress/history.md` — bitácora de sesiones anteriores
> - `progress/feature_list.json` — lista de features completadas y pendientes
>
> Esto te dará contexto completo del estado del proyecto.
>
> **IMPORTANTE:**
> - NO actualices el repositorio (git push) a menos que el usuario te lo solicite explícitamente.
> - NO hagas builds (`dotnet build`), publicaciones (`dotnet publish`), ni empaquetado (`vpk pack`) a menos que el usuario te lo pida directamente.
> - NO subas releases a GitHub (`vpk upload github`) a menos que el usuario te lo solicite explícitamente.
> - Solo haz commit y push cuando el usuario lo pida directamente.
> - Estas acciones (build, publish, upload, push) NUNCA deben ser parte de un plan; solo ejecútalas cuando el usuario lo ordene.

## Descripción

Aplicación WinUI 3 (Windows App SDK) para procesamiento de audio.

### Funcionalidades

| Módulo | Descripción |
|--------|-------------|
| **Normalización** (`NormalizationPage`) | Ajusta el nivel de pico de archivos de audio a un dBFS objetivo usando NAudio y exporta a WAV en subcarpeta `OneDj_Normalized`. Sobre el audio ya normalizado aplica una **masterización con 3 perfiles de intensidad** (selector en la página): `Ligera` (cadena original por pico, conserva dinámica), `Hard Limiter` (motor pro: compresor enlazado + soft clipper + limitador true-peak con lookahead, ganancia por sonoridad a **−10 LUFS**, techo **−1.0 dBTP**) y `Comercial EDM` (más denso: **−8.5 LUFS**, techo −1.0 dBTP). Los 3 comparten el front-end (DC blocker → HPF 30 Hz → EQ). Al terminar cada resultado muestra **Pico y LUFS reales** medidos en el archivo de salida. Al finalizar, corrige la ortografía de los nombres de salida (tildes) con un diccionario local (`SpanishNameCorrector`). Límite gratuito PUBLICADO de **50 archivos** (`AppLimits.NormalizationFreeLimitDisplay`, solo texto); el límite REAL de escaneo es de **1.000 archivos** (`AppLimits.NormalizationMaxFilesToScan`). El texto del InfoBar se monta en runtime desde `AppLimits`. **Arrastre nativo propio** (sin `DropTargetControl`/`FileSourceControl`, que colgaban al analizar): una sola carpeta sigue el camino manual idéntico; las canciones sueltas se **acumulan hasta 50** con análisis incremental solo de las nuevas; overlay local "Suelta para cargar". Cada fila del análisis tiene **play** (mini reproductor `AudioPreview`: onda + Play/Pausa/Stop + scrub, con iconos inicializados en el constructor para que Stop siempre sea visible) y **"x"** (`DismissCircle`) que quita la canción para que no se normalice (Normalizar procesa la lista mostrada). Análisis con **timeout de 20 s** por archivo (el atascado queda en rojo como omitido) y filas fallidas con "—" + tooltip del motivo. Omite los que ya tienen una salida procesada válida (firma RIFF/WAVE) — reconoce tanto el nombre original como el corregido. Muestra un loader (`ProgressRing`) durante el procesamiento y, al terminar, el estado "Completado" con icono de estado (check verde si todo salió bien, advertencia ámbar si hubo errores). Si todo terminó correctamente, aparece un botón "Limpiar" (centrado) que borra los resultados y resetea la página. |
| **Renombrado Masivo** (`BatchRenamePage`) | Elimina texto específico de los nombres de archivos en una carpeta (audio, video, imagen, documentos). Opera directamente sobre los archivos originales. El escaneo es **recursivo (incluye subcarpetas)** con **máximo 1.000 archivos por ejecución** (`AppLimits.BatchRenameMaxFilesToScan`). Persiste hasta **30 patrones** (`AppLimits.BatchRenameMaxPatterns`) en `%LOCALAPPDATA%\Remove_Top\patterns.json`. Etiqueta "Versión Gratuita" (badge verde) junto a los mensajes de límite (patrones y archivos, generados desde `AppLimits`). Si el escaneo se truncó y el renombrado terminó, aparece la **tarjeta premium** ("Adquiere la versión premium", mismo patrón que DuplicateRemoval). Botón **"Limpiar"** al final: resetea ruta, resultados, vista previa, progreso y sugerencias IA **conservando los patrones**. Botón **"Cancelar"** centrado debajo del botón principal para resetear la página en cualquier momento. |
| **Edición Rápida** (`QuickRenamePage`) | Lista los `.mp3`/`.wav` de la carpeta (recursivo, incluye subcarpetas) y permite editar cada nombre en una caja de texto inline (nombre completo, incluida la extensión). Aplica los cambios con `File.Move` directamente sobre los originales. Tope de **1.000 archivos** (`AppLimits.QuickRenameMaxFilesToScan`, solo los primeros N). Badge **"Versión Gratuita"** + mensaje de límite junto a la carpeta de origen (generado desde `AppLimits.QuickRenameLimitMessage`). Marca `www.top-remix.com` centrada en la línea "Origen" del control de origen (`FileSourceControl.ShowBrandSite`, opt-in solo en esta página). Si la carpeta trae más de 1000, el contador avisa "(mostrando los primeros 1000 de N)". Limpia el atributo de solo lectura antes de renombrar y distingue errores ("ya existe" vs "en uso"). Botón **"Limpiar"** al final (resetea ruta, lista y resultado). Al terminar muestra una etiqueta con cuántos archivos se renombraron y recarga la lista con los nombres nuevos. Sin barra de progreso ni lista de resultados. **Menú de caso** (`CaseButton`, DropDownButton compacto con icono `TextChangeCase`): `MAYÚSCULAS` / `minúsculas` / `Capitalizar Palabras` (cultura `es`), solo a la base sin tocar la extensión, pre-llenado en vivo y reversible con "Restaurar originales". **Reordenar partes** (`NamePartReorderer`): botón con icono `ArrowSwap` en el header de la lista que divide el nombre de una **canción guía** (checkbox por fila, selección única, por defecto la primera) en bloques según separador (guion `-` o espacio) y los muestra como **chips arrastrables** (fila horizontal) para reordenarlos. Doble clic en un chip lo selecciona como fuente de unión y doble clic en otro fusiona bloques consecutivos (solo separador espacio); clic derecho en un bloque unido lo separa. Botón **deshacer** (icono flecha) restaura el último cambio (unión, separación o reorden por arrastre). Vista previa en vivo de los **primeros 10 archivos** con el resultado. "Aplicar a todos" pre-llena los nombres reordenados y luego "Aplicar cambios" renombra en disco. La guía usa el **nombre original cargado** (`LoadedName`, inmune a renombrados previos). |
| **Extracción de Stems** (`VocalRemovalPage`) | Separa la voz del instrumental usando IA (modelo HT-Demucs FT en ONNX). Exporta vocal mono en subcarpeta `RemoveTop_Vocals`. Máximo **5 canciones** estéreo por lote (`AppLimits.VocalRemovalMaxFilesPerBatch`). |
| **Etiquetas** (`TagRemovalPage`) | Elimina o reemplaza las etiquetas (título, intérprete, artistas, álbum, género, año, comentario) y la portada de archivos de audio usando **TagLib#**. El origen acepta **carpetas y archivos** (carpetas escaneadas de forma recursiva o archivos sueltos; también por **arrastre**). Dos acciones en **pestañas** (color dorado `#B8860B`): **Eliminar etiquetas** — borra TODAS las tags + portada incrustada y además las **portadas externas** de la carpeta (`folder/cover/front/back/album/artwork` × `jpg/jpeg/png/webp/bmp/gif`); **Reemplazar etiquetas** — 7 campos que se aplican en lote a TODOS los archivos (el menú de inputs solo aparece cuando hay archivos cargados), con portada opcional (si no se elige una nueva, conserva la existente). Maneja archivos **de solo lectura** (quita el atributo para poder escribir). Límite **1.000 archivos** (`AppLimits.TagsMaxFilesToScan`) + tarjeta premium si se trunca. |
| **Eliminación de Duplicados** (`DuplicateRemovalPage`) | Escanea una carpeta (recursivo, incluye subcarpetas, máx. **1.000 archivos** `AppLimits.DuplicatesMaxFilesToScan`). Pipeline de detección por prioridad: **nombre normalizado → nombre contenido (subconjunto) → hash → palabra clave**. La MISMA CANCIÓN por nombre normalizado (`SameName`) se clasifica como **exacta** y se marca por defecto; también se detectan nombres que difieren en **una sola letra** (falta ortográfica). El detector de **nombre contenido** (`SubsetNameDetector`) agrupa archivos donde todas las palabras del **título** (último bloque) del nombre más corto aparecen en el título del más largo (máx. 3 palabras de diferencia; tope de 6 miembros por cluster). Fix: `StripAllExtensions` elimina extensiones múltiples conocidas (`.mp3.vdjstems` → `.mp3`). Exactos por hash SHA-256 solo sobre los no reclamados por nombre con tamaño repetido (en paralelo). Los "posibles" por palabra clave se verifican por duración de audio. Eliminación con dos opciones: Papelera de Windows (recuperable) o borrado definitivo, ambas con confirmación. Detecta además archivos < **6 KB** (`AppLimits.DuplicatesMinValidFileSizeBytes`) como "dañados" en una 3.ª pestaña. **Previsualizador unificado**: botón en cada fila de las pestañas Exactos/Posibles (módulos `Features/AudioPreview/` y `Features/ImagePreview/`, no disponible en dañados) con icono según tipo (`Play` para audio, `Image` para imágenes) que muestra una **tarjeta con forma de onda + scrub** y transporte **Play/Pausa/Stop** para el audio, o la **imagen ajustada al espacio** (Stretch Uniform, sin zoom) en la tarjeta de imágenes. Un solo preview activo a la vez (abrir uno cierra el otro). Icono check verde cuando no hay duplicados. Botones de acciones centrados. Botón **"Limpiar"** al final de los resultados de eliminación (resetea ruta + resultados). **Arrastre nativo propio** (sin `DropTargetControl`/`FileSourceControl`): overlay local "Suelta para cargar" (acento `#E74C3C`, timer anti-parpadeo 250 ms); una sola carpeta **carga la ruta sin auto-escanear** (archivos sueltos o varias carpetas muestran aviso). Cada fila de las 3 pestañas tiene **"x"** (`DismissCircle`, `DismissResultItem_Click`) que la quita de la lista sin tocar el disco (cierra el preview si era el archivo mostrado y refresca contadores/resumen/acciones). |
| **Cuenta** (`AccountPage`) | Centro de perfil y actualizaciones. La página muestra: logo profesional vectorial (`Assets/BrandLogo.xaml`, gradiente + nota + forma de onda), sección **Perfil** con **autenticación real de Firebase (Email/Password)** — sin sesión muestra un formulario de login/registro; con sesión muestra el perfil y "Cerrar sesión". **Gate de verificación de correo**: solo se admiten cuentas con correo verificado — el login de una cuenta sin verificar NO abre sesión y muestra un panel ámbar con "Reenviar enlace de verificación"; el registro envía el enlace y no deja sesión (panel verde de éxito); el refresh token se conserva como "pendiente" y un polling (`AccountPage`, cada 5 s) detecta la confirmación del correo y hace el **auto-login**. El login de una cuenta inexistente la crea automáticamente y envía el correo de verificación (`AuthService.LoginOrRegisterAsync`). El correo se envía por REST de Identity Toolkit (`FirebaseRestApi`, el paquete v4 no lo expone). La sesión se restaura al iniciar la app. Sección **Sugerencias** (visible SOLO con sesión): cuadro de feedback con tope de 1000 caracteres (contador) que guarda en **Cloud Firestore** vía REST (`FirebaseRestApi.AddSuggestionAsync`, colección `suggestions`). Sección **Actualizaciones** (`UpdateChecker` con **Velopack** — consulta GitHub Releases; badge que se ilumina verde/ámbar; botón "Descargar vX.Y.Z" con ProgressRing de progreso; al llegar a 100% aplica y reinicia; `VelopackApp.Build().Run()` aplica updates pendientes al inicio). **Dot de notificaciones** en el ítem "Cuenta" del menú (`InfoBadge` con `Severity="Attention"`) — se muestra cuando el auto-check al iniciar detecta una versión nueva; se oculta al revisar actualizaciones o al iniciar descarga. Auto-check en background al iniciar (`CheckForUpdatesOnStartup`). Al entrar a Cuenta con update pendiente se eleva un **popup con rebote** (`BounceEase`, icono `Gift`, "¡Tenemos una nueva actualización!", una sola vez por versión) cuyo botón "Descargar ahora" inicia la descarga directa (`StartDownloadAsync` refactorizado); "Ahora no" o el velo lo cierran (`UpdateChecker.HasPendingUpdate/PendingVersion`). Ítem de menú "Cuenta" con icono `Person` y color teal `#00A88F`. Textos del encabezado centralizados en `AppLimits` (`AccountPageTitle/Subtitle`). Sección **Soporte** al final: tarjeta compacta centrada con QR (`Assets/SupportQr.jpeg`, con placeholder si falta la imagen) y botón teal "Abrir chat de WhatsApp" (`wa.me/593982311600`); estilo glass neutro sobre la base de la app (sombra `ThemeShadow` como recurso + velo superior + línea de luz + acento teal, sin verdes). |

## Stack Tecnológico

- **Framework:** .NET 8.0 + Windows App SDK 2.2.0
- **UI:** WinUI 3 (XAML)
- **Audio:** NAudio 2.3.0
- **Tags/metadatos:** TagLibSharp 2.3.0
- **IA:** Microsoft.ML.OnnxRuntime 1.21.0 (HT-Demucs FT)
- **Target:** Windows 10 build 19041+ (unpackaged, self-contained)
- **Runtime:** Windows App SDK Runtime 1.6+ (empaquetado con la app)

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
        │   │   ├── AccountPage.xaml / .cs         # Cuenta: perfil (auth Firebase), sugerencias y actualizaciones
        │   │   ├── AuthService.cs                 # Autenticación Firebase Email/Password (login, registro, verificación, sugerencias, sesión)
        │   │   ├── FirebaseConfig.cs              # Config Firebase centralizada (ApiKey + AuthDomain + ProjectId + colección de sugerencias)
        │   │   ├── FirebaseRestApi.cs             # REST API Firebase Identity Toolkit + Firestore (verificación de correo, guardar sugerencias)
        │   │   ├── SecureUserRepository.cs        # Repositorio seguro del token (Windows PasswordVault)
        │   │   └── UpdateChecker.cs               # Verificador de actualizaciones (Velopack + GitHub Releases)
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
        │   └── TagRemoval/
        │       ├── TagRemovalPage.xaml / .cs        # Eliminar/Reemplazar etiquetas (pestañas, inputs, portada)
        │       ├── TagService.cs                    # TagLib#: recolección, análisis, borrado y reemplazo (EnsureWritable, portadas externas)
        │       ├── TagSourceControl.xaml / .cs      # Origen + análisis de tags (por pestaña)
        │       ├── TagMode.cs                       # enum Clear / Replace
        │       ├── TagValues.cs                     # DTO de los 7 campos + portada
        │       ├── TagFileItem.cs                   # Ítem del análisis (tags actuales + portada)
        │       ├── TagResult.cs                     # Resultado por archivo
        │       └── TagProgress.cs                   # Progreso del análisis/procesamiento
        ├── Controls/                    # Controles reutilizables entre features
        │   ├── DropTargetControl.xaml / .cs         # Toda la página como destino de arrastre + overlay (AccentColor, FilesDropped)
        │   ├── DropFilesEventArgs.cs                # Rutas soltadas
        │   └── FileSourceControl.xaml / .cs         # Origen genérico: carpeta/archivos + "Limpiar" + tinte (FileFilter, ScanRecursive, MaxFiles)
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
        ├── TagRemovalPage    → TagService (TagLib#) + TagSourceControl
        └── AccountPage → AuthService (Firebase Email/Password + PasswordVault) + UpdateChecker (Velopack)

Todas las páginas de funcionalidad envuelven su raíz en DropTargetControl (arrastre) y usan
FileSourceControl (selección de origen carpeta/archivos).
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

- Máximo **30 patrones** (`AppLimits.BatchRenameMaxPatterns`), persistidos en `%LOCALAPPDATA%\Remove_Top\patterns.json`.
- Etiqueta **"Versión Gratuita"** (badge #70AD47) junto al mensaje "Máx. 30 patrones." (texto generado desde `AppLimits.BatchRenameLimitMessage`).
- **Rendimiento**: `ProcessFilesAsync` mueve de 10 en 10 (`ChunkSize`, un `Task.Run` por lote); el progreso actualiza barra/contador por archivo, el nombre cada 10 y siempre en el último, con contadores O(1) y un solo `ScrollIntoView` final.
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

Módulo reutilizable `Features/ImagePreview/` (solo API nativa de WinUI 3, sin dependencias nuevas) que complementa al de audio: el botón de preview de las filas es **unificado** — icono `Play` para audio, `Image` para imágenes (`DuplicateItem.IsAudio` / `IsImage` / `IsPreviewable` / `PreviewIcon`). Para tipos no visualizables (video, documentos, etc.) el botón **no se oculta ni deja hueco**: queda **siempre visible pero deshabilitado** con icono `EyeOff` y tooltip "Sin previsualización" (`DuplicateItem.PreviewToolTip`).

- **`ImagePreviewSupport`** — `IsImageFile(path)` (extensiones `.jpg/.jpeg/.png/.gif/.bmp/.tiff/.tif/.webp/.ico/.jfif/.svg`, alineadas con `FileTypeIconConverter`) y `CreateSource(path)` → `BitmapImage` (raster, con `DecodePixelWidth = 1600` para limitar memoria) o `SvgImageSource` (SVG).
- **`ImagePreviewView`** — visor simple: imagen con `Stretch="Uniform"` (se ajusta al espacio, **sin zoom/pan**). Estados **vacío / cargando / error** y la imagen. `Load(path)`, `Clear()` (libera la fuente), `CurrentPath` y eventos `ImageLoaded(int w, int h)` / `ImageLoadFailed`. En SVG las dimensiones no se exponen y se notifica 0×0 (pie: "Vectorial (SVG)").
- **Integración**: tarjeta `ImagePreviewSection` (entre la tarjeta de audio y las acciones) con header (icono `Image` + nombre + cerrar), el visor de 300 px de alto y un pie con **dimensiones reales + tamaño en disco**.
- **Un solo preview activo**: `StopAllPreviews()` (cierra audio con `StopPreviewCore(closeFile: true)` y libera la imagen con `ClearImagePreview()`) se invoca al cambiar de preview, de carpeta (`ResetResults`), antes de borrar (`RunDeletionAsync`) y al salir de la página (`Unloaded`). `ClearImagePreview()` pone `Source = null` para liberar memoria y el posible bloqueo del archivo antes de resetear/borrar.
- **Pestaña Dañados**: sin botón de preview (sin cambios en su template).

## Eliminar y reemplazar etiquetas (detalle)

Módulo `Features/TagRemoval/` con **TagLib#** (paquete `TagLibSharp` 2.3.0). El origen acepta **carpetas (recursivas) y archivos sueltos** — botones "Carpeta..."/"Archivos...", pista de arrastre, botón **"Limpiar"** cuando hay carga y **tinte dorado** (`#B8860B`) en la tarjeta. `TagSourceControl` (una por pestaña) analiza las tags actuales (título/artista/álbum + portada).

- **Modo Eliminar** (`TagMode.Clear`): `RemoveTags(TagTypes.AllTags)` + `Save()` borra ID3v1/ID3v2 **y la portada incrustada**. Además, `DeleteExternalCovers` borra las **portadas externas** de la carpeta (`folder/cover/front/back/album/artwork` × `jpg/jpeg/png/webp/bmp/gif`).
- **Modo Reemplazar** (`TagMode.Replace`): primero vacía las tags (encoge el archivo) y reescribe los **7 campos** (Título, Intérprete, Artistas, Álbum, Género, Año, Comentario) — los vacíos se limpian. Portada: si se elige una nueva la reemplaza; si no, **conserva** la existente. Los inputs solo aparecen cuando hay archivos cargados.
- **Solo lectura**: `EnsureWritable` quita el atributo `FileAttributes.ReadOnly` antes de escribir (el archivo queda grabable). Si no se puede, el archivo reporta "Archivo de solo lectura...".
- **Límite**: `TagsMaxFilesToScan = 1000` (InfoBar/badge compacto "Versión Gratuita") + **tarjeta premium** si el escaneo se truncó.
- Formatos soportados: `.mp3 .flac .m4a .mp4 .wma .asf .ogg .oga .opus .aiff .aif .wav .ape .wv .mka .tta .dsf` (se excluye `.aac` suelto, que TagLib no soporta).

## Arrastre en toda la app (controles reutilizables)

Carpeta `Controls/` con dos controles compartidos por todas las páginas de funcionalidad:

- **`DropTargetControl`** — `UserControl` que envuelve el contenido de la página (`Host` DP), habilita `AllowDrop` en TODA la página (raíz con `Background="Transparent"` para que el área sea válida según la doc de drag-and-drop), muestra un **overlay oscuro** al arrastrar (título/subtítulo configurables + `AccentColor`) y notifica las rutas vía `FilesDropped` (carpetas y archivos). Timer anti-parpadeo de 250 ms. **Robustez Win10**: `OnDrop` captura fallos de `GetStorageItemsAsync` (bug conocido en Win10 aunque el overlay se muestre) y rutas vacías (carpetas virtuales/ZIP): registra en `crash.log` (`App.Log`) y notifica `DropFailed` (motivo mostrable) para que la página lo muestre con `SetStatus` en vez de quedarse en silencio. La ruta de éxito queda intacta.
- **`FileSourceControl`** — tarjeta "Origen" genérica (carpeta/archivos + pista de arrastre + "Limpiar" + tinte al cargar). Configurable: `FileFilter`, `ScanRecursive`, `MaxFiles`, `AccentColor`, `PickerExtensions`, `ShowBrandSite` (marca `www.top-remix.com` centrada en la línea "Origen", opt-in apagado por defecto). Expone `Files/HasFiles/Truncated/TotalFound`, `StateChanged`, `LoadSource/Reset/SetEnabled/SetStatus`.

**Integración:** cada página envuelve su raíz en `DropTargetControl` (con `AccentColor` = color de su feature) y reemplaza su tarjeta de carpeta por `FileSourceControl`. Al soltar/escoger, `StateChanged` dispara la lógica de la página con `Files` (ya no se escanea la carpeta a mano). **Excepción: Duplicados** usa BrowseButton + FolderPicker más **arrastre nativo propio** (overlay local que solo carga la ruta sin auto-escanear, sin DropTargetControl/FileSourceControl tras reversión). **Guardas**: si la página está procesando/analizando/escaneando/sugiriendo, el arrastre se ignora y `SetEnabled(false)` deshabilita los pickers. Sobrecargas por lista en servicios: `AudioNormalizer.GetAudioFiles(list, out totalFound, out alreadyProcessed)`, `FileRenamer.GetAffectedFiles(list, patterns, out totalFound)`.

**Colores por feature** (en `MainWindow.xaml` + overlay/tarjetas): Normalizar `#5B9BD5`, Renombrar `#70AD47`, Editar `#E67E22`, Stems `#9B59B6`, Duplicados `#E74C3C`, **Etiquetas `#B8860B` (dorado)**, Cuenta `#00A88F` (teal).

## Normalización (límite gratuito)

- `AppLimits.NormalizationMaxFilesToScan = 1000` → límite REAL de archivos analizados.
- `AppLimits.NormalizationFreeLimitDisplay = 50` → límite PUBLICADO en la UI (solo texto de marketing; el escaneo real sigue el límite real).
- El aviso de límite (badge "Versión Gratuita" + título/mensaje en **letra reducida**) se construye en runtime (`NormalizationPage` constructor) usando `AppLimits.NormalizationInfoBarTitle/Message`.

## Normalización (masterización por intensidad)

Selector `IntensityComboBox` en `NormalizationPage` con 3 perfiles (`MasteringIntensity` en `MasteringChain.cs`), por defecto **Hard Limiter**:

1. **Ligera** — cadena original por pico: DC blocker → HPF 30 Hz → EQ → compresor (−15 dB, 2:1) → limitador de pico clásico (−0.3 dB, +2 dB). Conserva la dinámica.
2. **Hard Limiter** — motor pro con **normalización por sonoridad a 2 pasadas**: mide **LUFS Integrated** (`LoudnessMeter`: K-weighting BS.1770-4, bloques 400 ms/75 % solape, gating −70/−10 LU), calcula `ganancia = −10 LUFS − LUFS_origen`, ejecuta la cadena, mide el LUFS real de salida y corrige (topes de seguridad). Cadena: front-end → compresor estéreo-enlazado (−16 dB, 2.5:1, atk 15 ms) → **`SoftClipperSampleProvider`** (tanh, umbral −1 dB) → **`TruePeakLimiterSampleProvider`** (lookahead 6 ms, detección true-peak 4× Catmull-Rom, release adaptativo 60 ms, estéreo-enlazado, techo **−1.0 dBTP**) → **dither TPDF** adaptado a la profundidad.
3. **Comercial EDM** — igual con compresor (−22 dB, 3.5:1, **atk 22 ms** para conservar el punch), clipper −2 dB, lookahead 5 ms, release 50 ms y objetivo **−8.5 LUFS**.

- Los 3 perfiles comparten el front-end (`MasteringChain.BuildInputConditioning` + `BuildEq`): **DC blocker** (~10 Hz) → **HPF 30 Hz** → EQ (graves/medios/agudos). Así nada de subinfrasónicos ni DC come headroom ni asimetra el clipper.
- `AudioNormalizer.NormalizeFile` mide el **pico y LUFS reales** del archivo de salida y los muestra en el mensaje de cada resultado (p. ej. `Hard Limiter · Pico −1.0 dB · LUFS −10.0`), para verificar la mejora sin abrir un DAW.

## Límites de la versión gratuita (componente central)

**Todos los límites de las funcionalidades se definen en `Helpers/AppLimits.cs`** y se cambian manualmente ahí:

| Funcionalidad | Constante | Valor |
|---------------|-----------|-------|
| Normalización | `NormalizationFreeLimitDisplay` (publicado) · `NormalizationMaxFilesToScan` (real) | 50 · 1.000 |
| Renombrado masivo | `BatchRenameMaxPatterns` | 20 |
| Stems | `VocalRemovalMaxFilesPerBatch` | 5 |
| Duplicados | `DuplicatesMaxFilesToScan` · `DuplicatesMaxDeletionsPerRun` · `DuplicatesMinValidFileSizeBytes` | 1.000 · 1.000 · 6 KB |
| Etiquetas | `TagsMaxFilesToScan` | 1.000 |

Las funcionalidades **consumen la lógica y los textos de UI desde `AppLimits`**: los servicios los usan en sus `Take(n)`/topes reales y las páginas montan los InfoBars, contadores y descripciones en runtime desde las propiedades de texto (`AppLimits.NormalizationInfoBar*`, `DuplicatesInfoBar*`, `BatchRenameLimitMessage`, `VocalRemovalPageDescription`). Así los textos nunca se desincronizan de los límites reales. Para cambiar un límite, editar el valor aquí y recompilar.

Además de los límites, `AppLimits` centraliza los **textos del encabezado de cada página** (título, subtítulo) y el badge **"Versión Gratuita"** (`AppLimits.FreeBadgeText`): `NormalizationPageTitle/Subtitle`, `BatchRenamePageTitle/Subtitle`, `QuickRenamePageTitle/Subtitle`, `VocalRemovalPageTitle/Subtitle`, `DuplicatesPageTitle/Subtitle`, `TagsPageTitle/Subtitle`, `AccountPageTitle/Subtitle`. Cada página los monta en su constructor desde estas propiedades, por lo que todos los textos de las funcionalidades se cambian en un solo lugar. También `TagsClearInfoText`/`TagsReplaceInfoText` (descripciones de las pestañas de Etiquetas) y los avisos de la versión gratuita (`TagsInfoBarTitle`, `BatchRenameLimitMessage`, `BatchRenameFilesLimitMessage`, `QuickRenameLimitMessage`, `VocalRemovalPageDescription`), mostrados con badge + **letra reducida** (11-12 px).

### Identidad de la aplicación (branding)

También en `AppLimits` se centraliza la identidad de la app, para cambiar el nombre y textos de marca en un solo lugar:

| Constante | Valor | Dónde se usa |
|-----------|-------|--------------|
| `AppName` | `One Dj App` | Título de ventana, nombre del menú (`BrandNameText`), badges de marca de las 6 páginas (`BrandText`) |
| `AppSubtitle` | `Mejorador de Audio` | Subtítulo del menú (`BrandSubtitleText`) |
| `AppBrandSite` | `www.top-remix.com` | `SiteBrandText` (QuickRename) y `BrandSiteRun` (DuplicateRemoval). **No cambiar el dominio** |
| `AppDataFolderName` | `Remove_Top` | Carpeta de datos en `%LOCALAPPDATA%` (crash.log, patterns.json, models). Conservar para no perder datos |

El ejecutable se genera como `OneDjApp.exe` (AssemblyName en el csproj); el `RootNamespace` sigue siendo `Remove_Top`.

## Sugerencia de patrones con IA (BatchRename)

- `BatchRenamePage` usa `IPatternSuggestionProvider` (interfaz en `Features/BatchRename/PatternSuggestion.cs`): dado los patrones actuales + los primeros 10 nombres de archivos afectados, sugiere NUEVOS patrones a eliminar.
- **`GroqPatternSuggester`**: envía `{ patrones, archivos }` (solo los primeros 10 nombres base sin extensión de los archivos afectados) y pide hasta 10 patrones nuevos. La API key se configura en el cliente compartido.
- El proveedor usa el cliente HTTP `Helpers/TopRemixServerApiClient.cs` (endpoint/modelo/apiKey/configuración de conexión ahí).
- El flujo: la página envía patrones + primeros 10 nombres → el proveedor devuelve `PatternSuggestion` → el usuario aprueba con CheckBox → "Agregar aprobados" los incorpora a los patrones (persistiendo y recalculando la vista previa).

## Cuenta (verificación de correo y sugerencias)

- **Gate de verificación**: solo se admiten cuentas con correo verificado. `AuthService.LoginAsync` devuelve `LoginResult` — si `IsEmailVerified` es false NO abre la sesión y marca `RequiresEmailVerification`, pero **conserva el refresh token como "pendiente"** (el sign-in del paquete ya puebla `User.Info.IsEmailVerified`). `RegisterAsync` devuelve `RegisterResult` (`EmailVerificationSent`): crea la cuenta, envía el enlace y no deja sesión activa (token pendiente). `SendVerificationEmailAsync(email,password)` reenvía el enlace (re-sign-in → envío). `RestoreSession` abre sesión solo si el correo está verificado; si no, deja el token pendiente sin sesión activa.
- **Auto-login tras confirmar (sin deep link)**: el correo usa el enlace estándar de Firebase; el usuario confirma en el navegador y vuelve a la app. `AuthService.CompleteVerificationAsync` refresca el idToken con el refresh token guardado (sin volver a pedir la contraseña), consulta `accounts:getAccountInfo` (`FirebaseRestApi.IsEmailVerifiedAsync`) y, si `emailVerified` es true, marca la `UserInfo` local, la persiste y abre la sesión. `AccountPage` lo invoca con un `DispatcherTimer` de 5 s mientras hay verificación pendiente (`HasPendingVerification`), con tope de ~10 min, y también al cargar la página y al salir. Al loguearse automáticamente se muestran el perfil y las sugerencias.
- **Auto-creación en primer uso**: el login de una cuenta inexistente la crea automáticamente y envía el correo de verificación. `AuthService.LoginOrRegisterAsync` intenta `LoginAsync`; si falla con `INVALID_LOGIN_CREDENTIALS` (Google usa ese único código para correo inexistente y contraseña incorrecta), llama a `RegisterAsync` y devuelve `LoginResult.AccountCreated = true` (la UI muestra solo "Correo de verificación enviado" y arranca el polling de auto-login). Si el registro falla con `EMAIL_EXISTS`, la cuenta ya existía y la contraseña era incorrecta → se muestra "Correo o contraseña incorrectos.". Cualquier otro error (red, etc.) se propaga sin crear nada.
- **Envío del correo por REST**: el paquete `FirebaseAuthentication.net` v4 no expone el envío del correo de verificación, así que `FirebaseRestApi.SendVerificationEmailAsync` lo hace por la REST API de Identity Toolkit: `POST https://identitytoolkit.googleapis.com/v1/accounts:sendOobCode?key={ApiKey}` con `{ "requestType": "VERIFY_EMAIL", "idToken": "..." }`.
- **Sugerencias → Cloud Firestore por REST** (sin paquetes nuevos, sin gRPC): `FirebaseRestApi.AddSuggestionAsync(uid, email, message, idToken)` hace `POST https://firestore.googleapis.com/v1/projects/{ProjectId}/databases/(default)/documents/suggestions` con `Authorization: Bearer <idToken>` (ID auto-asignado) y campos `uid/email/message/createdAt` (timestampValue). `FirebaseConfig` centraliza `ProjectId` y `SuggestionsCollection`.
- **UI (AccountPage)**: panel ámbar (`AuthVerifyNotePanel`, con `ResendVerifyButton` "Reenviar enlace de verificación") cuando el login no se abre por verificación pendiente; panel verde (`AuthSuccessPanel`, título + subtítulo variables) tras registro o reenvío; sección **Sugerencias** (tarjeta `SuggestionsSection`, visible solo con sesión) con `TextBox` multiline `MaxLength=AppLimits.SuggestionsMaxLength` (1000), contador `n/1000`, botón teal `Icon.Send` + `ProgressRing` e `InfoBar` de éxito/error.
- **Requisito manual en Firebase Console** (no se puede hacer desde la app): crear la base de datos Firestore y las reglas de seguridad, p. ej. `match /suggestions/{document=**} { allow create: if request.auth != null && request.auth.token.email_verified == true; allow read, update, delete: if false; }`. Sin esto, el envío falla con un error mapeado (NOT_FOUND / PERMISSION_DENIED).
- Los errores de las REST APIs se traducen con `FirebaseRestApi.MapErrorCode/MapErrorStatus/MapHttpStatus` a mensajes amigables; `AuthService.GetAuthErrorMessage` también cubre `FirebaseApiException`.

## Auto-actualización (Velopack)

| Campo | Valor |
|-------|-------|
| **Paquete** | Velopack 1.2.* (NuGet) |
| **Versión actual** | `0.3.6` (en `Remove_Top.csproj`, `<Version>`) |
| **Fuente de updates** | GitHub Releases: `NavyHernandez/Remove_Top_Remix` |
| **Startup** | `VelopackApp.Build().SetAutoApplyOnStartup(true).Run()` en `App.xaml.cs:OnLaunched` |
| **Check** | `UpdateManager.CheckForUpdatesAsync()` → `UpdateInfo` o `null` |
| **Download** | `UpdateManager.DownloadUpdatesAsync(info, progress, ct)` con callback 0-100 |
| **Apply** | `UpdateManager.ApplyUpdatesAndRestart(info)` — reinicia y aplica |

### Flujo de actualización (usuario)
1. Pestaña **Cuenta** → "Buscar actualizaciones" → consulta GitHub Releases.
2. Si hay versión nueva: badge ámbar + botón **"Descargar vX.Y.Z"**.
3. Click en Descargar → ProgressRing con % → al llegar a 100% se aplica y reinicia.
4. Al reiniciar, `VelopackApp.Build().Run()` aplica los archivos descargados.

### Publicación de releases en GitHub
Para publicar una nueva versión, ejecutar el script `publish.ps1` desde la raíz del repo:

```powershell
# Publicar con versión del .csproj (0.3.1, etc.)
.\publish.ps1

# Forzar versión específica (p. ej. subir de minor/major manualmente)
.\publish.ps1 -Version "1.0.0"

# Solo empaquetar (sin subir a GitHub)
.\publish.ps1 -SkipUpload
```

El script automatiza:
1. `dotnet publish` (Release, win-x64, self-contained, **WindowsAppSDKSelfContained=true**)
2. `vpk pack` (crea .nupkg en `releases/`)
3. **Limpieza idempotente**: borra cualquier release/tag `v$Version` existente (incluidos drafts huérfanos) vía REST API antes de subir. Así re-ejecutar con la misma versión nunca falla con "already exists".
4. `vpk upload github` (sube a GitHub Releases con token), mostrando la salida real de vpk.

**Requisitos:** .NET 8 SDK + Velopack CLI (`dotnet tool install -g vpk`).
**Token:** via parámetro `-Token` o variable de entorno `GH_TOKEN` (no hardcodeado).

### Cómo hacer un NUEVO BUILD (empaquetado para otra PC)

Cuando el usuario pida "hacer un nuevo build" o "empaquetar para pasar a otras PC", el proceso correcto es:

1. **Subir la versión** en `Remove_Top/Remove_Top/Remove_Top.csproj` → `<Version>` (fuente única). Velopack exige versión mayor que la publicada (p. ej. `0.2.0 → 0.2.1`, `0.2.0 → 0.3.0`).
2. **Empaquetar** desde la raíz del repo (autorizado explícitamente por el usuario):
   ```powershell
   .\publish.ps1 -SkipUpload      # Solo genera el paquete en releases/ (sin subir a GitHub)
   ```
   (Para además subir a GitHub Releases: `.\publish.ps1` con `GH_TOKEN` definido.)
3. **El instalador para otra PC es `releases\OneDjApp-win-Setup.exe`** (doble clic → instala). También se generan `OneDjApp-<ver>-full.nupkg`, `OneDjApp-<ver>-delta.nupkg` (update/autoupdate de Velopack) y `OneDjApp-win-Portable.zip` (portátil).
4. **Verificar** que `releases\` contenga los archivos de la nueva versión y que el Setup.exe tenga fecha actual.
5. **CI automático**: al pushear a `main`, el workflow `.github/workflows/publish.yml` ejecuta `publish.ps1` y sube la release a GitHub Releases (requiere el secret `GH_TOKEN` en GitHub).

> NUNCA sustituyas esto por un `dotnet build` a secas: un build normal no produce el instalador. El entregable instalable SIEMPRE es el `Setup.exe` de `releases/` generado por `publish.ps1`.

### Auto-bump de versión (CI)

El workflow **deriva la versión automáticamente** del último tag de GitHub sumando 1 al patch:
- Último tag `v0.3.0` → publica `0.3.1` · `v0.3.1` → `0.3.2` · etc.
- Así cada push a `main` genera una versión nueva sin gestionarla a mano.
- Para subir de **minor/major**, forzar la versión manualmente (`publish.ps1 -Version "0.4.0"`) o crear el tag correspondiente.

### Workflow de branches

- **`main`** — única rama de trabajo y producción. Los releases de GitHub se publican automáticamente al pushear a `main` (push directo, sin PR obligatorio).
- **Protección**: `main` está protegida en GitHub con "Restrict who can push → solo `NavyHernandez`". Otros usuarios pueden abrir PRs (repo público), pero solo el dueño puede pushear o mergear.
- **Regla de push**: SOLO el usuario puede pedir que se empuje al repositorio. Los agentes NUNCA hacen push sin orden explícita.
- No hay rama `staging`; el desarrollo se hace en `main` (o en branches temporales que se fusionan directo).

### Orden standing: "empuja al repositorio"

Cuando el usuario diga **"empuja al repositorio"** (o "sube/pushea al repo"), con o sin número de versión, ejecuta SIEMPRE este checklist en orden (es una orden explícita: cubre documentar + versionar + empaquetar + commit + push):

1. **Documentar** — actualizar `AGENTS.md` (filas de funcionalidad afectadas), añadir entrada al final de `progress/history.md` (append-only), `progress/feature_list.json` si cambia el estado de alguna feature, y reescribir `Remove_Top/Assets/release_notes.txt` con las novedades de la versión.
2. **Versionar** — subir `<Version>` en `Remove_Top/Remove_Top.csproj` (patch +1 por defecto, o la versión exacta que indique el usuario, p. ej. "con la versión 0.3.2").
3. **Empaquetar** — ejecutar `.\publish.ps1 -SkipUpload` desde la raíz y verificar que `releases\OneDjApp-win-Setup.exe` tenga fecha actual (los artefactos de `releases/` están gitignoreados; el Release de GitHub lo publica el CI).
4. **Commit + push a `main`** — mensaje claro (p. ej. `v0.3.2: ...`), luego `git push`. El workflow CI crea el tag y el Release en GitHub automáticamente.
5. **Reportar** — URL del commit/push y resumen de lo publicado.

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
bin\x64\Debug\net8.0-windows10.0.19041.0\win-x64\OneDjApp.exe
```

## Solución de problemas

### La ventana se abre y cierra inmediatamente

1. Revisar `%LOCALAPPDATA%\Remove_Top\crash.log`
2. Verificar que el perfil correcto sea "Unpackaged" (no "Package")
3. Verificar que la plataforma sea "x64" (no "x86")
4. Verificar que la app esté compilada con `WindowsAppSDKSelfContained=true` (el Runtime se empaqueta automáticamente):
   ```powershell
   winget list "Windows App Runtime"
   ```

### Los cambios de XAML no se reflejan al ejecutar

- Existen **dos salidas de build**: `bin/Debug/...` (AnyCPU / `dotnet build`) y `bin/x64/Debug/net8.0-windows10.0.19041.0/win-x64/` (Debug|x64). El usuario lanza con **F5 en Visual Studio (Debug|x64)**: para ver los cambios hay que compilar `-p:Platform=x64`.
- El csproj tiene `<DisableFastUpToDateCheck>true</DisableFastUpToDateCheck>` para forzar la recompilación XAML en cada F5.
- **Cerrar instancias de `OneDjApp.exe` en ejecución antes de compilar**: el ejecutable queda bloqueado y la compilación falla.

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
- Etiquetas: caso de **solo lectura** real en `F:\GS FARRA 23\SUCIO\98 - 7A - Dani Flow - LMPDGTO (Harmony Extended).mp3` (el módulo quita el atributo, borra tags + portada incrustada + `folder.jpg`).

## Cómo agregar una nueva página

1. Crear una carpeta `Features/<Feature>/`
2. Crear archivos `NuevaPagina.xaml` y `NuevaPagina.xaml.cs` en esa carpeta (namespace `Remove_Top.Features.<Feature>`)
3. Agregar un `NavigationViewItem` en `MainWindow.xaml` con un Tag único
4. Agregar el case correspondiente en `MainWindow.xaml.cs` → `NavView_ItemInvoked`
5. Si recibe archivos/carpetas: envolver la raíz en `Controls/DropTargetControl` (con `AccentColor` del feature) y usar `Controls/FileSourceControl` para el origen (ver "Arrastre en toda la app").

## Cómo agregar un nuevo servicio

1. Crear archivo en la carpeta de su feature (`Features/<Feature>/`)
2. Definir clases de resultado y progreso (similar a `NormalizationResult` + `NormalizationProgress`)
3. Usar `IProgress<T>` para comunicación con la UI
4. Soporte de `CancellationToken` para cancelación

## Notas de compilación

- El proyecto usa `<WindowsPackageType>None</WindowsPackageType>` para modo unpackaged
- El proyecto usa `<WindowsAppSDKSelfContained>true</WindowsAppSDKSelfContained>` para empaquetar el Windows App SDK Runtime con la app (el usuario final NO necesita instalarlo aparte)
- No requiere proyecto `.wapproj` separado
- Los binarios se generan en `bin\$(Platform)\$(Configuration)\`
- La compilación requiere .NET 8 SDK y Windows SDK 10.0.19041+

## Delegación de I/O (hf_shunt — ayudante HF)

Este proyecto usa un **ayudante de lectura via Hugging Face Router** (modelo pequeño `Qwen/Qwen3.8-27B:ovhcloud`) para ahorrar tokens del modelo principal. Es como tener un asistente que lee archivos por ti y te trae solo el resumen/contexto.

**Ámbito: SOLO contexto (texto), NO código — y dentro del texto, SOLO `.json`/`.txt`.** El ayudante es para `progress/feature_list.json`, `Assets/release_notes.txt` y similares. NUNCA para código (`.cs/.xaml/.ps1/.csproj`): el código se lee directo con `read offset/limit` + `grep`/`glob`, porque el resumen pierde el texto exacto que `edit` necesita como `oldString`. **Tampoco para `.md`**: el ayudante no procesa Markdown de forma fiable (respuestas vacías) — los `.md` van directo por vía local (headings + `grep` + `read offset/limit`), sin gastar la llamada HF.

- **bulk_read** (única tool HF): para **entender/resumir** un archivo `.json`/`.txt` sin traerlo completo al contexto — ahorra 80-90% tokens. Para texto `> SHUNT_MIN_LINES=350` es obligatorio (el hook bloquea `read`/`cat` sin pipe); para texto pequeño es **opcional pero recomendado** si tu pregunta cabe en 5-10 bullets. Ej: `bulk_read paths="progress/feature_list.json" question="¿qué features faltan?"`. **Un archivo por llamada, siempre**: nunca `paths="a,b"` en una sola llamada (los lotes grandes devuelven vacío). Si hay N archivos de texto, N llamadas en paralelo.
- **code_write eliminado**: todo código se genera directo (más ágil). Si luego necesitas otra ayuda (tests, stubs), la agregamos como segunda tool HF.

Si `read` o `cat/head/tail` sin pipe es bloqueado (>350), el hook te pide `bulk_read`. Bypasses locales (cero coste): `grep`/`glob`, `read` con `offset/limit`, o `bash` con pipe `|`.

**Snippets locales (procesamiento sin API, cero coste):**
```bash
# Cualquier archivo — headings + nº líneas (decide si vale bulk_read)
python3 -c "import re,sys; p=sys.argv[1]; t=open(p,encoding='utf-8').read(); print('\n'.join(re.findall(r'^## .*',t,re.M))[:30]); print('lines',len(t.splitlines()))" "AGENTS.md"
python3 -c "import re,sys; p=sys.argv[1]; t=open(p,encoding='utf-8').read(); print('\n'.join(re.findall(r'^## .*',t,re.M))); print('lines',len(t.splitlines()))" "progress/history.md"

# Grep + lectura puntual (más eficiente que bulk_read si solo buscas 1 símbolo)
grep -rn "QuickRenameMaxFilesToScan\|QuickRenameLimitMessage" Remove_Top --include="*.cs" | head
# luego: read Remove_Top/Helpers/AppLimits.cs offset 100 limit 90

# Bash filtrado con pipe (pasa el hook porque usa |)
cat "progress/history.md" | head -n 80 | tail -n 40
python3 -c "import collections, re,sys; p=sys.argv[1]; t=open(p,encoding='utf-8').read(); print(collections.Counter(re.findall(r'^## .*',t,re.M)))" "progress/history.md"

# Contar líneas antes de leer (decide estrategia)
wc -l AGENTS.md progress/history.md progress/feature_list.json Remove_Top/Features/QuickRename/QuickRenamePage.xaml.cs

# Bulk voluntario en archivos pequeños (ahorra contexto vs read completo)
# bulk_read paths="progress/feature_list.json" question="¿qué features faltan?"
```

**Regla anti-vacío:** si `bulk_read` devuelve vacío, reintenta 1 vez el MISMO archivo con pregunta más corta (≤5 bullets). Si sigue vacío, usa el fallback local (`read` con `offset/limit` o `bash` con pipe `|`) e indícalo en tu resumen.

**Modo chunk local (texto > 350 líneas):** `bulk_read` lee el archivo completo y no admite rangos, así que los textos grandes se procesan en local sin el ayudante: 1) `wc -l` + headings para definir cortes; 2) `read offset/limit` por tramos de ≤150 líneas, cortando por heading completo; 3) 3-5 bullets por tramo (qué dice + decisiones + referencia `archivo:líneas`); 4) merge final de 8-12 bullets únicos — solo el merge queda en contexto. Si un tramo se necesita para editar, se re-lee el bloque exacto (el resumen nunca sirve como `oldString`).

**Receta fija de arranque:** toda sesión empieza con estos 2 pasos, en orden, sin `read` completo de `progress/`:
```bash
# bulk_read paths="progress/feature_list.json" question="¿qué features faltan? incluye referencia de línea"
cat "progress/history.md" | tail -n 25
```
Primero el estado (`.json` por ayudante), luego lo último (solo la cola del `.md`). Costo fijo y mínimo en cada arranque.

**Spot-check anti-alucinación:** todo dato crítico venido de `bulk_read` (nombres de constantes, límites, versiones) se confirma con 1 `grep` antes de usarlo en código o docs. El resumen orienta, el `grep` decide.

**Referencias en respuestas:** toda pregunta `bulk_read` termina con *"incluye referencia de línea"*. Permite saltar directo al `read offset/limit` exacto cuando ese dato se necesite para editar.

Config: `HF_MODEL` en `opencode.json:3` (repo), `HF_API_KEY` en `~/.config/opencode/opencode.json:4` (usuario, no commitear). El plugin (`shunt.ts`) la resuelve vía `process.env` con fallback al fichero global, así queda siempre disponible sin reiniciar.
