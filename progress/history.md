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
