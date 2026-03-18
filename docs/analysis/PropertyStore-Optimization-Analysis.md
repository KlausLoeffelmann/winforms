# PropertyStore Optimization Analysis — Pro / Con Assessment

This document provides a detailed analysis of the WinForms **PropertyStore optimization effort**
(Jul 2024 – Feb 2025). It evaluates the optimization potential versus the regression risk, and draws
lessons for future optimization work in the WinForms runtime.

> **See also:** [PropertyStore-Optimization-PRs.md](PropertyStore-Optimization-PRs.md) for the
> complete PR lookup table.

---

## Executive Summary

The PropertyStore optimization replaced a legacy, untyped, array-based property storage mechanism
with a modern `Dictionary<int, Value>` design using a custom discriminated-union struct (`Value`)
that avoids boxing for common value types. While the optimization goals were valid and the
implementation is technically sophisticated, the effort introduced **four confirmed regressions** —
including one ECMA spec violation and multiple `InvalidCastException` bugs — because the migration
from untyped to strictly-typed access patterns was not accompanied by sufficient analysis of the
existing polymorphic usage patterns across the codebase.

**Verdict:** The new design offers genuine improvements in API clarity, type coverage, and broader
boxing avoidance. However, the old design was not the naive implementation it may appear to be — it
used O(log n) binary search, 4:1 key packing, and already avoided boxing for the most common cases.
The performance improvement over the old design is more modest than a "linear scan → hash table"
narrative suggests. More critically, the type-strictness of the new design is both its greatest
strength (compile-time safety) and its greatest weakness (breaks polymorphic patterns that the old
untyped API tolerated).

---

## 1. What Was Optimized

### Before (Legacy Design — More Sophisticated Than It Appears)

The original PropertyStore was designed with extreme care for the Win98 era, optimizing for memory
footprint first, then read performance, then write performance. It was **not** a naive linear scan.

```
PropertyStore
├── IntegerEntry[]? _intEntries       // Sorted array, binary search
│   ├── short Key                     // Packed: upper bits = entry key
│   ├── short Mask                    // 4-bit mask of occupied slots
│   ├── int Value1 .. Value4          // 4 raw ints per entry (NO boxing)
│
├── ObjectEntry[]? _objEntries        // Sorted array, binary search
│   ├── short Key
│   ├── short Mask
│   └── object? Value1 .. Value4      // 4 object refs per entry
│
├── SplitKey(key) → (entryKey, element)   // Bottom 2 bits select slot 0-3
├── LocateIntegerEntry(key) → index       // Hand-unrolled binary search
├── LocateObjectEntry(key) → index        // Hand-unrolled binary search
│
├── Wrapper types for value-type boxing avoidance:
│   ├── ColorWrapper     (mutable class, allocated once)
│   ├── PaddingWrapper   (mutable class, allocated once)
│   ├── SizeWrapper      (mutable class, allocated once)
│   └── RectangleWrapper (mutable class, allocated once)
│
├── GetInteger(key) / SetInteger(key, int)   // No boxing
├── GetObject(key) / SetObject(key, object?)  // Polymorphic
├── GetColor(key) / SetColor(key, Color)      // Via ColorWrapper
└── GetPadding(key) / SetPadding(key, Padding) // Via PaddingWrapper
```

**Key design features of the legacy PropertyStore:**

- **O(log n) binary search** — Arrays were sorted by key. Lookup used a **hand-unrolled binary
  search** for ≤16 entries (with a "DON'T change this code unless you are very confident!" comment),
  falling back to standard binary search for larger arrays. For typical controls with 10-15 entries,
  this was highly efficient.

- **4:1 key packing** — `SplitKey` uses the bottom 2 bits of the key as an element index (0–3) and
  the upper bits as the entry key. Each entry stores **4 values**, so 40 property keys need only
  ~10 array entries. This dramatically reduces both memory and search space.

- **No boxing for integers** — `IntegerEntry` stores raw `int` fields. Booleans, enums, and other
  integer-representable values were stored via `GetInteger`/`SetInteger` without any boxing.

- **Mutable wrapper objects** — `ColorWrapper`, `PaddingWrapper`, `SizeWrapper`, and
  `RectangleWrapper` were mutable reference types stored in `ObjectEntry`. Unlike boxed structs
  (which are immutable copies), wrappers allowed **in-place mutation**: `wrapper.Padding = newValue`
  — zero allocations after the initial wrapper creation. This was an era-appropriate boxing
  avoidance pattern.

