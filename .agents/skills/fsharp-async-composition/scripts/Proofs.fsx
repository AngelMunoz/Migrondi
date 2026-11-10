#r "nuget: IcedTasks, 0.11.7"

open System
open System.Threading
open System.Threading.Tasks
open System.Threading.Tasks.Sources
open IcedTasks

let mutable failures = 0

let check name (expected: obj) (actual: unit -> obj) =
    let value =
        try actual () with ex -> box $"EXN {ex.GetType().Name}"
    if value = expected then
        Console.WriteLine $"PASS {name}: {value}"
    else
        failures <- failures + 1
        Console.WriteLine $"FAIL {name}: expected {expected}, got {value}"

let errorName (f: unit -> unit) =
    try f (); "none" with ex -> ex.GetType().Name

type PooledSource() =
    let mutable core = ManualResetValueTaskSourceCore<int>()
    member this.Next(value: int) =
        core.Reset()
        let vt = ValueTask<int>(this :> IValueTaskSource<int>, core.Version)
        core.SetResult value
        vt
    interface IValueTaskSource<int> with
        member _.GetResult(token) =
            let value = core.GetResult token
            core.Reset()
            value
        member _.GetStatus(token) = core.GetStatus token
        member _.OnCompleted(c, s, token, flags) = core.OnCompleted(c, s, token, flags)

check "async is cold: a defined async does not run" (box 0) (fun () ->
    let mutable runs = 0
    let work = async { runs <- runs + 1 }
    work |> ignore
    Thread.Sleep 50
    box runs)

check "Async.Start runs a cold async" (box 1) (fun () ->
    let mutable runs = 0
    Async.Start(async { runs <- runs + 1 })
    Thread.Sleep 100
    box runs)

check "task is hot: code before the first await runs at creation" (box 1) (fun () ->
    let mutable runs = 0
    let work = task { runs <- runs + 1 }
    let seen = runs
    work.Wait()
    box seen)

check "an exception in task gives a faulted task, not a throw at the call" (box "faulted") (fun () ->
    let work () : Task<int> = task { return raise (InvalidOperationException "bad") }
    let t = work ()
    box (if t.IsFaulted then "faulted" else string t.Status))

check "Async.RunSynchronously throws at the call" (box "InvalidOperationException") (fun () ->
    let work () : Task<int> =
        Task.FromResult(Async.RunSynchronously(async { return raise (InvalidOperationException "bad") }))
    box (errorName (fun () -> work () |> ignore)))

check "let! of an Async inside task runs on Async.DefaultCancellationToken" (box true) (fun () ->
    let probe = async {
        let! ct = Async.CancellationToken
        return ct = Async.DefaultCancellationToken }
    box (task { return! probe }).Result)

check "Async.StartAsTask with cancellationToken cancels the work" (box "Canceled") (fun () ->
    use cts = new CancellationTokenSource()
    let t = Async.StartAsTask(Async.Sleep 300, cancellationToken = cts.Token)
    cts.CancelAfter 50
    (try t.Wait 2000 |> ignore with _ -> ())
    box (string t.Status))

check "cancellableTask passes its token to a bound Async" (box true) (fun () ->
    use cts = new CancellationTokenSource()
    let work = cancellableTask {
        let! ct = CancellableTask.getCancellationToken ()
        let! inner = Async.CancellationToken
        return ct = cts.Token && inner = cts.Token }
    box (work cts.Token).Result)

check "Async.AwaitTask on a faulted task raises AggregateException" (box "AggregateException") (fun () ->
    let failed = Task.FromException<int>(InvalidOperationException "bad")
    box (errorName (fun () -> Async.RunSynchronously(Async.AwaitTask failed) |> ignore)))

check "try/with inside async sees AggregateException from Async.AwaitTask" (box "AggregateException") (fun () ->
    let work = async {
        try
            let! v = Async.AwaitTask(Task.FromException<int>(InvalidOperationException "bad"))
            return "none"
        with
        | :? InvalidOperationException -> return "InvalidOperationException"
        | ex -> return ex.GetType().Name }
    box (Async.RunSynchronously work))

check "Async.AwaitTask(start ()) starts the task once, however often the Async runs" (box 1) (fun () ->
    let mutable starts = 0
    let start () =
        starts <- starts + 1
        Task.FromResult 1
    let work = Async.AwaitTask(start ())
    Async.RunSynchronously work |> ignore
    Async.RunSynchronously work |> ignore
    box starts)

check "async { return! Async.AwaitTask(start ()) } starts the task on each run" (box 2) (fun () ->
    let mutable starts = 0
    let start () =
        starts <- starts + 1
        Task.FromResult 1
    let work = async { return! Async.AwaitTask(start ()) }
    Async.RunSynchronously work |> ignore
    Async.RunSynchronously work |> ignore
    box starts)

check "let! of a faulted task inside task raises the inner exception" (box "InvalidOperationException") (fun () ->
    let failed = Task.FromException<int>(InvalidOperationException "bad")
    let t = task { let! v = failed in return v }
    box (errorName (fun () -> t.GetAwaiter().GetResult() |> ignore)))

check ".Result wraps the exception in AggregateException" (box "AggregateException") (fun () ->
    let t = task { return raise (InvalidOperationException "bad") : int }
    box (errorName (fun () -> t.Result |> ignore)))

check "Async.StartChild leaves a sibling running after a failure" (box "sibling finished") (fun () ->
    let mutable state = "sibling running"
    let sibling = async {
        do! Async.Sleep 300
        state <- "sibling finished" }
    let failing = async {
        do! Async.Sleep 30
        return raise (InvalidOperationException "bad") }
    let parent = async {
        let! s = Async.StartChild sibling
        let! f = Async.StartChild failing
        do! f
        do! s }
    (try Async.RunSynchronously parent with _ -> ())
    Thread.Sleep 500
    box state)

check "Async.Parallel cancels siblings after a failure" (box "sibling cancelled") (fun () ->
    let mutable state = "sibling running"
    let sibling = async {
        use! _ = Async.OnCancel(fun () -> state <- "sibling cancelled")
        do! Async.Sleep 300
        state <- "sibling finished" }
    let failing = async {
        do! Async.Sleep 30
        return raise (InvalidOperationException "bad") }
    (try Async.Parallel [ sibling; failing ] |> Async.RunSynchronously |> ignore with _ -> ())
    Thread.Sleep 500
    box state)

check "a pooled ValueTask awaited twice throws" (box "InvalidOperationException") (fun () ->
    let source = PooledSource()
    let vt = source.Next 5
    let first = task { let! v = vt in return v }
    first.Wait()
    box (errorName (fun () -> (task { let! v = vt in return v }).GetAwaiter().GetResult() |> ignore)))

check "a ValueTask converted once with AsTask can be awaited many times" (box 10) (fun () ->
    let source = PooledSource()
    let t = (source.Next 5).AsTask()
    box (task {
        let! a = t
        let! b = t
        return a + b }).Result)

check "IcedTasks valueTask returns a ValueTask" (box "ValueTask`1") (fun () ->
    let vt : ValueTask<int> = valueTask { return 1 }
    box (vt.GetType().Name))

if failures > 0 then exit 1
