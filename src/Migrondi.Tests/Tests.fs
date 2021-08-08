namespace Migrondi.Tests

open System
open Microsoft.VisualStudio.TestTools.UnitTesting
open System.IO
open System.Text
open Migrondi.FileSystem
open Migrondi.Options
open Migrondi.Migrations
open Migrondi.Database
open Migrondi.Types

[<TestClass>]
type TestUtils() =

    [<TestMethod>]
    member _.``Run Init with Custom Path``() =
        let tempPath =
            Path.Combine(Path.GetTempPath(), "migrondi-init-custom-path")
        let opts: InitOptions = { path = tempPath; noColor = true; json = false }
        let result = MigrondiRunner.RunInit(opts)
        match result with 
        | Ok result ->
            Assert.AreEqual(0, result)
            Assert.IsTrue(tempPath |> Path.GetFullPath |> Directory.Exists)
        | Error err ->
            eprintfn "%O" err
            Assert.Fail()

    [<TestMethod>]
    member _.``Create Migrondi Config Json``() =
        let tempPath =
            Path.Combine(Path.GetTempPath(), "migrondi-create-config", "migrations")
        Directory.CreateDirectory tempPath |> ignore
        let result = FileSystem.TryGetOrCreateConfiguration "test.json" tempPath
        match result with 
        | Ok config ->
            Assert.IsTrue(tempPath |> Path.GetFullPath |> Directory.Exists)
            let fullPath = tempPath |> Path.GetFullPath
            let fullPathOnConfig = config.migrationsDir |> Path.GetFullPath
            Assert.AreEqual(fullPath + $"{Path.DirectorySeparatorChar}", fullPathOnConfig)
        | Error err ->
            eprintfn "%O" err
            Assert.Fail()

    [<TestMethod>]
    member _.``Get Separator``() =
        let timestamp =
            DateTimeOffset.Now.ToUnixTimeMilliseconds()

        let actualDownSeparator =
            Queries.getSeparator MigrationType.Down timestamp

        let actualUpSeparator = FileSystem.GetSeparator timestamp MigrationType.Up 

        let expectedDown =
            sprintf "-- ---------- MIGRONDI:%s:%i --------------" "DOWN" timestamp

        let expectedUp =
            sprintf "-- ---------- MIGRONDI:%s:%i --------------" "UP" timestamp

        Assert.AreEqual(expectedDown, actualDownSeparator)
        Assert.AreEqual(expectedUp, actualUpSeparator)

    [<TestMethod>]
    member _.``Run New Migration``() =

        let tempPath =
            Path.Combine(Path.GetTempPath(), "createNewMigrationFile")

        let result = FileSystem.TryCreateNewMigrationFile tempPath "RunNewMigrationTest"

        let getContent path = 
            try 
                File.ReadAllText path |> Some
            with ex -> None

        match result with 
        | Ok path ->
            let file = FileInfo(path)
            Assert.IsTrue(file.FullName.Contains "createNewMigrationFile")
            Assert.IsTrue(file.FullName.Contains "RunNewMigrationTest")
            Assert.IsTrue(file.FullName.Contains "_")
            Assert.IsTrue(file.FullName.Contains ".sql")
            match getContent path with 
            | Some content -> 
                Assert.IsTrue(content.Contains "MIGRONDI:UP")
                Assert.IsTrue(content.Contains "MIGRONDI:DOWN")
            | None -> Assert.Fail()
        | Error err ->
            eprintfn "%O" err
            Assert.Fail()

    [<TestMethod>]
    member _.``Create New Migration File With Complex Name``() =
        let randomName n =
            let r = Random()

            let chars =
                Array.concat (
                    [ [| 'a' .. 'z' |]
                      [| 'A' .. 'Z' |]
                      [| '0' .. '9' |]
                      [| '!'; '@'; ' '; '_'; '#'; '%' |] ]
                )

            let sz = Array.length chars in
            String(Array.init n (fun _ -> chars.[r.Next sz]))

        let name = randomName 20
        let tempPath =
            Path.Combine(Path.GetTempPath(), "createNewMigrationFileComplex")

        let result = FileSystem.TryCreateNewMigrationFile tempPath name

        let getContent path = 
            try 
                File.ReadAllText path |> Some
            with ex -> None

        match result with 
        | Ok path ->
            let file = FileInfo(path)
            Assert.IsTrue(file.FullName.Contains "createNewMigrationFile")
            Assert.IsTrue(file.FullName.Contains name)
            Assert.IsTrue(file.FullName.Contains "_")
            Assert.IsTrue(file.FullName.Contains ".sql")
            match getContent path with 
            | Some content -> 
                Assert.IsTrue(content.Contains "MIGRONDI:UP")
                Assert.IsTrue(content.Contains "MIGRONDI:DOWN")
            | None -> Assert.Fail()
        | Error err ->
            eprintfn "%O" err
            Assert.Fail()
