module Migrondi.UI.Tests.HttpServerTests

open System
open System.Net
open System.Net.Http
open System.Net.Sockets
open System.Text
open System.Threading
open System.Threading.Tasks

open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging
open ModelContextProtocol.Protocol
open ModelContextProtocol.Server
open Xunit

open Migrondi.Mcp

let private freePort() =
  let listener = new TcpListener(IPAddress.Loopback, 0)
  listener.Start()
  let port = (listener.LocalEndpoint :?> IPEndPoint).Port
  listener.Stop()
  port

// A no-argument read tool used as the target of the tools/call request.
type private TestPingDelegate =
  delegate of ct: CancellationToken -> Task<CallToolResult>

let private pingHandler(_ct: CancellationToken) : Task<CallToolResult> =
  let element =
    System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(
      """{"ok":true}"""
    )

  Task.FromResult(CallToolResult(StructuredContent = Nullable element))

let private testServerOptions() =
  let options = McpServerOptions()

  options.ServerInfo <-
    Implementation(Name = "migrondi-http-test", Version = "1.0.0")

  let collection = McpServerPrimitiveCollection<McpServerTool>()

  collection.Add(
    McpServerTool.Create(
      TestPingDelegate(pingHandler),
      McpServerToolCreateOptions(
        Name = "test_ping",
        Title = "Test Ping",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false
      )
    )
  )

  options.ToolCollection <- collection
  options

let private startServer
  (loggerFactory: ILoggerFactory)
  (options: McpServerOptions)
  =
  let port = freePort()

  let services = ServiceCollection()
  services.AddSingleton<ILoggerFactory>(loggerFactory) |> ignore
  let serviceProvider = services.BuildServiceProvider()

  // Not disposed here: ownership moves to the caller, which cancels
  // it to stop the server.
  let cts = new CancellationTokenSource()

  let runTask =
    HttpServer.runHttpServer
      port
      options
      loggerFactory
      serviceProvider
      cts.Token

  port, cts, runTask

let private postJson (port: int) (body: string) = task {
  use client = new HttpClient()

  use content = new StringContent(body, Encoding.UTF8, "application/json")

  use request =
    new HttpRequestMessage(HttpMethod.Post, $"http://localhost:{port}/mcp/")

  request.Content <- content
  request.Headers.Add("Accept", "application/json, text/event-stream")

  return! client.SendAsync(request)
}

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

[<Fact>]
let ``POST sequence completes initialize, tools/list and a tool call``
  ()
  : Task = task {
  use loggerFactory =
    LoggerFactory.Create(fun builder ->
      builder.SetMinimumLevel(LogLevel.None) |> ignore)

  let port, cts, runTask = startServer loggerFactory (testServerOptions())

  try
    let initializeBody =
      """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"http-tests","version":"1.0"}}}"""

    let! initializeResponse = postJson port initializeBody

    Assert.True(initializeResponse.IsSuccessStatusCode, "initialize failed")
    Assert.False(initializeResponse.Headers.Contains("Mcp-Session-Id"))

    let! initializeBody' = initializeResponse.Content.ReadAsStringAsync()
    Assert.Contains("\"result\"", initializeBody')

    let! notification =
      postJson port """{"jsonrpc":"2.0","method":"notifications/initialized"}"""

    Assert.True(
      notification.IsSuccessStatusCode,
      "initialized notification failed"
    )

    Assert.False(notification.Headers.Contains("Mcp-Session-Id"))

    let! listResponse =
      postJson
        port
        """{"jsonrpc":"2.0","id":2,"method":"tools/list","params":{}}"""

    Assert.True(listResponse.IsSuccessStatusCode, "tools/list failed")
    Assert.False(listResponse.Headers.Contains("Mcp-Session-Id"))

    let! listBody = listResponse.Content.ReadAsStringAsync()
    Assert.Contains("\"result\"", listBody)
    Assert.Contains("test_ping", listBody)

    let! callResponse =
      postJson
        port
        """{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"test_ping","arguments":{}}}"""

    Assert.True(callResponse.IsSuccessStatusCode, "tools/call failed")
    Assert.False(callResponse.Headers.Contains("Mcp-Session-Id"))

    let! callBody = callResponse.Content.ReadAsStringAsync()
    Assert.Contains("\"result\"", callBody)
    Assert.Contains("\"ok\"", callBody)
  finally
    cts.Cancel()

  do! runTask
}

[<Fact>]
let ``POST with a non-localhost Origin is rejected with 403``() : Task = task {
  use loggerFactory =
    LoggerFactory.Create(fun builder ->
      builder.SetMinimumLevel(LogLevel.None) |> ignore)

  let port, cts, runTask = startServer loggerFactory (testServerOptions())

  try
    use client = new HttpClient()

    use content =
      new StringContent(
        """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}""",
        Encoding.UTF8,
        "application/json"
      )

    use request =
      new HttpRequestMessage(HttpMethod.Post, $"http://localhost:{port}/mcp/")

    request.Content <- content
    request.Headers.Add("Origin", "http://evil.example")
    request.Headers.Add("Accept", "application/json, text/event-stream")

    let! response = client.SendAsync(request)

    Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode)
  finally
    cts.Cancel()

  do! runTask
}

[<Fact>]
let ``GET requests are rejected with 405``() : Task = task {
  use loggerFactory =
    LoggerFactory.Create(fun builder ->
      builder.SetMinimumLevel(LogLevel.None) |> ignore)

  let port, cts, runTask = startServer loggerFactory (testServerOptions())

  try
    use client = new HttpClient()
    let! response = client.GetAsync($"http://localhost:{port}/mcp/")
    Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode)
  finally
    cts.Cancel()

  do! runTask
}
