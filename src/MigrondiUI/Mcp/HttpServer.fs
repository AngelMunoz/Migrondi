namespace MigrondiUI.Mcp

open System
open System.Collections.Concurrent
open System.Threading
open System.Threading.Tasks

open Microsoft.Extensions.Logging

open ModelContextProtocol
open ModelContextProtocol.Protocol
open ModelContextProtocol.Server

open IcedTasks

type private McpHttpSession = {
  id: string
  transport: StreamableHttpServerTransport
  server: McpServer
  serverTask: Task
}

type IMcpHttpServer =
  abstract Run: CancellationToken -> Task<unit>

module private HttpServerHelpers =

  let createSession
    (serverOptions: McpServerOptions)
    (loggerFactory: ILoggerFactory)
    (serviceProvider: IServiceProvider)
    (cts: CancellationTokenSource)
    : McpHttpSession =
    let sessionId = Guid.NewGuid().ToString("N")

    let transport =
      StreamableHttpServerTransport(loggerFactory, SessionId = sessionId)

    let server =
      McpServer.Create(transport, serverOptions, loggerFactory, serviceProvider)

    let serverTask = server.RunAsync(cts.Token)

    {
      id = sessionId
      transport = transport
      server = server
      serverTask = serverTask
    }

  let getSession
    (sessions: ConcurrentDictionary<string, McpHttpSession>)
    sessionId
    =
    match sessions.TryGetValue(sessionId) with
    | true, s -> Some s
    | false, _ -> None

  let removeSession
    (sessions: ConcurrentDictionary<string, McpHttpSession>)
    sessionId
    =
    match sessions.TryGetValue(sessionId) with
    | true, session ->
      session.transport.DisposeAsync().AsTask() |> ignore
      session.server.DisposeAsync().AsTask() |> ignore
      sessions.TryRemove(sessionId) |> ignore
    | false, _ -> ()

  let cleanupSessions(sessions: ConcurrentDictionary<string, McpHttpSession>) =
    for kvp in sessions do
      kvp.Value.transport.DisposeAsync().AsTask() |> ignore
      kvp.Value.server.DisposeAsync().AsTask() |> ignore

  let sendError
    (context: Net.HttpListenerContext)
    (statusCode: int)
    (message: string)
    =
    try
      context.Response.StatusCode <- statusCode
      use writer = new IO.StreamWriter(context.Response.OutputStream)
      writer.Write(message)
      writer.Flush()
    with _ ->
      ()

    context.Response.Close()

  let getSessionId(context: Net.HttpListenerContext) =
    match context.Request.Headers.["Mcp-Session-Id"] with
    | null -> None
    | id -> Some(string id)

  let handlePostRequest
    (context: Net.HttpListenerContext)
    (sessions: ConcurrentDictionary<string, McpHttpSession>)
    (serverOptions: McpServerOptions)
    (loggerFactory: ILoggerFactory)
    (serviceProvider: IServiceProvider)
    (cts: CancellationTokenSource)
    : Task<unit> =
    task {
      let sessionId = getSessionId context

      try
        let session =
          match sessionId with
          | Some id ->
            match getSession sessions id with
            | Some s -> s
            | None ->
              createSession serverOptions loggerFactory serviceProvider cts
          | None ->
            createSession serverOptions loggerFactory serviceProvider cts

        sessions.[session.id] <- session

        use stream = context.Request.InputStream
        use reader = new IO.StreamReader(stream)
        let! body = reader.ReadToEndAsync(cts.Token)

        let message =
          System.Text.Json.JsonSerializer.Deserialize<JsonRpcMessage>(
            body,
            McpJsonUtilities.DefaultOptions
          )

        match message with
        | null -> sendError context 400 "Invalid JSON-RPC message"
        | msg ->
          context.Response.Headers.Add("Mcp-Session-Id", session.id)
          use responseStream = context.Response.OutputStream

          let! wroteResponse =
            session.transport.HandlePostRequestAsync(
              msg,
              responseStream,
              cts.Token
            )

          if not wroteResponse then
            context.Response.StatusCode <- 202
            context.Response.Close()
      with ex ->
        sendError context 500 $"Internal server error: {ex.Message}"
    }

  let handleGetRequest
    (context: Net.HttpListenerContext)
    (sessions: ConcurrentDictionary<string, McpHttpSession>)
    (cts: CancellationTokenSource)
    : Task<unit> =
    task {
      let sessionId = getSessionId context

      match sessionId with
      | None -> sendError context 400 "Mcp-Session-Id header is required"
      | Some id ->
        match getSession sessions id with
        | None ->
          context.Response.StatusCode <- 404
          context.Response.Close()
        | Some session ->
          context.Response.Headers.Add("Mcp-Session-Id", session.id)
          context.Response.ContentType <- "text/event-stream"
          context.Response.Headers.Add("Cache-Control", "no-cache")

          try
            do!
              session.transport.HandleGetRequestAsync(
                context.Response.OutputStream,
                cts.Token
              )
          with
          | :? OperationCanceledException -> ()
          | _ -> ()
    }

  let handleDeleteRequest
    (context: Net.HttpListenerContext)
    (sessions: ConcurrentDictionary<string, McpHttpSession>)
    : unit =
    let sessionId = getSessionId context

    match sessionId with
    | None -> sendError context 400 "Mcp-Session-Id header is required"
    | Some id ->
      removeSession sessions id
      context.Response.StatusCode <- 200
      context.Response.Close()

  let handleUnknownMethod(context: Net.HttpListenerContext) : unit =
    context.Response.StatusCode <- 405
    context.Response.Close()

