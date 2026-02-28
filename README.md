# 🎭 Raka

**Playwright-like automation for WinUI 3 apps.**

Raka lets AI agents (and humans) see, inspect, modify, and interact with running WinUI 3 applications — making it faster to build UI with tools like GitHub Copilot.

```
raka inspect --app MyApp                          # See the full visual tree
raka search -t Button --app MyApp                 # Find all buttons
raka set-property e5 Background "#FF5500"         # Change a property live
raka add-xaml e6 "<Button Content='New'/>"        # Inject XAML at runtime
raka hot-reload src/MyApp/ --app MyApp             # Watch dir & live-reload all XAML
raka screenshot -f screenshot.png                 # Pixel-perfect screenshot
raka tap-inspect --app Calculator                 # Inspect ANY WinUI 3 app (no NuGet)
```

---

## Two Modes of Operation

Raka has two fundamentally different ways to connect to a WinUI 3 app. Choose based on your situation:

### 🔌 NuGet Mode (full power — your own apps)

Add the `Raka.DevTools` NuGet package to your app. Gives you **full read/write access**: inspect, modify properties, inject XAML, click buttons, take screenshots. Best for apps you're building.

**All commands** work in this mode: `inspect`, `search`, `get-property`, `set-property`, `click`, `screenshot`, `add-xaml`, `remove`, `replace`, `hot-reload`, `ancestors`.

### 🔍 TAP Mode (read-only — any app)

Use `tap-inspect` to inject a diagnostics DLL into **any running WinUI 3 app** — no NuGet needed. Gives you a read-only view of the visual tree with types, names, sizes, and key properties. Best for inspecting third-party apps or apps you can't modify.

**Only `tap-inspect`** works in this mode — it's a standalone one-shot command.

| Capability | NuGet Mode | TAP Mode |
|---|---|---|
| **Setup required** | Add NuGet package | None |
| **Works on any app** | ❌ Only your apps | ✅ Any WinUI 3 app |
| **Visual tree** | ✅ Full tree with IDs | ✅ Full tree with handles |
| **Properties** | ✅ Read + write | ✅ Key properties (read-only) |
| **Modify UI** | ✅ set-property, add-xaml, replace, remove | ❌ |
| **Click / interact** | ✅ click | ❌ |
| **Screenshots** | ✅ capture + render modes | ❌ |
| **XAML injection** | ✅ add-xaml, replace, hot-reload | ❌ |
| **Element IDs** | ✅ Stable across commands (e0, e1...) | Instance handles (per-session) |

---

## Quick Start — NuGet Mode

### 1. Add the NuGet package to your WinUI 3 app

```bash
dotnet add package Raka.DevTools
```

### 2. Run your app, then use the CLI

```bash
raka inspect --app MyApp
```

**That's it.** No code changes needed — zero-code setup. The NuGet package automatically discovers your `Window` and starts a named pipe server. The CLI connects to it and sends commands.

> **How it works:** Raka injects a `[ModuleInitializer]` into your app at compile time that uses a Win32 timer to safely attach to the UI thread after your `Window` is created. It reflects over your `App` class to find `Window`-typed fields/properties and calls `UseRakaDevTools()` automatically.

<details>
<summary>Advanced: Manual initialization</summary>

If auto-init doesn't find your window (e.g., it's not stored as a field on your `App` class), you can initialize explicitly:

```csharp
#if RAKA_DEVTOOLS
using Raka.DevTools;
#endif

protected override void OnLaunched(LaunchActivatedEventArgs args)
{
    _window = new MainWindow();
#if RAKA_DEVTOOLS
    _window.UseRakaDevTools();
#endif
    _window.Activate();
}
```

The `RAKA_DEVTOOLS` symbol is automatically defined in Debug builds only — `#if` blocks compile out in Release.

To disable auto-init (when using manual initialization), add to your `.csproj`:

```xml
<PropertyGroup>
  <RakaDevToolsAutoInit>false</RakaDevToolsAutoInit>
</PropertyGroup>
```

