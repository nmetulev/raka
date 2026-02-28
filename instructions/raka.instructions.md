---
description: 'Raka — WinUI 3 app automation tool. Use this to inspect, modify, navigate, screenshot, and hot-reload running WinUI 3 apps during development.'
applyTo: '**/*.cs, **/*.xaml, **/*.csproj'
---

# Raka — WinUI 3 App Automation for AI Agents

Raka is a CLI tool that lets you see, inspect, modify, and interact with a **running** WinUI 3 application. Use it to verify your UI changes, navigate between pages, take screenshots, and iterate on XAML without rebuilding.

---

## Setup

### 1. Add Raka.DevTools to the WinUI 3 project

```bash
dotnet add package Raka.DevTools
```

**Important:** After adding, check the `.csproj` — if `dotnet add` inserted `<IncludeAssets>` or `<PrivateAssets>` restrictions on the PackageReference, remove them:

```xml
<!-- ✅ Correct — no asset restrictions -->
<PackageReference Include="Raka.DevTools" Version="0.1.0" />

<!-- ❌ Wrong — compile asset excluded, auto-init won't work -->
<PackageReference Include="Raka.DevTools" Version="0.1.0">
  <IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
</PackageReference>
```

### 2. Build in Debug mode

```bash
dotnet build -c Debug
```

Always use `-c Debug` explicitly. Raka.DevTools is stripped from Release builds automatically.

### 3. Launch the app

```bash
# If using winapp tool:
winapp run <build-output-dir>

# Or launch from Visual Studio (F5)
```

### 4. Run Raka commands

```bash
raka status --app MyAppName
```

The `--app` flag matches by process name or window title. After the first command, it's saved as the default target — subsequent commands can omit `--app`.

---

## Core Workflow

The typical agent workflow is:

```
1. raka status                          # What page am I on? What theme?
2. raka inspect -d 3 --format tree      # See the UI structure
3. raka search --interactive --text X   # Find clickable elements
4. raka click --type Button --text X    # Interact with controls
5. raka screenshot -f screenshot.png    # Verify the result visually
6. raka hot-reload src/MyApp/           # Edit XAML → see changes live
```

---

## Command Reference

### `raka status`
Quick situational awareness — shows current page, theme, window size, element count.
```bash
raka status --app MyApp
```
Output:
```
  Title:     My App
  Size:      1872×1165
  Theme:     Dark
  Page:      MyApp.Pages.DashboardPage
  Backdrop:  MicaBackdrop
  Elements:  323
```

### `raka inspect`
View the visual tree structure.
```bash
raka inspect -d 3                     # JSON, depth-limited
raka inspect -d 4 --format tree       # ASCII tree (human-readable)
raka inspect -e e5 -d 2               # Subtree from element e5
```

The `--format tree` output looks like:
```
Grid (e0)
├─ TitleBar #AppTitleBar (e1)
├─ NavigationView #NavView (e3)
│  ├─ Frame #ContentFrame (e47) → SettingsPage [Pages/SettingsPage.xaml]
```

Elements show: `Type #Name (id) → FrameContent [sourceFile]`

### `raka search`
Find elements by various criteria. Filters combine with AND logic.
```bash
raka search -t Button                              # By type
raka search -n SaveButton                          # By x:Name
raka search --text "Submit"                        # By text content
raka search --interactive --text "Dashboard"       # Only clickable elements matching text
raka search --property Tag=settings                # By property value
raka search --class NavigationViewItem             # By full class name
raka search --visible -t TextBlock                 # Only visible elements
raka search --automation-id save-btn               # By AutomationId
```

**Tip:** Always use `--interactive` when searching for elements you plan to click — it filters out structural elements (ContentPresenter, TextBlock) and returns only actionable controls.

### `raka click`
Interact with controls. Supports buttons, checkboxes, toggles, navigation items.
```bash
raka click e5                                          # By element ID
raka click --name SaveButton                           # By x:Name (stable across navigation)
raka click --type NavigationViewItem --text "Settings"  # By type + text (search + click in one)
raka click -t Button --text "Submit"                    # Short form
```

**Best for navigation:** Use `--type NavigationViewItem --text "PageName"` to click nav items without needing to search first.

### `raka navigate`
Navigate a Frame to a page directly (more reliable than clicking nav items).
```bash
raka navigate SettingsPage                # By class name
raka navigate MyApp.Pages.SettingsPage    # By full type name
```
Also auto-updates the NavigationView selection indicator.

### `raka screenshot`
Capture the app visually.
```bash
raka screenshot -f screenshot.png              # Full window (auto-detects Mica)
raka screenshot e5 -f element.png              # Specific element
raka screenshot --mode render --bg "#1E1E1E"   # Explicit dark background
raka screenshot --mode render --bg "#F3F3F3"   # Explicit light background
```

**Mica/Acrylic auto-detect:** If the app uses `MicaBackdrop` or `DesktopAcrylicBackdrop`, Raka automatically switches to render mode with a theme-appropriate background. No special flags needed.

