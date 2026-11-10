# Idiom review examples

[scripts/Idioms.fsx](../scripts/Idioms.fsx) runs the before and after forms of each checklist item in [SKILL.md](../SKILL.md) and checks that they give the same result. The longer examples in this file are not in the proof script.

## Port a C# class

A line-by-line port keeps the C# shape. This C# method has a value tuple, an `out` parameter, an `if`/`else if` chain, a `foreach` accumulator, and an index loop:

```csharp
public static (decimal Subtotal, decimal Discount, int ItemCount) ComputeTotals(Order order) { ... }
public static bool TryGetDiscountRate(string code, out decimal rate) { if (code == "SAVE10") { ... } else if ... }
for (int i = 0; i < order.Lines.Count; i++) { var line = order.Lines[i]; ... }
```

Port each construct to its F# form:

| C# construct | C#-shaped F# | F# form |
| --- | --- | --- |
| Value tuple return | `decimal * decimal * int` | Record `{ Subtotal; Discount; ItemCount }` |
| `bool TryX(..., out T v)` | `bool * T`, `outref<T>` | `T option` |
| `if (s == "A") ... else if (s == "B")` | `if s = "A" then ... elif` | `match s with "A" -> ... \| "B" -> ...` |
| `foreach` with `+=` | `let mutable` and `for` | `List.sumBy`, `List.fold` |
| `foreach` with `return` | mutable flag | `List.tryFind`, `List.tryPick` |
| `for (i...) list[i]` | `for i in 0 .. xs.Length - 1 do xs.[i]` | `List.mapi`, `List.iteri` |
| `return null` | `Unchecked.defaultof<_>` | `None` |
| `StringBuilder` with `Append` | `sb.Append(...) \|> ignore` per line | List of lines and `String.concat` |

## Exceptions

Keep the C# shape and state the reason when one of these applies:

- A measured hot path needs a mutable loop or an array. Keep the mutable state local to the function.
- A .NET API requires the shape, for example an interface method with an `out` parameter, or a `byref` for a `Span` API.
- A two-value tuple with different types and an obvious meaning, for example `List.partition` output or a local `let ok, value = ...`, can stay a tuple.
- Ordered numeric ranges read well as `if`/`elif`. `match` with `when` guards does not add exhaustiveness for ranges.

## Mutable bound in branches

```fsharp
// Before
let mutable status = ""
if order.Lines.IsEmpty then status <- "empty"
elif order.Lines |> List.exists (fun l -> not l.InStock) then status <- "partial"
else status <- "ready"

// After
let status =
    match order.Lines with
    | [] -> "empty"
    | lines when lines |> List.exists (fun l -> not l.InStock) -> "partial"
    | _ -> "ready"
```

## Several aggregates over a seq

A `seq` from a database query, a file, or a generator runs its work again on each enumeration.

```fsharp
// Before: reads the file three times
let rows = File.ReadLines path |> Seq.map parse
let count = Seq.length rows
let total = rows |> Seq.sumBy _.Amount
let largest = rows |> Seq.maxBy _.Amount

// After: one read
let rows = File.ReadLines path |> Seq.map parse |> List.ofSeq
let count = List.length rows
let total = rows |> List.sumBy _.Amount
let largest = rows |> List.maxBy _.Amount
```

For a large input that must stream, compute the aggregates in one `Seq.fold` with a record accumulator.

## static member val

`static member X = expr` compiles to a property getter. Each access runs `expr`, so `Options` makes a new object each time and reference checks fail.

```fsharp
type Defaults =
    static member Options = JsonSerializerOptions()      // new instance on each access
    static member val Shared = JsonSerializerOptions()   // one instance
```

Use a module-level `let` when the value does not need to be on a type.

## Generated code

Build F# source with Fantomas `Oak` or the Fabulous.AST builders, C# source with Roslyn `SyntaxFactory`, XML with `XElement`, and JSON with `Utf8JsonWriter` or `JsonNode`.
String concatenation produces invalid output when a value contains a quote, a newline, or a reserved character, and indentation in F# output changes meaning.
