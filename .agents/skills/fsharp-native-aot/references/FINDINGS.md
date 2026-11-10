# Verified probe results

`scripts/run.ps1` produced these results on win-x64 with .NET SDK 10.0.401,
FSharp.Core 10.1.401, and Microsoft.DotNet.ILCompiler 10.0.12.
The probe code is [../scripts/Probe/Program.fs](../scripts/Probe/Program.fs).

Verdicts:

- `same`: the native exe prints the JIT text.
- `throws`: the JIT returns text and the native exe throws.
- `differs`: the native exe returns different text with no exception.

`\n` in a cell stands for a line break in the output.

| Probe | Verdict | JIT | Native exe |
|---|---|---|---|
| `classic-s` | same | `OK x!` | `OK x!` |
| `classic-d` | throws | `OK Order 42` | `EXN NotSupportedException` |
| `classic-f` | throws | `OK Avg: 1.50` | `EXN NotSupportedException` |
| `classic-b` | throws | `OK true` | `EXN NotSupportedException` |
| `failwithf-classic` | differs | `OK Exception: bad id 42` | `` OK NotSupportedException: 'Microsoft.FSharp.Core.PrintfImpl+Specializations`3[Microsoft.FSharp.Core.Unit,System.String,System.String].CaptureFinal1[System.Int32](Microsoft.FSharp.Core.PrintfImpl+Step[])' is missing native code. MethodInfo.MakeGenericMethod() is not compatible with AOT compilation. Inspect and fix AOT related warnings that were generated when the app was published. For more information see https://aka.ms/nativeaot-compatibility `` |
| `interp-d` | same | `OK Order 42` | `OK Order 42` |
| `interp-f` | same | `OK Avg: 1.50` | `OK Avg: 1.50` |
| `interp-flags` | same | `OK [   42] [true] ff 1.5` | `OK [   42] [true] ff 1.5` |
| `sprintf-interp` | same | `OK Order 42` | `OK Order 42` |
| `failwithf-interp` | same | `OK Exception: bad id 42` | `OK Exception: bad id 42` |
| `A-record-plain` | same | `OK { Url = "u"\n  Retries = 3\n  Proxy = None }` | `OK { Url = "u"\n  Retries = 3\n  Proxy = None }` |
| `A-some-int` | same | `OK Some 5` | `OK Some 5` |
| `A-list-int` | same | `OK [1; 2]` | `OK [1; 2]` |
| `A-int` | throws | `OK 5` | `EXN NotSupportedException` |
| `string-union` | differs | `OK Rect (2.0, 3.0)` | `OK Rect` |
| `A-union` | differs | `OK Rect (2.0, 3.0)` | `OK Rect` |
| `A-tuple` | differs | `OK (1, 2)` | `OK ()` |
| `interp-A-tuple` | differs | `OK (1, 2)` | `OK ()` |
| `A-map` | differs | `OK map [(1, "a")]` | `OK Error: Object reference not set to an instance of an object.` |
| `string-record-with-union` | differs | `OK { Id = 1\n  Shape = Rect (2.0, 3.0) }` | `OK { Id = 1\n  Shape = Rect }` |
| `string-record-with-tuple` | differs | `OK { Id = 1\n  Pair = (1, 2) }` | `OK { Id = 1\n  Pair = () }` |
| `string-record-with-map` | differs | `OK { Id = 1\n  Items = map [(1, "a")] }` | `OK { Id = 1\n  Items = Error: Object reference not set to an instance of an object. }` |
| `string-union-override` | same | `OK Err 7` | `OK Err 7` |
| `A-union-override` | differs | `OK Err 7` | `OK Err` |
| `string-record-with-override-union` | differs | `OK { Id = 1\n  Code = Err 7 }` | `OK { Id = 1\n  Code = Err }` |
| `log-union-arg` | differs | `OK Shape Rect (2.0, 3.0)` | `OK Shape Rect` |
| `log-interp-union` | differs | `OK Shape Rect (2.0, 3.0)` | `OK Shape Rect` |
| `log-plain-args` | same | `OK Order 42 avg 1.5` | `OK Order 42 avg 1.5` |
| `json-reflection` | same | `EXN InvalidOperationException` | `EXN InvalidOperationException` |
| `json-context-plain-option` | same | `OK {"url":"u","retries":3,"proxy":{"value":"p"}}` | `OK {"url":"u","retries":3,"proxy":{"value":"p"}}` |
| `json-context-fsharp-converters` | same | `OK {"name":"Ada","age":36,"tags":["a","b"],"shape":{"kind":"rect","w":2,"h":3}} roundtrip=True` | `OK {"name":"Ada","age":36,"tags":["a","b"],"shape":{"kind":"rect","w":2,"h":3}} roundtrip=True` |
| `quotation-eval` | throws | `OK 3` | `EXN InvalidOperationException` |

The runner also checks these results:

- `json-reflection` throws under the JIT, because `PublishAot` disables reflection serialization.
- `json-context-plain-option` writes `Some "p"` as `{"value":"p"}` without F# converters.
- The publish reports IL2026 and IL3050 at the `json-reflection` call site in `Program.fs`.
- `WarnGate` publishes with `TreatWarningsAsErrors` and `NoWarn` IL2104 and IL3053.
- `WarnGate` fails with `error IL2026` when user code calls reflection JSON.
- `WarnGate` fails with `-p:TrimmerSingleWarn=false`, because FSharp.Core reports detailed codes.

The `A-map` native text varies between builds. Earlier runs printed `map [(); ()]`. This run
printed an error text. Both lose the map contents.

Results from the research harness, not in the runner:

| Case | Native exe |
|---|---|
| FSharp.SystemTextJson 1.4.36, reflection enabled | `NullReferenceException` |
| Newtonsoft.Json 13.0.4 on an F# record | `NullReferenceException` |
| Thoth.Json.Net 12.0.0 hand-written encoders and decoders | same as JIT |
| Serilog 4.3.1 template and `{@X}` on a record | same as JIT, no Serilog warnings |
| `ConfigurationBinder.Get<T>` on a `[<CLIMutable>]` record | throws "missing a public instance constructor" |
| The same call with `DynamicDependency` on the binding function | same as JIT |
| `<ReflectionFree>true</ReflectionFree>` with `sprintf "%d"` | throws `NotSupportedException` |
| `<ReflectionFree>true</ReflectionFree>` with `string` on a union | prints the type name, for example `Program+Shape+Rect` |
| `task`, `async`, `MailboxProcessor`, structural equality, SRTP, deep tail calls, `Expression.Compile()`, `Regex` | same as JIT |
