# Bitácora histórica (append-only)

Cada vez que se cierra una sesión, su resumen se añade aquí. No edites entradas anteriores. Solo añades al final.

---

## 2026-09-08 — Feature 16 telemetría + fixes QuickRename + reorden de partes

**Agente:** humano + opencode (mimo-v2.5-free)

### Cambios realizados
1. **Telemetría (Feature 16)** — `Features/Telemetry/InstallTracker.cs` (ID anónimo + `install.json` local con `launchCount`/`lastLaunchAt`), `TelemetryService.cs` (fire-and-forget), `FirebaseRestApi.ReportInstallLaunchAsync` (upsert a Firestore `installs/{installId}` con API key), `FirebaseConfig.InstallsCollection`, hook en `App.OnLaunched`. Requiere reglas de Firestore `allow create, update: if request.auth == null` en la colección `installs`.
2. **QuickRename — reporte de fallos por archivo** — `QuickRenamer` devuelve `QuickRenameResult[]` con `Item`/`NewPath`; la página muestra los errores en `ResultErrorsList`; badges verde `✓`/ámbar `✕`; fix case-only (`Ordinal`); validación pre-vuelo de conflictos (`ValidateBatch`); actualización en el lugar de los ítems (la lista muestra los nombres nuevos).
3. **QuickRename — reorden de partes** — `NamePartReorderer` (bloques `BlockIndex`/`WordCount`, `Merge`/`Split`, `BuildSizes`, `ReorderName`); chips arrastrables horizontales; unir con doble clic (fuente → destino) y separar con clic derecho (solo separador espacio); botón deshacer; vista previa de los primeros 10 archivos; botón `UndoReorderButton`.
4. **Fix deshacer del movimiento** — el undo del reorden ahora se dispara con `CollectionChanged` + `Action == Move` (el `ListView` reordena la colección) usando `_lastStableChips`, en vez de depender de `DragItemsStarting/Completed` (bloqueados por el `DropTargetControl` con `AllowDrop`). Se eliminó `NamePartOrderComparer`.
5. **Fix cache de la guía** — `QuickRenameItem.LoadedName` (nombre al cargar, inmune a renames) + checkbox de **canción guía** por fila (selección única). La guía usa `LoadedName`, así que al reabrir "Reordenar" no arrastra posiciones previas.
6. **`DropTargetControl`** — `OnDragOver` ya no fuerza `AcceptedOperation = None` para drags sin `StorageItems` (deja pasar drags internos de controles hijos, p. ej. el reorder de ListView).

### Release v0.3.0
- Publicado en GitHub Releases (dot de notificaciones, sync versiones, controles de arrastre, telemetría, reorden de partes).

---

## 2026-09-08 — Versión 0.3.0: dot de notificaciones + sync versiones + controles arrastre

**Agente:** humano + opencode (mimo-v2.5-free)

### Cambios realizados
1. **`MainWindow.xaml`** — `InfoBadge` con `Severity="Attention"` en el ítem "Cuenta" del NavigationView (dot de notificaciones).
2. **`MainWindow.xaml.cs`** — `Current` estática, `CheckForUpdatesOnStartup()` en background al iniciar, `ShowUpdateDot()` / `HideUpdateDot()` públicos.
3. **`AccountPage.xaml.cs`** — `ApplyUpdateResult()` oculta el dot cuando no hay actualización; `DownloadUpdateButton_Click()` oculta el dot al iniciar descarga.
4. **`Remove_Top.csproj`** — `<Version>0.2.0</Version>` → `<Version>0.3.0</Version>` (sync con GitHub).
5. **`Assets/release_notes.txt`** — actualizado a v0.3.0 con todas las features nuevas.
6. **Duplicados** — `DuplicateRemovalPage.xaml/.cs` y `DuplicateScanner.cs` revertidos a la versión original (BrowseButton + FolderPicker, sin DropTargetControl/FileSourceControl).
7. **Controles compartidos** — `DropTargetControl` y `FileSourceControl` creados y usados por las otras features (BatchRename, QuickRename, Normalization, VocalRemoval, Tags) pero no por Duplicados.

### Investigación realizada
- **Actualizaciones**: GitHub API `/releases/latest` devuelve v0.1.3 (publicado después de v0.2.0) a pesar de que v0.2.0 tiene tag mayor. Solución: bump a v0.3.0.
- **Dot de notificaciones**: Investigado el mechanism existente (UpdateStatusBadge en AccountPage, 100% client-side, sin Firestore). Implementado dot en NavigationView con InfoBadge nativo de WinUI 3.

