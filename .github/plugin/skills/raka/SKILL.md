---
name: raka
description: Inspect, modify, navigate, screenshot, and hot-reload running WinUI 3 apps using the Raka CLI. Use this skill when building or debugging WinUI 3 applications.
---

# Raka — WinUI 3 App Automation for AI Agents

Raka is a CLI tool that lets you see, inspect, modify, and interact with a **running** WinUI 3 application. Use it to verify your UI changes, navigate between pages, take screenshots, and iterate on XAML without rebuilding.

---

## Setup

### 1. Add Raka.DevTools to the WinUI 3 project

```bash
dotnet add package Raka.DevTools
```

That's it — no code changes needed. The NuGet package auto-discovers your Window and starts a pipe server in Debug builds.

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

The three most important commands form the agent's main loop:

```
1. raka status                          # What page/theme/state am I on?
2. raka click --name MyButton           # Interact with controls (by x:Name)
3. raka screenshot -f screenshot.png    # Verify the result visually
```

Expanded workflow when building or exploring:

```
1. raka status                          # Situational awareness
2. raka screenshot -f before.png        # See current state
3. raka click --name SaveButton         # Interact (x:Name is most reliable)
4. raka navigate SettingsPage           # Switch pages (more reliable than clicking nav items)
5. raka screenshot -f after.png         # Verify the change
```

For **XAML iteration** (adjusting layout, colors, text) — use hot-reload instead of rebuilding:

```
6. raka hot-reload src/MyApp/           # Start watching XAML files
   # Edit .xaml files → changes appear in ~0.5s, no rebuild needed
7. raka screenshot -f tweaked.png       # Verify the live change
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

> **Note:** Text set via `{x:Bind}` compiled bindings is NOT visible to `search --text`. This is a WinUI 3 limitation — compiled bindings bypass the automation/accessibility layer. Use `{Binding}` for text you need to search for, or search by `--name` or `--type` instead.

### `raka click`
Interact with controls. Supports buttons, checkboxes, toggles, radio buttons, navigation items.
```bash
raka click --name SaveButton                           # ⭐ Best method — by x:Name (stable, reliable)
raka click --type NavigationViewItem --text "Settings"  # By type + text (search + click in one)
raka click -t Button --text "Submit"                    # Short form
raka click e5                                          # By element ID (fragile — IDs change after navigation)
```

**Always prefer `--name` over element IDs.** Element IDs change when the visual tree changes (e.g., page navigation). `x:Name` is stable. When writing XAML, give interactive elements meaningful `x:Name` values.

Text search works on composite content too — a Button containing `StackPanel { Icon, TextBlock "New Note" }` will match `--text "New Note"`.

### `raka navigate`
Navigate a Frame to a page directly — **more reliable than clicking navigation items**.
```bash
raka navigate SettingsPage                # By class name
raka navigate MyApp.Pages.SettingsPage    # By full type name
```
Also auto-updates the NavigationView selection indicator. Use this as your primary navigation method.

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
Read and modify element properties live. `set-property` triggers change events (e.g., TextChanged), making it useful for testing input-driven behavior.
```bash
raka get-property e5 Background           # Read one property
raka get-property e5 -a                   # Read ALL properties

raka set-property e5 Text "Hello!"        # Set string (fires TextChanged)
raka set-property e5 Value 22             # Set numeric (e.g., Slider)
raka set-property e5 Background "#FF5500" # Set color
raka set-property e5 Margin "10,20,10,20" # Set Thickness
raka set-property e5 Visibility Collapsed # Set enum
raka set-property e5 FontSize 24          # Set numeric

