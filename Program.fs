open CommandLine
open FSharp.Data.Dapper
open Sqlator.Options


[<EntryPoint>]
let main argv =

  OptionHandler.RegisterTypes()
  let result = CommandLine.Parser.Default.ParseArguments<NewOptions, UpOptions, DownOptions, ListOptions>(argv)
  match result with
  | :? Parsed<obj> as command ->
    match command.Value with 
    | :? NewOptions as newOptions ->
            printfn "newOptions: %A" newOptions
    | :? UpOptions as upOptions ->
            printfn "upOptions: %A" upOptions
    | :? DownOptions as downOptions ->
            printfn "downOptions: %A" downOptions
    | :? ListOptions as listOptions ->
            printfn "listOptions: %A" listOptions
    | _ -> failwith "Unexpected parsing result"
  | :? NotParsed<obj> as notParsed ->
    printfn "Not Parsed: %A" (notParsed.Errors |> Seq.map(fun err -> err.Tag))
  | _ -> failwith "Unexpected parsing result"

  0