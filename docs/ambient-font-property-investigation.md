# Ambient Font Property Investigation

## Summary
WinForms `Control.Font` is an ambient property. The runtime code resolves it through the parent chain and then the ambient service/default fallback, but the real design-time risk is that the designer can still emit a local `Font` assignment even when that value is just the inherited ambient value. Once the generated `InitializeComponent` code runs, the design-time ambient graph is no longer available, so a redundant local value can outlive the intended inherited value and effectively "break" the ambient chain.

## Relevant code references

- `src/System.Windows.Forms/System/Windows/Forms/Control.cs`
  - `Control.Font` is declared with `[AmbientValue(null)]` and the getter resolves through `GetCurrentFontAndDpi()`.
  - The most important comment is in the `DataContext` property block:
    - "`[AmbientValue]` only matters for the designer's CodeDOM serializer: when it is forced to emit an otherwise-ambient property ... it writes the AmbientValue as the 'reset to ambient' sentinel."
  - That comment documents the exact risk: the ambient metadata matters to the designer/serializer, not to the runtime resolution path.

- `src/System.Windows.Forms.Design/src/System/ComponentModel/Design/Serialization/PropertyMemberCodeDomSerializer.cs`
  - This serializer checks `property.ShouldSerializeValue(value)` and, when it decides the property should not be serialized, falls back to `AmbientValueAttribute` to emit the ambient sentinel value instead.
  - This is the serialization mechanism that makes ambient semantics visible to design-time code generation.

- `src/System.Windows.Forms/System/Windows/Forms/Control.cs`
  - `GetCurrentFontAndDpi()` resolves explicit `Font` > parent > activeX ambient > service ambient > default.
  - The runtime logic itself is correct for inherited values; the trap is that an explicit local value that merely matches the inherited font can still be persisted as a real override in generated code.

## Property-store semantics in WinForms
WinForms does not treat "a property has a value" and "a property has an explicit override" as the same thing. The property-value store tracks whether there is a dedicated entry for a property, and that entry is often the real signal for whether serialization should happen. In other words, the value store distinguishes "this property was explicitly set" from "this property currently happens to equal the inherited ambient/default value."

This is why the common WinForms pattern is to update the property bag in the setter (`Properties.AddOrRemoveValue(...)`) and let `ShouldSerialize...` answer whether there is still an explicit override to persist. The serializer is therefore not a generic "compare to ambient each time" check; it is reading the explicit state of the property backfield, and the property setter is the place where that explicit/ambient distinction is enforced.

## Why the ordering problem matters
The ambient chain is known at design time, but it is not reconstructed when `InitializeComponent` runs at runtime. If a child control's `Font` is serialized as a redundant local assignment equal to the parent or ambient value, and the form-level `Font` is assigned later in generated code, the child keeps a concrete local value instead of participating in the ambient chain. From the outside, it looks like the top-level assignment was never effective.

This is not a problem for the `GetCurrentFontAndDpi()` algorithm itself; it is a serialization problem caused by persisting a value that should have been treated as ambient.

## What changed
The fix keeps design-time serialization honest for ambient properties by suppressing redundant local values when they equal the inherited/ambient/default value for `Font`, `BackColor`, `ForeColor`, and `Cursor`.

This avoids generating code that creates a local override where the value should instead remain ambient, which is the runtime scenario behind the apparent ordering bug.

## Assessment by Claude Opus

### Verdict

The *diagnosis* is partly right, but the *fix as implemented is not safe to ship*. `ShouldSerializeBackColor` /
`ShouldSerializeForeColor` / `ShouldSerializeFont` / `ShouldSerializeCursor` are not design-time-only hooks in this
codebase — roughly 30 runtime call sites use them as the canonical "was this explicitly set?" predicate. Redefining
them as "explicitly set **and** different from the inherited value" changes runtime rendering behavior, not just
code generation.

### 1. The change breaks runtime consumers (blocking)

These call sites read the methods as "has a custom value", and all of them silently lose the user's explicit value
whenever it happens to equal the inherited/ambient value:

- `ComboBox.BackColor` (`ComboBox.cs:258`), `ListView.BackColor` (`ListView.cs:348`), `TreeView.BackColor`
  (`TreeView.cs:195`), `DateTimePicker.BackColor` (`DateTimePicker.cs:130`), `ListBox` (`ListBox.cs:153,448`),
  and the matching `ForeColor` getters. Concrete regression: on a default `Form`
  (`BackColor == SystemColors.Control`), `comboBox1.BackColor = SystemColors.Control;` now makes
  `ComboBox.BackColor` return `SystemColors.Window` — the assignment is discarded at runtime, with no designer
  involved at all.
