module Domain

type Shape =
    | Circle of radius: float
    | Rect of w: float * h: float

type Code =
    | Ok200
    | Err of int
    override this.ToString() =
        match this with
        | Ok200 -> "Ok200"
        | Err n -> $"Err {n}"

type Settings = { Url: string; Retries: int; Proxy: string option }

type Envelope = { Id: int; Shape: Shape }

type CodeEnvelope = { Id: int; Code: Code }

type Person = { Name: string; Age: int option; Tags: string list; Shape: Shape }
