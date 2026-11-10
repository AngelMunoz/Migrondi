open System
open System.Collections.Generic
open System.Diagnostics
open System.Globalization
open System.Runtime.CompilerServices
open System.Text

let mutable failures = 0

let check name ok (detail: string) =
    if not ok then failures <- failures + 1
    printfn "%s %s [%s]" (if ok then "PASS" else "FAIL") name detail

let allocated (f: unit -> 'a) =
    for _ in 1..3 do
        f () |> ignore

    let before = GC.GetAllocatedBytesForCurrentThread()
    f () |> ignore
    GC.GetAllocatedBytesForCurrentThread() - before

let elapsedTicks (f: unit -> 'a) =
    f () |> ignore
    let sw = Stopwatch.StartNew()
    f () |> ignore
    sw.ElapsedTicks

let n = 10_000
let keys = Array.init 100 (fun i -> "key" + string i)

let mutable bodyRuns = 0

let source =
    seq {
        bodyRuns <- bodyRuns + 1
        yield! [ 1..100 ]
    }

let _ = Seq.length source + Seq.sum source + Seq.max source
let runsLazy = bodyRuns
bodyRuns <- 0
let once = Array.ofSeq source
let _ = once.Length + Array.sum once + Array.max once
check "A seq runs its source again on each enumeration; Array.ofSeq runs it once" (runsLazy = 3 && bodyRuns = 1) $"lazy {runsLazy}, materialized {bodyRuns}"

let appendInLoop count =
    let mutable acc = []

    for i in 1..count do
        acc <- acc @ [ i ]

    acc

let consThenRev count =
    let mutable acc = []

    for i in 1..count do
        acc <- i :: acc

    List.rev acc

let appendBytes = allocated (fun () -> appendInLoop 1000)
let consBytes = allocated (fun () -> consThenRev 1000)
check "@ in a loop allocates quadratically; cons then List.rev allocates linearly" (appendBytes > 100L * consBytes) $"@ {appendBytes} bytes, cons {consBytes} bytes"

let numbers = List.init 20_000 id

let indexTicks =
    elapsedTicks (fun () ->
        let mutable s = 0

        for i in 0 .. numbers.Length - 1 do
            s <- s + List.item i numbers

        s)

let foldTicks = elapsedTicks (fun () -> List.fold (+) 0 numbers)
check "List.item in a loop is quadratic; List.fold is linear" (indexTicks > 20L * foldTicks) $"List.item {indexTicks} ticks, fold {foldTicks} ticks"

let map = keys |> Array.map (fun k -> k, 1) |> Map.ofArray
let dict = Dictionary<string, int>(StringComparer.Ordinal)

for k in keys do
    dict[k] <- 1

let mapFind =
    allocated (fun () ->
        let mutable s = 0

        for i in 0 .. n - 1 do
            match Map.tryFind keys[i % 100] map with
            | Some v -> s <- s + v
            | None -> ()

        s)

let dictFind =
    allocated (fun () ->
        let mutable s = 0

        for i in 0 .. n - 1 do
            match dict.TryGetValue keys[i % 100] with
            | true, v -> s <- s + v
            | _ -> ()

        s)

check "Map.tryFind allocates a Some on each hit; match on TryGetValue allocates nothing" (mapFind >= 24L * int64 n && dictFind = 0L) $"Map {mapFind} bytes, Dictionary {dictFind} bytes for {n} lookups"

let mapAdd =
    allocated (fun () ->
        let mutable m = map

        for i in 0 .. n - 1 do
            m <- Map.add keys[i % 100] i m

        m.Count)

let dictSet =
    allocated (fun () ->
        for i in 0 .. n - 1 do
            dict[keys[i % 100]] <- i

        dict.Count)

check "Map.add allocates a new tree path on each update; a Dictionary update allocates nothing" (mapAdd > 100L * int64 n && dictSet = 0L) $"Map {mapAdd} bytes, Dictionary {dictSet} bytes for {n} updates"

let names = [ "b"; "B"; "a"; "A"; "a-b"; "ab" ]
let fsharpSorted = List.sort names
let arraySortCopy = List.toArray names
Array.Sort arraySortCopy
let resizeSorted = ResizeArray names
resizeSorted.Sort()
let linqSorted = List.ofSeq (Linq.Enumerable.OrderBy(names, fun x -> x))
let sortedBy = List.sortBy id names
let sortedInPlaceBy = List.toArray names
Array.sortInPlaceBy id sortedInPlaceBy
let sortedDict = SortedDictionary<string, int>()

for k in names do
    sortedDict[k] <- 1

check
    "List.sort, List.sortBy, Array.sort, Array.sortInPlaceBy, Map, and Set order strings by ordinal; Array.Sort, List<T>.Sort, SortedDictionary, and LINQ OrderBy use the current culture"
    (fsharpSorted = List.sortWith (fun a b -> String.CompareOrdinal(a, b)) names
     && Array.sort (List.toArray names) = List.toArray fsharpSorted
     && sortedBy = fsharpSorted
     && List.ofArray sortedInPlaceBy = fsharpSorted
     && linqSorted <> fsharpSorted
     && List.ofSeq (Set.ofList names) = fsharpSorted
     && List.ofSeq (Map.keys (Map.ofList [ for k in names -> k, 1 ])) = fsharpSorted
     && List.ofArray arraySortCopy <> fsharpSorted
     && List.ofSeq resizeSorted <> fsharpSorted
     && List.ofSeq sortedDict.Keys <> fsharpSorted)
    $"F# {fsharpSorted}, Array.Sort {List.ofArray arraySortCopy}"

let composed = "\u00C5"
let decomposed = "A\u030A"
let stringKeyed = Dictionary<string, int>()
stringKeyed[composed] <- 1
stringKeyed[decomposed] <- 2
stringKeyed["a"] <- 3
stringKeyed["A"] <- 4

check
    "Dictionary<string, _> without a comparer uses ordinal equality, the same as F# ="
    (stringKeyed.Count = 4 && composed <> decomposed && String.Equals(composed, decomposed, StringComparison.InvariantCulture))
    $"{stringKeyed.Count} keys"

let sumStart = List.sum [ -0.0 ]
let averageStart = List.average [ -0.0 ]
let firstStart = -0.0
let zeroStart = 0.0 + firstStart

check
    "List.sum and List.average of one -0.0 give +0.0; a sum that starts at the first value keeps -0.0, and F3 prints -0.000"
    (not (Double.IsNegative sumStart)
     && not (Double.IsNegative averageStart)
     && Double.IsNegative firstStart
     && not (Double.IsNegative zeroStart)
     && firstStart.ToString("F3", CultureInfo.InvariantCulture) = "-0.000"
     && averageStart.ToString("F3", CultureInfo.InvariantCulture) = "0.000")
    $"average {averageStart}, first {firstStart}, 0.0 + first {zeroStart}"

let values = Array.init n float

let filterLength = allocated (fun () -> values |> Array.filter (fun v -> v > 10.0) |> Array.length)
let sumByCount = allocated (fun () -> values |> Array.sumBy (fun v -> if v > 10.0 then 1 else 0))
check "Array.filter |> Array.length allocates the filtered array; inline Array.sumBy allocates nothing" (filterLength > 8L * int64 (n - 20) && sumByCount = 0L) $"filter {filterLength} bytes, sumBy {sumByCount} bytes"

let series = List.init n float
let pairwise = allocated (fun () -> series |> List.pairwise |> List.filter (fun (a, b) -> b - a > 0.5) |> List.length)

let previousLoop =
    allocated (fun () ->
        match series with
        | [] -> 0
        | first :: rest ->
            let mutable previous = first
            let mutable count = 0

            for v in rest do
                if v - previous > 0.5 then count <- count + 1
                previous <- v

            count)

check "List.pairwise allocates a tuple and a list cell for each pair; a loop with the previous value allocates nothing" (pairwise > 50L * int64 n && previousLoop = 0L) $"pairwise {pairwise} bytes, loop {previousLoop} bytes"

let windowed = allocated (fun () -> values |> Seq.windowed 2 |> Seq.filter (fun w -> w[1] - w[0] > 0.5) |> Seq.length)
check "Seq.windowed allocates an array for each window" (windowed > 32L * int64 n) $"windowed {windowed} bytes"

let listToArray = allocated (fun () -> List.toArray series)
let listLoop = allocated (fun () -> List.fold (fun acc v -> acc + v) 0.0 series)
check "List.toArray copies the list; for and List.fold over a list allocate nothing" (listToArray >= 8L * int64 n && listLoop = 0L) $"toArray {listToArray} bytes, fold {listLoop} bytes"

let tryEven (x: int) = if x % 2 = 0 then Some x else None
let tryEvenV (x: int) = if x % 2 = 0 then ValueSome x else ValueNone

let optionBytes =
    allocated (fun () ->
        let mutable s = 0

        for i in 0 .. n - 1 do
            match tryEven i with
            | Some v -> s <- s + v
            | None -> ()

        s)

let voptionBytes =
    allocated (fun () ->
        let mutable s = 0

        for i in 0 .. n - 1 do
            match tryEvenV i with
            | ValueSome v -> s <- s + v
            | ValueNone -> ()

        s)

check "An option result allocates each Some; a voption result allocates nothing" (optionBytes = 24L * int64 (n / 2) && voptionBytes = 0L) $"option {optionBytes} bytes, voption {voptionBytes} bytes"

let someIntBytes = allocated (fun () -> Some 1)
let someDecimalBytes = allocated (fun () -> Some 1M)
check "Some 1 allocates 24 bytes and Some 1M allocates 32 bytes on 64-bit .NET" (someIntBytes = 24L && someDecimalBytes = 32L) $"Some 1 {someIntBytes} bytes, Some 1M {someDecimalBytes} bytes"

let (|Even|_|) (x: int) = if x % 2 = 0 then Some(x / 2) else None

[<return: Struct>]
let (|EvenV|_|) (x: int) = if x % 2 = 0 then ValueSome(x / 2) else ValueNone

let patternBytes =
    allocated (fun () ->
        let mutable s = 0

        for i in 0 .. n - 1 do
            match i with
            | Even h -> s <- s + h
            | _ -> ()

        s)

let structPatternBytes =
    allocated (fun () ->
        let mutable s = 0

        for i in 0 .. n - 1 do
            match i with
            | EvenV h -> s <- s + h
            | _ -> ()

        s)

check "A partial active pattern allocates each match; [<return: Struct>] allocates nothing" (patternBytes > 0L && structPatternBytes = 0L) $"option pattern {patternBytes} bytes, struct pattern {structPatternBytes} bytes"

[<MethodImpl(MethodImplOptions.NoInlining)>]
let bounds (a: int) (b: int) = (min a b, max a b)

[<MethodImpl(MethodImplOptions.NoInlining)>]
let boundsV (a: int) (b: int) = struct (min a b, max a b)

let tupleBytes =
    allocated (fun () ->
        let mutable s = 0

        for i in 0 .. n - 1 do
            let lo, hi = bounds i 5
            s <- s + lo + hi

        s)

let structTupleBytes =
    allocated (fun () ->
        let mutable s = 0

        for i in 0 .. n - 1 do
            let struct (lo, hi) = boundsV i 5
            s <- s + lo + hi

        s)

check "A tuple returned from a function that is not inlined allocates; a struct tuple allocates nothing" (tupleBytes = 24L * int64 n && structTupleBytes = 0L) $"tuple {tupleBytes} bytes, struct tuple {structTupleBytes} bytes"

let tupleKeyed = Dictionary<int * int, int>()
let structKeyed = Dictionary<struct (int * int), int>()

for i in 0..99 do
    tupleKeyed[(i, i)] <- i
    structKeyed[struct (i, i)] <- i

let tupleKeyBytes =
    allocated (fun () ->
        let mutable s = 0

        for i in 0 .. n - 1 do
            s <- s + tupleKeyed[(i % 100, i % 100)]

        s)

let structKeyBytes =
    allocated (fun () ->
        let mutable s = 0

        for i in 0 .. n - 1 do
            s <- s + structKeyed[struct (i % 100, i % 100)]

        s)

check "A Dictionary with a tuple key allocates on each lookup; a struct tuple key allocates nothing" (tupleKeyBytes > 32L * int64 n && structKeyBytes = 0L) $"tuple key {tupleKeyBytes} bytes, struct tuple key {structKeyBytes} bytes"

type Line = { Region: string; Code: string }

let lines =
    List.init 500 (fun i -> { Region = string (i % 7); Code = string (293 * i % 500) })

let sortByString = allocated (fun () -> lines |> List.sortBy _.Region)
let sortByStructTuple = allocated (fun () -> lines |> List.sortBy (fun l -> struct (l.Region, l.Code)))

let sortWithOrdinal =
    allocated (fun () ->
        lines
        |> List.sortWith (fun x y ->
            match String.CompareOrdinal(x.Region, y.Region) with
            | 0 -> String.CompareOrdinal(x.Code, y.Code)
            | c -> c))

check
    "List.sortBy with a struct tuple key boxes on each comparison; List.sortWith with String.CompareOrdinal does not"
    (sortByStructTuple > 5L * sortByString && sortWithOrdinal < 2L * sortByString)
    $"string key {sortByString} bytes, struct tuple key {sortByStructTuple} bytes, sortWith {sortWithOrdinal} bytes"

[<Struct>]
type Point = { X: int; Y: int }

[<Struct>]
type SharedField =
    | Small of size: float
    | Large of size: float

[<Struct>]
type SeparateFields =
    | Wide of width: float
    | Tall of height: float

check
    "A struct union stores the fields of all cases; cases with the same field name and type share it"
    (sizeof<SharedField> < sizeof<SeparateFields>)
    $"shared {sizeof<SharedField>} bytes, separate {sizeof<SeparateFields>} bytes"

let structList = allocated (fun () -> [| for i in 0 .. n - 1 -> { X = i; Y = i } |])
let boxedList = allocated (fun () -> [| for i in 0 .. n - 1 -> { X = i; Y = i } :> IComparable |])
check "A struct record in an array is stored inline; the same value through an interface is boxed" (boxedList >= structList + 24L * int64 n) $"struct array {structList} bytes, interface array {boxedList} bytes"

let inv = CultureInfo.InvariantCulture
let sprintfBytes = allocated (fun () -> sprintf "order %s qty=%d price=%.2f" "A-17" 5 19.99)
let concatBytes = allocated (fun () -> String.Concat("order ", "A-17", " qty=", (5).ToString(inv), " price=", (19.99).ToString("F2", inv)))
check "sprintf allocates more than 2 x String.Concat for the same text" (sprintfBytes > 2L * concatBytes) $"sprintf {sprintfBytes} bytes, Concat {concatBytes} bytes"

let builder = StringBuilder(64)
let appendIntBytes = allocated (fun () -> builder.Clear().Append(12345).Append('x') |> ignore)
check "StringBuilder.Append of an int or a char allocates nothing" (appendIntBytes = 0L) $"{appendIntBytes} bytes"

let parts = Array.init 1000 (fun i -> "line " + string i)
let plusBytes = allocated (fun () -> parts |> Array.fold (fun acc p -> acc + p + "\n") "")

let builderFoldBytes =
    allocated (fun () ->
        let sb = StringBuilder()

        for p in parts do
            sb.Append(p).Append('\n') |> ignore

        sb.ToString())

check "String + in a fold allocates quadratically; one StringBuilder allocates linearly" (plusBytes > 20L * builderFoldBytes) $"+ {plusBytes} bytes, StringBuilder {builderFoldBytes} bytes"

[<MethodImpl(MethodImplOptions.NoInlining)>]
let each (f: float -> unit) (xs: float list) =
    for x in xs do
        f x

let captureBytes =
    allocated (fun () ->
        let mutable total = 0.0
        series |> each (fun x -> total <- total + x)
        total)

let loopBytes =
    allocated (fun () ->
        let mutable total = 0.0

        for x in series do
            total <- total + x

        total)

check "A lambda that captures a let mutable local allocates a closure and a ref cell; a for loop allocates nothing" (captureBytes >= 48L && loopBytes = 0L) $"lambda {captureBytes} bytes, loop {loopBytes} bytes"

let arrayMap = Map.ofList [ [| 1; 2 |], "a"; [| 1; 2 |], "b" ]
let arrayDict = Dictionary<int[], string>()
arrayDict[[| 1; 2 |]] <- "a"
arrayDict[[| 1; 2 |]] <- "b"
let structuralDict = Dictionary<int[], string>(HashIdentity.Structural)
structuralDict[[| 1; 2 |]] <- "a"
structuralDict[[| 1; 2 |]] <- "b"
check "Two equal arrays are one Map key and two Dictionary keys; HashIdentity.Structural makes them one" (arrayMap.Count = 1 && arrayDict.Count = 2 && structuralDict.Count = 1) $"Map {arrayMap.Count}, Dictionary {arrayDict.Count}, structural {structuralDict.Count}"

let keyValueBytes =
    allocated (fun () ->
        let mutable s = 0

        for KeyValue(_, v) in dict do
            s <- s + v

        s)

check "for KeyValue(k, v) in a Dictionary allocates nothing" (keyValueBytes = 0L) $"{keyValueBytes} bytes"

let samePoint (a: Point) (b: Point) = a = b

let structEqualBytes =
    allocated (fun () ->
        let mutable s = 0

        for i in 0 .. n - 1 do
            if samePoint { X = i; Y = 0 } { X = 0; Y = 0 } then s <- s + 1

        s)

check "F# = on a struct record allocates nothing" (structEqualBytes = 0L) $"{structEqualBytes} bytes"

let structCompareBytes =
    allocated (fun () ->
        let mutable s = 0

        for i in 0 .. n - 1 do
            s <- s + compare { X = i; Y = 0 } { X = 0; Y = 1 }

        s)

check "F# compare on a struct record allocates nothing" (structCompareBytes = 0L) $"{structCompareBytes} bytes"

let pointKeyed = Dictionary<Point, int>()

for i in 0..99 do
    pointKeyed[{ X = i; Y = i }] <- i

let pointKeyBytes =
    allocated (fun () ->
        let mutable s = 0

        for i in 0 .. n - 1 do
            s <- s + pointKeyed[{ X = i % 100; Y = i % 100 }]

        s)

check "A Dictionary with a [<Struct>] record key allocates nothing on lookup" (pointKeyBytes = 0L) $"{pointKeyBytes} bytes"

let buildAndMatchBytes =
    allocated (fun () ->
        let mutable s = 0

        for i in 0 .. n - 1 do
            let lo, hi = (i, i + 1)
            s <- s + lo + hi

        s)

check "A tuple built and matched in the same expression allocates nothing" (buildAndMatchBytes = 0L) $"{buildAndMatchBytes} bytes"

let saved = CultureInfo.CurrentCulture
CultureInfo.CurrentCulture <- CultureInfo "de-DE"
let printfText = sprintf "%.3f" 1.5
let typedInterpolated = $"%.3f{1.5}"
let toStringText = (1.5).ToString("F3")
let dotnetInterpolated = $"{1.5:F3}"
let invariantText = (1.5).ToString("F3", inv)
let stringText = string 1.5
CultureInfo.CurrentCulture <- saved

check
    "sprintf, %.3f holes, and string use the invariant culture; ToString and {x:F3} holes use the current culture"
    (printfText = "1.500" && typedInterpolated = "1.500" && invariantText = "1.500" && stringText = "1.5" && toStringText = "1,500" && dotnetInterpolated = "1,500")
    $"string {stringText}, sprintf {printfText}, typed hole {typedInterpolated}, ToString {toStringText}, .NET hole {dotnetInterpolated}"

if failures > 0 then
    printfn "%d checks failed" failures
    exit 1