### Release v0.3.0
- Publicado en GitHub Releases con dot de notificaciones, sync de versiones y controles de arrastre.

---

## 2026-09-01 — Windows App SDK Runtime empaquetado (self-contained)

**Agente:** humano + opencode (mimo-v2.5-free)

### Cambios realizados
1. **`Remove_Top.csproj`** — agregado `<WindowsAppSDKSelfContained>true</WindowsAppSDKSelfContained>` para empaquetar el Windows App SDK Runtime con la app.
2. **`publish.ps1`** — agregados `-p:WindowsAppSDKSelfContained=true` y `-p:WindowsPackageType=None` al comando `dotnet publish`.
3. **Build** — `dotnet publish` self-contained con SDK Runtime incluido (362 MB en directorio publish).
4. **vpk pack** — generado instalador `OneDjApp-win-Setup.exe` (154 MB) con todo empaquetado.
5. **Limpieza** — eliminados ejecutables anteriores (`bin/Debug`, `bin/Release`, `bin/x64/Debug`, `OneDjApp-0.1.2-full.nupkg`).
6. **`AGENTS.md`** — actualizado: stack self-contained, notas de compilación, flujo de publicación, troubleshooting.

### Problema resuelto
Al instalar la app en otra PC aparecía el error "Required components of the Windows App Runtime are missing (MSIX package version >= 2.2.0.0)". Con `WindowsAppSDKSelfContained=true` el Runtime se incluye en el paquete y el usuario final NO necesita instalarlo aparte.

---

## 2026-08-21 — Versión 0.1.3: versión visible + copyright + novedades

**Agente:** humano + opencode (mimo-v2.5-free)

### Cambios realizados
1. **`AccountPage.xaml`** — copyright "© GH Dev Company" con icono Info debajo del branding.
2. **`AccountPage.xaml`** — botón [i] al lado de "Actualizaciones" que muestra/oculta la sección de novedades.
3. **`AccountPage.xaml.cs`** — versión instalada visible siempre al cargar la página (sin pulsar "Buscar actualizaciones").
4. **`Assets/release_notes.txt`** (nuevo) — archivo de texto editable antes de cada build con las novedades de la versión.
5. **`Remove_Top.csproj`** — `release_notes.txt` como Content con CopyToOutputDirectory; versión 0.1.3.
6. **`publish.ps1`** — fix ruta del csproj (`Remove_Top.csproj` en vez de `OneDjApp.csproj`) + flags vpk v1.2.0 (`--packId/--packVersion/--packDir/--outputDir`).
7. **`Assets/release_notes.txt`** — release notes de v0.1.3.

### Release v0.1.3
- Publicado en GitHub Releases con 6 assets (Setup, portable, nupkg full + delta).
- Delta de v0.1.2 → v0.1.3 generado automáticamente por Velopack.

---

## 2026-08-21 — Velopack 1.2.0 + fix de actualizaciones

**Agente:** humano + opencode (mimo-v2.5-free)

### Cambios realizados
1. **Velopack NuGet** — actualizado de `0.0.1298` a `1.2.0` (igual que CLI vpk).
2. **`UpdateChecker.cs`** — usa `GithubSource` explícito en vez del constructor string (fix de detección de updates).
3. **`UpdateChecker.cs`** — errores se loguean en `crash.log` en vez de tragarse silenciosamente.
4. **`App.xaml.cs`** — `VelopackApp.Build().Run()` con try-catch para unpackaged/debug.
5. **Versión** — 0.1.2 (corrección de bugs leves).

### Release v0.1.2
- Publicado en GitHub Releases con 5 assets.

---

## 2026-08-20 — Renombrado a "One Dj App" + iconos + publish script

**Agente:** humano + opencode (mimo-v2.5-free)

### Cambios realizados
1. Renombrado completo "Top Dj App" → "One Dj App" (AppLimits, MainWindow, AccountPage, AGENTS.md).
2. Iconos reales copiados de `iconos/` a `Assets/` (AppIcon.ico, SplashScreen, StoreLogo, etc.).
3. `Win32Helper.cs` — P/Invoke para icono de barra de título WinUI 3 unpackaged.
4. `AssemblyName` = `OneDjApp` (salida: `OneDjApp.exe`).
5. `DuplicateRemoval` — botón "Cancelar" reemplazado por "Limpiar" con icono Broom.
6. `AccountPage` — logo en círculo blanco con StoreLogo.png.
7. `publish.ps1` — script automatizado de publicación Velopack.

