# PropertyStore Optimization Effort — PR Lookup Table

This document catalogs every Pull Request involved in the WinForms **PropertyStore optimization
effort** between .NET 8 and .NET 10. Use it as a quick-reference for investigation, servicing,
and regression tracking.

> **Last updated:** 2026-03-18
> **Branch:** `IntroduceSkills` (analysis committed on current branch)

---

## 1. Core Optimization PRs (Chronological)

These PRs form the heart of the PropertyStore rewrite, replacing the legacy sorted-array design
(with hand-unrolled binary search, 4:1 key packing, and mutable wrapper objects) with
`Dictionary<int, Value>` backed by a custom `Value` struct that extends boxing avoidance to all
primitive types.

| # | PR | Title | Author | Merged | Summary |
|---|-----|-------|--------|--------|---------|
| 1 | [#11666](https://github.com/dotnet/winforms/pull/11666) | Adds Value type | JeremyKuhne | 2024-07-12 | Introduced the `Value` struct in `System.Private.Windows.Core` — a discriminated union that stores primitives, enums, `Color`, `Size`, `DateTime`, etc. without boxing. |
| 2 | [#11668](https://github.com/dotnet/winforms/pull/11668) | Replace the guts of PropertyStore | JeremyKuhne | 2024-07-15 | Replaced PropertyStore internals with `Dictionary<int, Value>`. Removed `ColorWrapper`/`SizeWrapper`. **The single most impactful change.** |
| 3 | [#11676](https://github.com/dotnet/winforms/pull/11676) | Delete other PropertyStore wrappers | JeremyKuhne | 2024-07-15 | Removed legacy wrapper types that were no longer needed after the `Value` migration. |
| 4 | [#11677](https://github.com/dotnet/winforms/pull/11677) | Mark PropertyStore methods for removal | JeremyKuhne | 2024-07-16 | Deprecated old untyped `GetObject`/`SetObject`/`GetInteger`/`SetInteger` APIs. |
| 5 | [#11770](https://github.com/dotnet/winforms/pull/11770) | Pull PropertyStore feature branch changes into 10 | JeremyKuhne | 2024-08-02 | Merged the `feature/propertystore` branch into `main` (.NET 10). |
| 6 | [#11779](https://github.com/dotnet/winforms/pull/11779) | Property Store Refactor | elachlan | 2024-08-02 | Refactored `GetSize`/`SetSize`, `GetColor`/`SetColor` to use the new generic APIs. Updated 11 files. |
| 7 | [#11803](https://github.com/dotnet/winforms/pull/11803) | Refactor `PropertyStore.GetInteger()` | elachlan | 2024-08-04 | Migrated `GetInteger`/`SetInteger` callers to `GetValueOrDefault<int>` / `AddOrRemoveValue<int>`. |
| 8 | [#11814](https://github.com/dotnet/winforms/pull/11814) | Refactor `PropertyStore.ContainsObject` to `ContainsKey` | elachlan | 2024-08-08 | Replaced `ContainsObject` / `ContainsInteger` with unified `ContainsKey`. Added `GetStringOrEmptyString` / `AddOrRemoveString`. 18 files changed. |
| 9 | [#12174](https://github.com/dotnet/winforms/pull/12174) | Add default value to `PropertyStore.AddOrRemoveValue` | JeremyKuhne | 2024-09-20 | Added `defaultValue` parameter so callers can specify when to remove vs. store. Addressed the "default != absence" semantic issue. |
| 10 | [#12191](https://github.com/dotnet/winforms/pull/12191) | Move child controls collection out of PropertyStore | JeremyKuhne | 2024-09-21 | Moved `ControlCollection` from PropertyStore to a direct field on `Control`. Tightened type expectations for remaining properties. **Introduced the AccessibleObject type mismatch regression.** |
| 11 | [#12205](https://github.com/dotnet/winforms/pull/12205) | Update more PropertyStore usage | JeremyKuhne | 2024-09-23 | Continued DataGridView migration. Improved `AddOrRemoveValue` null correctness. 16 files changed. |
| 12 | [#12209](https://github.com/dotnet/winforms/pull/12209) | Update another set of PropertyStore usages | JeremyKuhne | 2024-09-24 | Further DataGridView/ToolStrip property migrations. |
| 13 | [#12215](https://github.com/dotnet/winforms/pull/12215) | Remove remaining old PropertyStore APIs | JeremyKuhne | 2024-09-25 | Deleted all deprecated `GetObject`/`SetObject`/`GetInteger`/`SetInteger` methods. Point of no return. |
| 14 | [#12956](https://github.com/dotnet/winforms/pull/12956) | Avoid ECMA spec violation in PropertyStore | JeremyKuhne | 2025-02-14 | Replaced `Unsafe.Unbox<T>` with `StrongBox<T>` for `Padding`/`Rectangle` to comply with ECMA-335. Fixes #12933. |

---

## 2. Preparatory / Adjacent PRs

These PRs were not optimization PRs per se but simplified or prepared the PropertyStore surface area.

| # | PR | Title | Author | Merged | Summary |
|---|-----|-------|--------|--------|---------|
| 15 | [#8594](https://github.com/dotnet/winforms/pull/8594) | Add `ContainsObjectThatIsNotNull` in PropertyStore | gpetrou | 2023-02-08 | Early enhancement to PropertyStore API for nullability support. |
| 16 | [#9503](https://github.com/dotnet/winforms/pull/9503) | `PropertyStore` refactor for generic wrappers | elachlan | 2023-08 | Added generic wrapper patterns. Preparatory for later migration. |
| 17 | [#9509](https://github.com/dotnet/winforms/pull/9509) | Fix `Form.Dispose` `NullReferenceException` | elachlan | 2023-08-09 | Fixed NRE during Form disposal related to PropertyStore nullability. |
| 18 | [#10760](https://github.com/dotnet/winforms/pull/10760) | Refactor `ErrorProvider` to remove `PropertyStore` | elachlan | 2024-01-30 | Removed PropertyStore dependency from ErrorProvider entirely. |
| 19 | [#10788](https://github.com/dotnet/winforms/pull/10788) | Refactor `IArrangedElement` to remove `PropertyStore` usage | elachlan | 2024-02 | Simplified layout element property storage. |

---

## 3. Test Refactoring PRs

| # | PR | Title | Author | Merged | Summary |
|---|-----|-------|--------|--------|---------|
| 20 | [#11719](https://github.com/dotnet/winforms/pull/11719) | Refactor `PropertyStore` tests | elachlan | 2024-07 | Initial test cleanup for the new PropertyStore API. |
| 21 | [#11730](https://github.com/dotnet/winforms/pull/11730) | Refactor `PropertyStoreTests` to `TheoryData` and use `Fact`/`Theory` | elachlan | 2024-08 | Modernized test patterns. |

---

## 4. Regression-Fix PRs

These PRs fixed bugs **caused by** the optimization effort.

| # | PR | Title | Author | Status | Fixes | Regression Source |
|---|-----|-------|--------|--------|-------|-------------------|
| 22 | [#11961](https://github.com/dotnet/winforms/pull/11961) | Fix Setting `DataGridViewImageCell.ImageLayout` to NotSet | lonitra | **Merged** 2024-08-21 | [#11927](https://github.com/dotnet/winforms/issues/11927) | `AddOrRemoveValue` removes value when set to `default`, but `ImageLayout.NotSet` (default=0) had different semantics from "not stored". |
| 23 | [#12613](https://github.com/dotnet/winforms/pull/12613) | Fix `DataGridViewComboBoxCell.ObjectCollection` casting | ricardobossan | **Merged** 2024-12-19 | [#12612](https://github.com/dotnet/winforms/issues/12612) | `AddRangeInternal` passed `ObjectCollection` where the generic API expected a different type. |
| 24 | [#12956](https://github.com/dotnet/winforms/pull/12956) | Avoid ECMA spec violation in PropertyStore | JeremyKuhne | **Merged** 2025-02-15 | [#12933](https://github.com/dotnet/winforms/issues/12933) | `Unsafe.Unbox<T>` assignment violates ECMA-335 §III.4.32. Could destabilize runtime. |
| 25 | [#14295](https://github.com/dotnet/winforms/pull/14295) | Fix `InvalidCastException` in `Control.OnHandleDestroyed` | LeafShi1 | **Open** | [#14291](https://github.com/dotnet/winforms/issues/14291) | `TryGetValue<ControlAccessibleObject>` fails when `CreateAccessibleObject()` returns a non-`ControlAccessibleObject` type. Regression from PR #12191. |

---

## 5. Regression Issue Tracker

| Issue | Title | Severity | Status | Root Cause PR |
|-------|-------|----------|--------|---------------|
| [#11927](https://github.com/dotnet/winforms/issues/11927) | `DataGridViewImageCell.ImageLayout` NotSet regression | Medium | **Closed** | PR #11814 (AddOrRemoveValue default semantics) |
| [#12612](https://github.com/dotnet/winforms/issues/12612) | `DataGridViewComboBoxCell.ObjectCollection` InvalidCast | High | **Closed** | PR #12205 or #12209 (collection type migration) |
| [#12933](https://github.com/dotnet/winforms/issues/12933) | Illegal `Unsafe.Unbox` in PropertyStore | Critical | **Closed** | PR #11668 (original PropertyStore rewrite) |
| [#14291](https://github.com/dotnet/winforms/issues/14291) | `InvalidCastException` from `Control.OnHandleDestroyed` | Critical | **Open** | PR #12191 (tightened TryGetValue type param) |

---

## 6. Key Contributors

| Contributor | GitHub | Role | PR Count |
|-------------|--------|------|----------|
| Jeremy Kuhne | [@JeremyKuhne](https://github.com/JeremyKuhne) | Architect / Lead | 11 |
| Lachlan Ennis | [@elachlan](https://github.com/elachlan) | Refactoring / Migration | 7 |
| Loni Tra | [@lonitra](https://github.com/lonitra) | Regression fix | 1 |
| Ricardo Bossan | [@ricardobossan](https://github.com/ricardobossan) | Regression fix | 1 |
| gpetrou | [@gpetrou](https://github.com/gpetrou) | Preparatory work | 1 |
| LeafShi1 | [@LeafShi1](https://github.com/LeafShi1) | Regression fix (open) | 1 |

---

## 7. Timeline Visualization

```
2023-02  ─── #8594  ContainsObjectThatIsNotNull (gpetrou)
2023-08  ─── #9503  Generic wrappers (elachlan)
         ─── #9509  Form.Dispose NRE fix (elachlan)
2024-01  ─── #10760 ErrorProvider removal (elachlan)
         ─── #10788 IArrangedElement removal (elachlan)
2024-07  ┌── #11666 Value type introduced ─────────────┐
         ├── #11668 ★ PropertyStore guts replaced ★    │ CORE
         ├── #11676 Wrapper types deleted               │ OPTIMIZATION
         ├── #11677 Old APIs marked for removal         │ PHASE
         ├── #11719 Tests refactored                    │
         ├── #11730 Tests modernized                    │
2024-08  ├── #11770 Feature branch merged to main       │
         ├── #11779 GetSize/GetColor migration          │
         ├── #11803 GetInteger migration                │
         ├── #11814 ContainsObject → ContainsKey        │
         ├── #11927 ⚠️ REGRESSION: ImageLayout          │
         ├── #11961 Fix: ImageLayout                    ┘
2024-09  ├── #12174 AddOrRemoveValue default param ────┐
         ├── #12191 ControlCollection out of store      │ CLEANUP
         ├── #12205 More DataGridView migration         │ PHASE
         ├── #12209 Further migrations                  │
         └── #12215 Old APIs fully removed ─────────────┘
2024-12  ─── #12612 ⚠️ REGRESSION: ComboBoxCell cast
         ─── #12613 Fix: ComboBoxCell cast
2025-02  ─── #12933 ⚠️ BUG: ECMA spec violation
         ─── #12956 Fix: StrongBox pattern
2026-02  ─── #14291 ⚠️ REGRESSION: OnHandleDestroyed cast
         ─── #14295 Fix: AccessibleObject type check (OPEN)
```