- `WM_CTLCOLORSTATIC` handling in `ComboBox.cs:2074` and `TextBoxBase.cs:2409,1214`.
- Dark-mode/modern adapters: `CheckBoxModernAdapter.cs:93,115`, `RadioButtonModernAdapter.cs:92,114`,
  `ButtonDarkModeAdapter.cs:58`, `ComboBox.ModernComboAdapter.cs:311`, `AnimatedPopupButtonRenderer.cs:142,161`.
- `MonthCalendar.cs:196,412`, `GroupBox.cs:457`, `ProgressBar.cs:84,89`, `UpDownBase.cs:661`,
  `PrintPreviewControl.cs:770`, `ToolStripItem.cs:1210`.
- `ToolStripTextBox.ToolStripTextBoxControl.cs:101` (`_isFontSet = ShouldSerializeFont();`).
- `PropertyGridView.cs:3446` uses `ShouldSerializeCursor()` to decide whether to restore the caller's cursor after a
  wait-cursor scope; it now skips the restore when the explicit cursor equals the parent's.

Secondary cost: these are hot paths (WndProc color messages, paint). The methods went from O(1) property-bag lookups
to an O(parent-depth) walk, and via `IsActiveX` they can now issue COM ambient-property calls
(`ActiveXAmbientBackColor` / `ActiveXAmbientForeColor`) from inside message handling.

### 2. The new ambient computations do not match the real getters

Even judged purely as design-time logic, the four helpers reimplement resolution incorrectly:

- **`Cursor` ignores the `DefaultCursor` rule.** `Control.Cursor` (`Control.cs:1740-1744`) only consults the parent
  when `DefaultCursor == Cursors.Default`. For `TextBoxBase` (`TextBoxBase.cs:511`) and `Splitter`
  (`Splitter.cs:98`) the parent's cursor is never inherited, so an explicit cursor that equals the parent's is
  meaningful — dropping it changes the effective cursor to `DefaultCursor`. It also ignores
  `States.UseWaitCursor`, which makes `parent.Cursor` (and therefore the answer) transiently wrong.
- **`BackColor` ignores `IsValidBackColor`** (`Control.cs:1059,2658`). If the parent's color is translucent and the
  child lacks `ControlStyles.SupportsTransparentBackColor`, the child does *not* inherit it — but the new code still
  suppresses an identical explicit value.
- **`Font` ignores `ScaledControlFont`.** `GetCurrentFontAndDpi` returns `ScaledControlFont` before the property-bag
  value, and `SetScaledFont` (`Control.cs:8061`, `ContainerControl.cs:1397`) writes the *scaled* font back into
  `s_fontProperty`. So the comparison can mix a stored unscaled font against a parent's scaled font (or two scaled
  fonts), making the result DPI-dependent. Serializing a form on a 150% design surface can therefore drop a
  legitimate explicit font.
- `Cursor` omits the `CanAccessProperties` guard that the other three use, so `AxHost` children are handled
  inconsistently.

### 3. `[AmbientValue]` does not cover all four properties

`PropertyMemberCodeDomSerializer.GetPropertyValue` (lines 23-48) only emits the ambient sentinel when serialization
was *forced* — i.e. a `SerializeAbsoluteContext` for a `DesignerSerializationVisibility.Content` property
(lines 374-388) or a live `MemberRelationship` (lines 403-413). In the normal path a `false` from
`ShouldSerializeValue` simply omits the statement. More importantly, only `Font` (`Control.cs:2068`) and `Cursor`
(`Control.cs:1724`) carry `[AmbientValue(null)]`; `BackColor` (`Control.cs:1042-1045`) and `ForeColor`
(`Control.cs:2256-2259`) carry none, and no `[DefaultValue]` either. In the forced path they now fall through to
`validValue = false` and are dropped entirely rather than reset. The doc should not imply the sentinel mechanism
applies uniformly to all four.

### 4. Factual correction to the "ordering problem" section

"The ambient chain ... is not reconstructed when `InitializeComponent` runs" is not accurate. Ambient resolution is
fully dynamic: `GetCurrentFontAndDpi` (`Control.cs:5780`) and the `BackColor`/`ForeColor`/`Cursor` getters walk the
parent chain on every read, and `OnParentFontChanged` / `OnParentBackColorChanged` propagate changes to children.
Statement order inside `InitializeComponent` is therefore irrelevant for a control with no explicit value —
assigning `this.Font` after `Controls.Add(child)` works correctly. The only failure mode is the one the doc already
identifies: a redundant *explicit* value was persisted. Recommend rewording to drop the ordering framing.

### 5. Semantic regression: intentional pinning

