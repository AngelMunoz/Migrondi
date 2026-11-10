open System
open System.Xml.Linq

let mutable failures = 0

let check name (expected: 'a) (actual: 'a) =
    if expected = actual then
        printfn "PASS %s" name
    else
        failures <- failures + 1
        printfn "FAIL %s: expected %A, got %A" name expected actual

type Line = { Sku: string; Quantity: int; Price: decimal; InStock: bool }

let lines =
    [ { Sku = "W-1"; Quantity = 2; Price = 150.00m; InStock = true }
      { Sku = "G-7"; Quantity = 1; Price = 30.50m; InStock = false }
      { Sku = "B-2"; Quantity = 3; Price = 9.99m; InStock = false } ]

// 1 and 4: tuple and mutable accumulators, then a record and expressions.
let totalsBefore (lines: Line list) (rate: decimal option) : decimal * decimal * int =
    let mutable subtotal = 0m
    let mutable count = 0
    for l in lines do
        subtotal <- subtotal + decimal l.Quantity * l.Price
        count <- count + l.Quantity
    let mutable discount = 0m
    if rate.IsSome then discount <- subtotal * rate.Value
    (subtotal, discount, count)

type Totals = { Subtotal: decimal; Discount: decimal; ItemCount: int }

let totalsAfter (lines: Line list) (rate: decimal option) =
    let subtotal = lines |> List.sumBy (fun l -> decimal l.Quantity * l.Price)
    { Subtotal = subtotal
      Discount = (match rate with Some r -> subtotal * r | None -> 0m)
      ItemCount = lines |> List.sumBy _.Quantity }

let s, d, c = totalsBefore lines (Some 0.10m)
let t = totalsAfter lines (Some 0.10m)
check "A record keeps the tuple values with names" (s, d, c) (t.Subtotal, t.Discount, t.ItemCount)

// 2 and 3: bool * value with if/elif, then an option with match.
let tryRateBefore (code: string) : bool * decimal =
    if code = "SAVE10" then (true, 0.10m)
    elif code = "SAVE25" then (true, 0.25m)
    else (false, 0m)

let tryRateAfter (code: string) =
    match code with
    | "SAVE10" -> Some 0.10m
    | "SAVE25" -> Some 0.25m
    | _ -> None

for code in [ "SAVE10"; "SAVE25"; "OTHER" ] do
    let ok, rate = tryRateBefore code
    check $"match with option gives the same rate for {code}" (if ok then Some rate else None) (tryRateAfter code)

let tryParse (s: string) =
    match Int32.TryParse s with
    | true, v -> Some v
    | _ -> None

check "A .NET TryParse call becomes an option" [ Some 42; None ] [ tryParse "42"; tryParse "x" ]

// 5: early exit with a mutable flag, then List.tryFind.
let firstBackorderedBefore (lines: Line list) =
    let mutable found = None
    for l in lines do
        if found.IsNone && not l.InStock then found <- Some l
    found

check "List.tryFind returns the first match, like the loop"
    (firstBackorderedBefore lines |> Option.map _.Sku)
    (lines |> List.tryFind (fun l -> not l.InStock) |> Option.map _.Sku)

// 6: position index on a list, then List.mapi.
let numberedBefore (lines: Line list) =
    [ for i in 0 .. lines.Length - 1 -> $"{i + 1}. {lines.[i].Sku}" ]

let numberedAfter (lines: Line list) = lines |> List.mapi (fun i l -> $"{i + 1}. {l.Sku}")
check "List.mapi numbers lines like the index loop" (numberedBefore lines) (numberedAfter lines)

// 7: two passes, then one; a seq runs its work on each enumeration.
check "List.sumBy equals List.map then List.sum"
    (lines |> List.map _.Price |> List.sum)
    (lines |> List.sumBy _.Price)

let mutable reads = 0
let source = seq { for l in lines do reads <- reads + 1; yield l }
let _ = Seq.length source
let _ = Seq.sumBy _.Price source
check "A seq enumerated twice runs its work twice" 6 reads
reads <- 0
let once = List.ofSeq source
let _ = List.length once
let _ = List.sumBy _.Price once
check "List.ofSeq runs the seq work once" 3 reads

// 8: let! then match, then match!.
let load id = async { return if id = 1 then Some "Ada" else None }

let nameBefore id =
    async {
        let! user = load id
        match user with
        | Some n -> return n
        | None -> return "unknown"
    }

let nameAfter id =
    async {
        match! load id with
        | Some n -> return n
        | None -> return "unknown"
    }

for id in [ 1; 2 ] do
    check $"match! gives the same result for id {id}" (Async.RunSynchronously(nameBefore id)) (Async.RunSynchronously(nameAfter id))

// 9: static member property, then static member val.
let mutable created = 0

type Settings() =
    static member Each = (created <- created + 1; obj ())
    static member val Once = (created <- created + 1; obj ())

created <- 0
let _ = Settings.Each
let _ = Settings.Each
check "static member X = expr runs expr on each access" 2 created
let before = created
let a = Settings.Once
let b = Settings.Once
check "static member val gives the same instance on each access" true (obj.ReferenceEquals(a, b))
check "static member val does not run expr again on access" before created

// 10: string concatenation, then a syntax API.
let sku = "A\"1<"
let concatenated = "<item sku=\"" + sku + "\" />"
let built = XElement(XName.Get "item", XAttribute(XName.Get "sku", sku)).ToString()
let parses (text: string) = try XElement.Parse text |> ignore; true with _ -> false
check "String concatenation makes invalid XML for a quote in the value" false (parses concatenated)
check "XElement escapes the value" (true, sku) (parses built, (XElement.Parse built).Attribute(XName.Get "sku").Value)

if failures > 0 then exit 1
