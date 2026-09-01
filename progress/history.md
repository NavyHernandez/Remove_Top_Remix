# Bitácora histórica (append-only)

Cada vez que se cierra una sesión, su resumen se añade aquí. No edites entradas anteriores. Solo añades al final.

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
