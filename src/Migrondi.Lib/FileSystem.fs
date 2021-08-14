namespace Migrondi.FileSystem

open System
open System.Text.RegularExpressions

open FsToolkit.ErrorHandling

open Migrondi.Types
open Migrondi.Writer
/// A function that takes a path an creates a directory it should return the path of the newly created directory
type TryGetOrCreateDirectoryFn = string -> Result<string, exn>
/// <summary>
/// A funcion that takes a filename, a path like string and creates a <see cref="Migrondi.Types.MigrondiConfig">MigrondiConfig</see> file
/// </summary>
/// <remarks>
/// As of version 0.7.0 the filename is always assumed to be "migrondi.json" in several internal parts of the Migrondi Lib
/// this should change eventually
/// </remarks>
type TryGetOrCreateConfigFn = string -> string -> Result<MigrondiConfig, exn>
/// <summary>A function that Gets a <see cref="Migrondi.Types.MigrondiConfig">MigrondiConfig</see> object</summary>
type TryGetMigrondiConfigFn = unit -> Result<MigrondiConfig, exn>
/// A function that takes a path like string and tries to get all the migrations present in the user's disk
type TryGetMigrationsFn = string -> MigrationFile array
/// A function that takes a path like string where the migrations are stored (migrationsDir) and the name of the migration file
/// This should return the path of the migration file or an exception in case the operation was not successful
type TryCreateFileFn = string -> string -> Result<string, exn>

[<RequireQualifiedAccess>]
module FileSystem =
    open System.IO

    let TryGetOrCreateDirectory: TryGetOrCreateDirectoryFn =
        fun (path: string) ->
            let path =
                match Path.EndsInDirectorySeparator path with
                | true -> path
                | false -> sprintf "%s%c" path Path.DirectorySeparatorChar

            try
                Directory.CreateDirectory path
                |> (fun x -> x.FullName)
                |> Ok
            with
            | ex -> Error ex

    let TryGetOrCreateConfiguration: TryGetOrCreateConfigFn =
        let tryGetFileFromPath path =
            try
                File.ReadAllBytes path |> Ok
            with
            | :? System.IO.FileNotFoundException -> [||] |> Ok
            | ex -> Error ex

        let tryWriteFile path config =
            let content = Json.serializeBytes config true

            try
                use file = File.Create(path)
                file.Write(ReadOnlySpan content)
                Ok()
            with
            | ex -> Error ex

        fun filename migrationsDir ->
            result {
                let configPath =
                    Path.Join(migrationsDir, $"../{filename}")

                let! file = tryGetFileFromPath configPath

                let config =
                    { connection = "Data Source=migrondi.db"
                      migrationsDir = $"{migrationsDir}/"
                      driver = "sqlite" }

                if file.LongLength = 0L then
                    do! tryWriteFile configPath config
                    return config
                else
                    return! Json.tryDeserializeFile (file)
            }

    let TryGetMigrondiConfig: TryGetMigrondiConfigFn =
        fun () ->
            match
                Directory.EnumerateFiles(Directory.GetCurrentDirectory(), "migrondi.json")
                |> Seq.tryHead
                with
            | Some file ->
                File.ReadAllBytes file
                |> Json.tryDeserializeFile
                |> Result.bind
                    (fun config ->
                        match Driver.IsValidDriver config.driver with
                        | true -> Ok config
                        | false ->
                            let drivers = "mssql | sqlite | postgres | mysql"

                            let message =
                                $"""The driver selected "{config.driver}" does not match the available drivers  {drivers}"""

                            Error(InvalidDriverException message))
            | None -> Error(ConfigurationNotFound "We couldn't find the migrondi.json file")

    let private getNameAndTimestamp name =
        // matches any name that ends with a timestamp.sql
        let migrationNamePattern = "(.+)_([0-9]+).(sql|SQL)"

        let (|Regex|_|) pattern input =
            let m = Regex.Match(input, pattern)

            if m.Success then
                Some(List.tail [ for g in m.Groups -> g.Value ])
            else
                None

        match name with
        | Regex migrationNamePattern [ filename; timestamp; _ ] -> (filename, timestamp |> int64)
        | _ -> raise (InvalidMigrationName name)

    /// gives the separator string used inside the migrations file
    let GetSeparator (timestamp: int64) (migrationType: MigrationType) =
        let str =
            match migrationType with
            | MigrationType.Up -> "UP"
            | MigrationType.Down -> "DOWN"

        sprintf "-- ---------- MIGRONDI:%s:%i --------------" str timestamp

    let GetMigrations: TryGetMigrationsFn =
        let fileMapping (path: string) =
            let (name, timestamp) =
                getNameAndTimestamp (Path.GetFileName path)

            let content =
                try
                    File.ReadAllText path
                with
                | ex -> raise (FailedToReadFile ex.Message)

            let up, down =
                let separator =
                    GetSeparator timestamp MigrationType.Down

                let parts = content.Split(separator)

                if parts.Length <> 2 then
                    raise (MissingMigrationContent parts)

                parts.[0], parts.[1]

            { name = name
              timestamp = timestamp
              upContent = up
              downContent = down }

        fun path ->
            Directory.GetFiles(path, "*.sql")
            |> Array.Parallel.map fileMapping
            |> Array.sortBy (fun migration -> migration.timestamp)


    let TryCreateNewMigrationFile: TryCreateFileFn =

        let getContent timestamp =
            let l1 =
                $"-- ---------- MIGRONDI:UP:%i{timestamp} --------------"

            let l2 = $"-- Write your Up migrations here"

            let l3 =
                $"-- ---------- MIGRONDI:DOWN:%i{timestamp} --------------"

            let l4 =
                $"-- Write how to revert the migration here"

            $"{l1}\n{l2}\n\n{l3}\n{l4}\n\n"

        let tryCreateFile path (content: string) =
            try
                use file = File.CreateText path
                file.Write content
                Ok path
            with
            | ex -> Error ex

        let tryCreateDirectory path =
            try
                let dirinfo = Directory.CreateDirectory path
                dirinfo.FullName |> Ok
            with
            | ex -> Error ex

        fun migrationsDir migrationName ->
            let timestamp =
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()

            let name =
                $"{migrationName.Trim()}_{timestamp}.sql"

            tryCreateDirectory migrationsDir
            |> Result.bind
                (fun migrationsDir ->
                    let path = Path.Combine(migrationsDir, name)
                    tryCreateFile path (getContent timestamp))
