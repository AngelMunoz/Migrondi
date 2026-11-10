// Checks each parameter rule of the skill: tupled default, curried by design,
// contextual object past three data parameters, InlineIfLambda extras on inline functions.

// Rule 1: tupled by default. A call without qty does not compile.
let tryReserve (sku: string, qty: int) = qty > 0 && sku.Length > 0

// Rule 2: curried by design. `pad 5` is the point of the function.
let pad (width: int) (text: string) = text.PadLeft width
let padded = [ "a"; "bb" ] |> List.map (pad 5)

// Rule 4: past three data parameters, one contextual object holds all of them.
type ChargeArgs = { OrderId: int; Amount: decimal; Currency: string; Capture: bool }

let charge (args: ChargeArgs) =
    $"charge {args.Amount} {args.Currency} to order {args.OrderId} capture={args.Capture}"

// Rule 5: inline functions take InlineIfLambda callbacks after the contextual object.
type RetryArgs = { Attempts: int; DelayMs: int }

let inline retry (args: RetryArgs, [<InlineIfLambda>] action: unit -> 't, [<InlineIfLambda>] onRetry: int -> unit) : 't =
    let rec go attempt =
        try
            action ()
        with _ when attempt < args.Attempts ->
            onRetry attempt
            go (attempt + 1)
    go 1

// Rule 5: non-inline functions carry callbacks as fields of the contextual object.
type WatchArgs = { Ticks: int; OnTick: int -> unit }

let watch (args: WatchArgs) =
    for i in 1 .. args.Ticks do args.OnTick i

// Run every form.
if not (tryReserve ("sku-1", 2)) then failwith "tryReserve"
if padded <> [ "    a"; "   bb" ] then failwith "pad"

let charged = charge { OrderId = 7; Amount = 9.5m; Currency = "EUR"; Capture = true }

let calls = ref 0
let retries = ref 0
let action () =
    calls.Value <- calls.Value + 1
    if calls.Value < 3 then failwith "not yet"
    "ok"
let onRetry (_: int) = retries.Value <- retries.Value + 1
let result = retry ({ Attempts = 3; DelayMs = 0 }, action, onRetry)

let ticks = ResizeArray ()
let watched = watch { Ticks = 2; OnTick = fun i -> ticks.Add i }

if result <> "ok" then failwith $"retry result {result}"
if calls.Value <> 3 || retries.Value <> 2 then failwith $"retry calls {calls.Value} retries {retries.Value}"
if ticks.Count <> 2 then failwith $"ticks {ticks.Count}"
printfn $"{charged}, retries={retries.Value}, ticks={ticks.Count}"
