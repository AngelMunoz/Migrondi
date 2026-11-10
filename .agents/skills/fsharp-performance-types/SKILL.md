---
name: fsharp-performance-types
description: Choose F# collections and types for run time and allocation, and prove the result with a measurement. Use when F# code is slow or allocates too much, when a reviewer asks for fewer passes or allocations, or when you write a hot loop over list, array, seq, Map, Set, Dictionary, option, tuple, or sprintf.
compatibility: The proof script requires the .NET 10 SDK and PowerShell 7.2 or later. scripts/global.json selects .NET 10.
---

# F# performance types

Agents write F# collection code that makes several passes, allocates on each element, or uses a tree where a hash table fits.
The compiler accepts this code. Human reviewers then ask for changes.
Review evidence ranks collection efficiency among the strongest categories of human comments on agent F# code.

For readable shape (records, `match`, no `let mutable` accumulators), use [fsharp-idiom-review](../fsharp-idiom-review/SKILL.md). This skill does not repeat it.
Use this skill only for code that a measurement shows to be hot, or that a reviewer or task names as hot. Outside a hot path, prefer the readable form.

## Rules

**Collections.** [references/COLLECTIONS.md](references/COLLECTIONS.md) has the choice table and examples.

- Iterate an F# `list` with `for`, `List.fold`, or pattern matching. Do not index it, and do not copy it to an array only to loop over it.
- Do not enumerate a `seq` more than once. Each enumeration runs the source again. Materialize it once, or compute all results in one pass.
- Do not use `@` or `List.append` in a loop. Cons onto the head and call `List.rev` once, or fill a `ResizeArray`.
- In a hot loop that updates a table, use a local `Dictionary` or `HashSet`. `Map.add` allocates a new tree path on each update, and `Map.tryFind` allocates a `Some` on each hit. Return an immutable result at the end if the contract requires one. A `Dictionary` uses .NET equality: pass `HashIdentity.Structural` when the key needs F# structural equality, for example an array key.
- Keep the order that the contract defines. `List.sort`, `Array.sort`, `Map`, and `Set` order strings by ordinal. `Array.Sort`, `List<T>.Sort`, and `SortedDictionary` use the current culture unless you pass `StringComparer.Ordinal`.
- For a lookup table that is built once and read many times, use `FrozenDictionary` or `FrozenSet`.

**Single pass.**

- Replace a `filter |> map |> length` chain over large input with one `fold` or one loop that computes every result.
- Track the previous value in the loop instead of `List.pairwise` or `Seq.windowed`, which allocate a tuple or an array for each element.
- Use `Array.Parallel` only when the work for each element is large compared with the cost of a task, and measure it.

**Value types.** [references/VALUE-TYPES.md](references/VALUE-TYPES.md) has the costs and the exceptions.

- In a hot loop, return `voption` instead of `option`, and mark a partial active pattern `[<return: Struct>]`.
- Use `struct (a, b)` for a tuple that a non-inlined function returns in a hot loop, and for a `Dictionary` key. A reference tuple key allocates on each lookup.
- Do not use a struct tuple as a `sortBy` key. F# generic comparison boxes it on each comparison. Use `sortWith` with `String.CompareOrdinal` or `compare` on each field.
- Use `[<Struct>]` for a small record or union of a few primitive fields. Do not use a struct for a large value, for a value stored as `obj` or an interface, or for a value that you change through a copy.

**Allocation.**

- Do not allocate on each element in a hot loop: no `Some`, tuple, closure, list cell, substring, or boxed value for each element.
- Do not call `sprintf` in a hot loop. Use `StringBuilder.Append` or `String.Concat` with `ToString(format, CultureInfo.InvariantCulture)`. `sprintf` and `%.3f` holes use the invariant culture. `ToString("F3")` and `{x:F3}` holes use the current culture, so the output changes on a machine with a comma decimal separator.
- Do not build a string with `+` in a loop or a fold. Use one `StringBuilder`.
- Use `inline` collection functions such as `Array.sumBy` instead of `Array.filter |> Array.length`. Mark your own small higher-order helper `inline` only when a measurement shows the closure cost.

**Measurement.** [references/MEASURE.md](references/MEASURE.md) has a harness and a BenchmarkDotNet setup.

- Measure allocated bytes with `GC.GetAllocatedBytesForCurrentThread()` around one warm call in a Release build. Measure time as the median of several runs.
- Compare before and after on the same input size as production. Report the numbers, not a claim.

## Steps

1. Find the hot functions. Build a production-size input and measure the allocated bytes and the median time of one call of each. Done when you have a number for each function.
2. List each extra pass, each repeated `seq` enumeration, each `@` or index in a loop, each `Map` or `Set` update in a loop, and each allocation for each element.
3. Rewrite each item with the rules above. Keep the public types and signatures.
4. Check that the output is the same as before on the same inputs, under a culture with a comma decimal separator such as `de-DE`, with strings that differ only in case, and with a single `-0.0` value.
5. Measure again with the same harness. Report the numbers before and after. If an item does not change the numbers, revert it to the readable form.

## Verify

Run the proof script from the skill root:

```powershell
pwsh ./scripts/run.ps1
```

The script runs in `dotnet fsi --optimize+`. It measures the allocation, order, and culture claims of this skill with `GC.GetAllocatedBytesForCurrentThread()`, a run count, or a time ratio.
It fails when a measured result differs from the claim. The `FrozenDictionary`, `ImmutableArray`, and `Array.Parallel` advice comes from the .NET documentation, and the script does not measure it.