WinForms deliberately separates "explicitly set" from "currently equals the inherited value". Setting
`child.Font = parent.Font` is a legitimate way to pin a child so it stops tracking later parent changes; `null` /
`Color.Empty` (via `ResetFont()` / `ResetBackColor()`) is the documented way back to ambient. The change makes the
pin silently non-round-trippable: it survives the session but disappears on reload, and the PropertyGrid immediately
un-bolds the property and disables **Reset** (`ReflectPropertyDescriptor.CanResetValue` routes through
`ShouldSerializeValue`). Serialization also becomes parent-dependent, so the same control serializes differently
before and after `Controls.Add`.

### 6. Test coverage gaps

`Control_ShouldSerializeAmbientProperties_WhenEqualToInheritedValue_ReturnsFalse` only covers the simple parented
case. Missing: root/no-parent controls, `TextBox`/`Splitter` (non-default `DefaultCursor`), translucent parent
`BackColor`, PerMonitorV2 DPI scaling, `AxHost`/`CanAccessProperties == false`, and the derived overrides that
delegate to base (`ToolStripControlHost.cs:861-868`, `ToolStripItem.cs:3285-3309`). The two `Font` instances the
test creates are never disposed. Nothing asserts the actual goal (that the designer stops emitting the redundant
assignment), and there is no regression coverage for the runtime consumers in §1.

Note also the pre-existing copy/paste bug in `ToolStripControlHost.ShouldSerializeFont` (`ToolStripControlHost.cs:867-868`),
which delegates to `_control.ShouldSerializeBackColor()`. Out of scope here, but it means the ToolStrip host's font
serialization is already wrong and will now be wrong in a new way.

### Recommended follow-up

1. **Revert the runtime `Control` changes.** Keep `ShouldSerializeBackColor`/`ForeColor`/`Font`/`Cursor` as pure
   "is an explicit value present" predicates. `IsFontSet()` / `TryGetExplicitlySetFont` (`Control.cs:6493,11566`)
   already establish that contract.
2. **Move the redundancy suppression into the design-time layer**, where it only affects code generation: filter the
   property in `ControlDesigner`/`ComponentDesigner.PreFilterProperties` with a wrapping `PropertyDescriptor` whose
   `ShouldSerializeValue` performs the ambient comparison, or supply a `MemberCodeDomSerializer` for these
   properties. That confines the behavior change to `System.Windows.Forms.Design` and leaves runtime semantics
   untouched.
3. **Better still, fix the source.** Verify whether the designer is actually writing a redundant explicit value (for
   example a property-window round-trip or a `ControlDesigner` initialization path that assigns `Font`/`BackColor`
   from the resolved ambient value). Not persisting a value that was never intentionally set is preferable to
   suppressing it at serialization time.
4. If any comparison-based logic is kept, factor the resolution out of the getters into shared helpers
   (`TryGetInheritedBackColor` etc.) so `IsValidBackColor`, `DefaultCursor`, `UseWaitCursor`, `ScaledControlFont`,
   and the ActiveX fallbacks cannot drift from the getters.
5. Add `[AmbientValue]` coverage or an explicit decision for `BackColor`/`ForeColor` before relying on the sentinel
   path for them.

## Assessment by Grok

### Verdict
The **theory is mostly correct**. Runtime ambient resolution is fine; the real failure mode is persisting a local `Font` that merely matches the live ambient value, then replaying it in `InitializeComponent` before the parent/`Form.Font` assignment. The **proposed `ShouldSerialize*` value-equality fix is not safe as written**, especially for `BackColor`/`ForeColor`/`Cursor`, and it overstates what the setters currently do.

### What is correct
- `GetCurrentFontAndDpi()` order is: scaled font, explicit store, parent, ActiveX ambient, `AmbientPropertiesService`, `DefaultFont`. Inherited values work if nothing is stored locally.
- `[AmbientValue(null)]` is CodeDOM metadata, not a runtime resolver. `PropertyMemberCodeDomSerializer.GetPropertyValue` uses it only when it is already emitting a property whose `ShouldSerializeValue` is false. The normal path simply omits the assignment.
- `Font`/`Cursor` setters use `Properties.AddOrRemoveValue(..., value)` with a null default, so `Font = null` / `Cursor = null` really clears the override. `ResetFont`/`ResetCursor` rely on that.
- `InitializeComponent` does not rebuild the designer ambient graph. A baked-in child assignment that equals the parent font at serialize time will not follow a later parent assignment at runtime.