raka set-property --name MyButton Background "#FF5500"  # ⭐ By x:Name (no element ID needed)
```

**Tip:** Use `--name` / `-n` to target elements by their `x:Name` without needing to `inspect` first — searches the visual tree automatically.

### `raka type`
Type text into a TextBox or other text input element. Uses the UI Automation `IValueProvider` for reliable input.
```bash
raka type "Hello, World!" --name SearchBox    # ⭐ By x:Name (best)
raka type "Hello, World!" -e e8               # By element ID
```

### `raka list-pages`
List all Page types available in the app's assemblies — useful to discover navigation targets.
```bash
raka list-pages
```
Output:
```json
{ "pages": ["MyApp.Pages.HomePage", "MyApp.Pages.SettingsPage", "MyApp.Pages.ToolsPage"] }
```

### `raka styles`
Show the Style applied to an element — reveals all setters, target type, and base style chain.
```bash
raka styles e5                                # By element ID
raka styles --name NavigationViewBackButton   # By x:Name
```
Output shows each setter's property name and value:
```json
{
  "targetType": "Button",
  "setters": [
    { "property": "Background", "value": "Transparent" },
    { "property": "Foreground", "value": "#C5FFFFFF" },
    { "property": "FontSize", "value": "16" }
  ]
}
```

### `raka resources`
Browse the app's ResourceDictionary — find theme colors, spacing, brushes, styles, and more.
```bash
raka resources                                         # All resources
raka resources --filter Button                         # Filter by key name
raka resources --filter TextFillColor --scope app      # App-level only
raka resources --theme Light                           # Light theme entries only
raka resources --element e5                            # Element-level resources
```

Returns resource keys, values, types, and scopes (app, page, app/merged/theme/Default, etc.). Recurses into all `MergedDictionaries` and `ThemeDictionaries` to expose WinUI's full resource tree including framework theme brushes.

### `raka set-resource`
Modify a resource value at runtime. Works for Thickness, CornerRadius, Double, Color, SolidColorBrush, and more.
```bash
raka set-resource ButtonPadding "20,10,20,10"          # Change Thickness
raka set-resource ControlCornerRadius "4"              # Change CornerRadius
raka set-resource BodyTextBlockFontSize "16"           # Change Double
raka set-resource ButtonPadding "20,10,20,10" --apply  # Set + reload page
```

The `--apply` flag sets the resource at the app level AND reloads the current page so newly created elements pick up the change.

> **Limitation:** WinUI 3 theme brushes (`{ThemeResource}`) resolve from compiled `XamlControlsResources` and cannot be overridden at runtime for existing elements. For theme brush changes, use `set-property` on individual elements instead. Non-brush resources (Thickness, Double, CornerRadius) work correctly with `set-resource`.

### `raka add-xaml` / `raka replace` / `raka remove`
Inject, replace, or remove XAML elements at runtime.
```bash
raka add-xaml e6 "<Button Content='New' Background='#FF5500'/>"
raka add-xaml e6 "<TextBlock Text='Hello' FontSize='24'/>" --index 0
raka replace e7 "<TextBlock Text='Replaced!' Foreground='Red'/>"
raka remove e7
```

### `raka hot-reload`
Watch XAML files and auto-reload on save — **the killer feature for rapid XAML iteration**.
```bash
# Watch entire project directory (recommended)
raka hot-reload src/MyApp/ --app MyApp

