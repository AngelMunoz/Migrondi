namespace Migrondi.Env

open System
open System.IO
open System.Threading
open System.Threading.Tasks

open ModelContextProtocol.Protocol
open ModelContextProtocol.Server

open Migrondi.Mcp
open Migrondi.Mcp.McpResults
open IcedTasks

type GetProjectInfoDelegate =
  delegate of ct: CancellationToken -> Task<CallToolResult>

/// CLI-owned single-project tool: reports the resolved project surface
/// (name, config path, driver, migrations folder, table name) and NEVER
/// the connection string.
[<RequireQualifiedAccess>]
module internal McpProjectInfo =

  let getProjectInfo(binding: McpBinding) : CancellableTask<CallToolResult> =
    cancellableTask {
      let separator = Path.DirectorySeparatorChar

      let name = Path.GetFileName(binding.Root.TrimEnd(separator))

      let migrationsFolder =
        Path.GetFullPath(binding.Config.migrations, binding.Root)

      let result: ProjectInfoResult = {
        name = name
        configPath = binding.ConfigPath
        driver = binding.Config.driver.AsString
        migrationsFolder = migrationsFolder
        tableName = binding.Config.tableName
      }

      return result |> McpResultMapper.fromEncoder ProjectInfoResult.Encoder
    }

  let createTool(binding: McpBinding) : McpServerTool =
    let spec: ToolSpec = {
      Name = "get_project"
      Title = "Get Project"
      ReadOnly = true
      Destructive = false
      Idempotent = true
      OpenWorld = false
    }

    McpServerTool.Create(
      GetProjectInfoDelegate(fun ct -> getProjectInfo binding ct),
      McpServerToolCreateOptions(
        Name = spec.Name,
        Title = spec.Title,
        ReadOnly = spec.ReadOnly,
        Destructive = spec.Destructive,
        Idempotent = spec.Idempotent,
        OpenWorld = spec.OpenWorld
      )
    )
