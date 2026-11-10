module Shop.Program

open Shop.Ordering

type FakePricing () =
    interface IPricing with
        member _.Rate _ = 1.0m

type FakeEnv () =
    interface IProvidePricing with
        member _.Pricing = FakePricing () :> IPricing

[<EntryPoint>]
let main _ =
    let env = FakeEnv ()
    let line = { Sku = "sku-1"; Qty = 2; Price = 3m }
    let lines = [ line ]

    // Direct: the implementation functions run with no factory and no interface.
    let directTotal = Ordering.total (env, lines)
    let directList = Ordering.addLine (env, [], line)

    // Through the factory: the object expression delegates to the same functions.
    let service = Ordering.create env
    let serviceTotal = service.Total lines
    let serviceList = service.AddLine ([], line)

    if directTotal <> 6.00m then failwith $"direct total {directTotal}"
    if serviceTotal <> directTotal then failwith $"service total {serviceTotal}"
    if directList <> serviceList then failwith "addLine lists differ"
    printfn $"total {directTotal} via the function, {serviceTotal} via the service"
    0
