namespace Migrondi.Mcp

open Microsoft.Extensions.Logging

open MigrondiUI

type IMcpServer =
  abstract Run: unit -> Async<unit>

module private ServerHelpers =

  let createEnvironment
    (connectionFactory: unit -> System.Data.IDbConnection)
    (loggerFactory: ILoggerFactory)
    : McpEnvironment =
    Migrations.GetMigrondi loggerFactory
    |> ValueOption.defaultWith(fun () -> failwith "No migrondi found")
    |> Migrations.Migrate

    let projects =
      Services.ProjectCollection(
        loggerFactory.CreateLogger(),
        connectionFactory
      )

    let migrondiFactory =
      Services.MigrationOperationsFactory(loggerFactory, connectionFactory)

    {
      lf = loggerFactory
      projects = projects
      migrondiFactory = migrondiFactory
    }

module Server =

  let tryParseArgs = ServerBuilder.parseArgs

  let runMcpServer
    (connectionFactory: unit -> System.Data.IDbConnection)
    (options: McpOptions)
    (loggerFactory: ILoggerFactory)
    : Async<unit> =
    let env = ServerHelpers.createEnvironment connectionFactory loggerFactory
    ServerBuilder.run env options loggerFactory

  let create
    (connectionFactory: unit -> System.Data.IDbConnection)
    (options: McpOptions)
    (loggerFactory: ILoggerFactory)
    : IMcpServer =

    {
      new IMcpServer with
        member _.Run() : Async<unit> =
          runMcpServer connectionFactory options loggerFactory
    }
