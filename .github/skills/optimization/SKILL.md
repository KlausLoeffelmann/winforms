---
name: optimization
description: >-
  Guidelines for optimizing WinForms runtime code. Use when asked to optimize,
  improve performance, reduce allocations, or refactor for efficiency in this
  repository. Covers profiling-first approach, type-safety preservation,
  PropertyStore patterns, GC pressure reduction, and regression prevention.
metadata:
  author: dotnet-winforms
  version: "1.0"
---

# Optimization Skill — WinForms Runtime Performance

These rules apply when **optimizing or improving performance** of existing code
in the WinForms runtime. They codify lessons learned from past optimization
efforts — including the PropertyStore rewrite — to ensure that performance
improvements do not introduce regressions.

> **Golden rule:** An optimization that introduces a regression is not an
> optimization. Measure first, change carefully, validate thoroughly.

---

## 1. Profiling-First Principle

**Never optimize without evidence.** Before making any performance change:

1. **Identify the hot path** — Use profiling data (BenchmarkDotNet, PerfView,
   dotnet-trace) to confirm the code is actually on a hot path.
2. **Quantify the cost** — Measure the current cost (allocations, CPU time,
   cache misses) with a reproducible benchmark.
3. **Set a target** — Define what "better" looks like (e.g., "reduce
   allocations by 50%" or "improve lookup from O(n) to O(1)").
4. **Measure after** — Run the same benchmark after the change and confirm
   the target was met.

### Hot Paths in WinForms

The following are confirmed hot paths where optimization effort is justified:

| Path | Why It's Hot | Typical Properties |
|------|-------------|-------------------|
| Control layout cycle | Called on resize, DPI change, child add/remove | `Padding`, `Margin`, `Dock`, `Anchor`, `Bounds` |
| Control painting | Called every `WM_PAINT` | `BackColor`, `ForeColor`, `Font`, `BackgroundImage` |
| DataGridView cell rendering | Called per-cell during scroll/paint | Cell properties, styles, images |
| Message dispatching | Called for every Windows message | `WndProc` overhead, `IsHandleCreated` |
| Control creation | Called during form initialization | Constructor properties, accessibility |

Code that runs **only during initialization** (one-time setup, dialog construction)
is generally NOT a hot path and does not justify complex optimizations.

---

## 2. Type Safety Preservation

### The "Try" Method Contract

Any method prefixed with `Try` **MUST NOT throw** on expected failure conditions.
It must return `false` (or equivalent) instead. This is a fundamental .NET API
design principle defined in the [Framework Design Guidelines](https://learn.microsoft.com/en-us/dotnet/standard/design-guidelines/).

```csharp
// ✅ CORRECT — returns false on type mismatch
public bool TryGetValue<T>(int key, out T? value)
{
    if (_values.TryGetValue(key, out Value found) && found.TryGetValue(out T? result))
    {
        value = result;
        return value is not null;
    }

    value = default;
    return false;
}

// ❌ INCORRECT — throws on type mismatch despite "Try" prefix
public bool TryGetValue<T>(int key, out T? value)
{
    if (_values.TryGetValue(key, out Value found))
    {
        value = found.GetValue<T>();  // Throws InvalidCastException!
        return value is not null;
    }

    value = default;
    return false;
}
```

### Polymorphic Type Safety

When migrating from untyped (`object`) to generic (`T`) APIs:

1. **Map every store/retrieve pair** — For each property key, identify ALL
   locations where values are stored and ALL locations where values are
   retrieved. Verify that the retrieve type is always the same as or a base
   type of the store type.

2. **Watch for inheritance** — If a property stores a base type but some
   callers retrieve a derived type, the generic API will fail. Use the base
   type for retrieval and cast afterward:

```csharp
// ❌ DANGEROUS — assumes stored value is always ControlAccessibleObject
if (Properties.TryGetValue(key, out ControlAccessibleObject? accObj))
{
    accObj.Handle = IntPtr.Zero;
}

// ✅ SAFE — retrieves as base type, then pattern-matches
if (Properties.TryGetValue(key, out AccessibleObject? accessibleObject)
    && accessibleObject is ControlAccessibleObject accObj)
{
    accObj.Handle = IntPtr.Zero;
}
```

3. **Never narrow the type** — When converting `GetObject()` calls to
   `TryGetValue<T>()`, the type parameter `T` must be the same type (or a
   base type) as what is stored. Never use a derived type.

---

## 3. PropertyStore Patterns

### Default Value Semantics

The PropertyStore's `AddOrRemoveValue<T>()` removes entries when the value
equals the default. This optimization keeps the dictionary compact, but
conflates two distinct states:

- **"Not set"** — the property was never assigned by the user.
- **"Set to default"** — the user explicitly assigned the default value.

When these states have different semantics (e.g., the getter returns a
non-default value when "not set"), you MUST NOT rely on default removal:

```csharp
// ❌ BROKEN — getter returns Normal when "not set", but NotSet is the default
set
{
    // If value == NotSet (which is default/0), it gets removed from the store.
    // Then the getter returns Normal, not NotSet. Round-trip is broken.
    Properties.AddOrRemoveValue(s_imageLayoutProperty, value);
}

// ✅ CORRECT — always store the value, never remove
set
{
    Properties.AddValue(s_imageLayoutProperty, value);
}
```

**Rule of thumb:** Use `AddOrRemoveValue` only when the getter returns
`default(T)` for absent keys. Otherwise, use `AddValue` directly.

### StrongBox Pattern for Large Value Types

Value types larger than 8 bytes (`Padding`, `Rectangle`) cannot be stored
inline in the `Value` union. The PropertyStore uses `StrongBox<T>` to wrap
them, allowing in-place updates without re-boxing:

```csharp
// StrongBox<T> is allocated once, then mutated in place.
if (_values.TryGetValue(key, out Value found) && found.Type == typeof(StrongBox<T>))
{
    found.GetValue<StrongBox<T>>().Value = newValue;  // No allocation
}
else
{
    _values[key] = Value.Create(new StrongBox<T>(newValue));  // One allocation
}
```

**Never** use `Unsafe.Unbox<T>` to write into boxed values on the heap. This
violates ECMA-335 §III.4.32 and can destabilize the runtime. Always use
`StrongBox<T>` or re-box.

---

## 4. Allocation Reduction

### Preferred Techniques (Safe)

| Technique | When to Use | Example |
|-----------|------------|---------|
| `stackalloc` | Fixed-size temp buffers ≤ 1 KB | `Span<byte> buf = stackalloc byte[256];` |
| `ArrayPool<T>` | Variable-size temp buffers | `byte[] buf = ArrayPool<byte>.Shared.Rent(size);` |
| `string.Create` | Building strings without intermediate alloc | `string.Create(len, state, (span, s) => ...)` |
| `ValueStringBuilder` | Internal string building | Already available in this repo |
| Cached pens/brushes | GDI+ drawing | `color.GetCachedPenScope()` — see GDI+ skill |
| `ReadOnlySpan<T>` params | Avoiding array allocations for params | `params ReadOnlySpan<T>` (C# 13+) |
| Collection expressions | Avoiding intermediate collections | `[item1, item2, ..items]` |

### Techniques Requiring Extra Caution

| Technique | Risk | Mitigation |
|-----------|------|------------|
| `Unsafe.As<TFrom, TTo>` | Type safety bypassed at runtime | Only for same-size blittable types, add `Debug.Assert(sizeof(...))` |
| `Unsafe.Unbox<T>` **read-only** | Spec-safe for reads only | **Never assign** to the returned `ref T` |
| `MemoryMarshal` | Layout-dependent | Verify with `[StructLayout]` attributes |
| `ref struct` / `Span<T>` returns | Lifetime management complexity | Keep scope tight, don't store in fields |

---

## 5. Regression Prevention Checklist

Before merging any optimization PR, verify:

### Semantic Preservation

- [ ] All existing tests pass without modification (if tests need changes,
      the optimization changed semantics — investigate why).
- [ ] Round-trip tests exist for properties with non-obvious default
      behavior (store value → retrieve → compare).
- [ ] Polymorphic retrieval patterns are tested (store as base type →
      retrieve as derived type → verify no exception).

### Type Migration (Untyped → Generic)

- [ ] For each property key being migrated, document the stored type and
      all retrieval types across the codebase.
- [ ] No retrieval site uses a **more derived** type than the stored type.
- [ ] `TryGetValue` callers handle `false` return values correctly.

### Performance Validation

- [ ] Benchmark shows measurable improvement on the identified hot path.
- [ ] No unintended allocation increase (check with `GC.GetAllocatedBytesForCurrentThread()`).
- [ ] Dictionary growth does not cause unexpected memory spikes for
      controls with many properties.

### Spec Compliance

- [ ] No use of `Unsafe.Unbox<T>` for write operations.
- [ ] No reliance on undocumented runtime behavior (JIT-specific
      optimizations, memory layout assumptions).
- [ ] Any `Unsafe.*` usage has been reviewed against the ECMA-335 spec.

---

## 6. Common Anti-Patterns

### Anti-Pattern: Optimizing Non-Hot Paths

```csharp
// ❌ Micro-optimization of one-time initialization code
// This runs once during control creation — not worth the complexity.
private void InitializeComponent()
{
    // Using Span<T> and stackalloc to avoid a single string allocation
    // that happens once during form setup.
    Span<char> buffer = stackalloc char[64];
    // ... complex code ...
}

// ✅ Simple, readable code for initialization paths
private void InitializeComponent()
{
    Text = "My Form";  // One string allocation, happens once. Fine.
}
```

### Anti-Pattern: Strictness Without Analysis

```csharp
// ❌ Changing from object to specific type without checking all callers
// Old code (works with any AccessibleObject):
//   object? obj = Properties.GetObject(s_accessibilityProperty);
//   if (obj is ControlAccessibleObject cao) { ... }

// New code (breaks if stored type isn't exactly ControlAccessibleObject):
Properties.TryGetValue(s_accessibilityProperty, out ControlAccessibleObject? cao);
```

### Anti-Pattern: Default-As-Absent Assumption

```csharp
// ❌ Assuming "default value" means "never set"
Properties.AddOrRemoveValue(s_property, value);  // Removes entry if value == default

// ✅ Explicit handling when "set to default" has meaning
if (value == default && PropertyAbsentMeansDefault)
    Properties.AddOrRemoveValue(s_property, value);
else
    Properties.AddValue(s_property, value);
```

---

## 7. Reference: PropertyStore API Quick Guide

| Method | Behavior | Use When |
|--------|----------|----------|
| `AddValue<T>(key, value)` | Always stores. Overwrites existing. | Setting a property unconditionally. |
| `AddOrRemoveValue<T>(key, value, default)` | Stores if ≠ default, removes if = default. | Property where absent ≡ default. |
| `AddOrRemoveString(key, value)` | Stores if non-null/empty, removes otherwise. | String properties. |
| `TryGetValue<T>(key, out T)` | Returns true if found and non-null. | Retrieving a property. |
| `TryGetValueOrNull<T>(key, out T)` | Returns true if found (even if null). | Properties where null is valid. |
| `GetValueOrDefault<T>(key, default)` | Returns value or default. Never throws. | Properties with known defaults. |
| `GetStringOrEmptyString(key)` | Returns value or `""`. | String properties. |
| `ContainsKey(key)` | Returns true if key exists. | Checking if a property was set. |
| `RemoveValue(key)` | Removes the entry. | Cleanup / disposal. |

---

## 8. Lessons from the PropertyStore Rewrite

These are the key takeaways from the 2024 PropertyStore optimization effort:

1. **Understand the old design before replacing it.** The legacy PropertyStore
   used sorted arrays with hand-unrolled binary search, 4:1 key packing, no
   boxing for integers, and mutable wrapper objects for large value types. It
   was a carefully tuned design, not a naive implementation. Characterizing
   existing code as "slow" without measuring can lead to overestimating the
   value of a rewrite.

2. **The storage mechanism was the easy part.** The `Value` struct and dictionary
   replacement were well-designed. The regressions came from migrating callers.

3. **Untyped APIs are forgiving.** `GetObject()` returning `object?` let callers
   do safe casts, null checks, and polymorphic access. Generic APIs are strict.
   Migrating from forgiving to strict requires auditing every call site.

4. **"Try" methods must not throw.** `PropertyStore.TryGetValue<T>()` throwing
   `InvalidCastException` when the stored type doesn't match `T` is a design
   defect that contradicts the method's name contract.

5. **Default value semantics are subtle.** `AddOrRemoveValue` conflating "never
   set" with "set to default" is a correctness trap for any property whose getter
   returns a non-default value when absent.

6. **ECMA spec compliance is non-negotiable.** `Unsafe.*` APIs that seem to work
   in practice may violate the spec and could break with future runtime updates.
   Always verify against the spec, especially for write operations.

7. **Regressions have long tails.** The AccessibleObject `InvalidCastException`
   was discovered 18 months after the change. Optimization efforts need thorough
   static analysis, not just unit tests, to catch type-safety regressions.

8. **Respect prior art.** The original WinForms team (targeting Windows 98) made
   performance decisions that were well-suited to their constraints. Many of those
   decisions — sorted arrays for small N, key packing, mutable wrappers — remain
   sound patterns. Before replacing "old" code, verify that its design choices
   were arbitrary rather than deliberate.