</details>

---

## Quick Start — TAP Mode

No setup needed. Just point at any running WinUI 3 app:

```bash
raka tap-inspect --app Calculator
raka tap-inspect --pid 12345
```

Returns the full visual tree as JSON with element types, names, sizes, and key properties (Name, Text, Content, Visibility, IsEnabled, margins, alignment, etc.).

> **How it works:** Raka injects a native C++ DLL into the target process via `InitializeXamlDiagnosticsEx` — the same API used by Visual Studio's Live Visual Tree. The DLL walks the XAML visual tree using `IVisualTreeService` and sends the data back over a named pipe.

> **Requirements:** The `raka_tap.dll` native DLL must be built and placed next to `raka.exe` (or in a `tap/` subdirectory). See [Building from Source](#building-from-source) for build instructions.

---

## Installation

### CLI (standalone binary)

Download the latest release from [GitHub Releases](https://github.com/nicholasmelo/project-raka/releases) and add it to your PATH.

**Or build from source:**

```powershell
git clone https://github.com/nicholasmelo/project-raka.git
cd project-raka
.\build.ps1 -Runtime win-x64 -SkipNuGet
# Binary is at: artifacts/cli/raka-win-x64/raka.exe
```

### NuGet package (in-app)

```bash
dotnet add package Raka.DevTools
```

---

## Commands

### Targeting an app

Every NuGet-mode command accepts `--app` or `--pid` to specify which running app to connect to:

```bash
raka inspect --app MyApp          # Match by process name or window title
raka inspect --pid 12345          # Match by process ID
```

The first command that connects saves the target as a default session. Subsequent commands reuse it automatically:

```bash
raka inspect --app MyApp          # Connects and saves default
raka search -t Button             # Reuses saved target
raka click e5                     # Reuses saved target
```

Use `raka list` to see active connections and `raka disconnect` to clear the default.

---

### `inspect` — View the visual tree

```bash
raka inspect                      # Full tree
raka inspect -d 2                 # Limit depth to 2 levels
raka inspect -e e5                # Subtree from element e5
```

Returns JSON with element IDs (`e0`, `e1`, ...), types, names, bounds, and children. Element IDs are stable across commands and used by all other commands.

<details>
<summary>Example output</summary>

```json
{
  "id": "e0",
  "type": "Grid",
  "className": "Microsoft.UI.Xaml.Controls.Grid",
  "bounds": { "x": 0, "y": 0, "width": 794.7, "height": 643.3 },
  "visibility": "Visible",
  "children": [
    {
      "id": "e6",
      "type": "StackPanel",
      "name": "MainPanel",
      "children": [
        { "id": "e7", "type": "TextBlock", "name": "WelcomeText" },
        { "id": "e8", "type": "TextBox", "name": "NameInput" },
        { "id": "e9", "type": "Button", "name": "GreetButton" }
      ]
    }
  ]
}
```
</details>

---

### `search` — Find elements

```bash
raka search -t Button             # By type
raka search -n GreetButton        # By x:Name
raka search --text "Hello"        # By text content
raka search --automation-id save  # By AutomationId
```

Multiple criteria can be combined (AND logic).

---

### `get-property` / `set-property` — Read and modify properties

```bash
raka get-property e5 Background   # Single property
raka get-property e5 -a           # All DependencyProperties
```

```bash
raka set-property e5 Background "#FF5500"       # Hex color
raka set-property e5 Margin "10,20,10,20"       # Thickness
raka set-property e5 Visibility "Collapsed"     # Enum
raka set-property e5 FontSize "24"              # Numeric
raka set-property e5 CornerRadius "8"           # CornerRadius
raka set-property e4 Text "Hello, World!"       # String
```

Changes take effect immediately. Supports colors (hex, named), Thickness, CornerRadius, GridLength, enums, and primitives.

---

### `add-xaml` — Inject XAML into the live app

```bash
raka add-xaml e6 "<Button Content='Click Me' Background='#FF5500'/>"
raka add-xaml e6 "<TextBlock Text='Hello' FontSize='24'/>" --index 0
```

Parses XAML at runtime via `XamlReader.Load()` and adds it as a child of the target element. The `--index` option controls insertion position (default: append).

**Supported parent types:** Panel (Grid, StackPanel, etc.), ContentControl, Border, Viewbox, ContentPresenter.

> **Note:** `{Binding}` works in injected XAML (inherits DataContext), but `{x:Bind}` does **not** (it's compiled at build time). Event handlers from code-behind are also not available.

---

### `replace` — Replace an element with new XAML

```bash
raka replace e7 "<TextBlock Text='Replaced!' FontSize='32' Foreground='Red'/>"
```

Removes the target element from its parent and inserts the new XAML in its place at the same position.

---

### `hot-reload` — Watch XAML files and live-reload

**Single file mode:**
```bash
raka hot-reload ui.xaml --target-name MainPanel --app MyApp   # Target by x:Name
raka hot-reload ui.xaml --element e3 --app MyApp              # Target by element ID
raka hot-reload ui.xaml --app MyApp                           # Auto-detect target
```

**Directory mode (whole app):**
```bash
raka hot-reload src/MyApp/ --app MyApp                        # Watch all XAML files
raka hot-reload --app MyApp                                   # Watch current directory
```

Directory mode auto-maps each XAML file to its live element via `x:Class`. When any `.xaml` file is saved, the corresponding element in the running app is updated automatically. Files not currently visible in the tree (e.g., a Page that hasn't been navigated to) are skipped.

**Targeting options (single-file mode):**
- `--target-name <name>` / `-n` — Find the element by its `x:Name` (recommended — stable across reloads)
- `--element <id>` / `-e` — Use a specific element ID (from `inspect`)
- *(omit both)* — Auto-detects by matching the XAML file's root element type against the live tree

**How it works:**
1. Connects to the running app (NuGet mode)
2. Parses `x:Class` from each XAML file to identify the target element
3. For Window files, replaces the Window's content (root Grid/Panel)
4. For other types, searches the live tree by class name
5. Watches for file changes — on each save, re-parses and re-replaces
6. Attached properties (`<Window.SystemBackdrop>`, `<Page.Resources>`, etc.) are preserved/skipped

**Example workflow:**
```bash
# Watch the entire project — edit any XAML file and see changes immediately
raka hot-reload src/MyApp/ --app MyApp

# Or watch a single file targeting a specific element
raka hot-reload panel.xaml --target-name MainPanel --app MyApp

# Press Ctrl+C to stop watching
```

> **Note:** Uses `XamlReader.Load()` under the hood, so `{Binding}` works but `{x:Bind}` and event handlers do not survive reload. Best for rapid visual prototyping.

---

### `remove` — Remove an element

```bash
raka remove e7
```

Removes the element from the visual tree. The element and its children are no longer rendered.

---

### `click` — Interact with controls

```bash
raka click e5                     # Invoke a button
raka click e6                     # Toggle a checkbox
raka click e7                     # Select a radio button
```

Uses the UI Automation peer system. Supports:
- **Buttons / Hyperlinks / MenuItems** → Invoke
- **CheckBoxes / ToggleSwitches** → Toggle
- **RadioButtons / ListItems** → Select

---

### `screenshot` — Capture the app

```bash
raka screenshot -f window.png                     # Whole window (pixel-perfect)
raka screenshot e5 -f button.png                  # Specific element
raka screenshot --mode capture -f perfect.png     # Force capture mode
raka screenshot --mode render --bg "#1E1E1E" -f dark.png  # Render with background
```

**Two screenshot modes:**

| Mode | Flag | How it works | Best for |
|------|------|-------------|----------|
| **capture** | `--mode capture` | `Windows.Graphics.Capture` — real screen pixels | Full window, includes Mica/Acrylic |
| **render** | `--mode render` | `RenderTargetBitmap` — XAML render | Specific elements, works offscreen |

**Default behavior** (auto mode): `capture` for full window, `render` for elements.

The `--bg` option composites the render output onto a solid background color — useful when theme-aware text is invisible against the transparent backdrop.

---

### `ancestors` — Parent chain

```bash
raka ancestors e9
```

Shows every element from the target up to the visual tree root.

---

### `connect` / `list` / `disconnect` — Session management

```bash
raka connect --app MyApp          # Test connection and save as default
raka list                         # Show saved connection
raka disconnect                   # Clear saved connection
```

---

### `tap-inspect` — Inspect any WinUI 3 app (no NuGet)

```bash
raka tap-inspect --app Calculator
raka tap-inspect --pid 12345
```

Injects a native C++ TAP DLL into the target process and returns the full visual tree as JSON. Works on **any** WinUI 3 (Windows App SDK) application — no source code access or NuGet package required.

<details>
<summary>Example output</summary>

```json
[{
  "type": "Microsoft.UI.Xaml.Hosting.DesktopWindowXamlSource",
  "handle": 2326851388688,
  "children": [{
    "type": "Microsoft.UI.Xaml.Controls.Grid",
    "handle": 2326853382104,
    "width": 1233.3,
    "height": 768.7,
    "properties": {
      "ActualWidth": "1233.33",
      "ActualHeight": "768.667",
      "HorizontalAlignment": "3"
    },
    "children": [
      { "type": "Microsoft.UI.Xaml.Controls.TextBlock", "properties": { "Text": "Welcome" } },
      { "type": "Microsoft.UI.Xaml.Controls.Button", "properties": { "Content": "Click Me" } }
    ]
  }]
}]
```
</details>

**Properties collected:** Name, AutomationId, AutomationProperties.Name, Text, Content, Header, PlaceholderText, Visibility, IsEnabled, Margin, Padding, HorizontalAlignment, VerticalAlignment, Background, Foreground, FontSize, FontWeight, ActualWidth, ActualHeight, Opacity.

> **Note:** `tap-inspect` is a standalone command — it does not use the named pipe session system. Each invocation performs a fresh DLL injection and tree walk.

---

## Production Safety

Raka.DevTools is automatically **excluded from Release builds**. The NuGet package ships with MSBuild `.props`/`.targets` that:

1. **Zero-code auto-init** — Injects a `[ModuleInitializer]` that discovers your `Window` and attaches DevTools automatically in Debug builds
2. **Debug-only symbol** — Defines `RAKA_DEVTOOLS` only in Debug → `#if RAKA_DEVTOOLS` blocks compile out in Release
3. **Assembly stripping** — Removes all Raka assembly references in Release → no DLLs in production output
4. **Development dependency** — Package won't flow to downstream consumers
5. **Crash prevention** — Hooks `Application.UnhandledException` to suppress transient composition-thread COMExceptions from aggressive tree manipulation

**Override:** To force-enable in any configuration (e.g., staging), add to your `.csproj`:

```xml
<PropertyGroup>
  <RakaDevToolsEnabled>true</RakaDevToolsEnabled>
</PropertyGroup>
```

---

## Architecture

Raka has two connection paths depending on the mode:

```
NuGet Mode (full access):
┌─────────────────┐         Named Pipe          ┌──────────────────────┐
│   raka CLI      │  ──── JSON protocol ────►   │  Your WinUI 3 App    │
│   (standalone)  │  ◄──── responses ────────   │  + Raka.DevTools     │
└─────────────────┘                              └──────────────────────┘
        │                                                │
   System.CommandLine                          DispatcherQueue → UI thread
   Pipe client                                 VisualTreeHelper
   Session manager                             AutomationPeer
                                               XamlReader.Load
                                               RenderTargetBitmap
                                               Windows.Graphics.Capture

TAP Mode (read-only, any app):
┌─────────────────┐    InitializeXamlDiagnosticsEx    ┌──────────────────┐
│   raka CLI      │  ──── DLL injection ──────────►   │  Any WinUI 3 App │
│   (standalone)  │                                   │  (no NuGet)      │
│                 │  ◄──── JSON tree (named pipe) ──  │  + raka_tap.dll  │
└─────────────────┘                                   └──────────────────┘
        │                                                    │
   LoadLibrary(FrameworkUdk.dll)                  IObjectWithSite
   P/Invoke InitializeXamlDiagnosticsEx           IVisualTreeService
   Named pipe server                              AdviseVisualTreeChange
                                                  GetPropertyValuesChain
```

### Components

- **Raka.Cli** — Standalone .NET console app. Connects to apps via named pipes (NuGet mode) or injects TAP DLL (TAP mode).
- **Raka.DevTools** — NuGet package added to your WinUI 3 app. Starts a named pipe server, routes commands to the UI thread, executes them using WinUI 3 APIs.
- **Raka.Protocol** — Shared message types (JSON-over-pipe protocol).
- **Raka.Tap** — Native C++ DLL injected into target processes. Uses XAML diagnostics COM interfaces (`IVisualTreeService`) to walk the visual tree. Adapted from [asklar/lvt](https://github.com/asklar/lvt) (MIT License).

The NuGet-mode pipe name follows the convention `raka-devtools-{pid}`, so the CLI can discover it from the process ID.

---

## Building from Source

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download) (preview)
- Windows 10/11 with Windows App SDK
- **For TAP DLL:** Visual Studio with C++ Desktop workload (includes MSVC compiler and CMake)

### Build everything

```powershell
.\build.ps1
```

### Build only CLI for your platform

```powershell
.\build.ps1 -Runtime win-x64 -SkipNuGet
.\build.ps1 -Runtime win-arm64 -SkipNuGet
```

### Build only NuGet packages

```powershell
.\build.ps1 -SkipCli
```

### Build the TAP DLL (native C++)

The TAP DLL must match the target app's architecture (x64 or ARM64):

```powershell
# From VS Developer Command Prompt (ARM64):
cmake -S src/Raka.Tap -B src/Raka.Tap/build -G Ninja -DCMAKE_BUILD_TYPE=Release
cmake --build src/Raka.Tap/build

# Output: src/Raka.Tap/build/bin/raka_tap.dll
# Place next to raka.exe or in a tap/ subdirectory
```

For x64 targets, use the x64 Developer Command Prompt instead.

### Artifacts

```
artifacts/
├── cli/
│   ├── raka-win-x64/raka.exe
│   ├── raka-win-x64-v0.1.0.zip
│   ├── raka-win-arm64/raka.exe
│   └── raka-win-arm64-v0.1.0.zip
└── nuget/
    └── Raka.DevTools.0.1.0.nupkg

src/Raka.Tap/build/bin/
└── raka_tap.dll          # Place next to raka.exe
```

---

## Releasing

Releases are automated via GitHub Actions. To create a release:

1. Update the version in `Directory.Build.props`:
   ```xml
   <Version>0.2.0</Version>
   ```
2. Commit and push to `master`/`main`.

The workflow reads the version, checks if a GitHub Release already exists for it, and if not — builds CLI binaries (x64 + ARM64), packs the NuGet package, and creates a GitHub Release with all artifacts.

---

## Roadmap

- [x] **Phase 1** — Core CLI + NuGet (inspect, search, properties, click, screenshot)
- [x] **Phase 2** — XAML injection (add-xaml, replace, remove)
- [ ] **Phase 3** — Style/resource/context inspection
- [x] **Phase 4** — C++ TAP DLL injection (inspect any WinUI 3 app without NuGet)
- [x] **Phase 4b** — Hot Reload (watch XAML files, auto-replace on save)
- [ ] **Phase 5** — TAP-mode write operations via IVisualTreeService3, MCP server, Copilot integration
- [ ] **Phase 6** — Watch mode, XAML source correlation

---

## License

[MIT](LICENSE)
