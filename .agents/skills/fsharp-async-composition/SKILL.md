---
name: fsharp-async-composition
description: Compose F# Task, Async, and ValueTask code with the correct semantics and builder (task, async, valueTask, cancellableTask, taskResult). Covers hot and cold start, boundary conversion, cancellation flow, exception wrapping, and ValueTask values read twice. Also covers FS0193, FS3501, and FS3511. Use when F# code mixes Task and Async, or returns or consumes ValueTask. Also use when code passes a CancellationToken, starts fire-and-forget work, or changes its async type.
compatibility: Requires the .NET 10 SDK, PowerShell 7.2 or later, and NuGet access for the proof runner. The proof runner verified the results with SDK 10.0.401 and FSharp.Core 10.1.401.
---

# F# async composition

Most defects in this area compile. The code runs, but cancellation does not reach the work,
an `Async` never starts, a `ValueTask` is read twice, or an exception changes type. Choose the
builder from the boundary type, convert once at the boundary, and pass the token explicitly.

## Builder choice

| Situation | Use |
|---|---|
| Code returns `Task` or `Task<'T>` to .NET callers (ASP.NET, interfaces, libraries) | `task { }` |
| Interface or hot path returns `ValueTask<'T>` | `valueTask { }` from IcedTasks, or `task { }` then `ValueTask<'T>(t)` at the edge |
| Work must be re-runnable or retried, or the token must flow implicitly | `async { }` |
| Task-based code that must pass a token to bound `Async` and `CancellableTask` values | `cancellableTask { }` from IcedTasks |
| `Task<Result<_,_>>` | `taskResult { }` from FsToolkit.ErrorHandling |

FSharp.Core has `task` and `async`, but no `valueTask` builder. If the code does not open
IcedTasks, `valueTask { }` gives FS0039. FS0193 between `ValueTask<'T>` and `Task<'T>`,
or between `Task<'T>` and `Async<'T>`, means the builder does not match the declared return
type. Change the builder or convert once. Do not add a conversion at each call site.

## Hot and cold

- `task` and `valueTask` are hot. The body runs up to the first incomplete await when you call
  the function. A `Task` value is a running operation, not a recipe. To retry, take
  `unit -> Task<'T>`, not `Task<'T>`.
- `async` is cold. Nothing runs until `Async.Start`, `Async.StartAsTask`,
  `Async.RunSynchronously`, or a `let!`. `work |> ignore` on an `Async` drops the work, and the
  compiler does not warn.
- `Async.AwaitTask(api.Get x)` calls `api.Get` when the `Async` value is built, not when it
  runs. A retry of that value awaits the same failed task again. Build the task inside the
  body: `async { return! Async.AwaitTask(api.Get x) }`.
- Fire-and-forget: `Async.Start(work, ct)` for `Async`, or `work () |> ignore` for a hot
  `Task`. Put an exception handler inside the work. An unhandled exception in `Async.Start`
  work is rethrown on a thread-pool thread and stops the process. Never ignore an `Async` value.

## Conversion at boundaries

| From | To | Use | Note |
|---|---|---|---|
| `Async<'T>` | `Task<'T>` | `Async.StartAsTask(work, cancellationToken = ct)` | Always pass the token. |
| `Async<'T>` | `Task<'T>` on the caller's thread | `Async.StartImmediateAsTask(work, ct)` | Same token rule. |
| `Task<'T>` | `Async<'T>` | `Async.AwaitTask t` | Faults arrive as `AggregateException`. |
| `ValueTask<'T>` | `Task<'T>` | `vt.AsTask()` once | After this, await only the `Task`. |
| `Task<'T>` | `ValueTask<'T>` | `ValueTask<'T>(t)` | |
| `Async<'T>` inside `task` | | `let! x = work` | Runs on `Async.DefaultCancellationToken`, not the caller's token. |

Do not block: `.Result`, `.Wait()`, `GetAwaiter().GetResult()`, and
`Async.RunSynchronously` hold a thread, throw at the call instead of in the returned task, and
`.Result` and `.Wait()` wrap the exception in `AggregateException`. Do not use
`ContinueWith(...).Unwrap()` in F#. `task { let! x = t ... }` composes directly.

## Cancellation

A `CancellationToken` parameter that the body does not pass on is a defect. The tests still pass.