---

## 2026-08-20 — Velopack: auto-update + versionado + branches

**Agente:** humano + opencode (mimo-v2.5-free)

### Cambios realizados
1. **Velopack** integrado para auto-updates desde GitHub Releases.
2. **`UpdateChecker.cs`** — `UpdateManager(GitHubRepoUrl)` con Check/Download/Apply.
3. **`AccountPage`** — botón Descargar con ProgressRing de progreso.
4. **`Remove_Top.csproj`** — `<Version>0.1.0</Version>`.
5. **Branches** — workflow `feature branches → staging → main (producción)`.

---

## 2026-08-19 — Firebase auth + Firestore + verificación de correo

**Agente:** humano + opencode (deepseek-v4-flash-free)

### Cambios realizados
1. **Firebase Email/Password auth** — `AuthService.cs` con login/registro/logout y `PasswordVault`.
2. **Gate de verificación** — login sin verificar NO abre sesión; polling cada 5 s para auto-login.
3. **Auto-creación** — login de cuenta inexistente la crea y envía verificación.
4. **Firestore** — `FirebaseRestApi.cs` para guardar sugerencias vía REST.
5. **AccountPage** — formulario login/registro con icono ojo, paneles de verificación, sección Sugerencias.
6. **Fix** — mapeo de `INVALID_LOGIN_CREDENTIALS` a "Correo o contraseña incorrectos".

---

## 2026-08-19 — Features 22, 24, 25: icono encabezado + preview imágenes + archivos examinados

**Agente:** humano + opencode (deepseek-v4-flash-free)

### Cambios realizados
1. **Feature 22** — icono de badge coherente con icono del menú en cada página.
2. **Feature 24** — previsualizador de imágenes en Duplicados (`Features/ImagePreview/`).
3. **Feature 25** — contador de archivos examinados en el encabezado de Resultados de Duplicados.
4. **Feature 11** — botón Cancelar durante análisis de Normalización.

---

## 2026-08-13 — Fundamentos: features base + estructura progress

**Agente:** humano + opencode (mimo-v2.5-free)

### Cambios realizados
1. **BatchRename** — simplificación de card de IA, cliente HTTP unificado.
2. **QuickRename** — eliminación total de IA, fix validación `.mp3`, tope 200.
3. **Normalization** — corrección ortográfica automática (`SpanishNameCorrector`), limpieza de nombres, botón Cancelar.
4. **DuplicateRemoval** — `SubsetNameDetector` (nombre contenido), fix extensiones múltiples, botones centrados.
5. **Helpers** — `AppLimits.cs` (límites centralizados), `PremiumLinks.cs`.
6. **`progress/`** — estructura de agentes con `history.md` y `feature_list.json`.

---

## 2026-09-09 — Versión 0.3.2: menú de caso + marca en Origen (QuickRename)

**Agente:** humano + opencode (muse-spark)

### Cambios realizados
1. **Menú de caso** — `CaseButton` (DropDownButton compacto icon-only `TextChangeCase`, opciones 12px): `MAYÚSCULAS` / `minúsculas` / `Capitalizar Palabras` (cultura `es`), solo base sin tocar extensión, pre-llenado en vivo reversible con "Restaurar originales".
2. **Marca al Origen** — `www.top-remix.com` sale del header de la lista y pasa centrada a la línea "Origen" (`FileSourceControl.ShowBrandSite`, opt-in solo en QuickRename; grid `Auto,*,Auto`).
3. **Icono Reordenar** — `ArrowBidirectionalUpDown` → `ArrowSwap`.
4. **P1 robustez** — aviso "(mostrando los primeros 200 de N)" al truncar; `EnsureWritable` antes de `File.Move`; `IOException` distingue "ya existe" vs "en uso".
5. **Docs** — `AGENTS.md`: orden standing "empuja al repositorio" + fila QuickRename actualizada + `ShowBrandSite` en controles.

### Release v0.3.2
- Instalador `releases\OneDjApp-win-Setup.exe` regenerado con `publish.ps1 -SkipUpload`; push a `main`, Release publicado por CI.

