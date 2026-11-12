namespace MigrondiUI.Logic

open System
open FSharp.Data.Adaptive
open Migrondi.Core
open MigrondiUI

/// Update-style setters for adaptive values, same call shape the GUI code
/// has always used for `cval` state.
[<AutoOpen>]
module internal AdaptiveStateExtensions =

  type ChangeableValue<'T> with

    member value.setValue(update: 'T -> 'T) : unit =
      transact(fun () -> value.Value <- update value.Value)

    member value.setValue(newValue: 'T) : unit =
      transact(fun () -> value.Value <- newValue)

module VirtualProjectState =

  let setName (name: string | null) (project: VirtualProject cval) =
    Ok name
    |> Result.bind(fun name ->
      if String.IsNullOrWhiteSpace(name) then
        Error "Name string cannot be empty."
      else
        Ok(nonNull name))
    |> Result.toOption
    |> Option.iter(fun name ->
      project.setValue(fun p -> { p with name = name }))

  let setDescription
    (description: string | null)
    (project: VirtualProject cval)
    =
    let description =
      if String.IsNullOrWhiteSpace(description) then
        None
      else
        Some(nonNull description)

    project.setValue(fun p -> { p with description = description })

  let setConnection (connection: string | null) (project: VirtualProject cval) =
    Ok connection
    |> Result.bind(fun conn ->
      if String.IsNullOrWhiteSpace(conn) then
        Error "Connection string cannot be empty."
      else
        Ok(nonNull conn))
    |> Result.toOption
    |> Option.iter(fun connection ->
      project.setValue(fun p -> { p with connection = connection }))

  let setDriver (driver: string) (project: VirtualProject cval) =
    project.setValue(fun p -> {
      p with
          driver = MigrondiDriver.FromString driver
    })
