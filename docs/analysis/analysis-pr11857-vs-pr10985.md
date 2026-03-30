# Analysis: Upstream PR #11857 vs Original PR #10985

## Overview

This document compares the original Dark Mode + Visual Styles PR ([#10985](https://github.com/dotnet/winforms/pull/10985))
with the cherry-picked PR ([#11857](https://github.com/dotnet/winforms/pull/11857)) that was merged.

| Metric | PR #10985 (Original) | PR #11857 (Cherry-picked) |
|--------|---------------------|--------------------------|
| Files changed | 88 | 61 |
| Lines added | 4,318 | 1,699 |
| Lines deleted | 484 | 71 |
| Commits | 71 | 2 |
| Created | March 3, 2024 | August 11, 2024 |
| Merged | *(closed, not merged)* Aug 10 | August 12, 2024 |
| Time open | ~5 months | **~2 hours** |
| Review comments from team | 55+ threads | 6 comments |

**Key observation:** PR #11857 was opened the calendar day after #10985 was closed,
and was merged within approximately 2 hours with minimal review.

---

## What PR #11857 Kept (Cherry-Picked)

The first commit of PR #11857 (`b238485`) cherry-picked the **dark mode infrastructure** from #10985,
specifically the following 30 files (58 files total, including translations):

### Core Dark Mode Infrastructure
- `Application.cs` — `SetColorMode()`, `ColorMode`, `SystemColorMode`, `IsDarkModeEnabled`
- `Control.cs` — Dark mode brush handling, `ApplyThemingImplicitly` style
- `ControlStyles.cs` — New `ApplyThemingImplicitly` flag
- `SystemColorMode.cs` — Enum definition (Classic/Dark)
- `Form.cs` — Form caption/border color properties, corner preferences
- `FormCornerPreference.cs` — Enum for DWM corner styles

### Control-Level Dark Mode Support
- `ButtonBase.cs`, `ComboBox.cs`, `DataGridView.cs`, `DateTimePicker.cs`
- `GroupBox.cs`, `Label.cs`, `ListBox.cs`, `ListView.cs`
- `MonthCalendar.cs`, `PictureBox.cs`, `ProgressBar.cs`, `PropertyGrid.cs`
- `Splitter.cs`, `TabControl.cs`, `TabPage.cs`, `TextBoxBase.cs`
- `StatusStrip.cs`, `ToolStripManager.cs`, `ToolStripMenuItem.cs`
- `TrackBar.cs`, `TreeView.cs`, `UpDownBase.cs`
- `ScrollBar.cs`, `ScrollableControl.cs`, `Panel.cs`, `MDIClient.cs`
- `DarkProfessionalColors.cs`, `TextRenderer.cs`

### VB.NET Application Framework Support
- `ApplyApplicationDefaultsEventArgs.vb`
- `WindowsFormsApplicationBase.vb`
- `Interaction.vb`

### Resources & API Surface
- `SR.resx` + 13 language `.xlf` files
- `PublicAPI.Unshipped.txt` (both WinForms and VB)

---

## What PR #11857 Stripped Out (30 Files)

These files were present in #10985 but **entirely excluded** from #11857:

### Visual Styles Infrastructure (Core Omission)
| File | Purpose |
|------|---------|
| `VisualStylesMode.cs` | Enum defining visual style modes — the primary feature flag for visual styles |
| `VisualStyleRenderer.cs` | Modifications to the existing Visual Style renderer |
| `VisualStyleRenderer.ThemeHandle.cs` | Theme handle lifecycle management |
| `Appearance.cs` | Addition of experimental ID (WFO5000) for visual styles |

### Rendering & Animation Framework
| File | Purpose |
|------|---------|
| `Rendering/Animation/AnimatedControlRenderer.cs` | Base class for animated control rendering |
| `Rendering/Animation/AnimationCycle.cs` | Animation cycle/timing definitions |
| `Rendering/Animation/AnimationManager.cs` | Central animation orchestration |
| `Rendering/Animation/AnimationManager.AnimationRendererItem.cs` | Per-renderer tracking |
| `Rendering/CheckBox/AnimatedToggleSwitchRenderer.cs` | Modern toggle-switch style checkbox |
| `Rendering/CheckBox/ModernCheckBoxStyle.cs` | Enum for modern checkbox styles |
| `Rendering/TextBox/AnimatedBorderStyleRenderer.cs` | Animated focus-border for text boxes |

### Control-Specific Visual Enhancements
| File | Purpose |
|------|---------|
| `TextBoxBase.NonClientBitmapCache.cs` | Non-client area bitmap caching for custom borders |
| `TextBox.cs` | Visual style rendering in TextBox |
| `CheckBox.cs` | Modern checkbox rendering integration |
| `RichTextBox.cs` | Visual style overrides for RichTextBox |
| `LinkLabel.cs` | Visual style for link labels |
| `ProfessionalColorTable.cs` | ToolStrip color table adjustments |
| `PropertyGridView.cs` | Property grid visual style support |
| `PropertyGridView.DropDownHolder.cs` | Property grid dropdown visuals |
| `DropDownButton.DropDownButtonAdapter.cs` | Property grid button visuals |

### Theming Infrastructure
| File | Purpose |
|------|---------|
| `Theming/DarkThemedApplicationColors.cs` | Dark theme color definitions |
| `Theming/ForcedLightThemedSystemColors.cs` | Forced light theme when in dark mode |
| `Theming/LightThemedApplicationColors.cs` | Light theme color definitions |

### Other
| File | Purpose |
|------|---------|
| `.editorconfig` | Analyzer severity adjustments |
| `SystemBrushes.cs` | System brush cache invalidation on color mode change |
| `SystemPens.cs` | System pen cache invalidation on color mode change |
| `DeviceContextExtensions.cs` | Dark mode aware color translation |
| `CreateBrushScope.cs` | Dark mode aware brush creation |
| `NativeMethods.txt` (Core) | Additional P/Invoke definitions |
| `GlobalUsings.cs` | Global using additions |
| `PublicAPI.Shipped.txt` | API surface tracking |
| `AccessibilityTests/ContainerControls2.Designer.cs` | Test designer file |

---

## Changes Made in the Second Commit of PR #11857

The second commit (`0d6ae9a`) was titled *"Apply my feedback to Klaus' dark mode feature"*
and contained **115 additions / 97 deletions** across 20 files.

### Substantive Changes

1. **SystemBrushes.cs / SystemPens.cs** — Removed the manual cache-invalidation logic
   (checking `UseAlternativeColorSet` on each access) that was in the cherry-picked code.
   Replaced with a system-events-based notification approach (see #3).

2. **DeviceContextExtensions.cs** — Removed the `FindNearestColor` workaround that
   "un-systemed" colors for dark mode. The rationale was that `ColorTranslator.ToWin32`
   already looks up the current color value. *(A related runtime issue was filed:
   [dotnet/runtime#105992](https://github.com/dotnet/runtime/issues/105992))*

3. **Application.cs** — Major refactoring of `SetColorMode()`:
   - Added early-return when color mode hasn't changed
   - Replaced direct `SystemColors.UseAlternativeColorSet = IsDarkModeEnabled` with
     a new `NotifySystemEventsOfColorChange()` local function
   - This function finds the `.NET-BroadcastEventWindow` and sends `WM_SYSCOLORCHANGE`
     via `SendMessageCallback` to trigger `SystemColorTracker` to update all cached colors
   - This is a meaningful architectural improvement that properly integrates with the
     existing .NET color tracking infrastructure

4. **PInvoke.GetSysColorBrush.cs** — Moved the dark mode brush logic here (create solid
   brush when using alternative color set) instead of having it in `CreateBrushScope`.
   This centralizes the decision.

5. **PInvoke.SendMessageCallback.cs** — New file implementing the native callback
   mechanism needed for the `NotifySystemEventsOfColorChange()` approach.

6. **Control.cs** — Simplified brush creation by removing the `IsDarkModeEnabled`
   check, since `GetSysColorBrush` now handles it internally.

7. **CreateBrushScope.cs** — Simplified to always delegate to `GetSysColorBrush`
   for system colors (which now internally decides whether to use solid brush).

### Mechanical/Cosmetic Changes

8. **Moved `WinFormsExperimentalUrl`** constant from `Application.cs` to
   `DiagnosticIDs.UrlFormat` — purely organizational, applied across ~15 files.

9. **Removed `ExperimentalVisualStyles` (WFO5000)** diagnostic ID — this effectively
   removed the Visual Styles experimental feature flag entirely from the codebase.

10. **ToolStripMenuItem.cs** — Replaced `using static Control` with explicit `Control.`
    qualification (5 call sites). This is a code style preference, not a bug fix.

11. **TreeView.cs** — Removed a blank line between variable declaration and usage.

12. **StatusStripTests.cs** — Removed a TODO comment.

13. **VB files** — Minor warning pragma cleanup.

14. **SR.resx** — Added missing newline at end of file.

### Assessment

| Category | Count | Significance |
|----------|-------|-------------|
| Architectural improvement (SystemColorTracker notification) | 1 | High — genuinely better approach |
| Code centralization (brush logic) | 3 | Medium — cleaner separation of concerns |
| Mechanical refactoring (URL constant move) | ~15 | Low — purely organizational |
| Visual Styles removal (WFO5000) | 1 | **Policy decision, not technical** |
| Style/cosmetic | 4 | Negligible |

**The single most significant technical change** was the `NotifySystemEventsOfColorChange()`
mechanism in `Application.cs`, which properly hooks into .NET's `SystemColorTracker`
infrastructure instead of manually invalidating brush/pen caches. This is a genuine
improvement worth preserving.

**The most significant policy change** was removing `ExperimentalVisualStyles` (WFO5000),
which effectively excised the entire Visual Styles feature from the merged code.

---

## Timeline

| Date | Event |
|------|-------|
| March 3, 2024 | PR #10985 opened |
| July 17, 2024 | 29 review comments posted in ~2 hours |
| August 5, 2024 | 18 more review comments posted in ~5.5 hours |
| August 8, 2024 | 2 follow-up comments |
| **August 10, 2024** | **PR #10985 closed (not merged)** |
| **August 11, 2024** | **PR #11857 opened (cherry-pick)** |
| **August 12, 2024** | **PR #11857 merged (~2 hours after opening)** |
