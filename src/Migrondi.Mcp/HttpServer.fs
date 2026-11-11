namespace Migrondi.Mcp

open System
open System.Threading
open System.Threading.Tasks

open Microsoft.Extensions.Logging

open ModelContextProtocol
open ModelContextProtocol.Protocol
open ModelContextProtocol.Server

open IcedTasks

type IMcpHttpServer =
  abstract Run: CancellationToken -> Task<unit>

module HttpServerHelpers =

  /// The only origins a localhost-bound MCP server accepts from browsers.
  /// `Uri.Host` reports IPv6 hosts without brackets, so both spellings are kept.
  let allowedOriginHosts = [| "localhost"; "127.0.0.1"; "::1"; "[::1]" |]

  /// An absent or empty Origin is a non-browser client: allowed.
  /// Only an explicit non-localhost origin is rejected.
  let isDisallowedOrigin(origin: string | null) =
    ValueOption.ofObj origin
    |> ValueOption.filter(String.IsNullOrWhiteSpace >> not)
    |> ValueOption.map(fun value ->
      match Uri.TryCreate(value, UriKind.Absolute) with
      | true, uri -> not(Array.contains uri.Host allowedOriginHosts)
      | false, _ -> true)
    |> ValueOption.defaultValue false

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

  // Stateless serving (SDK 2.4 model): one transient transport + server per
  // POST. `Stateless = true` makes the transport answer the single request
  // inline, so no session store exists and `Mcp-Session-Id` is never emitted;
  // the process keeps no cross-request state. Verified by the strict-client
  // tests in src/MigrondiUI.Tests/HttpServerTests.fs.
  let handlePostRequest
    (context: Net.HttpListenerContext)
    (serverOptions: McpServerOptions)
    (loggerFactory: ILoggerFactory)
    (serviceProvider: IServiceProvider)
    : CancellableTask<unit> = cancellableTask {
    try
      use stream = context.Request.InputStream
      use reader = new IO.StreamReader(stream)
      let! ct = CancellableTask.getCancellationToken()
      let! body = reader.ReadToEndAsync ct

      let message =
        System.Text.Json.JsonSerializer.Deserialize<JsonRpcMessage>(
          body,
          McpJsonUtilities.DefaultOptions
        )

      match message with
      | null -> sendError context 400 "Invalid JSON-RPC message"
      | msg ->
        let transport =
          StreamableHttpServerTransport(loggerFactory, Stateless = true)

        let server =
          McpServer.Create(
            transport,
            serverOptions,
            loggerFactory,
            serviceProvider
          )

        let serverTask = server.RunAsync(ct)

        try
          use responseStream = context.Response.OutputStream

          let! wroteResponse =
            transport.HandlePostRequestAsync(msg, responseStream, ct)

          if not wroteResponse then
            context.Response.StatusCode <- 202
        finally
          // End the transient session; RunAsync completes once the
          // transport closes. Fire-and-forget like the old session cleanup.
          transport.DisposeAsync().AsTask() |> ignore
          server.DisposeAsync().AsTask() |> ignore

        do! serverTask
    with ex ->
      sendError context 500 $"Internal server error: {ex.Message}"
  }

module HttpServer =

  let runHttpServer
    (port: int)
    (serverOptions: McpServerOptions)
    (loggerFactory: ILoggerFactory)
    (serviceProvider: IServiceProvider)
    (ct: CancellationToken)
    : Task<unit> =

    task {
      let logger = loggerFactory.CreateLogger "MigrondiMcpHttpServer"
      use listener = new Net.HttpListener()
      listener.Prefixes.Add $"http://localhost:{port}/mcp/"
      listener.Start()

      use linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct)

      let handleRequest(context: Net.HttpListenerContext) = task {
        if
          HttpServerHelpers.isDisallowedOrigin context.Request.Headers["Origin"]
        then
          HttpServerHelpers.sendError context 403 "Origin not allowed"
        else
          match context.Request.HttpMethod with
          | "POST" ->
            do!
              HttpServerHelpers.handlePostRequest
                context
                serverOptions
                loggerFactory
                serviceProvider
                linkedCts.Token
          // GET/DELETE return 405: this server offers no standalone SSE
          // stream and keeps no sessions; clients must accept 405.
          | _ -> HttpServerHelpers.sendError context 405 "Method Not Allowed"
      }

      let fireAndForgetRequest(context: Net.HttpListenerContext) =
        asyncEx {
          let! result = handleRequest context |> Async.AwaitTask |> Async.Catch

          match result with
          | Choice1Of2() -> ()
          | Choice2Of2(:? OperationCanceledException) -> ()
          | Choice2Of2 ex ->
            logger.LogError(ex, "Error handling HTTP request (fire-and-forget)")
        }
        |> fun work -> Async.StartImmediate(work, linkedCts.Token)

      // GetContextAsync takes no token. Stopping the listener when the
      // token fires is what wakes the accept loop on an idle server.
      use _cancellationRegistration =
        linkedCts.Token.Register(fun () ->
          try
            listener.Stop()
          with _ ->
            ())

      try
        try
          while not linkedCts.Token.IsCancellationRequested do
            let! context = listener.GetContextAsync()
            fireAndForgetRequest context
        with
        | :? OperationCanceledException -> ()
        | :? Net.HttpListenerException when
          linkedCts.Token.IsCancellationRequested
          ->
          ()
        | :? ObjectDisposedException when
          linkedCts.Token.IsCancellationRequested
          ->
          ()
      finally
        linkedCts.Cancel()

        try
          listener.Stop()
        with _ ->
          ()
    }

  let create
    (port: int)
    (serverOptions: McpServerOptions)
    (loggerFactory: ILoggerFactory)
    (serviceProvider: IServiceProvider)
    : IMcpHttpServer =

    {
      new IMcpHttpServer with
        member _.Run(ct: CancellationToken) : Task<unit> =
          runHttpServer port serverOptions loggerFactory serviceProvider ct
    }