- `task { let! x = someAsync }` ignores the function's `ct`. Start the `Async` with
  `Async.StartAsTask(work, cancellationToken = ct)`, or write the body as `async` and convert at
  the edge, or use `cancellableTask` and bind the `Async` inside it.
- Pass `ct` to every .NET call that accepts one: `Task.Delay(ms, ct)`, `stream.ReadAsync(buffer, ct)`.
- Inside `async`, `Async.CancellationToken` reads the token. `Async.Sleep` and `let!` check it.
- `Async.StartChild` does not cancel a sibling when another child fails. `Async.Parallel` does.
  The sibling stops only if its .NET call received the token from `Async.CancellationToken`.
  `CancellationToken.None` in a child means a failure cannot stop it.

```fsharp
let handle (lib: Library) (id: int) (ct: CancellationToken) : Task<Report> =
    let work =
        async {
            let! report = buildReport lib id
            let audit =
                async {
                    try
                        do! lib.Audit $"report {id} served"
                    with ex ->
                        eprintfn "audit failed: %s" ex.Message
                }
            Async.Start(audit, ct)
            return report
        }
    Async.StartAsTask(work, cancellationToken = ct)
```

## Exceptions

- An exception inside `task { }` faults the returned task. Code before `task { }` throws at
  the call. Keep validation inside the builder so callers see one failure path.
- `let!` and `GetAwaiter().GetResult()` give the original exception. `.Result`, `.Wait()`, and
  `Async.AwaitTask` give `AggregateException`. Inside `async`, a `try ... with` around
  `Async.AwaitTask` also sees `AggregateException`, so `with :? MyException` and F# exception
  patterns do not match. Unwrap one level at the boundary: match
  `:? AggregateException as a when a.InnerExceptions.Count = 1` and raise `a.InnerExceptions.[0]`. `Task.WhenAll` faults with every inner
  exception, and `let!` rethrows only the first.
- Cancellation surfaces as `OperationCanceledException` or `TaskCanceledException`. Do not
  catch it with a general `with ex ->` that logs and continues.

## ValueTask

A `ValueTask<'T>` can be consumed once. A pooled source, such as a socket, recycles its
storage when you read the result. A second read throws `InvalidOperationException`. A `Task`
source does not throw, so the defect appears only when the source is pooled.

- Do not `let!` the same `ValueTask` twice. Do not read `.Result` after `let!`.
- Do not pass `vt.AsTask()` to `Task.WhenAny` and then read `vt.Result`. Keep the `Task`:
  `let t = vt.AsTask()`, then await `t` as often as needed.
- Do not store a `ValueTask` in a field or a list for later. Convert it with `AsTask()` first.

```fsharp
let read = source.ReadAsync().AsTask()
let! winner = Task.WhenAny(read, Task.Delay timeoutMs)
if obj.ReferenceEquals(winner, read) then
    let! frame = read
    return Some frame
else
    return None
```

## State machine diagnostics

- FS3511: the compiler cannot compile a `task` state machine statically and uses a slower
  dynamic one. A `let rec` inside the builder is one cause. Move the `let rec` outside the
  builder. Other causes are non-inlined lambdas that hold resumable code and some
  generic-constraint cases. Treat FS3511 on a hot path as a defect.
- FS3501 and the related resumable-code errors come from a custom builder. Builder methods
  that take or return resumable code must be `inline`. Use an existing builder from IcedTasks
  or FsToolkit before you write one.

## Verify

1. Test each function that takes a `CancellationToken`. Cancel during the work. Check that the work stops, not only that the returned task completes.
2. For each fire-and-forget call, check that the side effect happens.
3. For a `ValueTask` API, test with a source that throws on a second read, as in
   [scripts/Proofs.fsx](scripts/Proofs.fsx).
4. Build in Release and treat FS3511 on changed code as a failure.

To check the facts in this skill, run the bundled proof:

```powershell
pwsh ./scripts/run.ps1
```

The runner runs [scripts/Proofs.fsx](scripts/Proofs.fsx) and builds
[scripts/StateMachine](scripts/StateMachine) in Release. It fails when a fact differs or when
FS3511 is not reported only for the `let rec` case. Each `PASS` line prints one fact and its
observed value. The `IcedTasks` version is pinned in the script.
