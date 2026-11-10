# Measure F# performance

Measure before and after each change. A change that does not move the numbers is not worth its cost in readability.

## Allocated bytes for one call

`GC.GetAllocatedBytesForCurrentThread()` counts the bytes that the current thread allocated. The count is exact, so one warm call is enough.

```fsharp
open System
open System.Diagnostics

let allocatedBy (f: unit -> 'a) =
    for _ in 1..3 do f () |> ignore
    let before = GC.GetAllocatedBytesForCurrentThread()
    f () |> ignore
    GC.GetAllocatedBytesForCurrentThread() - before

let medianMs (runs: int) (f: unit -> 'a) =
    for _ in 1..3 do f () |> ignore
    let times =
        Array.init runs (fun _ ->
            GC.Collect()
            let sw = Stopwatch.StartNew()
            f () |> ignore
            sw.Elapsed.TotalMilliseconds)
    Array.sortInPlace times
    times[runs / 2]

let input = makeProductionSizeInput ()
printfn "bytes %d" (allocatedBy (fun () -> Orders.summarize input))
printfn "median ms %.2f" (medianMs 15 (fun () -> Orders.summarize input))
```

Rules for the harness:

- Build the input before the measured call. Otherwise the count includes the input.
- Call the function a few times first. The first calls include JIT work and static initialization.
- Count only work on the current thread. A function that starts tasks allocates on other threads. Use `GC.GetTotalAllocatedBytes(true)` for that case, with no other work in the process.
- Use a production-size input. A quadratic cost hides at 100 elements and dominates at 100,000.
- Run a compiled project in Release (`dotnet run -c Release`), or run a script with `dotnet fsi --optimize+`. A Debug build keeps tuples and closures that the optimizer removes.
- Take the median of several timed runs. One timed run includes noise from other processes.

## BenchmarkDotNet in F#

Use BenchmarkDotNet when you compare small differences in time, or when you need a report for review.

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="Benchmarks.fs" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="BenchmarkDotNet" Version="0.15.*" />
    <ProjectReference Include="../Orders/Orders.fsproj" />
  </ItemGroup>
</Project>
```

```fsharp
module Benchmarks

open BenchmarkDotNet.Attributes
open BenchmarkDotNet.Running

[<MemoryDiagnoser>]
type SummarizeBenchmarks() =
    let mutable input = []

    [<Params(1_000, 100_000)>]
    member val Size = 0 with get, set

    [<GlobalSetup>]
    member this.Setup() = input <- makeInput this.Size

    [<Benchmark(Baseline = true)>]
    member _.Before() = OrdersBefore.summarize input

    [<Benchmark>]
    member _.After() = Orders.summarize input

[<EntryPoint>]
let main argv =
    BenchmarkSwitcher.FromAssembly(typeof<SummarizeBenchmarks>.Assembly).Run(argv) |> ignore
    0
```

Run it in Release:

```powershell
dotnet run -c Release -- --filter '*'
```

F# rules for BenchmarkDotNet:

- A benchmark class must be public and not sealed. A `type` in a module is public by default. Do not mark it `private` or `internal`.
- `[<Params>]` needs a settable property: `member val Size = 0 with get, set`. A `let` field cannot carry it.
- Return the result from the benchmark method. A method that returns `unit` lets the JIT remove work whose result nobody uses.
- `[<MemoryDiagnoser>]` adds the `Allocated` column. Compare that column first: it does not change between runs.
- Keep the old code in a separate module, such as `OrdersBefore`, so that one run compares both.

## Report

Report the input size, the bytes and the median time before and after, and the command that you ran.

```text
summarize, 200,000 orders: 31.7 MB -> 0.25 MB, 19.4 ms -> 4.2 ms (Release, median of 15)
```
