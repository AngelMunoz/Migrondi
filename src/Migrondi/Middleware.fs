namespace Migrondi.Middleware

open System
open System.Collections.Generic
open System.Threading.Tasks

open System.CommandLine
open System.CommandLine.Invocation

open Microsoft.Extensions.Logging

open FsToolkit.ErrorHandling

[<Struct>]
type MiddlewareResult =
  | Continue
  | Exit of ExitCode: int

[<Struct>]
type MigrondiMiddleware =
  | AsMiddleware of
    Middleware:
      (Command * KeyValuePair<string, string seq> seq -> MiddlewareResult)

module Helpers =
  let ShouldRunFor (candidate: string) (commands: string list) =
    if List.contains candidate commands then
      Ok()
    else
      Error Continue

  let HasDirective
    (directive: string)
    (directives: KeyValuePair<string, string seq> seq)
    : bool =
    let comparison (current: KeyValuePair<string, string seq>) =
      current.Key.Equals(directive, StringComparison.InvariantCultureIgnoreCase)

    Seq.exists comparison directives

  let ToSCLMiddleware (middleware: MigrondiMiddleware) : InvocationMiddleware =
    let inline mdl
      (context: InvocationContext)
      (next: Func<InvocationContext, Task>)
      =
      task {
        let command = context.ParseResult.CommandResult.Command
        let directives = context.ParseResult.Directives

        let result =
          match middleware with
          | AsMiddleware middleware -> middleware(command, directives)

        match result with
        | Continue -> return! next.Invoke context
        | Exit code ->
          context.ExitCode <- code
          return ()
      }
      :> Task

    InvocationMiddleware(mdl)

module internal Database =
  open Migrondi.Env

  let setup
    (appEnv: AppEnv)
    (command: Command, _: KeyValuePair<string, string seq> seq)
    =
    let db = appEnv.Database

    result {
      do! Helpers.ShouldRunFor command.Name [ "status"; "up"; "down"; "list" ]

      try
        db.SetupDatabase()
        return ()
      with :? Migrondi.Core.SetupDatabaseFailed as ex ->
        appEnv.Logger.LogError("Database was not setup", ex)
        return! Error(Exit 1)
    }
    |> function
      | Ok() -> Continue
      | Error err -> err



module internal Middleware =

  let SetupDatabase appEnv =
    Database.setup appEnv
    |> MigrondiMiddleware.AsMiddleware
    |> Helpers.ToSCLMiddleware