---

## 2026-09-09 — Versión 0.3.3: tarjeta de Soporte en Cuenta (QR + WhatsApp)

**Agente:** humano + opencode (muse-spark)

### Cambios realizados
1. **Tarjeta Soporte** (`AccountPage.xaml/.cs`) — al final de Cuenta: QR (`Assets/SupportQr.jpeg`, registrado como Content) + título "Soporte" + botón sólido teal "Abrir chat de WhatsApp" (`wa.me/593982311600`, centrado y compacto 96px).
2. **Estilo glass neutro** — base de la app (`LayerFillColorDefaultBrush`), sombra `ThemeShadow` como recurso (`SupportCardShadow` + `Translation`), velo superior + línea de luz + acento teal lateral; sin verdes (se descartó el degradado inicial).
3. **Placeholder QR** — `SupportQrImage_Failed` muestra icono `QrCode` + "QR próximamente" si falta la imagen.
4. **Fixes de compilación** — `Shadow="{ThemeShadow}"` → recurso + `StaticResource` (error 0x09e1); `>>` duplicado en el `Border` (error "Child no admite String"). Build x64 0 errores.
5. **Docs** — `AGENTS.md` (fila Cuenta + sección Soporte), `feature_list.json` (feature 33 `account_support_card`), `release_notes.txt` v0.3.3.

### Release v0.3.3
- Instalador `releases\OneDjApp-win-Setup.exe` regenerado con `publish.ps1 -SkipUpload`; push a `main`, Release publicado por CI.

---

## 2026-09-11 — Versión 0.3.4: QuickRename 1000+recursivo, patrones 30, arrastre Win10, BatchRename más rápido

**Agente:** humano + opencode (muse-spark)

### Cambios realizados
1. **Edición Rápida: tope 1000 + recursivo** — `AppLimits.QuickRenameMaxFilesToScan` 200 → 1000 (única fuente); `QuickRenamePage.xaml` `ScanRecursive="False" → "True"` (antes no traía subcarpetas); comentario actualizado en el code-behind. Textos y contadores se actualizan solos desde la constante.
2. **Renombrado masivo: 30 patrones** — `AppLimits.BatchRenameMaxPatterns` 20 → 30; carga, validación, contador `n/30` y sugerencias IA ya consumían la constante. Sin migración (`patterns.json` con ≤20 sigue válido).
3. **Arrastre robusto en Win10** — `DropTargetControl`: raíz con `Background="Transparent"` (requisito de la doc de drag-and-drop); `OnDrop` captura fallos de `GetStorageItemsAsync` (bug Win10 aunque el overlay se muestre) y rutas vacías (virtuales/ZIP): registra en `crash.log` (`App.Log`) y notifica el nuevo evento `DropFailed`; las 5 páginas lo conectan y muestran el motivo con `SetStatus`. Ruta de éxito intacta.
4. **BatchRename más rápido (opción A)** — `FileRenamer.ProcessFilesAsync` por lotes de 10 (`ChunkSize`, un `Task.Run` por lote: 700 archivos = 70 saltos en vez de 1400); `Regex` de espacios precompilado; página sin `ScrollIntoView` por fila (uno solo al final), contadores O(1), nombre cada 10 + siempre en el último. Con <10 archivos todo se pinta en una pasada.
5. **Docs** — `AGENTS.md` (filas QuickRename/BatchRename + DropTargetControl + rendimiento), `feature_list.json` (features 2 y 3), `release_notes.txt` v0.3.4.

### Release v0.3.4
- Instalador `releases\OneDjApp-win-Setup.exe` regenerado con `publish.ps1 -SkipUpload`; push a `main`, Release publicado por CI.

---

## 2026-09-11 — Versión 0.3.5: Normalización pro (LUFS + true-peak), arrastre nativo, preview y popup de update

**Agente:** humano + opencode (muse-spark)

