---
name: fsharp-function-parameters
description: Shape F# function parameters, tupled arguments by default, curried only when the function is designed for partial application, at most 3 parameters with env counted, one contextual object (FnArgs record) when the data parameters exceed three, and InlineIfLambda callbacks as extra tuple parameters on inline functions only. Use whenever you write or review an F# function signature or parameter list, a parameter list grows past three, you add lambda callbacks, or the user says tuple arguments, contextual object, Args record, arity, or InlineIfLambda.
compatibility: The proof script requires the .NET 10 SDK and PowerShell 7.2 or later. Verified with SDK 10.0.402 on win-x64.
---

# F# function parameters

A parameter list is the part of a function every caller reads. Three rules shape it: arguments
tupled by default, three parameters or fewer, one contextual object when the data does not fit.
One exception exists: `inline` functions with `[<InlineIfLambda>]` callbacks.

## Rules

1. **Tupled by default.** Write `let f (a: A, b: B) = ...`, not `let f (a: A) (b: B) = ...`.
   A curried list turns every missing argument into a returned closure, and the call site still
   compiles. The tupled form makes a wrong-arity call fail at the call site. A one-parameter
   function needs no tuple.

   ```fsharp
   // Use: a call without qty does not compile
   let tryReserve (sku: string, qty: int) = qty > 0 && sku.Length > 0
   // Skip: tryReserve "sku-1" returns a closure, the bug ships
   let tryReserve (sku: string) (qty: int) = qty > 0 && sku.Length > 0
   ```

2. **Curried only by design.** Curried parameters are for functions a caller partially applies:
   `lines |> List.map (pad 5)`, `parse >> validate`, a function passed half-applied as an
   argument. When no such call site exists, use the tuple. A pipeline that needs a lambda at one
   call site takes `fun x -> f (a, x)` without changing the signature.

   ```fsharp
   // Curried by design: `pad 5` is the point of the function
   let pad (width: int) (text: string) = text.PadLeft width
   let padded = [ "a"; "bb" ] |> List.map (pad 5)
   ```

3. **Three parameters or fewer, `env` counts.** Count the tuple elements. The `env` of
   [fsharp-env-capabilities](../fsharp-env-capabilities/SKILL.md) is one of them and comes
   first. One parameter needs no tuple; two and three take one tuple.

4. **Past three data parameters, one contextual object.** Move all data parameters into the
   record, not only the extras. Name it after the operation: `ship` takes `ShipArgs`. A record
   by default; a `[<Struct>]` record when it is a short-lived argument carrier on a hot path.
   The `env` stays outside the record, first.

   Why: named fields state the meaning of each argument at the call site, a new field does not
   change the arity so call sites keep compiling, and callers copy with `with` for variations.

   ```fsharp
   type ShipArgs = { OrderId: int; Address: string; Carrier: string; Tracking: bool }

   let ship (env, args: ShipArgs) = ...
   ```

5. **The InlineIfLambda exception, inline functions only.** An `inline` function may take its
   contextual object first, then one `[<InlineIfLambda>]` parameter per callback, past three
   parameters in total.

   ```fsharp
   type RetryArgs = { Attempts: int; DelayMs: int }

   let inline retry (args: RetryArgs, [<InlineIfLambda>] action: unit -> 't, [<InlineIfLambda>] onRetry: int -> unit) : 't = ...
   ```

   Why: `[<InlineIfLambda>]` makes the compiler splice the caller's lambda body into the
   function. A callback stored in a record field is a delegate call, and no inlining happens.
   A non-inline function gets no exception. Its callbacks are fields of the contextual object,
   or its parameter list stays within three.

   ```fsharp
   type WatchArgs = { Ticks: int; OnTick: int -> unit }

   let watch (args: WatchArgs) = ...
   ```

6. **Members take tupled arguments.** An interface or object member with tupled arguments
   compiles to one .NET method. A curried member compiles to a method that returns an
   `FSharpFunc`, which C# callers cannot use. See
   [fsharp-module-shape](../fsharp-module-shape/SKILL.md) for the member shape.

## Decision list

Apply in order:

1. The function exists for partial application at call sites (pipe, compose, higher-order
   argument): curried.
2. Count the parameters, `env` included. Three or fewer: one tuple.
3. More than three data parameters: all data parameters move into one `FnArgs` record.
4. Callbacks needed: the default is a plain function with callbacks as fields of the record.
   Make the function `inline` with `[<InlineIfLambda>]` extras only when the calls must not
   allocate, for example a measured hot path.

## Checklist for review

List each of these in the diff:

- A curried function with no partial-application call site.
- A parameter list over three, `env` counted.
- A contextual object that still leaves loose data parameters beside it.
- An `inline` function whose callback parameters lack `[<InlineIfLambda>]`.
- A non-inline function with loose callback parameters.

Each item: fix it, or state the reason it stays.

## Verify

Run the bundled proof from the skill root:

```powershell
pwsh ./scripts/run.ps1
```

The script runs [scripts/Shape.fsx](scripts/Shape.fsx). It exercises each rule, fails when a
result is wrong, and proves the forms compile as written.
