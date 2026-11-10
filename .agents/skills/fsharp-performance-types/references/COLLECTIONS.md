# Collection choice

Use this table to pick a collection for hot code. `scripts/Perf.fsx` measures the `seq`, `@`, `List.item`, `List.toArray`, `Map`, `Dictionary`, float sum, string equality, and string order claims. The `Seq.cache`, Frozen, immutable, `HashSet`, and `Array.Parallel` notes come from the .NET documentation, and the script does not measure them.

| Need | Use | Avoid in a hot loop |
|---|---|---|
| Build a sequence of results, then read it in order | `list` built with cons and `List.rev`, or a list comprehension | `@`, `List.append` |
| Random access by index | `array` | `List.item`, `xs.[i]` on a list |
| Grow a buffer of unknown size | `ResizeArray` | `Array.append` in a loop |
| Lazy or streamed input that you read once | `seq` | Two or more enumerations of one `seq` |
| Update counts or totals by key | local `Dictionary` | `Map.add`, `Map.tryFind` on each element |
| Membership test in a loop | `HashSet` | `List.contains`, `Set.add` on each element |
| Lookup table built once, read many times | `FrozenDictionary`, `FrozenSet` | rebuilding the table on each call |
| Immutable result for callers | `Map`, `Set`, `list`, or `FrozenDictionary` built once at the end | a mutable collection in the public signature |

## Lists

An F# list is a chain of cells. Iteration with `for`, `List.fold`, or a pattern match allocates nothing.
`List.item i` and `xs.[i]` walk from the head, so a loop over the indexes is quadratic.
`List.toArray` copies every element. Do not copy a list only to loop over it.

```fsharp
// Before: quadratic
for i in 1 .. List.length prices - 1 do
    if List.item i prices > List.item (i - 1) prices then rises <- rises + 1

// After: one pass, no allocation
match prices with
| [] -> ()
| first :: rest ->
    let mutable previous = first
    for p in rest do
        if p > previous then rises <- rises + 1
        previous <- p
```

`acc @ [ x ]` copies `acc` on each call. In a loop it allocates quadratically.

```fsharp
// Before
let mutable lines = []
for order in orders do lines <- lines @ [ format order ]

// After
let lines = [ for order in orders -> format order ]
```

## Seq

A `seq` is a recipe. `Seq.length`, `Seq.isEmpty`, `Seq.sum`, and `for` each run the recipe again.
A `seq` over a filter, a query, or a file repeats the filter, the query, or the read on each enumeration.

```fsharp
// Before: three enumerations of the filter
let paid = orders |> Seq.filter isPaid
if Seq.isEmpty paid then 0m else Seq.sumBy total paid / decimal (Seq.length paid)

// After: one pass
let mutable count = 0
let mutable sum = 0m
for o in orders do
    if isPaid o then
        count <- count + 1
        sum <- sum + total o
if count = 0 then 0m else sum / decimal count
```

Use `Seq.cache` only when callers enumerate the cached sequence many times. It keeps every element alive.

## Map and Set against Dictionary and HashSet

`Map` and `Set` are balanced trees. A lookup compares keys at each level. An update allocates a new path to the changed node.
`Map.tryFind` returns an `option`, so each hit allocates a `Some`.
A `Dictionary` lookup and update allocate nothing after the table has grown. A match on `TryGetValue` with a tuple pattern also allocates nothing, because the compiler removes the tuple.

```fsharp
// Before: a tree path and a Some for each order
let mutable totals = Map.empty
for o in orders do
    let current = Map.tryFind o.Customer totals |> Option.defaultValue 0m
    totals <- Map.add o.Customer (current + o.Amount) totals

// After: no allocation for each order
let totals = Dictionary<string, decimal>(StringComparer.Ordinal)
for o in orders do
    match totals.TryGetValue o.Customer with
    | true, current -> totals[o.Customer] <- current + o.Amount
    | _ -> totals[o.Customer] <- o.Amount
```

Keep the `Dictionary` local to the function. Return an immutable value when the signature or the callers need one. An `IReadOnlyDictionary` over a `Dictionary` is only a read-only view: the owner can still change it.

`Map` uses F# structural equality and comparison. `Dictionary` uses .NET equality. For `string` and integer keys, the two agree. For an array key they do not: two equal arrays are one `Map` key and two `Dictionary` keys. A tuple that holds an array can also differ. Pass `HashIdentity.Structural` to the `Dictionary` for such a key.
For a mutable accumulator with several fields, store a small class in the `Dictionary` and change its fields. A struct value in a `Dictionary` is a copy, so a change to the copy is lost.

Start a float sum at `0.0`, as `List.sum` and `List.average` do, for example `Sum = 0.0 + first`. A sum that starts at the first value keeps a `-0.0`, and `ToString("F3")` and `sprintf "%.3f"` then print `-0.000` where `List.average` gives `0.000`.

## Order and comparers

F# comparison orders strings by ordinal: `"B"` comes before `"a"`. `List.sort`, `List.sortBy`, `Array.sort`, `Array.sortInPlaceBy`, `Map`, and `Set` use it.
`Array.Sort`, `List<T>.Sort`, `SortedDictionary`, and LINQ `OrderBy` use `Comparer<string>.Default`, which follows the current culture: `"a"` comes before `"B"`.
When you replace a `Map` or an F# sort with a .NET collection, pass `StringComparer.Ordinal` to keep the order.

```fsharp
let sorted = SortedDictionary<string, int>(StringComparer.Ordinal)
Array.Sort(names, StringComparer.Ordinal)
```

`Dictionary<string, _>()` without a comparer uses ordinal equality, the same as F# `=`.

## Frozen and immutable collections

`FrozenDictionary` and `FrozenSet` (`System.Collections.Frozen`) take longer to build and give faster reads. Build them once, for example in a module-level `let` or a `static member val`, and do not build them on each call.
`ImmutableArray` gives array reads with an immutable contract. `ImmutableDictionary` and `ImmutableList` are trees, like `Map`. They do not make a lookup faster than `Map`.

## Array.Parallel

`Array.Parallel.map` splits the work over threads. It helps only when the work for each element is large, for example a parse or a hash of a large value.
For a cheap function such as an addition, the scheduling cost is larger than the work. Measure both forms before you keep the parallel one.
