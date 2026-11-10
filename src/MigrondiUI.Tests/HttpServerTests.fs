module MigrondiUI.Tests.HttpServerTests

open System
open System.Net
open System.Net.Sockets
open System.Threading
open System.Threading.Tasks

open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging
open ModelContextProtocol.Server
open Xunit

open MigrondiUI.Mcp

let private freePort() =
  let listener = new TcpListener(IPAddress.Loopback, 0)
  listener.Start()
  let port = (listener.LocalEndpoint :?> IPEndPoint).Port
  listener.Stop()
  port

[<Fact>]
let ``runHttpServer returns when cancelled while idle``() : Task = task {
  let port = freePort()

  use loggerFactory =
    LoggerFactory.Create(fun builder ->
      builder.SetMinimumLevel(LogLevel.None) |> ignore)

  let services = ServiceCollection()
  services.AddSingleton<ILoggerFactory>(loggerFactory) |> ignore
  let serviceProvider = services.BuildServiceProvider()

  let serverOptions = McpServerOptions()
  use cts = new CancellationTokenSource()

  let runTask =
    HttpServer.runHttpServer
      port
      serverOptions
      loggerFactory
      serviceProvider
      cts.Token

  // Give the listener time to start, then cancel with no request in flight.
  do! Task.Delay(250)
  cts.Cancel()

  let! winner = Task.WhenAny(runTask, Task.Delay(5000))

  Assert.True(
    obj.ReferenceEquals(winner, runTask),
    "runHttpServer did not return within 5 seconds of cancellation"
  )

  do! runTask
}