### `raka get-property` / `raka set-property`
Read and modify element properties live.
```bash
raka get-property e5 Background           # Read one property
raka get-property e5 -a                   # Read ALL properties

raka set-property e5 Background "#FF5500" # Set color
raka set-property e5 Margin "10,20,10,20" # Set Thickness
raka set-property e5 Visibility Collapsed # Set enum
raka set-property e5 FontSize 24          # Set numeric
raka set-property e5 Text "Hello!"        # Set string
```

### `raka add-xaml` / `raka replace` / `raka remove`
Inject, replace, or remove XAML elements at runtime.
```bash
raka add-xaml e6 "<Button Content='New' Background='#FF5500'/>"
raka add-xaml e6 "<TextBlock Text='Hello' FontSize='24'/>" --index 0
raka replace e7 "<TextBlock Text='Replaced!' Foreground='Red'/>"
raka remove e7
```

### `raka hot-reload`
Watch XAML files and auto-reload on save — **the killer feature for rapid iteration**.
```bash
# Watch entire project directory
raka hot-reload src/MyApp/ --app MyApp

# Watch single file
raka hot-reload MyPage.xaml --target-name MainPanel --app MyApp
```

Directory mode auto-maps XAML files to live elements via `x:Class`. Edit any `.xaml` file → the running app updates in ~0.5s. Pages not currently visible are skipped (navigate to them first).

### `raka batch`
Run multiple commands in a single call (saves CLI startup overhead).
```bash
raka batch --app MyApp "navigate SettingsPage" "status" "screenshot -f settings.png"
```

### `raka ancestors`
Show the parent chain from an element to the root.
```bash
raka ancestors e15
```

---

## Best Practices for AI Agents

### 1. Start every session with `status`
Before making changes, understand the current state:
```bash
raka status --app MyApp
```

### 2. Use `--format tree` for orientation
JSON is great for parsing, but tree view is faster to understand:
```bash
raka inspect -d 4 --format tree
```

### 3. Use `--interactive` search for click targets
Never click a ContentPresenter or TextBlock — use `--interactive` to find the actual control:
```bash
raka search --interactive --text "Submit"
```

### 4. Prefer `raka navigate` over click for page navigation
`navigate` is more reliable than clicking NavigationViewItems:
```bash
raka navigate SettingsPage
```

### 5. Use `click --type --text` instead of search + click
One command instead of two:
```bash
# Instead of:
raka search -t NavigationViewItem --text "Settings"  # find ID
raka click e9                                         # click it

# Do:
raka click -t NavigationViewItem --text "Settings"    # search + click in one
```

### 6. Always screenshot after significant changes
Verify your work visually:
```bash
raka screenshot -f after-change.png
```

### 7. Use hot-reload for XAML iteration
Don't rebuild for XAML-only changes:
```bash
raka hot-reload src/MyApp/ --app MyApp
# Now edit any .xaml file — changes appear in ~0.5s
```

### 8. Element IDs change after navigation
After navigating to a new page, element IDs are reassigned. Always re-search after navigation:
```bash
raka navigate SettingsPage
raka search --interactive --text "Theme"   # Re-search, don't reuse old IDs
```

### 9. Use `x:Name` for stable references
Click by `x:Name` is stable across tree changes:
```bash
raka click --name SaveButton
```

### 10. Use `batch` for multi-step operations
Reduces overhead from multiple CLI invocations:
```bash
raka batch "navigate SettingsPage" "screenshot -f settings.png" "status"
```

---

## Limitations

| Limitation | Workaround |
|---|---|
| `{x:Bind}` doesn't survive hot-reload | Use `{Binding}` for prototyping, switch to `x:Bind` after |
| Event handlers lost on hot-reload | Hot-reload is for visual/layout iteration only |
| Element IDs change after navigation | Re-search after navigating pages |
| Non-visible pages can't be hot-reloaded | Navigate to the page first, then edit its XAML |
| `capture` mode + Mica = black screenshots | Auto-detected; use `--mode render` if needed |
| Hot-reload with `xmlns:local` custom controls | Supported — namespaces are auto-injected |

---

## Source File Correlation

When Raka returns elements, Pages and Windows include a `sourceFile` field:
```json
{
  "id": "e47",
  "type": "Frame",
  "name": "ContentFrame",
  "contentClassName": "MyApp.Pages.SettingsPage",
  "sourceFile": "Pages/SettingsPage.xaml"
}
```

Use this to know which XAML file to edit for the current view.

---

## Common Patterns

### Build a new page and verify
```bash
# 1. Create the page XAML and code-behind files
# 2. Add navigation to MainWindow
# 3. Build and launch the app
dotnet build -c Debug && winapp run <output-dir>

# 4. Navigate and verify
raka navigate NewPage --app MyApp
raka screenshot -f new-page.png

# 5. Iterate with hot-reload
raka hot-reload src/MyApp/ --app MyApp
# Edit NewPage.xaml → changes appear live
```

### Debug a layout issue
```bash
raka inspect -e e15 -d 5 --format tree    # See the subtree
raka get-property e15 -a                   # Read all properties
raka set-property e15 Margin "20,0,20,0"  # Test a fix live
raka screenshot -f debug.png              # Verify
```

### Explore an unfamiliar app
```bash
raka status --app MyApp
raka inspect -d 3 --format tree
raka search --interactive --visible       # All interactive elements
raka screenshot -f overview.png
```
