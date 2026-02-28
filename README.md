# 🎭 Raka

**Playwright-like automation for WinUI 3 apps.**

Raka lets AI agents (and humans) see, inspect, modify, and interact with running WinUI 3 applications — making it faster to build UI with tools like GitHub Copilot.

```
raka inspect --app MyApp              # See the full visual tree
raka search -t Button                 # Find all buttons
raka set-property e5 Background "#FF5500"  # Change a property live
raka click e5                         # Click a button
raka screenshot -f screenshot.png     # Pixel-perfect screenshot with Mica
```

---

## Quick Start

### 1. Add the NuGet package to your WinUI 3 app

```bash
dotnet add package Raka.DevTools
```

### 2. Enable DevTools in your `App.xaml.cs`

```csharp
protected override void OnLaunched(LaunchActivatedEventArgs args)
{
    _window = new MainWindow();
    _window.UseRakaDevTools();   // ← Add this line
    _window.Activate();
}
```

### 3. Run your app, then use the CLI

```bash
raka inspect --app MyApp
```

That's it. The NuGet package starts a named pipe server inside your app. The CLI connects to it and sends commands.

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

Every command accepts `--app` or `--pid` to specify which running app to connect to:

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

### `get-property` — Read property values

```bash
raka get-property e5 Background   # Single property
raka get-property e5 -a           # All DependencyProperties
```

Returns the property name, current value, type, and value source (Local, Default, Style, etc.).

---

### `set-property` — Modify properties live

```bash
raka set-property e5 Background "#FF5500"       # Hex color
raka set-property e5 Margin "10,20,10,20"       # Thickness
raka set-property e5 Visibility "Collapsed"     # Enum
raka set-property e5 FontSize "24"              # Numeric
raka set-property e5 CornerRadius "8"           # CornerRadius
raka set-property e4 Text "Hello, World!"       # String (also: enter text)
```

Changes take effect immediately. Supports colors (hex, named), Thickness, CornerRadius, GridLength, enums, and primitives.

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

### `list` / `disconnect` — Session management

```bash
raka list                         # Show saved connection
raka disconnect                   # Clear saved connection
```

---

## Architecture

```
┌─────────────────┐         Named Pipe          ┌──────────────────────┐
│   raka CLI      │  ──── JSON protocol ────►   │  Your WinUI 3 App    │
│   (standalone)  │  ◄──── responses ────────   │  + Raka.DevTools     │
└─────────────────┘                              └──────────────────────┘
        │                                                │
   System.CommandLine                          DispatcherQueue → UI thread
   Pipe client                                 VisualTreeHelper
   Session manager                             AutomationPeer
                                               RenderTargetBitmap
                                               Windows.Graphics.Capture
```

- **Raka.Cli** — Standalone .NET console app. Connects to any app that has Raka.DevTools via named pipes.
- **Raka.DevTools** — NuGet package added to your WinUI 3 app. Starts a named pipe server, routes commands to the UI thread, executes them using the WinUI 3 API.
- **Raka.Protocol** — Shared message types (JSON-over-pipe protocol).

The pipe name follows the convention `raka-devtools-{pid}`, so the CLI can discover it from the process ID.

---

## Building from Source

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download) (preview)
- Windows 10/11 with Windows App SDK

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
- [ ] **Phase 2** — Enhanced XAML parsing, search improvements
- [ ] **Phase 3** — Style/resource/context inspection
- [ ] **Phase 4** — C++ TAP DLL injection (inspect apps without NuGet)
- [ ] **Phase 5** — NuGet publishing, Copilot skill integration
- [ ] **Phase 6** — MCP server wrapper, watch mode, XAML source correlation

---

## License

[MIT](LICENSE)
