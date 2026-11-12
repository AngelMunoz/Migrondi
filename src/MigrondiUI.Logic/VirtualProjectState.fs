namespace MigrondiUI.Logic

module VirtualProjectState =

  open System
  open FSharp.Data.Adaptive
  open Migrondi.Core
  open MigrondiUI

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
