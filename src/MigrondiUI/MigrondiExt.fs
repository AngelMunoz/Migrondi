module MigrondiUI.MigrondiExt

open System
open System.Collections.Generic
open System.IO
open System.Threading
open System.Threading.Tasks
open System.Runtime.InteropServices
open System.Text.RegularExpressions

open Migrondi.Core
open Microsoft.Extensions.Logging

open MigrondiUI


type IMigrondiUI =
  inherit IMigrondi

  abstract member RunUpdateAsync:
    migration: VirtualMigration * ?cancellationToken: CancellationToken -> Task


let private normalizeSqliteConnection (vProjectId: Guid) (connection: string) =
  let pattern = Regex(@"Data Source\s*=\s*(.+?)(?:;|$)", RegexOptions.IgnoreCase)
  let m = pattern.Match(connection)
  
  if not m.Success then connection
  else
    let dataSource = m.Groups.[1].Value.Trim()
    
    if Path.IsPathRooted dataSource then connection
    else
      let appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
      let baseDir = Path.Combine(appData, "MigrondiUI", vProjectId.ToString())
      let fileName = dataSource.Replace("./", "").Replace(".\\", "")
      let absolutePath = Path.Combine(baseDir, fileName)
      
      if not (Directory.Exists baseDir) then
        Directory.CreateDirectory baseDir |> ignore
      
      let restOfConnection = connection.Substring(m.Index + m.Length)
      $"Data Source={absolutePath}{restOfConnection}"


let getMigrondiUI(lf: ILoggerFactory, vpr: Projects.IVirtualProjectRepository) =
  let mufs = lf.CreateLogger<VirtualFs.MigrondiUIFs>()
  let ml = lf.CreateLogger<IMigrondi>()
  let vfs = VirtualFs.getVirtualFs(mufs, vpr)

  fun (config: MigrondiConfig, rootDir: string, projectId: Guid) ->

    let normalizedConfig =
      match config.driver with
      | MigrondiDriver.Sqlite ->
        { config with connection = normalizeSqliteConnection projectId config.connection }
      | _ -> config

    let migrondi =
      Migrondi.Core.Migrondi.MigrondiFactory(
        normalizedConfig,
        rootDir,
        ml,
        vfs
      )


    { new IMigrondiUI with
        member _.DryRunDown(?amount) = migrondi.DryRunDown(?amount = amount)


        member _.DryRunDownAsync(?amount, ?cancellationToken) =
          migrondi.DryRunDownAsync(
            ?amount = amount,
            ?cancellationToken = cancellationToken
          )

        member _.DryRunUp(?amount) : IReadOnlyList<Migration> =
          migrondi.DryRunUp(?amount = amount)

        member _.DryRunUpAsync(?amount, ?cancellationToken) =

          migrondi.DryRunUpAsync(
            ?amount = amount,
            ?cancellationToken = cancellationToken
          )

        member _.Initialize() : unit = migrondi.Initialize()

        member _.InitializeAsync(?cancellationToken) : Task =
          migrondi.InitializeAsync(?cancellationToken = cancellationToken)

        member _.MigrationsList() : IReadOnlyList<MigrationStatus> =

          migrondi.MigrationsList()

        member _.MigrationsListAsync(?cancellationToken) =
          migrondi.MigrationsListAsync(?cancellationToken = cancellationToken)

        member _.RunDown(?amount) = migrondi.RunDown(?amount = amount)

        member _.RunDownAsync(?amount, ?cancellationToken) =
          migrondi.RunDownAsync(
            ?amount = amount,
            ?cancellationToken = cancellationToken
          )


        member _.RunNew
          (friendlyName: string, ?upContent, ?downContent, ?manualTransaction)
          : Migration =

          migrondi.RunNew(
            friendlyName,
            ?upContent = upContent,
            ?downContent = downContent,
            ?manualTransaction = manualTransaction
          )

        member _.RunNewAsync
          (friendlyName: string,
           ?upContent,
           ?downContent,
           ?manualTransaction,
           ?cancellationToken)
          : Task<Migration> =

          migrondi.RunNewAsync(
            friendlyName,
            ?upContent = upContent,
            ?downContent = downContent,
            ?manualTransaction = manualTransaction,
            ?cancellationToken = cancellationToken
          )

        member _.RunUp(?amount) = migrondi.RunUp(?amount = amount)

        member _.RunUpAsync(?amount, ?cancellationToken) =
          migrondi.RunUpAsync(
            ?amount = amount,
            ?cancellationToken = cancellationToken
          )

        member _.RunUpdateAsync
          (migration: VirtualMigration, ?cancellationToken)
          : Task =
          let ct = defaultArg cancellationToken CancellationToken.None
          vpr.UpdateMigration migration ct

        member _.ScriptStatus(migrationPath: string) : MigrationStatus =
          migrondi.ScriptStatus(migrationPath)

        member _.ScriptStatusAsync
          (arg1: string, ?cancellationToken)
          : Task<MigrationStatus> =
          migrondi.ScriptStatusAsync(
            arg1,
            ?cancellationToken = cancellationToken
          )
    }