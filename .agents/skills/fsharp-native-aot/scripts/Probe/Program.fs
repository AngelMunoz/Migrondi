module Program

open System
open System.Text.Json
open Microsoft.Extensions.Logging
open Domain

type CaptureLogger() =
    member val Last = "" with get, set
    interface ILogger with
        member _.BeginScope(_) = null
        member _.IsEnabled(_) = true
        member this.Log(_, _, state, ex, formatter) = this.Last <- formatter.Invoke(state, ex)

type TupleEnvelope = { Id: int; Pair: int * int }

type MapEnvelope = { Id: int; Items: Map<int, string> }

let id = 42
let avg = 1.5
let flag = true
let settings = { Url = "u"; Retries = 3; Proxy = None }
let shape = Rect(2.0, 3.0)
let pair = (1, 2)
let person = { Name = "Ada"; Age = Some 36; Tags = [ "a"; "b" ]; Shape = shape }

let logged (write: ILogger -> unit) =
    let l = CaptureLogger()
    write l
    l.Last

let probes: (string * (unit -> string)) list =
    [ "classic-s", (fun () -> sprintf "%s!" "x")
      "classic-d", (fun () -> sprintf "Order %d" id)
      "classic-f", (fun () -> sprintf "Avg: %.2f" avg)
      "classic-b", (fun () -> sprintf "%b" flag)
      "failwithf-classic", (fun () -> try failwithf "bad id %d" id with e -> e.GetType().Name + ": " + e.Message)
      "interp-d", (fun () -> $"Order %d{id}")
      "interp-f", (fun () -> $"Avg: %.2f{avg}")
      "interp-flags", (fun () -> $"[%5d{id}] [%-4b{flag}] %x{255} {avg:N1}")
      "sprintf-interp", (fun () -> sprintf $"Order %d{id}")
      "failwithf-interp", (fun () -> try failwithf $"bad id %d{id}" with e -> e.GetType().Name + ": " + e.Message)
      "A-record-plain", (fun () -> sprintf "%A" settings)
      "A-some-int", (fun () -> sprintf "%A" (Some 5))
      "A-list-int", (fun () -> sprintf "%A" [ 1; 2 ])
      "A-int", (fun () -> sprintf "%A" 5)
      "string-union", (fun () -> string shape)
      "A-union", (fun () -> sprintf "%A" shape)
      "A-tuple", (fun () -> sprintf "%A" pair)
      "interp-A-tuple", (fun () -> $"%A{pair}")
      "A-map", (fun () -> sprintf "%A" (Map.ofList [ 1, "a" ]))
      "string-record-with-union", (fun () -> string { Envelope.Id = 1; Shape = shape })
      "string-record-with-tuple", (fun () -> string { TupleEnvelope.Id = 1; Pair = pair })
      "string-record-with-map", (fun () -> string { MapEnvelope.Id = 1; Items = Map.ofList [ 1, "a" ] })
      "string-union-override", (fun () -> string (Err 7))
      "A-union-override", (fun () -> sprintf "%A" (Err 7))
      "string-record-with-override-union", (fun () -> string { CodeEnvelope.Id = 1; Code = Err 7 })
      "log-union-arg", (fun () -> logged (fun l -> l.LogInformation("Shape {Shape}", shape)))
      "log-interp-union", (fun () -> logged (fun l -> l.LogInformation($"Shape {shape}")))
      "log-plain-args", (fun () -> logged (fun l -> l.LogInformation("Order {Id} avg {Avg}", id, avg)))
      "json-reflection", (fun () -> JsonSerializer.Serialize(settings))
      "json-context-plain-option",
      (fun () -> JsonSerializer.Serialize({ settings with Proxy = Some "p" }, DomainJson.PlainContext.Default.Settings))
      "json-context-fsharp-converters",
      (fun () ->
          let ti = DomainJson.FSharpContext.Default.Person
          let json = JsonSerializer.Serialize(person, ti)
          json + " roundtrip=" + string (JsonSerializer.Deserialize(json, ti) = person))
      "quotation-eval",
      (fun () -> string (Linq.RuntimeHelpers.LeafExpressionConverter.EvaluateQuotation <@ 1 + 2 @>)) ]

[<EntryPoint>]
let main _ =
    for (name, f) in probes do
        let result =
            try
                "OK " + (f ()).Replace("\r", "").Replace("\n", "\\n")
            with e ->
                "EXN " + e.GetType().Name
        Console.WriteLine(name + " | " + result)
    0