### Cambios realizados
1. **Normalización: arrastre nativo con análisis (feature 34)** — se revirtió el intento con `DropTargetControl`/`FileSourceControl` (colgaba en "Analizando" porque analizan al cargar). Arrastre propio: `Grid` raíz con `AllowDrop` + overlay local ("Suelta para cargar", insignia `FolderOpen` #5B9BD5, timer anti-parpadeo 250 ms). Una sola carpeta = camino manual idéntico (`LoadFolderAsync`); canciones sueltas se **acumulan hasta 50** (`NormalizationFreeLimitDisplay`) sin repetir, con análisis incremental. Timeout **20 s** por archivo + cancelación por bloque; Limpiar/Cancelar detienen lo en curso. Salida en **`OneDj_Normalized`**; aviso "Versión gratuita: arrastra y analiza hasta 50 archivos".
2. **Normalización: previsualizador + quitar con "x" (feature 35)** — botón play y botón `Dismiss` por fila en `AnalysisListView`; tarjeta `PreviewSection` reutilizando `AudioPreview` (onda 88 px + Play/Pausa/Stop + reloj + scrub). `AnalysisResult` guarda `FilePath`; Normalizar procesa la lista mostrada (ya no re-escanea). **Fix de carrera**: `valid` se calcula con el array devuelto (backing `_analysisAll`); filas fallidas muestran "—" con tooltip del motivo; resumen "N válidos · M con error".
3. **Masterización pro (feature 36)** — `LoudnessMeter` nuevo (K-weighting BS.1770-4, bloques 400 ms/75 %, gating −70/−10 LU) → ganancia por **LUFS a 2 pasadas** (Hard **−10**, EDM **−8.5** LUFS). `TruePeakLimiter` (lookahead, detección true-peak 4× Catmull-Rom, release adaptativo, enlace estéreo, techo **−1.0 dBTP**) + `SoftClipper` + compresor enlazado + front-end (DC blocker + HPF 30 Hz) + **dither TPDF** adaptativo. Compresor EDM attack 8 → 22 ms (punch). Ligera intacta. Resultado muestra Pico + LUFS.
4. **Cuenta: popup de actualización con rebote (feature 37)** — al entrar con update pendiente se eleva tarjeta (icono `Gift`, BounceEase) una vez por versión; "Descargar ahora" inicia la descarga directa (`StartDownloadAsync` refactorizado); "Ahora no" conserva dot y botón. `UpdateChecker` expone `HasPendingUpdate`/`PendingVersion`.
5. **Docs** — `AGENTS.md` (filas Normalización/Cuenta + masterización pro), `feature_list.json` (features 34–37), `release_notes.txt` v0.3.5.

### Release v0.3.5
- Instalador `releases\OneDjApp-win-Setup.exe` regenerado con `publish.ps1 -SkipUpload` (build 0 errores); push a `main`, Release publicado por CI.

---

## 2026-09-12 — Versión 0.3.6: arrastre en Duplicados, "x" por fila y Stop visible

**Agente:** humano + opencode (muse-spark)

### Cambios realizados
1. **Normalización: botón Stop visible (feature 40)** — `PreviewStopButton` no tenía `Content` (invisible aunque habilitado): se inicializan `PreviewPlayButton` (Play) y `PreviewStopButton` (Stop) en el constructor, mismo patrón que Duplicados.
2. **Normalización: "x" más profesional (feature 40)** — icono de quitar por fila `Dismiss` → `DismissCircle`.
3. **Duplicados: arrastre nativo de carpeta (feature 38)** — sin `DropTargetControl`/`FileSourceControl`: `Grid` raíz con `AllowDrop` + overlay local ("Suelta para cargar", insignia `Copy` #E74C3C, timer anti-parpadeo 250 ms). **Solo carga la ruta** (no auto-escanea); archivos sueltos o varias carpetas muestran aviso en `ScanStatusText`; drop ignorado durante escaneo/borrado; `try/catch GetStorageItemsAsync` con `App.Log`.
4. **Duplicados: quitar fila con "x" (feature 39)** — botón `DismissCircle` al final de cada fila en las 3 pestañas (`DismissResultItem_Click`, `Tag=FilePath`): desuscribe, cierra el preview si era el archivo mostrado (audio o imagen), remueve de su colección y refresca contadores/resumen/acciones. Solo quita de la lista, no toca el disco.
5. **Docs** — `AGENTS.md` (filas Normalización/Duplicados + arrastre + versión 0.3.6), `feature_list.json` (features 38–40), `release_notes.txt` v0.3.6.

### Release v0.3.6
- Instalador `releases\OneDjApp-win-Setup.exe` regenerado con `publish.ps1 -SkipUpload` (build 0 errores); push a `main`, Release publicado por CI.

---

## 2026-09-15 — Funcionalidad nueva: Descarga de YouTube (sin release)

**Agente:** humano + opencode (muse-spark)

### Cambios realizados
1. **Motor on-demand sin binarios en el instalador (feature 41)** — `ToolManager` aprovisiona en `%LOCALAPPDATA%\Remove_Top\tools`: Python embebido (firmado PSF, `python-3.12.10-embed`) + wheels `yt-dlp`/`yt-dlp-ejs` de PyPI (evita el `.exe` de PyInstaller que marca el antivirus) + `deno` + `ffmpeg` LGPL de BtbN, con **auto-update** del wheel (compara `pypi.org/pypi/yt-dlp/json`), extracción **anti zip-slip** y borrado del **MOTW** (`:Zone.Identifier`). El instalador **no crece** (la funcionalidad suma ~60–100 KB de código); el motor ocupa ~528 MB on-demand (dominado por ffmpeg 398 MB).
2. **Descarga + normalización encadenada** — `YtDlpService` (`python.exe -m yt_dlp`, `ArgumentList` sin inyección, `--newline`, centinela `after_move:onedj:%(filepath)s`, `-f bestaudio/best -x --audio-format wav`, kill del árbol al cancelar, salvaguarda del `.wav`/`.m4a` más reciente). La página descarga a la carpeta destino y llama a `AudioNormalizer.ProcessFilesAsync` (perfil fijo **HardLimiter −1.0 dBFS**); al terminar **borra el WAV intermedio** y deja solo `OneDj_Normalized`. **Fix de ruta con espacios**: la captura de la ruta final exigía "sin espacios" y siempre fallaba (la carpeta del usuario tiene espacio); se corrigió con centinela + salvaguarda (verificado con descarga real a carpeta temporal).
3. **Anti-bot (fix verificado)** — YouTube exige proof-of-origin al cliente `web` (bot-check sin cookies). Verificado con `--simulate` sobre el vídeo del reporte: `default`/`default,-web` dan bot-check; `tv,web_safari,mweb` extrae pero solo format 18 (44 kbps); **`web_embedded` da Opus 251 ≈133 kbps sin cookies**. Se fuerza `--extractor-args "youtube:player_client=web_embedded,tv,web_safari,mweb"` + `--sleep-requests 1`. Se investigó y descartó la vía de cookies (`--cookies-from-browser` roto en Chrome/Edge por App-Bound Encryption; se quitó la sección de cookies de la UI).
4. **Popup de error con reintento (feature 42)** — mismo patrón que Cuenta (velo `#66000000` + tarjeta con `BounceEase`, icono `Warning` ámbar, borde `#19376D`). Aparece al terminar con fallos o si el motor no queda listo; **"Intentar de nuevo"** (`RunFlowAsync` compartido) reprocesa solo los enlaces en estado `Error`. `DownloadErrorText` traduce los errores crudos a mensajes simples sin códigos (bot-check, red, no disponible, 403, formato); el detalle va a `crash.log` y las filas muestran el texto simple.
5. **UI azul marino + unificada (feature 43)** — acento `#19376D` centralizado en `Page.Resources` (`DownloaderAccentBrush/Soft/Light`); item de menú "Descargar" con icono `ArrowDownload` en **blanco** (el azul no se veía en fondo oscuro); botones de acción sólidos unificados (fondo marino, letras blancas) con hover blanco global (`Button*PointerOver/Pressed` en recursos); botón "Pegar" eliminado; check **"Normalizar al descargar"** arriba (perfil fijo); subtítulo del encabezado reducido a 12 px indicando la subcarpeta; InfoBar permanente eliminado (queda como aviso contextual solo ante advertencias/errores).

### Notas
- Build Debug x64: 0 errores, 0 advertencias. **No se hizo release, publish, commit ni push.**
- Verificado con descarga real a carpeta temporal: sin bot-check y genera el WAV normalizado.
- `progress/feature_list.json`: features 41–43 (`youtube_downloader`, `downloader_error_popup`, `downloader_ui_polish`).

---

## 2026-09-18 — Descarga YouTube: proveedor PO tokens + cascada anti-bot (sin release)

**Agente:** humano + opencode (muse-spark)

### Cambios realizados
1. **Fase 1 — PO tokens bgutil (`ToolManager`)** — `EnsureBgUtilAsync`: aprovisiona el plugin `bgutil-ytdlp-pot-provider` (wheel PyPI, misma mecánica que yt-dlp) + la carpeta `server/` del MISMO tag en GitHub (`ExtractZipSubfolder`, anti zip-slip) + `deno install --allow-scripts=npm:canvas --frozen` on-demand (`RunDenoInstall`, idempotente). Versión conjunta en `tools/bgutil/bgutil.version` (plugin y scripts deben compartir major). `IsBgUtilReady` exige plugin + `src/generate_once.ts` + `node_modules`. Deno con versión mínima 2.4.3 (`IsDenoVersionSupported`, re-descarga si es viejo). Degradación elegante: si GitHub falla, se sigue sin PO tokens. Cachés contenidas en `tools/` (`YtDlpCacheDir`, `DenoCacheDir`, `XDG_CACHE_HOME`).
2. **Fase 2 — Cascada + red (`YtDlpService`)** — cadena principal `web,web_embedded,tv,web_safari,mweb` (con PO tokens el `web` rinde mejor) + cascada de reintento `tv,web_safari,mweb` → `android` (pausa 8 s, solo si `IsBotCheck`). `--force-ipv4 --retries 5 --fragment-retries 5 --retry-sleep exp=1:20 --sleep-interval 2`; `--cache-dir` propio (antes `--no-cache-dir`); `DENO_DIR`/`XDG_CACHE_HOME` en el entorno del proceso. `IsBotCheck` ampliado (429, login required) excluyendo restricción de edad.
3. **Fase 3 — Errores (`DownloadErrorText`)** — causas definitivas primero; nuevos buckets: región, embed deshabilitado, DRM/protegido, 429.
4. **Docs** — `AGENTS.md` (fila Descarga: bgutil + cascada + clasificación).

### Notas
- Verificado con descarga real (Never Gonna Give You Up): `[pot] PO Token Providers: bgutil:script-deno-2.0.0 (external)` genera GVS PO token para `web`; descarga Opus → WAV 40 MB con centinela correcto; 2.ª corrida reutiliza token de caché. Doble `--extractor-args` combina bien.
- **No se hizo build, publish, commit ni push.**

---

## 2026-09-18 — Descarga YouTube completa + v0.4.0 (sin release previo, release por CI)

**Agente:** humano + opencode (muse-spark)

### Cambios realizados
1. **Límite 20 enlaces (feature 41, ext.)** — `AppLimits.DownloaderMaxLinksPerBatch = 20` + `DownloaderLimitMessage`; badge "Versión Gratuita" en tarjeta Enlaces, contador `n/20`, bloqueo al agregar (duplicados libres), `Take(20)` defensivo. Subtítulo sin "cadena profesional".
2. **Anti-bot POT + cascada (feature 44)** — bgutil (`EnsureBgUtilAsync`, `deno install`, deno ≥2.4.3), cadena `web,...` con PO tokens, cascada tv→android con pausa 8 s, `--force-ipv4/retries/retry-sleep/sleep-interval`, caché propia, clasificación de errores (región/embed/DRM/429). Verificado con descarga real (WAV 40 MB).
3. **Cuenta YouTube opcional (feature 45)** — `YouTubeSession` + `YouTubeLoginDialog` (WebView2, perfil aislado, autodetección, export Netscape); intento `--cookies` prioritario con fallback anónimo (`IsDefinitiveFailure`/`IsSessionDead`); tarjeta 3 estados + sugerencia; OAuth muerto (descartado). Fixes de compilación WinRT (`CreateWithOptionsAsync`, `.AsTask()`, `Icon.PersonAdd`).
4. **Refinamientos (feature 46)** — check "Conservar original" (NO por defecto), un solo Limpiar (`EndActionsSection`), botón fantasma "Abrir ubicación" (Explorador a `OneDj_Normalized`/destino), canonicalización de URLs (`watch?v=ID`, fix `youtu.be`), verificación tolerante del archivo (rescate + `output-not-found`), popup centrado, checks en una fila, `DuplicateScanner` excluye `OneDj_*`.
5. **Docs** — `AGENTS.md` (fila Descarga), `feature_list.json` (features 44–46), `release_notes.txt` v0.4.0.
6. **Versión 0.4.0** — `<Version>` en csproj; paquete con `publish.ps1 -SkipUpload`; commit + push a `main` (Release por CI).

### Notas
- Compilación verificada vía `publish.ps1` (build Release incluido).