- **Compact memory** — An `IntegerEntry` is 20 bytes (2+2+4×4), an `ObjectEntry` is ~36 bytes
  (2+2+4×8). For a control with 40 properties (~10 int entries + ~10 object entries), total storage
  is ~560 bytes.

- **Polymorphic-safe** — `GetObject` returned `object?`, allowing callers to do safe casts. No type
  assumptions were baked into the storage layer.

### After (New Design)

```
PropertyStore
├── Dictionary<int, Value> _values   // Hash-based O(1) amortized lookup
│
│   Value struct (16 bytes):
│   ├── Union _union      (8 bytes)  // Inline primitive storage
│   └── object? _object   (8 bytes)  // Type flag or reference
│
├── GetValueOrDefault<T>(key) → T?
├── TryGetValue<T>(key, out T?) → bool
├── AddValue<T>(key, T) → T
├── AddOrRemoveValue<T>(key, T?, defaultValue) → T?
└── ContainsKey(key) → bool
```

- **No boxing** for `bool`, `byte`, `char`, `short`, `int`, `long`, `float`, `double`, `Color`,
  `Size`, `Point`, `DateTime`, `DateTimeOffset`, and all enum types.
- **O(1) amortized** lookup via `Dictionary<int, Value>`.
- **Type-safe** generic API with compile-time enforcement.
- **16 bytes per `Value`** (union + object pointer).
- **`StrongBox<T>`** pattern for >8-byte value types (`Padding`, `Rectangle`) — **note:** the
  initial implementation used `Unsafe.Unbox<T>` for write operations, which violated ECMA-335
  §III.4.32. This shipped to customers in .NET 10 Preview 1 (2025-01-31) and was corrected to the
  `StrongBox<T>` approach in .NET 10 Preview 2 (PR #12956, fixed 2025-02-15). The lesson here is
  not one of blame but of process: performance tricks that rely on undocumented runtime behavior
  need spec review *before* they reach a release branch, not after.
- **Higher per-entry overhead** — Each Dictionary entry is ~28 bytes (hash + next + key + Value).
  For 40 properties, that's ~1120 bytes vs. ~560 bytes in the old design. The Dictionary also
  maintains ~30-50% empty slots for hash table efficiency.

---

## 2. Pro — Optimization Potential

### 2.1 Broader Boxing Avoidance ✅

The old design already avoided boxing for `int` values and used mutable wrapper objects for `Color`,
`Padding`, `Size`, and `Rectangle`. The new `Value` struct extends this to all primitive types
(`bool`, `byte`, `char`, `float`, `double`, `long`, etc.) and all enum types. It also avoids the
per-type wrapper class allocations for `Color`, `Size`, and `Point` (which are now stored inline in
the 8-byte union).

**Where this matters:** Controls that store many non-integer primitive properties (floats, doubles)
benefit. The elimination of wrapper objects for `Color` and `Size` removes one heap allocation per
property per control — meaningful at scale (e.g., DataGridView with thousands of cells).

**Where this doesn't matter much:** Most common properties in WinForms are either `int`-representable
(already unboxed in the old design) or reference types (no boxing either way). The incremental
improvement is primarily for `Color`, `Size`, `Point`, and `bool` (which was stored as `int` before).

### 2.2 Lookup Performance — Marginal Improvement ⚠️

The old design used O(log n) binary search with hand-unrolled branches for ≤16 entries. Typical
controls have 10-15 stored properties, meaning the old search examined 3-4 entries with excellent
branch prediction characteristics.

The new Dictionary provides O(1) amortized lookup. However, for the typical 10-15 entry case, the
practical difference between a 3-4 step unrolled binary search and a dictionary hash lookup is
**marginal at best**. Dictionary lookup involves computing a hash, modulo, and following a bucket
chain — the constant factor can be comparable to the old binary search for small N.

**Where Dictionary wins clearly:** Controls like DataGridView that may have 30+ stored properties.
The O(1) vs O(log 30) difference (~5 steps) becomes measurable in tight rendering loops.

### 2.3 Type Safety ✅

The generic API catches type mismatches at the PropertyStore boundary rather than at the point of
use. This is objectively better for maintainability — type errors are caught closer to their source.

### 2.4 API Simplification ✅

The old API had separate methods for objects, integers, colors, sizes, padding, and rectangles, each
with slightly different semantics. The new API is a single generic surface with consistent behavior.

---

## 3. Con — Regression Risk

### 3.1 Polymorphic Storage Patterns Were Not Analyzed 🔴

**This is the single biggest problem with the migration.**

The old untyped `GetObject()` / `SetObject()` API naturally supported polymorphism: you could store
a `ButtonAccessibleObject` (via `AccessibleObject` variable) and retrieve it later as
`ControlAccessibleObject` with a simple cast. The new `GetValue<T>()` has **strict type semantics**
but PropertyStore's `TryGetValue<T>()` wrapper calls `GetValue<T>()` (which throws) instead of the
underlying `Value.TryGetValue<T>()` (which returns false).

**Design flaw:** `PropertyStore.TryGetValue<T>()` is not a true "try" method. If the key exists but
the stored value's type is not assignment-compatible with T, it throws `InvalidCastException`
instead of returning `false`. This breaks the .NET convention that `Try*` methods should not throw.

**Confirmed regression:** Issue [#14291](https://github.com/dotnet/winforms/issues/14291) — When a
control overrides `CreateAccessibilityInstance()` to return a non-`ControlAccessibleObject`, the
new `TryGetValue<ControlAccessibleObject>()` call in `Control.OnHandleDestroyed` throws
`InvalidCastException`. The old code used `GetObject()` which returned `object?` and let the caller
do a safe cast.

### 3.2 "Default = Absent" Semantic Mismatch 🟡

`AddOrRemoveValue<T>()` removes the entry when the value equals `defaultValue`. This is an
optimization to keep the dictionary small, but it conflates two distinct states:

- **"Not set"** — the property was never assigned.
- **"Set to default"** — the user explicitly set the property to its default value.

**Confirmed regression:** Issue [#11927](https://github.com/dotnet/winforms/issues/11927) —
`DataGridViewImageCell.ImageLayout` set to `NotSet` (which is the enum's `default`) was removed
from the store. But the getter returned `Normal` (not `NotSet`) when the key was absent, breaking
the round-trip.

### 3.3 ECMA Spec Violation Shipped to Customers 🔴

The original optimization for `Padding` and `Rectangle` (>8-byte structs) used `Unsafe.Unbox<T>`
to write directly into the boxed value on the heap, avoiding a second allocation. This was an
**illegal use** per ECMA-335 §III.4.32 — the `T&` returned by `Unsafe.Unbox<T>` must not be
assigned to.

**Timeline:**
- **2024-07-15:** Violation introduced (PR #11676, merged to `feature/propertystore`)
- **2024-07-29:** Merged to `main` (PR #11770)
- **2025-01-31:** **Shipped to customers** in .NET 10 Preview 1 (`v10.0.0-preview.1.25080.3`)
- **2025-02-13:** Flagged by .NET runtime team (GrabYourPitchforks) via static analysis
- **2025-02-15:** Fixed in PR [#12956](https://github.com/dotnet/winforms/pull/12956) via `StrongBox<T>`
- **2025-03-13:** Fix shipped in .NET 10 Preview 2

The violation was present in a publicly released .NET build for approximately 2 weeks before being
fixed (though it existed in the codebase for ~7 months before shipping). .NET 9 was **not affected**
as the PropertyStore rewrite targeted .NET 10 only.

**Lesson learned:** This is a clear example of why performance patterns using `Unsafe.*` APIs need
spec-level review *before* reaching any release branch — not after a security team discovers the
issue via static analysis. The fix was fast and correct, but the gap between introduction and
detection highlights the need for `Unsafe.*` usage to be a mandatory review checkpoint.

### 3.4 Scope of Change vs. Incremental Validation 🟡

The core optimization was developed on a feature branch (`feature/propertystore`) and merged in a
batch of ~10 PRs over 2 months. The migration touched virtually every control in WinForms that uses
properties — Control, DataGridView, ToolStrip, Form, and dozens more.

Despite test additions, the regressions were found by:
- Internal QA (ImageLayout issue)
- Community contributors (ComboBoxCell issue)
- Runtime security team static analysis (ECMA violation)
- External users hitting crashes (AccessibleObject issue — found 18 months after the change!)

---

## 4. Regression Root Cause Analysis

| Regression | Root Cause Category | Preventable? |
|-----------|---------------------|-------------|
| ImageLayout NotSet | Semantic change in default handling | ✅ Yes — unit test with explicit default round-trip |
| ComboBoxCell InvalidCast | Collection type migration error | ✅ Yes — integration test with ObjectCollection |
| ECMA Unsafe.Unbox | Undocumented runtime behavior dependency | ✅ Yes — spec review before using `Unsafe.*` APIs |
| OnHandleDestroyed InvalidCast | Polymorphic type narrowing in TryGetValue | ✅ Yes — static analysis for store-vs-retrieve type mismatches |

**All four regressions were preventable** with appropriate analysis and testing before merge.

---

## 5. Architectural Assessment

### What Was Done Well

1. **`Value` struct design** — The discriminated union pattern is genuinely clever. Storing
   primitives inline without boxing while maintaining type safety is the right approach for a
   framework that creates thousands of controls.

2. **Incremental API migration** — The phased approach (add new API → migrate callers → remove old
   API) was textbook correct. Having a feature branch was appropriate for the scope.

3. **StrongBox fix** — The response to the ECMA violation was fast and architecturally sound.
   `StrongBox<T>` is a clean, spec-compliant solution.

### What Could Be Improved

1. **PropertyStore.TryGetValue should not throw** — It should use `Value.TryGetValue<T>()` instead
   of `Value.GetValue<T>()` for the type-checking path, and return `false` when the type doesn't
   match (just like when the key doesn't exist). This would make the API genuinely polymorphism-safe.

2. **Pre-migration type-flow analysis** — Before migrating each property from `GetObject()` to
   `TryGetValue<T>()`, the team should have verified that the stored type always matches the
   retrieved type parameter across all call sites for that property key.

3. **"Default = absent" opt-in** — The `AddOrRemoveValue` optimization should be opt-out for
   properties where "set to default" has different semantics than "never set". The `defaultValue`
   parameter added in PR #12174 partially addresses this, but callers must remember to use it.

4. **Regression test pattern** — Every property migration should include a test that stores a value
   of the expected base type and retrieves it as both the stored type and any derived/base types
   that callers might use.

---

## 6. Is PropertyStore a Hot Path? — Realistic Performance Estimation

> **Note:** No benchmarks were produced alongside the optimization effort. The estimates below are
> based on known hardware characteristics, typical WinForms usage patterns, and the measured
> property access counts from the codebase. **Before accepting any optimization as necessary,
> actual benchmarks should be created and run.** The original team's warning comment —
> *"DON'T change this code unless you are very confident!"* — likely exists for exactly this
> reason: the risk/reward of modifying proven, performance-tuned code demands hard evidence.

### 6.1 What Happens During a Layout Cycle

During `PerformLayout()`, the layout engine reads and writes properties from each child control via
`CommonProperties`. From the source, the following properties are accessed **per child control**:

| Property | Type | Access | Old Storage |
|----------|------|--------|-------------|
| `Margin` | `Padding` | Read | `PaddingWrapper` in ObjectEntry |
| `Padding` | `Padding` | Read | `PaddingWrapper` in ObjectEntry |
| `MaximumSize` | `Size` | Read | `SizeWrapper` in ObjectEntry |
| `MinimumSize` | `Size` | Read | `SizeWrapper` in ObjectEntry |
| `SpecifiedBounds` | `Rectangle` | Read/Write | `RectangleWrapper` in ObjectEntry |
| `PreferredSizeCache` | `Size` | Read/Write | `SizeWrapper` in ObjectEntry |
| `LayoutBounds` | `Size` | Read | `SizeWrapper` in ObjectEntry |
| `LayoutState` | `BitVector32` | Read/Write | `int` in IntegerEntry |

**Total: ~8 reads + ~3 writes = ~11 property operations per control per layout cycle.**

### 6.2 Per-Lookup Cost Comparison

For a typical control with ~10-12 populated property keys:

| Operation | Old Design | New Design |
|-----------|-----------|------------|
| **Lookup mechanism** | Unrolled binary search on sorted array | Dictionary hash lookup |
| **Entry count** | ~3 IntegerEntries + ~4 ObjectEntries | ~12 Dictionary entries |
| **Steps** | 2–3 key comparisons (log₂ 4 ≈ 2) | Hash + modulo + 1–2 bucket comparisons |
| **Cache behavior** | Sequential array scan (cache-friendly) | Hash table (less predictable) |
| **Estimated cost** | ~5–8 ns per lookup | ~5–8 ns per lookup |
| **Integer read extra** | Direct field access (Value1–4) | Value.TryGetValue + type check |
| **Wrapper read extra** | Type check + field access (~2 ns) | StrongBox dereference (~2 ns) |

**Net difference per lookup: approximately 0 ns.** For typical entry counts (≤16), the
hand-unrolled binary search and the hash table operate in the same ~5–8 ns range. The Dictionary
gains an advantage only above ~20–30 entries, where log₂(n) > 4–5 steps.

### 6.3 Per-Layout-Cycle Savings Estimate

**Property lookup time per layout cycle (11 operations × per-control):**

| Form Size | Controls | Old (est.) | New (est.) | Δ Lookup Time |
|-----------|----------|-----------|------------|---------------|
| Small | 40 | ~3.2 µs | ~3.2 µs | **≈ 0 µs** |
| Medium | 150 | ~12 µs | ~12 µs | **≈ 0 µs** |
| Large | 500 | ~40 µs | ~40 µs | **≈ 0 µs** |

**For context — what else happens during that same layout cycle:**

| Cost Center | 40 Controls | 150 Controls | 500 Controls |
|-------------|-------------|--------------|--------------|
| Layout engine computation | ~0.5–2 ms | ~2–8 ms | ~8–30 ms |
| Win32 `SetWindowPos` calls | ~0.1–0.5 ms | ~0.5–2 ms | ~2–5 ms |
| GDI measure/arrange | ~0.1–0.5 ms | ~0.5–2 ms | ~1–5 ms |
| **PropertyStore lookups** | **~0.003 ms** | **~0.012 ms** | **~0.040 ms** |
| **Total layout cycle** | **~1–5 ms** | **~5–15 ms** | **~15–50 ms** |

**PropertyStore lookups represent ~0.1–0.3% of total layout cycle time** regardless of form size.
Even if the new design were 2× faster per lookup (which it is not for typical N), the improvement
would be ~0.05–0.15% of the total cycle — completely unmeasurable in practice.

### 6.4 Allocation / GC Pressure Savings

**During steady-state layout cycles (after initialization):**

| | Old Design | New Design | Difference |
|---|-----------|------------|------------|
| Allocations per layout cycle | **0** (wrappers already allocated, mutated in place) | **0** (Values/StrongBoxes already allocated) | **None** |

Both designs allocate zero bytes during steady-state layout. The old wrappers (`PaddingWrapper`,
`SizeWrapper`, etc.) were **mutable classes** — once allocated at initialization, they were updated
in-place via field assignment. Similarly, the new `StrongBox<T>` pattern mutates in place.

**During one-time control initialization:**

The new design eliminates wrapper object allocations for `Color`, `Size`, and `Point` properties
(now stored inline in the `Value` union). Per control, this saves ~2–4 wrapper allocations:

| Form Size | Controls | Wrapper Allocs Saved | Memory Saved | Context |
|-----------|----------|---------------------|--------------|---------|
| Small | 40 | ~80–160 | **~2–4 KB** | One-time, Gen0 |
| Medium | 150 | ~300–600 | **~7–14 KB** | One-time, Gen0 |
| Large | 500 | ~1000–2000 | **~24–48 KB** | One-time, Gen0 |

This is a modest Gen0 GC pressure reduction during form initialization, amortized across the
entire startup sequence.

### 6.5 Memory Footprint Comparison

The new design actually uses **more memory per control** for typical property counts due to
Dictionary overhead:

| | Old Design (12 props) | New Design (12 props) | Difference |
|---|---|---|---|
| Entry storage | ~3 IntEntries × 20B + ~4 ObjEntries × 36B = **204 B** | 12 Dict entries × 28B = **336 B** | +132 B |
| Table overhead | 0 (raw arrays) | Buckets + metadata ≈ **100 B** | +100 B |
| Wrapper objects | ~4 wrappers × 24B = **96 B** | 0 | −96 B |
| **Total per control** | **~300 B** | **~436 B** | **+136 B** |

| Form Size | Controls | Old Total | New Total | Δ Memory |
|-----------|----------|-----------|-----------|----------|
| Small | 40 | ~12 KB | ~17 KB | **+5 KB** |
| Medium | 150 | ~45 KB | ~65 KB | **+20 KB** |
| Large | 500 | ~150 KB | ~218 KB | **+68 KB** |

The new design consumes **more heap memory** than the old design for typical controls. The 4:1 key
packing in the old design was remarkably space-efficient.

### 6.6 Summary of Measurable Impact

| Metric | Old → New | Magnitude | Hot Path? |
|--------|----------|-----------|-----------|
| Lookup speed | O(log n) → O(1) | ≈ 0 ns difference for typical N | ✅ Yes, but negligible |
| Layout cycle time | ~0.003–0.040 ms | ~0.1–0.3% of cycle | ✅ Unmeasurable |
| Steady-state allocs | 0 → 0 | No change | — |
| Init-time allocs | Wrappers eliminated | ~2–48 KB one-time | ❌ Init only |
| Heap footprint | Compact → Larger | +5 to +68 KB | ❌ Worse |

---

## 7. Risk-Benefit Assessment

### 7.1 The Benefit

The optimization delivers:
- **API simplification** — unified generic surface replacing 6+ method families (genuine win for
  maintainability)
- **Type safety at the storage boundary** — catches programming errors earlier (genuine win)
- **Broader boxing avoidance** — extending to all primitives, not just `int` (modest win)
- **No measurable throughput improvement** for typical forms during layout, painting, or interaction

### 7.2 The Cost

The optimization introduced:
- **4 confirmed regressions**, including customer-facing `InvalidCastException` crashes
- **1 ECMA spec violation** that could have destabilized the runtime
- **1 regression still open 18 months later** (issue #14291), meaning customer applications that
  worked on .NET 8 may crash on .NET 9/10 with no code change on their part
- **Higher memory footprint** per control than the design it replaced

### 7.3 Customer Impact Perspective

WinForms is the UI framework of choice for a significant segment of enterprise and line-of-business
applications — banking, healthcare, manufacturing, government, and logistics systems that often run
mission-critical workflows. For these customers:

- **Application stability is paramount.** A crash during `OnHandleDestroyed` or `WM_CREATE` due to
  an `InvalidCastException` from a property store type mismatch is not a minor inconvenience. It can
  interrupt workflows that involve live data, operator interactions, or time-sensitive processes.

- **Upgrading .NET versions is a significant investment.** Enterprise customers evaluate new .NET
  versions over months, with extensive regression testing. When a previously working application
  crashes after a framework update — with no code change on the customer's side — the trust cost is
  disproportionate to the technical severity of the bug.

- **Servicing has compounding costs.** Each regression requires investigation, a fix, code review,
  a servicing release, and communication to affected customers. When multiple regressions stem from
  the same effort, the cumulative servicing cost can exceed the original development cost.

For this particular optimization, the measurable runtime benefit is in the low single-digit
microseconds per layout cycle (indistinguishable from measurement noise), while the cost includes
production crashes, servicing overhead, and erosion of platform reliability expectations.

### 7.4 The Proportionality Question

A useful heuristic for evaluating optimization efforts in a mature framework:

> **Is the measurable improvement large enough to justify the probability-weighted cost of
> regressions?**

For PropertyStore, the answer is nuanced. The API simplification has long-term maintainability
value that is difficult to quantify. The type safety improvements caught real bugs during the
migration (code that was silently wrong under the old API). These are genuine benefits.

However, the performance narrative — faster lookups, less boxing, reduced GC pressure — does not
survive quantitative scrutiny for the typical case. The old design was already well-optimized for
its workload. The measurable runtime improvement is approximately zero for a small form and tens
of microseconds for a large form, against a layout cycle measured in tens of milliseconds.

When the improvement is indistinguishable from noise but the regressions cause application crashes,
the effort would have benefited from:
1. **Benchmarking before starting** — to set realistic expectations for the payoff
2. **A more conservative migration strategy** — preserving the polymorphic-safe API alongside the
   new typed API, rather than replacing it entirely
3. **Exhaustive type-flow analysis** — mapping every store/retrieve pair before migration

---

## 8. Recommendations

### For Immediate Action

0. **Create benchmarks first** — Before any further optimization of PropertyStore or similar
   infrastructure, create BenchmarkDotNet benchmarks that measure property lookup, property write,
   layout cycle, and painting cycle performance on realistic forms (40, 150, 500 controls). The
   original team's *"DON'T change this code unless you are very confident!"* comment on the binary
   search implementation was likely a warning born from experience. Future changes to
   performance-critical infrastructure should require benchmark evidence as a prerequisite.

1. Fix the `TryGetValue<T>` design flaw — make it a true "try" method that returns `false` on type
   mismatch rather than throwing. This is the root cause of the AccessibleObject regression and
   likely other latent bugs.

2. Audit all property keys where the stored type and retrieved type might differ (polymorphic
   patterns). See the companion bug analysis document for findings.

3. Ensure PR [#14295](https://github.com/dotnet/winforms/pull/14295) is reviewed and merged, then
   service the fix back to .NET 9.

### For Future Optimization Work

1. **Benchmark before starting, not after finishing** — Quantify the current cost and set a
   measurable target. If the estimated improvement is within measurement noise (as it is for
   PropertyStore lookups on typical forms), the effort may not justify the regression risk.

2. **Type-flow analysis first** — Before changing a storage mechanism from untyped to typed, map
   every store/retrieve pair and verify type compatibility.

3. **"Try" means "don't throw"** — Any method prefixed with `Try` must never throw on expected
   failure conditions. This is a fundamental .NET API design principle.

4. **Test the semantic edge cases** — Default values, null values, polymorphic types, and the
   "set to default" vs. "never set" distinction must all be tested.

5. **Avoid `Unsafe.*` unless reviewed** — Any use of `System.Runtime.CompilerServices.Unsafe`
   should be reviewed against the ECMA spec by someone familiar with the runtime internals.

6. **Respect "DON'T change" comments** — When code has explicit warnings against modification, treat
   those as requirements for benchmark evidence, not as challenges to overcome.

---

## 9. Conclusion

The PropertyStore optimization was a well-intentioned effort to modernize a core WinForms
infrastructure component. The `Value` struct and dictionary-based storage are genuine improvements
in API clarity and type coverage. However, the characterization of the old design as a naive
"linear scan with boxing" is inaccurate — it was a carefully tuned sorted-array design with
hand-unrolled binary search, 4:1 key packing, no boxing for integers, and mutable wrapper objects
for large value types. The actual performance delta is more nuanced than "O(n) → O(1)".

More importantly, the migration from untyped to strictly-typed APIs was done without sufficient
analysis of the existing polymorphic usage patterns, resulting in regressions that are still being
discovered 18+ months later. The old `GetObject()` API was polymorphic-safe by design; the new
`TryGetValue<T>()` API is strict by design. Migrating from forgiving to strict requires auditing
every call site.

The key lesson: **Before rewriting infrastructure that "looks old", verify that its design choices
were arbitrary rather than deliberate. The original WinForms team made performance decisions that
were well-suited to the constraints of their era — and many of those decisions remain sound today.**

---

## 10. Potential Risk Assessment for Third-Party Control Vendors

### 10.1 Scope of Exposure

WinForms has a large ecosystem of third-party control vendors (DevExpress, Telerik, Syncfusion,
Infragistics, ComponentOne, and many smaller vendors) as well as enterprise in-house control
libraries. These vendors build custom controls that inherit from `Control`, `UserControl`, and other
base classes, overriding virtual methods that interact with PropertyStore-backed properties.

`PropertyStore` itself is `internal sealed` — third-party code cannot access it directly. The risk
is **indirect**: through overriding virtual methods where the framework's internal PropertyStore
usage has become type-strict in ways that conflict with previously valid polymorphic patterns.

### 10.2 The CreateAccessibilityInstance() Risk — Critical

**84 built-in controls** override `CreateAccessibilityInstance()`. This is a standard, well-documented
override point that third-party vendors routinely use to provide custom accessibility behavior for
their controls.

The override contract is:

```csharp
// Control.cs — public virtual method, documented return type: AccessibleObject
protected virtual AccessibleObject CreateAccessibilityInstance()
    => new ControlAccessibleObject(this);
```

Any vendor returning a custom `AccessibleObject` subclass that is **not** a `ControlAccessibleObject`
will cause an `InvalidCastException` during form disposal or handle recreation on .NET 10. This is
a silent breaking change — the vendor's code compiled and worked on .NET 8 and .NET 9 without any
issue. No compiler warning, no runtime warning, no documentation of the behavioral change.

**Estimated exposure:** If even a small fraction of third-party controls use custom `AccessibleObject`
subclasses (which is common for controls with complex accessibility trees, such as grids, charts,
editors, and docking panels), the number of affected applications in production is significant.

### 10.3 The Default Value Semantics Risk — Moderate

Third-party controls that define enum or value-type properties backed by the base `Control`
infrastructure may encounter the "default = absent" confusion from `AddOrRemoveValue`. If a
vendor's property getter returns a non-default value when the property was never set (a common
pattern for backwards compatibility), setting the property to its enum default will now silently
remove it from the store, causing the getter to return the "never set" value instead.

This is a subtle behavioral change that may manifest as incorrect property values in designer
scenarios, serialization round-trips, or runtime state.

### 10.4 What Third-Party Vendors Cannot Easily Detect

The challenge for vendors is that these issues are **not caught by compilation, static analysis, or
most testing scenarios**:

1. **The `InvalidCastException` only occurs during handle destruction** — most test suites don't
   exercise control teardown under the specific conditions that trigger it (accessibility object
   already created before handle is destroyed or recreated).

2. **The default-value issue only manifests with specific enum values** — setting a property to
   `FormWindowState.Normal` or `ImageLayout.NotSet` (both value `0`) behaves differently from
   setting it to any other value.

3. **There is no compile-time signal** — the generic type parameter in `TryGetValue<T>` is an
   internal framework implementation detail that vendors cannot see or influence.

### 10.5 Risk Matrix

| Risk | Trigger | Severity | Detectability | .NET 9 | .NET 10 |
|------|---------|----------|---------------|--------|---------|
| Custom `AccessibleObject` crash | Form close / handle recreate | **Critical** — app crash | Low — requires specific teardown path | ❌ Safe | 🔴 Affected |
| Default value property loss | Set enum property to default(T) | **Medium** — wrong value | Low — subtle behavioral | ❌ Safe | 🟡 Case-by-case |
| ECMA spec violation | Any Padding/Rectangle write | **High** — runtime destabilization | None — invisible | ❌ Safe | ✅ Fixed in Preview 2 |

---

## 11. Recommendations for Immediate and Long-Term Mitigation

### 11.1 Immediate — Before .NET 10 GA (Windows 11 Timeframe)

These actions should be completed before .NET 10 ships as a GA release:

1. **Merge the `OnHandleDestroyed` fix** — PR [#14295](https://github.com/dotnet/winforms/pull/14295)
   fixes the `InvalidCastException` in `OnHandleDestroyed` for custom accessibility objects. This
   has been open since February 2026 and should be prioritized for merge. Additionally, the same
   pattern at `InternalWmCreate` (line 7373 — see fix branch `fix/propertystore-type-safety`) must
   be fixed as well; PR #14295 does not cover that code path.

2. **Fix `PropertyStore.TryGetValue<T>` to not throw on type mismatch** — The root cause is that
   `TryGetValue` calls `Value.GetValue<T>()` (which throws) instead of `Value.TryGetValue<T>()`
   (which returns false). Fixing this at the PropertyStore level would make ALL retrieval sites
   polymorphism-safe by default, eliminating the entire class of bugs rather than fixing them
   one call site at a time. This is the highest-leverage fix available.

3. **Audit all remaining `TryGetValue<DerivedType>` calls** — A comprehensive audit (see Section C
   of this analysis) found that the current codebase has only the two accessibility-related type
   mismatches. However, future code changes could introduce new ones. Consider adding a Roslyn
   analyzer that flags `TryGetValue<T>` calls where `T` is more derived than the stored type.

4. **Add regression tests for third-party override patterns** — Create tests that exercise:
   - Custom `CreateAccessibilityInstance()` returning non-`ControlAccessibleObject` types
   - Property round-trips for all enum-typed properties with `default(T)` values
   - Handle creation/destruction cycles with pre-existing accessibility objects

### 11.2 Short-Term — .NET 10 Servicing / .NET 11 Preview Cycle

5. **Publish a known-issue advisory** — Document the `CreateAccessibilityInstance()` behavioral
   change in the .NET 10 release notes and known issues list, so that third-party vendors can
   audit their controls proactively. Even though `PropertyStore` is internal, the *effect* of the
   change is visible through public virtual methods.

6. **Consider servicing the fix back to .NET 10 GA** — If the `TryGetValue` fix lands in .NET 11
   only, customers on .NET 10 LTS will carry the risk until they upgrade. Evaluate whether the fix
   qualifies for a .NET 10 servicing update.

7. **Create BenchmarkDotNet benchmarks for PropertyStore** — As recommended in Section 8, create
   benchmarks that quantify the actual performance difference between the old and new designs.
   This provides evidence for future optimization decisions and ensures that any further changes
   to PropertyStore are grounded in measurement.

### 11.3 Long-Term — .NET 11+ Architecture

8. **Make `PropertyStore.TryGetValue` genuinely polymorphism-safe** — If not done as an immediate
   fix, this should be a .NET 11 priority. The method should use `Value.TryGetValue<T>()` (which
   supports `_object is T` runtime type checking, including inheritance) rather than
   `Value.GetValue<T>()` (which throws on mismatch). This preserves the behavioral safety of the
   old `GetObject()` API while keeping the generic type system.

9. **Establish an "optimization review" process** — For changes to performance-critical
   infrastructure (PropertyStore, Control message dispatching, layout engine):
   - Require benchmark evidence before and after
   - Require ECMA/spec review for any `Unsafe.*` or `MemoryMarshal` usage
   - Require a type-flow audit when migrating from untyped to typed APIs
   - Require third-party vendor scenario testing (custom accessible objects, custom properties)

10. **Consider a compatibility mode** — For enterprise customers who cannot immediately update their
    third-party control libraries, a runtime compatibility switch (e.g.,
    `System.Windows.Forms.PropertyStore.UseLegacyPolymorphicAccess`) could allow opting into
    the old behavioral semantics. This is a pattern used elsewhere in .NET for managing breaking
    changes across framework versions.
