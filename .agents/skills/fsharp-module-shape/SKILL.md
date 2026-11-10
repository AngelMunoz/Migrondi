---
name: fsharp-module-shape
description: Write each F# .fs file in the service-module shape, one namespace, interface types first, one module private Helpers block, then one module per interface that holds plain implementation functions and a create factory that returns an object expression. Use whenever you create, split, or reorganize an F# file or module, add an F# service or interface with a create or deps factory, write an F# object expression, or the user says module shape, file layout, service module, or create factory.
compatibility: The proof project requires the .NET 10 SDK and PowerShell 7.2 or later. Verified with SDK 10.0.402 on win-x64.
---

# F# module shape

One `.fs` file holds one service group. The file declares the service contracts as interfaces
at the top, one private module for shared helpers, then one module per interface. Each module
carries the implementation as plain functions, and a `create` factory closes the interface over
them. The plain functions stay callable in tests without the interface, and `create` turns them
into a service you can hand to an environment.

## File template

```fsharp
namespace A.B.C

type A = ...
type B = ...
type C = ...

module private Helpers =
    let inline helper ... = ...

module A =

    let implementation args = ...

    let create (deps) =
        { new A with
            member _.Implementation(args) = implementation args }

// repeat as required for public APIs
module B = ...
```

## Rules

1. **Interfaces first.** Every public contract sits directly under the namespace, above all
   modules, together with the data types the interfaces use. The reader sees the surface before
   the code.
2. **One `module private Helpers`.** Only helpers that more than one module shares. A helper one
   module uses is a `let private` inside that module. Helpers may be `inline`, with
   `[<InlineIfLambda>]` callbacks as in
   [fsharp-function-parameters](../fsharp-function-parameters/SKILL.md).
3. **One module per interface, same name.** `type IOrdering` pairs with `module Ordering`. The
   interface is the contract, the module is the handle.
4. **Implementation functions are plain functions.** They do not see the interface. A test calls
   `Ordering.total (env, lines)` with no object expression. Their parameter shape follows
   [fsharp-function-parameters](../fsharp-function-parameters/SKILL.md).
5. **`create` is last in the module.** It takes the dependencies and returns
   `{ new A with ... }`. Each member delegates to exactly one implementation function with the
   same name in camel case. No logic in a member.
6. **`deps` is the env value** when [fsharp-env-capabilities](../fsharp-env-capabilities/SKILL.md)
   applies. Each implementation function takes `env` first. `create env` stays unannotated, so
   inference names the capability union at the factory and every environment that provides the
   union can build the service.

## Private and signature files

- **The file has a `.fsi`.** The `.fsi` does the hiding. Omit non-public modules and values from
  the `.fsi` instead of marking them `private` in the `.fs`. Write plain `module Helpers` in the
  `.fs`; the omission hides it. See
  [fsharp-signature-files](../fsharp-signature-files/SKILL.md).
- **The file has no `.fsi`.** `module private` fits when the majority of the module's functions
  are private: one annotation hides the whole block. When most functions are public, the module
  stays public, a helper inside it stays a `let private`, and the file hides its non-public
  surface best through a `.fsi`. See
  [fsharp-signature-files](../fsharp-signature-files/SKILL.md).

## Worked example

```fsharp
namespace Shop.Ordering

type OrderLine = { Sku: string; Qty: int; Price: decimal }

type IPricing =
    abstract Rate: sku: string -> decimal

type IProvidePricing =
    abstract Pricing: IPricing

type IOrdering =
    abstract Total: lines: OrderLine list -> decimal
    abstract AddLine: lines: OrderLine list * line: OrderLine -> OrderLine list

module private Helpers =

    let money (value: decimal) = System.Math.Round (value, 2)

module Ordering =

    let total (env: #IProvidePricing, lines: OrderLine list) =
        lines
        |> List.sumBy (fun line -> Helpers.money (decimal line.Qty * line.Price * env.Pricing.Rate line.Sku))

    let addLine (env, lines: OrderLine list, line: OrderLine) =
        line :: lines

    let create env =
        { new IOrdering with
            member _.Total (lines) = total (env, lines)
            member _.AddLine (lines, line) = addLine (env, lines, line) }
```

## Steps

1. Name the service group. The file and the namespace take its name.
2. List the public operations. One interface per service, one member per operation.
   Done when nothing else in the file is public.
3. Put helpers that several modules share into `module private Helpers`.
   Done when no module holds a helper another module duplicates.
4. Write each module: implementation functions first, `create` last.
   Done when each member of the interface has one same-named function to delegate to.
5. Pick the hiding mechanism. A `.fsi` exists, or the module is mostly public: hide by omission
   in the `.fsi`. No `.fsi` and the majority of functions are private: `module private`.
6. Check: call every implementation function from a test, without `create`.
   Done when no function needs the interface to run.

## Verify

Run the bundled proof from the skill root:

```powershell
pwsh ./scripts/run.ps1
```

The runner builds and runs [scripts/Shape](scripts/Shape). `Program.fs` calls the implementation
functions directly, then the same operations through `create`, and fails when the two disagree.