# Watch single file
raka hot-reload MyPage.xaml --target-name MainPanel --app MyApp
```

Directory mode auto-maps XAML files to live elements via `x:Class`. Edit any `.xaml` file → the running app updates in ~0.5s. Pages not currently visible are skipped (navigate to them first).

**When to use hot-reload vs rebuild:**
| Scenario | Use |
|----------|-----|
| Adjusting layout, colors, margins, text in existing XAML | **Hot-reload** — instant, no rebuild |
| Adding new elements to an existing page | **Hot-reload** — works for XAML-only changes |
| Creating a brand-new page or code-behind file | **Rebuild** — new files need compilation |
| Changing C# code (event handlers, logic, models) | **Rebuild** — hot-reload is XAML-only |
| Tweaking styling after initial build is done | **Hot-reload** — start it and leave it running |

**Recommended pattern:** After the initial `dotnet build` and launch, start hot-reload and leave it running. Make XAML edits and screenshot to verify — no rebuild needed. Only rebuild when you add new C# files or change code-behind.

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

*Ranked by impact — based on real agent experiments.*

### 1. `click --name` is the most reliable interaction method
Always use `x:Name` for buttons and controls. When writing XAML, give every interactive element a descriptive `x:Name`:
```xml
<Button x:Name="SaveButton" Content="Save" />
<ToggleSwitch x:Name="AutoSaveToggle" />
```
Then click reliably:
```bash
raka click --name SaveButton
```

### 2. Use `status` constantly for situational awareness
Run it before and after interactions to confirm page, theme, and crash detection:
```bash
raka status --app MyApp
# Check: Is the page what I expect? Did the theme change? Is the element count > 0 (not crashed)?
```

### 3. Use `navigate` instead of clicking nav items
Direct page navigation is more reliable than finding and clicking NavigationViewItems:
```bash
raka navigate SettingsPage       # Always works
```

### 4. Screenshot after every significant change
This is how you verify — visual confirmation beats assumptions:
```bash
raka screenshot -f after-change.png
```

### 5. Use hot-reload for XAML tweaks (don't rebuild)
After the initial build, start hot-reload and **leave it running**:
```bash
raka hot-reload src/MyApp/ --app MyApp
# Now edit any .xaml file → changes appear in ~0.5s, no rebuild needed
# Only rebuild when adding new C# files or changing code-behind
```
This saves 5-15 seconds per iteration compared to full rebuild cycles.

### 6. Element IDs change after navigation — use `x:Name` or re-search
After navigating to a new page, element IDs are reassigned:
```bash
raka navigate SettingsPage
raka search --interactive --text "Theme"   # Re-search, don't reuse old IDs
# Or better: use x:Name which is stable
raka click --name ThemeSelector
```

### 7. Use `--interactive` search when looking for click targets
This filters out structural elements (ContentPresenter, Grid, TextBlock) and returns only actionable controls:
```bash
raka search --interactive --text "Submit"
```

### 8. Test every interactive element after building it
Don't just screenshot — actually click buttons, toggle switches, and verify behavior:
```bash
raka click --name SaveButton
raka status                       # Did the page change? Did element count change?
raka screenshot -f after-save.png # Visual confirmation
```

### 9. Wrap AI/hardware API calls in try/catch
If the app uses AI APIs, hardware features, or optional capabilities, always wrap in try/catch with availability checks. Verify with Raka that the app doesn't crash:
```bash
raka click --name AiSummarizeButton
raka status    # Element count > 0 means app is still alive
```

### 10. Use `type` for text input and `set-property` for other values
Instead of typing key-by-key, inject values directly. Change events fire automatically:
```bash
raka type "Test content" --name SearchBox    # Best for TextBox input
raka set-property e13 Value 22               # Slider.ValueChanged fires
```

### 11. Use `resources` to discover theme values before modifying
Before tweaking colors or spacing, browse what's available:
```bash
raka resources --filter Padding              # Find all padding resources
raka resources --filter CornerRadius         # Find corner radius values
raka list-pages                              # Discover all navigable pages
raka styles --name MyButton                  # See what style is applied
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
| Theme brushes can't be overridden at runtime | WinUI 3 resolves from compiled XamlControlsResources; use `set-property` on individual elements |
| `{x:Bind}` text not searchable | Compiled bindings bypass automation; use `{Binding}`, `--name`, or `--type` |

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
# 2. Add navigation to MainWindow/NavigationView
# 3. Build and launch the app
dotnet build -c Debug && winapp run <output-dir>

# 4. Navigate and verify
raka status --app MyApp
raka navigate NewPage
raka screenshot -f new-page.png

# 5. Start hot-reload for XAML tweaking (leave running)
raka hot-reload src/MyApp/ --app MyApp
# Edit NewPage.xaml → changes appear live, no rebuild needed
raka screenshot -f tweaked.png
```

### Test all interactive elements on a page
```bash
raka navigate SettingsPage
raka screenshot -f settings-before.png

# Click every button, toggle every switch, verify each
raka click --name LightThemeRadio
raka status                              # Theme should now be "Light"
raka click --name AutoSaveToggle
raka set-property e13 Value 22           # Adjust slider
raka screenshot -f settings-after.png    # Visual confirmation
```

### Verify app doesn't crash after an action
```bash
raka click --name AiSummarizeButton
raka status    # If this returns data, app is alive. If it errors, app crashed.
```

### Debug a layout issue
```bash
raka inspect -e e15 -d 5 --format tree    # See the subtree
raka get-property e15 -a                   # Read all properties
raka set-property e15 Margin "20,0,20,0"  # Test a fix live
raka screenshot -f debug.png              # Verify
```