module HttpServer =

  let runHttpServer
    (port: int)
    (serverOptions: McpServerOptions)
    (loggerFactory: ILoggerFactory)
    (serviceProvider: IServiceProvider)
    (ct: CancellationToken)
    : Task<unit> =

    task {
      let logger = loggerFactory.CreateLogger("MigrondiMcpHttpServer")
      let sessions = ConcurrentDictionary<string, McpHttpSession>()
      use listener = new Net.HttpListener()
      listener.Prefixes.Add($"http://localhost:{port}/mcp/")
      listener.Start()

      use linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct)

      let handleRequest(context: Net.HttpListenerContext) = task {
        match context.Request.HttpMethod with
        | "POST" ->
          do!
            HttpServerHelpers.handlePostRequest
              context
              sessions
              serverOptions
              loggerFactory
              serviceProvider
              linkedCts
        | "GET" ->
          do! HttpServerHelpers.handleGetRequest context sessions linkedCts
        | "DELETE" -> HttpServerHelpers.handleDeleteRequest context sessions
        | _ -> HttpServerHelpers.handleUnknownMethod context
      }

      let fireAndForgetRequest(context: Net.HttpListenerContext) =
        asyncEx {
          let! result = handleRequest context |> Async.AwaitTask |> Async.Catch

          match result with
          | Choice1Of2() -> ()
          | Choice2Of2 ex ->
            logger.LogError(ex, "Error handling HTTP request (fire-and-forget)")
        }
        |> Async.StartImmediate

      try
        while not linkedCts.Token.IsCancellationRequested do
          let! context = listener.GetContextAsync()
          fireAndForgetRequest context
      with :? OperationCanceledException ->
        ()

      listener.Stop()
      HttpServerHelpers.cleanupSessions sessions
    }

  let create
    (port: int)
    (serverOptions: McpServerOptions)
    (loggerFactory: ILoggerFactory)
    (serviceProvider: IServiceProvider)
    : IMcpHttpServer =

    { new IMcpHttpServer with
        member _.Run(ct: CancellationToken) : Task<unit> =
          runHttpServer port serverOptions loggerFactory serviceProvider ct
    }