### Where the write-up is imprecise
- **Setters do not enforce ambient vs explicit for these four properties.** `AddOrRemoveValue` drops only the store default (`null` / `Color.Empty`), not “equals parent.” Assigning the current inherited `Font` still writes `s_fontProperty`. Contrast `DataContext` and `VisualStylesMode`, which remove the local entry when the new value matches the parent.
- **Committed `ShouldSerialize*` already keys off the store**, not the effective getter: `ContainsKey` / `!IsEmpty`. That already avoids serializing never-touched properties. The uncommitted change is a second policy: “explicit but equal to ambient ⇒ do not persist.” The investigation describes the old policy, then the fix implements the new one, without calling out the switch.
- **`[AmbientValue]` is not the usual omit path.** `ShouldSerialize` returning false normally means the member serializer never runs. AmbientValue matters for forced emit (inherited reset, some relationship/absolute cases). `BackColor`/`ForeColor` have neither `[AmbientValue]` nor `[DefaultValue]`; folding them into the same “ambient serialization” story as `Font`/`Cursor` is not accurate.
- Runtime “is this an override?” is `IsFontSet()` / `Properties.ContainsKey(s_fontProperty)`, which the ShouldSerialize change does **not** update. After `child.Font = parent.Font`, design-time `IsFontSet()` stays true (`OnParentFontChanged` will not flow parent changes), while generated code would omit `child.Font`. Designer and runtime can diverge.

### Likely issues with the ShouldSerialize equality change
1. **`ShouldSerializeBackColor` / `ShouldSerializeForeColor` are runtime API, not just CodeDOM.** Many controls use them as “was a local color set?” and then substitute a control-type default instead of inheriting:
   - `ComboBox`, `TextBoxBase`, `ListBox`, `ListView`, `TreeView`, `DateTimePicker` (`Window` / `WindowText` vs stored color)
   - painting/theme paths (`GroupBox`, `MonthCalendar`, button adapters, `UpDownBase`, etc.)
   Setting `combo.ForeColor = parent.ForeColor` (e.g. both `Color.Blue`) would make `ShouldSerializeForeColor()` false, so the ComboBox getter returns `SystemColors.WindowText`. That is a functional regression, not just a serialization tweak.
2. **`Cursor` DefaultCursor.** `Control.Cursor` does not inherit when `DefaultCursor != Cursors.Default`. `ShouldSerializeCursor` still compares to `parent.Cursor` and skips `CanAccessProperties`. An explicit `Cursor = Cursors.Default` on a TextBox that matches the parent can fail to serialize; at runtime the TextBox returns `IBeam`. `PropertyGridView` also uses `ShouldSerializeCursor()` to decide whether to restore a real cursor or `null`.
3. **`Font` comparison is not the full ambient function.** No ActiveX ambient (colors include it). Comparison is stored font vs `parent.Font` (effective, and possibly `ScaledControlFont`). After PMv2 `SetScaledFont`, the store holds a scaled instance, so serialize-time equality can be wrong in the designer on mixed DPI.
4. **Intentional snapshot is no longer representable.** A local value that happens to equal ambient used to persist (`ContainsKey`). After the change it will not, so the child cannot stay pinned if the parent later changes. Acceptable for a true ambient, harmful for window-style colors.
5. **Overrides not updated.** `ToolStripItem.ShouldSerializeFont` is still `TryGetExplicitlySetFont`. `ToolStripTextBox`/`ToolStripComboBox` compare to `ToolStripManager.DefaultFont`. `ToolStripControlHost` forwards to the hosted control and would inherit the ComboBox/TextBox color bug. `PrintPreviewControl`/`ProgressBar`/`MDIClient` keep their own ShouldSerialize overrides (good, but shows the method is overloaded).
6. **Tests only cover the happy path** (child assigned parent values ⇒ false; then different ⇒ true). Missing: no parent / equals `DefaultFont`; ComboBox/TextBox color getters; `DefaultCursor`; ActiveX; DPI/`ScaledControlFont`; nested parents; “explicit equal to ambient” still `IsFontSet()`.

### Recommended follow-up
- Treat **Font** separately from **BackColor/ForeColor/Cursor**. Do not change color/cursor `ShouldSerialize*` without auditing every runtime caller, or split “has local override” from “designer should persist.”
- For Font, prefer **clearing the store when the assigned value equals ambient** (DataContext/VisualStylesMode style) if the goal is a real ambient, so `IsFontSet()`, parent-font propagation, and CodeDOM stay aligned. If the goal is only CodeDOM, keep `ContainsKey` for runtime and teach the designer/serializer not to write a matching local value.
- If equality-based `ShouldSerializeFont` is kept, match `GetCurrentFontAndDpi` (ActiveX, `CanAccessProperties`) and add tests for DPI, no-parent/`DefaultFont`, and hosted ToolStrip controls.
- Do not expect `[AmbientValue(null)]` alone to fix ordering; it only helps when the serializer emits a reset sentinel instead of `control.Font = <effective font>`.
