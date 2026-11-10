---
name: fsharp-native-aot
description: Make F# code work under .NET Native AOT (PublishAot) and find the failures that publish warnings do not show. Covers which printf, sprintf, failwithf, and interpolated-string forms work, silent data loss from %A and ToString on unions and tuples, logging, System.Text.Json for F# records, option, list, and unions, configuration binding, warnings-as-errors, and JIT-versus-native verification. Use when an F# project sets PublishAot or PublishTrimmed, when the user asks about F# AOT or trimming, when F# output or logs differ in a native build, or when F# JSON fails with "Reflection-based serialization has been disabled".
compatibility: Requires the .NET 10 SDK, PowerShell 7.2 or later, the Native AOT toolchain for the platform (MSVC and the Windows SDK on Windows, clang on Linux), and NuGet access. Results were verified with SDK 10.0.401, FSharp.Core 10.1.401, and Microsoft.DotNet.ILCompiler 10.0.12 on win-x64.
---

# F# Native AOT

Native AOT removes runtime code generation and trims metadata. FSharp.Core formatting depends
on both. The worst F# failures are silent. The native exe prints less text than the JIT build,
and no warning points at your code. Treat the publish warning list as incomplete. Compare the
output of the JIT build with the output of the native exe.

[scripts/run.ps1](scripts/run.ps1) verifies the formatting, logging-argument, JSON-context, warning, and
quotation facts below. The other facts come from a research harness that is not bundled.
[references/FINDINGS.md](references/FINDINGS.md) lists which facts each source verifies. Run the
proof again on a newer FSharp.Core, because F# 11 changes interpolated strings and `ReflectionFree`.

## Formatting

| Form | Native exe |
|---|---|
| `sprintf "%s" s` | works |
| `sprintf "%d" x`, `%f`, `%b`, `%A` on an int, `printfn "%d"` | throws `NotSupportedException` |
| `failwithf "bad %d" x` | throws `NotSupportedException`, not the intended `Exception` |
| `$"%d{x}"`, `$"%.2f{x}"`, `$"%5d{x}"`, `$"%x{n}"`, `$"%b{b}"`, `$"{x:N1}"` | works |
| `sprintf $"..."`, `printfn $"..."`, `failwithf $"..."` | works |
| `%A` or `string` on a record with plain fields, `Some 5`, `[1; 2]` | works |
| `%A`, `string`, or `$"{x}"` on a union | loses data: `Rect`, not `Rect (2.0, 3.0)` |
| `%A` or `$"%A{x}"` on a tuple | loses data: `()`, not `(1, 2)` |
| `%A` on a `Map` | loses data. The exact text varies. |
| `string` on a record that holds a union, a tuple, or a map | loses data: `Shape = Rect`, `Pair = ()` |
| `string` on a union with a `ToString` override | works |
| `%A` on a union with a `ToString` override | loses data. `%A` ignores the override. |
| `string` on a record that holds a union with an override | loses data. The record uses `%A` for its fields. |

Rules:

- Keep the format specifier and move to interpolation. This is the smallest change:
  `sprintf "Order %d" id` becomes `$"Order %d{id}"`, and
  `failwithf "bad %d" id` becomes `failwithf $"bad %d{id}"`.
- Give every union that reaches text a `ToString` override, and call `string`, not `%A`.
- Give every record that holds a union, a tuple, or a map its own `ToString` override. A
  union override alone does not fix the record.
- Format tuples and maps with explicit code: `$"({a}, {b})"`.

```fsharp
type Shape =
    | Circle of radius: float
    | Rect of w: float * h: float
    override this.ToString() =
        match this with
        | Circle r -> $"Circle {r}"
        | Rect(w, h) -> $"Rect ({w}, {h})"

type Envelope =
    { Id: int; Shape: Shape }
    override this.ToString() = $"{{ Id = {this.Id}; Shape = {this.Shape} }}"
```

`<ReflectionFree>true</ReflectionFree>` is not a fix. `sprintf "%d"` still throws. `%A`
becomes compile error `FS0741`. Record and union `string` prints only the type name, for
example `Program+Shape+Rect`.

## Logging

Microsoft.Extensions.Logging templates, `LoggerMessage.Define` (no generator needed),
`AddSimpleConsole`, `AddJsonConsole`, and Serilog 4.3.1 or later work. Serilog 4.2.0 has
IL2072 warnings.

