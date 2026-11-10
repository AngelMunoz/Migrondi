module Program

open System

type Settings = { Url: string; Retries: int }

[<EntryPoint>]
let main argv =
    let s = { Url = "u"; Retries = 3 }
#if USER_WARNING
    Console.WriteLine(Text.Json.JsonSerializer.Serialize(s))
#else
    Console.WriteLine($"%s{s.Url} %d{s.Retries}")
#endif
    0
