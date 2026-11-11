module MigrondiUI.Tests.LoggingTests

open System
open Microsoft.Extensions.Logging
open Xunit

open Migrondi.Mcp

[<Fact>]
let ``createLoggerFactory returns a working factory``() : unit =
  use factory = McpLogging.createLoggerFactory LogLevel.Information

  Assert.NotNull factory

  let logger = factory.CreateLogger("test")

  logger.LogInformation("McpLogging test message at {Time}", DateTime.UtcNow)

[<Fact>]
let ``McpVersion.get returns a non-empty version that is not 1.2.0``() : unit =
  let version: string = McpVersion.get()

  Assert.False(String.IsNullOrEmpty version)
  Assert.NotEqual<string>("1.2.0", version)
