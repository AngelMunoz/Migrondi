---
name: fsharp-idiom-review
description: Review an F# change for C#-shaped code before you finish it. Use when writing, porting from C#, or reviewing F# that returns tuples, uses out parameters or bool * value pairs, chains if/elif on discrete values, uses let mutable with loops, indexes lists by position, makes several passes over one collection, or builds code text by string concatenation.
compatibility: The proof script requires the .NET 10 SDK and PowerShell 7.2 or later. scripts/global.json selects .NET 10.
---

# F# idiom review

The compiler accepts C#-shaped F#. Human reviewers then ask for a different shape.
Review evidence ranks idiomatic readability and collection efficiency among the strongest categories of human comments on agent F# code.
Codex and opencode sessions also hold 5 user corrections on this subject, for example: "can use a match statement instead of a mutable".

Apply this checklist to your own diff before you finish. It is most important when you port C# code, because a line-by-line port keeps the C# shape.
For domain modeling (constrained types, private constructors, unions for states), use [parse-dont-validate](../parse-dont-validate/SKILL.md). This skill does not repeat it.

## Checklist

Each item shows the shape to find and the shape to use. [references/EXAMPLES.md](references/EXAMPLES.md) has longer examples and the exceptions.

1. **Record, not tuple, for named results.** A function that returns or stores three or more values, or two values of the same type, returns a record.

   ```fsharp
   // Before
   let computeTotals order : decimal * decimal * int = (subtotal, discount, itemCount)
   // After
   type Totals = { Subtotal: decimal; Discount: decimal; ItemCount: int }
   let computeTotals order = { Subtotal = subtotal; Discount = discount; ItemCount = itemCount }
   ```

2. **Option, not out parameter or `bool * value`.** Port a C# `TryX(..., out T value)` method to a function that returns `T option`.

   ```fsharp
   // Before
   let tryGetRate code : bool * decimal = if code = "SAVE10" then (true, 0.10m) else (false, 0m)
   // After
   let tryGetRate code = match code with "SAVE10" -> Some 0.10m | _ -> None
   ```

   At a call to a .NET `TryParse` method, match the tuple once and return an option: `match Int32.TryParse s with | true, v -> Some v | _ -> None`.

3. **`match`, not `if`/`elif`, for discrete values.** Use `match` for string literals, union cases, enum values, and tuples. Keep `if`/`elif` for ordered numeric ranges such as `if total >= 1000m`.

   ```fsharp
   // Before
   if code = "SAVE10" then Some 0.10m elif code = "SAVE25" then Some 0.25m else None
   // After
   match code with
   | "SAVE10" -> Some 0.10m
   | "SAVE25" -> Some 0.25m
   | _ -> None
   ```

4. **Expression, not `let mutable` with a loop or branches.** Use `List.sumBy`, `List.fold`, or `List.map` for accumulators. Bind the result of a `match` or `if` instead of assigning in each branch.

   ```fsharp
   // Before
   let mutable subtotal = 0m
   for line in lines do subtotal <- subtotal + line.Price
   let mutable discount = 0m
   if ok then discount <- subtotal * rate
   // After
   let subtotal = lines |> List.sumBy _.Price
   let discount = match rate with Some r -> subtotal * r | None -> 0m
   ```

5. **`List.tryFind`, not a loop with an early exit.** F# has no `return`. A port of `foreach ... if (...) return x;` uses `tryFind`, `tryPick`, `exists`, or `forall`, not a mutable flag.

   ```fsharp
   // Before
   let mutable found = None
   for l in lines do if found.IsNone && not l.InStock then found <- Some l
   // After
   let found = lines |> List.tryFind (fun l -> not l.InStock)
   ```

6. **No position index on an F# list.** `xs.[i]` and `List.item i` walk the list from the head, so a loop over `0 .. xs.Length - 1` is quadratic. Use `List.mapi`, `List.iteri`, or `List.indexed`. Use an array when you need random access.

   ```fsharp
   // Before
   for i in 0 .. lines.Length - 1 do printfn "%d. %s" (i + 1) lines.[i].Sku
   // After
   lines |> List.iteri (fun i l -> printfn "%d. %s" (i + 1) l.Sku)
   ```

7. **One pass where one pass is enough.** Use `List.sumBy` for `List.map >> List.sum`, `List.tryFind` for `List.filter >> List.tryHead`, and `List.exists` for `List.filter >> List.isEmpty >> not`. Do not enumerate a `seq` twice: a `seq` from a query or a file runs its work again. Materialize it once with `List.ofSeq`, or compute the aggregates in one `fold`.

   ```fsharp
   // Before
   let total = lines |> List.map (fun l -> l.Price) |> List.sum
   // After
   let total = lines |> List.sumBy _.Price
   ```

8. **`match!`, not `let!` and then `match`.** In `task`, `async`, and other computation expressions, match the awaited value directly.

   ```fsharp
   // Before
   let! user = loadUser id
   match user with
   | Some u -> return u.Name
   | None -> return "unknown"
   // After
   match! loadUser id with
   | Some u -> return u.Name
   | None -> return "unknown"
   ```

9. **`static member val` for a value computed once.** `static member X = expr` is a property. It runs `expr` on each access. Use `static member val X = expr`, or a module-level `let`, for a shared value.

   ```fsharp
   // Before: a new options object on each access
   static member Options = JsonSerializerOptions(PropertyNameCaseInsensitive = true)
   // After
   static member val Options = JsonSerializerOptions(PropertyNameCaseInsensitive = true)
   ```

10. **A syntax API, not string concatenation, to make code or markup.** When the target format has a builder (Fantomas `Oak` and `Ast`, Roslyn `SyntaxFactory`, `XElement`, `Utf8JsonWriter`), use it. Concatenated text breaks on quotes, escapes, and indentation.

    ```fsharp
    // Before
    let xml = "<item sku=\"" + sku + "\"/>"
    // After
    let xml = XElement(XName.Get "item", XAttribute(XName.Get "sku", sku)).ToString()
    ```

## Steps

1. List each tuple return, `bool * value` pair, out parameter, `if`/`elif` chain, `let mutable`, `for` or `while` loop, list index, chained collection pass, `let!` followed by `match`, `static member` with a computed value, and string-built code in your diff.
   Done when each item is on the list.
2. For each item, apply the checklist entry, or keep it and state the reason. Valid reasons: a measured hot path, a .NET API that requires the shape, or an ordered numeric range for `if`/`elif`.
3. Run the code and its tests again. The public behavior must not change.

## Verify

Run the proof script from the skill root:

```powershell
pwsh ./scripts/run.ps1
```

The script runs each before form and each after form and checks that they give the same result.
It also shows that `static member X = expr` runs `expr` on each access and `static member val` runs it once.
It fails when an expected result does not occur.
