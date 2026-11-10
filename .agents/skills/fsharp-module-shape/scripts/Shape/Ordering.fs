namespace Shop.Ordering

type OrderLine = { Sku: string; Qty: int; Price: decimal }

type IPricing =
    abstract Rate: sku: string -> decimal

type IProvidePricing =
    abstract Pricing: IPricing

type IOrdering =
    abstract Total: lines: OrderLine list -> decimal
    abstract AddLine: lines: OrderLine list * line: OrderLine -> OrderLine list

module private Helpers =

    let money (value: decimal) = System.Math.Round (value, 2)

module Ordering =

    let total (env: #IProvidePricing, lines: OrderLine list) =
        lines
        |> List.sumBy (fun line -> Helpers.money (decimal line.Qty * line.Price * env.Pricing.Rate line.Sku))

    let addLine (env, lines: OrderLine list, line: OrderLine) =
        line :: lines

    let create env =
        { new IOrdering with
            member _.Total (lines) = total (env, lines)
            member _.AddLine (lines, line) = addLine (env, lines, line) }
