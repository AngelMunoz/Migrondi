# Signature file diagnostics

Each row has a folder in [../scripts/breaks](../scripts/breaks). The proof runner checks
the codes with SDK 10.0.401.

| Folder | Cause | Diagnostics | Build |
|---|---|---|---|
| `MissingQualifiedAccess` | `[<RequireQualifiedAccess>]` on the union in the `.fs` only | warning FS3888 | passes. A caller uses `Active` without the type name. |
| `StructMismatch` | `[<Struct>]` on the record in the `.fs` only | warning FS3888, error FS0300 | fails |
| `LiteralMismatch` | `[<Literal>]` on the value in the `.fs` only | warning FS3888, error FS0034 | fails |
| `InterfaceNotListed` | the class implements `IDisposable`, the `.fsi` does not list it | warning FS0050, error FS0193 at `use` in a caller | fails in the caller |
| `GenericMismatch` | `.fsi` says `int -> int`, the `.fs` infers `'a -> 'a` | FS0034 "The respective type parameter counts differ" | fails |
| `InlineMismatch` | `.fsi` says `val inline`, the `.fs` is not `inline` | FS0034 "The inline flags differ" | fails |
| `SealedMismatch` | `[<Sealed>]` on the class in the `.fsi` only | FS0297 "the implementation type is not sealed" | fails |
| `HiddenClassNoAttribute` | a class in the `.fsi` with members, no constructor, and no attribute | FS0938 "The representation of this type is hidden by the signature", and FS0297 because the compiler then treats the type as sealed | fails |
| `MissingValue` | `.fsi` lists a `val` that the `.fs` does not define | FS0193 "requires a value" | fails |
| `SignatureAfterImplementation` | `.fsi` after `.fs` in `Compile` | FS0238, FS0248 | fails |
| `HiddenRecordConstruction` | a caller builds a record whose type is abstract in the `.fsi` | FS0039 "The record label ... is not defined" | fails, as intended |
| `HiddenUnionCase` | a caller uses a union case whose type is abstract in the `.fsi` | FS0039 | fails, as intended |
| `HiddenHelper` | a caller uses a value that the `.fsi` leaves out | FS0039 | fails, as intended |
| `PrivateRepresentation` | a caller builds a record whose `.fsi` says `= private { ... }` | FS1093 | fails, as intended |

## Fixes

- FS3888: copy the attribute from the `.fs` to the `.fsi`. Do not remove it from the `.fs`.
- FS0938: add `[<Sealed>]` to the type in both files, or `[<Class>]` to the `.fsi`.
- FS0297: remove `[<Sealed>]` from the `.fsi`, or add it to the `.fs` class too.
- FS0050: add `interface <Name>` to the type in the `.fsi`.
- FS0034 with generic arity: add a type annotation in the `.fs`, or make the `val` generic.
- FS0034 with inline flags: make both files agree.
- FS0193 "requires a value": define the value in the `.fs`, or remove the `val`.
- FS0238 and FS0248: move the `.fsi` item before its `.fs` item.
- A union case whose name equals the type name (`type Email = Email of string`) gives
  FS0800 "Invalid use of a type name" in a caller, not FS0039, when the type is abstract.

## Parameter names in the compiled surface

The compiled parameter names come from the `.fs`, not the `.fsi`. When the `.fsi` says
`ledger: Ledger * entries: int` and the `.fs` says `_entries`, the public-surface line
shows `_entries`. Compare surfaces for types and members, not parameter names.

## Abstract type or private representation

Both `type Email` and `type Email = private { Value: string }` in the `.fsi` stop callers
from building values. The abstract type also hides the field names and is the shorter
signature. The compiled public surface is the same, except for one compiler-generated
`Equals` overload.
