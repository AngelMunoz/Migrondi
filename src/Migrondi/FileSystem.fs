﻿namespace Migrondi.FileSystem

open System
open System.Text.Json
open System.Text.Json.Serialization
open System.Text.RegularExpressions

open FsToolkit.ErrorHandling

open Migrondi.Types

type TryGetOrCreateDirectoryFn = string -> Result<string, exn>
type TryGetOrCreateConfigFn = string -> string -> Result<MigrondiConfig, exn>
type TryGetMigrondiConfigFn = unit -> Result<MigrondiConfig, exn>
type TryGetMigrationsFn = string -> MigrationFile array
type TryCreateFileFn = string -> string -> Result<string, exn>

[<RequireQualifiedAccess>]
module FileSystem =
    open System.IO

    let TryGetOrCreateDirectory: TryGetOrCreateDirectoryFn =
        fun (path: string) ->
            let path =
                if Path.EndsInDirectorySeparator path then
                    path
                else
                    sprintf "%s%c" path Path.DirectorySeparatorChar

            try
                Ok <| (Directory.CreateDirectory path).FullName
            with
            | ex -> Error ex

    let private tryDeserializeFile (bytes: byte []) =
        let opts = JsonSerializerOptions()
        opts.AllowTrailingCommas <- true
        opts.ReadCommentHandling <- JsonCommentHandling.Skip
        opts.DefaultIgnoreCondition <- JsonIgnoreCondition.WhenWritingNull
        opts.PropertyNamingPolicy <- JsonNamingPolicy.CamelCase

        try
            JsonSerializer.Deserialize<MigrondiConfig>(ReadOnlySpan(bytes), opts)
            |> Ok
        with
        | ex -> Error ex

    let TryGetOrCreateConfiguration: TryGetOrCreateConfigFn =
        let tryGetFileFromPath path =
            try
                File.ReadAllBytes path |> Ok
            with
            | ex -> Error ex

        let tryWriteFile path config =
            let content =
                let opts = JsonSerializerOptions()
                opts.WriteIndented <- true
                opts.PropertyNamingPolicy <- JsonNamingPolicy.CamelCase

                JsonSerializer.SerializeToUtf8Bytes(config, opts)

            try
                use file = File.Create(path)
                file.Write(ReadOnlySpan content)
                Ok()
            with
            | ex -> Error ex

        fun filename directory ->
            result {
                let path = Path.Combine(directory, filename)
                let! file = tryGetFileFromPath path

                let config =
                    { connection = "Data Source=migrondi.db"
                      migrationsDir = Path.GetDirectoryName(path)
                      driver = "sqlite" }

                if file.LongLength = 0L then
                    do! tryWriteFile path config
                    return config
                else
                    return! tryDeserializeFile (file)
            }

    let TryGetMigrondiConfig: TryGetMigrondiConfigFn =
        fun () ->
            result {
                match
                    Directory.EnumerateFiles(Directory.GetCurrentDirectory(), "migrondi.json")
                    |> Seq.tryHead
                    with
                | Some file ->
                    let! config = File.ReadAllBytes file |> tryDeserializeFile

                    if Driver.IsValidDriver config.driver then
                        return config
                    else
                        let drivers = "mssql | sqlite | postgres | mysql"

                        let message =
                            $"""The driver selected "{config.driver}" does not match the available drivers  {drivers}"""

                        return! (Error(InvalidDriverException message))
                | None -> return! Error(ConfigurationNotFound "We couldn't find the migrondi.json file")
            }

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
                    raise MissingMigrationContent

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

        fun migrationsDir migrationName ->
            let timestamp =
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()

            let name =
                $"{migrationName.Trim()}_{timestamp}.sql"

            let path = Path.Combine(migrationsDir, name)

            tryCreateFile path (getContent timestamp)
