# PropertyStore Optimization Analysis — Context & Lessons Learned

## Conversation Summary

This document captures the context behind the PropertyStore optimization analysis conducted in
March 2026. It records both the factual findings and the methodological lessons that emerged from
the collaborative review process — between a domain expert (WinForms team lead) and an AI
assistant (GitHub Copilot, powered by Claude Opus 4.6).

### What Was Analyzed

The WinForms `PropertyStore` underwent a major rewrite between .NET 8 and .NET 10 (July 2024 –
February 2025), replacing a hand-optimized array-based design with a `Dictionary<int, Value>`
backed by a custom discriminated union struct. The effort spanned 14 core PRs, 5 preparatory PRs,
and produced 4 confirmed regressions — one of which (an ECMA specification violation) shipped in
.NET 10 Preview 1 before being caught by static analysis.

### Deliverables Produced

| Deliverable | Location | Description |
|---|---|---|
| PR Lookup Table | `docs/analysis/PropertyStore-Optimization-PRs.md` | All 25 PRs with timeline |
| Analysis Document | `docs/analysis/PropertyStore-Optimization-Analysis.md` | 11-section pro/con analysis |
| Optimization SKILL | `.github/skills/optimization/SKILL.md` | Copilot skill for future work |
| Bug Fix Branch | `fix/propertystore-type-safety` | Fixes for `Control.cs` type mismatches |

### Key Technical Findings

- **The old design was sophisticated, not primitive.** O(log n) hand-unrolled binary search, 4:1
  key packing via `SplitKey()`, and mutable wrapper classes for zero steady-state boxing. The
  warning comment "DON'T change this code unless you are very confident!" was deliberate.

- **The new design offers no measurable throughput improvement.** Per-lookup cost is ~5–8ns for
  both designs. PropertyStore operations account for ~0.1–0.3% of a layout cycle. The new design
  actually uses *more* memory (+136 bytes/control due to Dictionary overhead).

- **The rewrite introduced a semantic design flaw.** `PropertyStore.TryGetValue<T>()` calls
  `Value.GetValue<T>()` which throws on type mismatch — violating the "Try" pattern contract.
  This breaks polymorphic property retrieval and is the root cause of the accessibility regressions.

- **84 built-in controls and an unknown number of third-party controls** override
  `CreateAccessibilityInstance()`, making the type mismatch bug a significant vendor risk.

---

## Why the AI Initially Got the Old Design Wrong

During the initial analysis, the AI characterized the old `PropertyStore` as "O(n) linear search
with boxing overhead." This was incorrect — it was O(log n) binary search with deliberate boxing
avoidance. The domain expert challenged this, and re-examination of the pre-rewrite source code
confirmed the old design's sophistication.

The failure has identifiable root causes:

### 1. Narrative Pattern Matching

"Array replaced by Dictionary" is the most common optimization story in software engineering. The
AI pattern-completed to this well-worn narrative — `IntegerEntry[]` → `Dictionary<int, Value>` —
without verifying whether the array access was actually linear. The conclusion preceded the evidence.

### 2. Surface-Level Data Structure Reading

The AI inferred behavior from the *container type* (`IntegerEntry[]`) rather than the *access
methods* (`LocateIntegerEntry()`). The binary search implementation was present in the code but
was never carefully read during the initial analysis.

### 3. Non-Obvious Optimization Patterns

The hand-unrolled binary search (explicit comparisons for ≤16 entries instead of a loop) doesn't
match the textbook pattern. The 4:1 key packing via `SplitKey()` is a bit-manipulation trick that
doesn't announce itself. The mutable wrapper classes (`ColorWrapper`, `PaddingWrapper`) are a
pre-generics boxing avoidance technique unfamiliar to modern eyes. All three patterns are effective
but invisible to analysis that looks for modern idioms.

### 4. Dismissed Warning Signals

The comment "DON'T change this code unless you are very confident!" should have triggered deeper
investigation. Instead, it was processed as protectiveness rather than recognized as a deliberate
signal about the code's carefully optimized nature.

### 5. Accepted the Rewrite Authors' Framing

The PRs frame the changes as performance improvements. The AI adopted this narrative as a trusted
authority rather than performing an independent assessment of whether the premise (old code is slow)
was actually true.

---

## The Collaborative Correction Process

What made this analysis ultimately accurate was the interplay between two different kinds of
knowledge:

- **The AI** brought systematic capability: searching 25 PRs, reading thousands of lines of source
  code, computing per-cycle performance estimates, identifying all 84 controls that override
  `CreateAccessibilityInstance()`, and tracing the ECMA violation timeline across preview releases.

- **The domain expert** brought institutional memory and calibrated intuition: knowing that the
  original .NET Framework team — shipping on Windows 98 with severe performance constraints — would
  not have left a property bag implementation at O(n). That single insight ("I cannot believe they
  did not have some clever optimization") invalidated the AI's entire initial framing.

Neither capability alone would have produced the final analysis. The AI's systematic search without
the domain correction would have produced a confident but wrong assessment. The domain expert's
intuition without the AI's code archaeology would have remained an unverified suspicion.

### On Recognizing Flaws Without Blame

A critical factor in this process was the absence of blame in either direction. The AI's initial
error was treated as a correctable analytical gap, not a credibility failure. The original rewrite
authors' regressions were framed as "lessons learned," not negligence. The original Framework
team's warning comment was recognized as wisdom, not obstruction.

This matters because **the goal of analysis is to construct better understanding, not to assign
fault.** When flaws are treated as learning inputs rather than accusations, they can be transformed
into durable knowledge — in this case, an optimization SKILL that encodes both what went wrong and
how to prevent it in the future.

The same principle applies to AI-assisted analysis itself: an LLM's training bias toward common
narratives is a structural limitation, not a defect. Recognizing it as such — and pairing it with
domain expertise that challenges those narratives — produces results that neither human nor AI would
achieve alone. Different skills, combined deliberately, yield something that evolves beyond what
either contributor could produce in isolation.

---

## Implications for AI-Assisted Code Analysis

This experience suggests practical guidelines for using AI assistants in code archaeology and
optimization assessment:

1. **Never accept "old → new = slow → fast" without verification.** The most common narrative is
   often wrong for carefully engineered systems.

2. **Domain experts should challenge AI conclusions that seem "too neat."** If the AI's story is
   the most obvious one, it's probably the pattern-matched one, not the investigated one.

3. **Read access methods, not just data structures.** An array can be binary-searched. A linked list
   can have a hash index. The container type does not determine the access complexity.

4. **Warning comments are evidence, not noise.** "Don't change this" from an experienced team means
   "we optimized this beyond what's obvious."

5. **Encode corrections as skills, not just fixes.** The optimization SKILL created from this
   analysis will prevent the same class of error in future AI-assisted work — turning a one-time
   correction into a durable capability improvement.