The logging libraries call `ToString` on arguments, so the formatting rules apply.
`logger.LogInformation("Shape {Shape}", shape)` and `$"Shape {shape}"` both log `Shape Rect`.
Pass plain values, or give the type a `ToString` override. With Serilog, a
`Destructure.ByTransforming<T>` that returns a `Dictionary<string, obj>` also works.

## JSON

`PublishAot=true` writes `JsonSerializerIsReflectionEnabledByDefault=false` into the runtime
config. Reflection serialization then throws under `dotnet run` too, so tests find it early.

Roslyn source generators do not run in F# projects. A C# project must hold a
`JsonSerializerContext`. That context alone writes `Some "x"` as `{"value":"x"}` and cannot
read an F# list. Add F# converters for option, list, and unions.

| Need | Use |
|---|---|
| Many records | C# `JsonSerializerContext` plus generic F# `OptionConverter<'T>` and `ListConverter<'T>`, and one converter per union |
| A few types | Hand-written `JsonConverter<T>` and a custom `IJsonTypeInfoResolver` |
| Thoth already in use | Thoth hand-written encoders and decoders. Not `Encode.Auto`. |
| Ad hoc documents | `Utf8JsonWriter`, `Utf8JsonReader`, `JsonDocument`, `JsonNode` |

Do not use FSharp.SystemTextJson or Newtonsoft.Json under AOT. Both throw
`NullReferenceException` in the native exe. Re-enabling reflection with
`JsonSerializerIsReflectionEnabledByDefault=true` fails on F# records with option or list fields.

Read [references/JSON.md](references/JSON.md) before you write the converters. It gives the
project layout, the converter code, the registration rules, and a check for missing entries.

## Configuration

`IConfiguration.Get<T>()` on a `[<CLIMutable>]` record can throw "missing a public instance
constructor" because the trimmer removed the constructor. The configuration binding source
generator does not run in F#. Read keys into the record directly, or add
`[<DynamicDependency(DynamicallyAccessedMemberTypes.PublicConstructors ||| DynamicallyAccessedMemberTypes.PublicProperties, typeof<Settings>)>]`
to the function that binds.

## Other results

- Works: `task`, `async`, `MailboxProcessor`, `seq`, `Set`, `Map`, structural equality and
  comparison, SRTP, deep mutual tail calls, `Expression.Compile()`, and `Regex`.
- Throws: quotation evaluation with `LeafExpressionConverter.EvaluateQuotation`.

## Warnings

- F# builds do not run Roslyn analyzers. `IsAotCompatible` and `EnableTrimAnalyzer` give no
  build-time warnings for F# code. Only `dotnet publish` reports IL2026, IL3050, and IL2xxx.
- FSharp.Core 10.1.401 produces about 200 publish warnings that you cannot fix.
- To read warnings, publish once with `-p:TrimmerSingleWarn=false`. Act only on warnings
  located in your `.fs` files.
- `TreatWarningsAsErrors` alone fails every AOT publish, because FSharp.Core reports IL2104
  and IL3053. To gate on your own code, add `<NoWarn>$(NoWarn);IL2104;IL3053</NoWarn>`. A
  user-code IL2026 still fails the publish.
- Do not combine the gate with `TrimmerSingleWarn=false`. FSharp.Core then reports detailed
  codes such as IL2067, and every publish fails. Keep the default in the gated build.
- Silent data loss from formatting emits no warning in your code.

## Verify

1. Publish and fix each warning located in your code.
2. Run the same scenarios with `dotnet run -c Release` and with the native exe. Capture stdout
   and logs. Diff them. A difference is a bug, even with zero warnings.
3. In a diff harness, write output with `Console.WriteLine` or interpolated strings. A classic
   `printfn` with a value-type hole throws before the harness can report.
4. Test each metadata fix, such as `DynamicDependency`, in its own small exe with a copy that
   has no fix. In a shared exe, a fix for one type keeps that type for every probe.

To check the facts in this skill, run the bundled proof:

```powershell
pwsh ./scripts/run.ps1
```

The runner publishes [scripts/Probe](scripts/Probe) and compares 32 probes between the JIT
and the native exe. Each probe has an expected verdict, and each failing probe has an expected exception name
or native text. It also checks three cases of the warnings-as-errors gate in
[scripts/WarnGate](scripts/WarnGate). [references/FINDINGS.md](references/FINDINGS.md) lists
the verified output.
