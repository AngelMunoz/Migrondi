# Value types and allocation

A value type lives inline in a local, a field, or an array element. It does not allocate by itself.
A reference type allocates on the heap. In a hot loop, one allocation for each element adds GC work that a profile shows as GC pauses.
`scripts/Perf.fsx` measures the option, active pattern, tuple, sort key, boxing, closure, string, and culture claims in this file. The struct size advice comes from the .NET design guidelines, and the script does not measure it.

## Option and voption

`Some 1` allocates 24 bytes on 64-bit .NET, and `Some 1M` allocates 32 bytes. `ValueSome x` allocates nothing.

```fsharp
// Before: a Some for each even number
let tryHalf x = if x % 2 = 0 then Some(x / 2) else None

// After
let tryHalf x = if x % 2 = 0 then ValueSome(x / 2) else ValueNone
```

A partial active pattern returns an `option` by default. Mark it `[<return: Struct>]` and return `ValueSome` or `ValueNone`.

```fsharp
[<return: Struct>]
let (|Half|_|) x = if x % 2 = 0 then ValueSome(x / 2) else ValueNone
```

Keep `option` in a public signature that callers already use, and outside hot paths. `voption` has fewer library functions and is less familiar to readers.

## Tuples

A reference tuple from a function that the compiler does not inline allocates on each call. A tuple that you build and match in the same expression usually does not allocate, because the optimizer removes it.
A `struct (a, b)` tuple does not allocate.

```fsharp
let bounds a b = struct (min a b, max a b)
let struct (lo, hi) = bounds x y
```

A reference tuple as a `Dictionary` key allocates the key on each lookup, and its hash and equality go through `IStructuralEquatable`. A struct tuple key or a `[<Struct>]` record key allocates nothing.

```fsharp
let cells = Dictionary<struct (int * int), Cell>()
cells[struct (row, col)] <- cell
```

A struct tuple is the wrong choice for a sort key. `List.sortBy (fun x -> struct (x.Region, x.Code))` goes through F# generic comparison, which boxes the struct tuple on each comparison. For 500 elements it allocates more than 5 times as much as a `string` key. Compare the fields in `sortWith`:

```fsharp
lines
|> List.sortWith (fun x y ->
    match String.CompareOrdinal(x.Region, y.Region) with
    | 0 -> String.CompareOrdinal(x.Code, y.Code)
    | c -> c)
```

`KeyValue` in `for KeyValue(k, v) in dict` does not allocate. The compiler inlines it.

## Struct records and unions

`[<Struct>]` on a record or a single-case union stores the fields inline. Use it for a small value with a few primitive fields that you create often, for example a point, a range, or an id wrapper.

```fsharp
[<Struct>]
type Range = { Start: int; Length: int }

[<Struct>]
type OrderId = OrderId of int
```

A struct costs more than it saves in these cases:

- The value is large. Each assignment, argument, and return copies all fields. The .NET design guidelines recommend a struct of 16 bytes or less.
- The value is stored as `obj` or as an interface, for example `IComparable`. Each conversion boxes the value and allocates.
- The value is in a `Dictionary`, a `Map`, or a `list` and you change it. You change a copy, not the stored value.
- A multi-case struct union stores the fields of all cases in each value. Cases share storage only for fields with the same name and type.

## Closures and inline

A lambda that captures a local value allocates a closure object when the code creates it. A lambda inside a loop body allocates on each iteration.
`Array.sumBy`, `List.sumBy`, and the other `inline` functions of `FSharp.Core` copy the lambda into the caller at compile time, so the lambda allocates nothing.

```fsharp
// Before: a filtered array to count it
let above = values |> Array.filter (fun v -> v > limit) |> Array.length

// After: no allocation
let above = values |> Array.sumBy (fun v -> if v > limit then 1 else 0)
```

Mark your own higher-order helper `inline` only when a measurement shows the closure cost. `inline` copies the body into each caller and makes stack traces harder to read.
A lambda that captures a `let mutable` local makes the compiler move the local into a heap `ref` cell. With a function that is not inlined, each call of the enclosing code then allocates the closure and the cell. Use a `for` loop instead of the lambda in a hot path.

## Strings

`sprintf` allocates more than 2 times as much as `String.Concat` for the same text.
`+` in a loop or a fold copies the whole string on each step. Use one `StringBuilder`.
`StringBuilder.Append(int)` and `Append(char)` write without an intermediate string.

Culture differs between the F# and the .NET format paths:

| Form | Culture |
|---|---|
| `sprintf "%.3f" x`, `$"%.3f{x}"` | invariant |
| `string x` | invariant |
| `x.ToString("F3")`, `$"{x:F3}"`, `String.Format("{0:F3}", x)` | current |
| `x.ToString("F3", CultureInfo.InvariantCulture)` | invariant |

When you replace `sprintf` with a .NET format call, pass `CultureInfo.InvariantCulture`. Otherwise the output has a comma decimal separator under `de-DE`.

```fsharp
let inv = CultureInfo.InvariantCulture
sb.Append(name).Append(" total=").Append(total.ToString("F2", inv)).Append('\n') |> ignore
```

## Boxing

A value type that goes to an `obj` or an interface parameter is boxed: `String.Format("{0}", 5)`, `box x`, `x :> IComparable`, or a `%A` format.
F# `=` and `compare` on primitive types and strings do not box. On a struct record, `=` and `compare` allocate nothing in the proof script. Measure a generic hot path before you trust it.
