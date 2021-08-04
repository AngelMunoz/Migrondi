namespace Migrondi.Tests

open System
open Microsoft.VisualStudio.TestTools.UnitTesting
open System.IO
open Migrondi.Utils
open System.Text
open Migrondi.Types

[<TestClass>]
type TestUtils() =

    [<TestMethod>]
    member this.CheckExistsPathAndMigrationsDirTest() =
        let tempPath = Path.GetTempPath()
        let path = Path.Combine(tempPath, "migronditests")

        let migrationsPath =
            Path.Combine(tempPath, "migronditests", "migrations")

        let (fileExists, directoryExists) =
            checkExistsPathAndMigrationsDir path migrationsPath

        Assert.IsFalse(fileExists)
        Assert.IsFalse(directoryExists)

    [<TestMethod>]
    member this.GetInitPathAndMigrationsPathTest() =
        let tempPath = Path.GetTempPath()
        let expected = Path.Combine(tempPath, "migronditests")

        let expectedMigrationsPath =
            "migrations"
            + Path.DirectorySeparatorChar.ToString()

        let (actualPath, actualMigrationsPath) = getInitPathAndMigrationsPath expected
        Assert.AreEqual(expected, actualPath)
        Assert.AreEqual(expectedMigrationsPath, actualMigrationsPath)

    [<TestMethod>]
    member this.CreateMigrationsDirTest() =
        let tempPath =
            Path.Combine(Path.GetTempPath(), "migrondiCreateDir", "migrations")

        let dir = getOrCreateMigrationsDir tempPath

        Assert.AreEqual(tempPath + Path.DirectorySeparatorChar.ToString(), dir.FullName)
        Assert.IsTrue(Directory.Exists dir.FullName)

    [<TestMethod>]
    member this.CreateMigrondiConfJsonTest() =
        let tempPath =
            Path.Combine(Path.GetTempPath(), "createMigrondiConfJson")

        Directory.CreateDirectory tempPath |> ignore

        let migrationsDir =
            Path.Combine(tempPath, "migrations")
            + Path.DirectorySeparatorChar.ToString()

        let file, content =
            createMigrondiConfJson tempPath migrationsDir

        file.Write(ReadOnlySpan<byte>(content))
        let contentStr = Encoding.UTF8.GetString content
        let expected = Path.Combine(tempPath, "migrondi.json")
        Assert.AreEqual(expected, file.Name)
        printfn "%s" contentStr
        Assert.IsTrue(contentStr.Contains("Data Source=migrondi.db"))
        Assert.IsTrue(contentStr.Contains("sqlite"))

    [<TestMethod>]
    member this.GetSeparatorTest() =
        let timestamp =
            DateTimeOffset.Now.ToUnixTimeMilliseconds()

        let actualDownSeparator =
            getSeparator MigrationType.Down timestamp

        let actualUpSeparator = getSeparator MigrationType.Up timestamp

        let expectedDown =
            sprintf "-- ---------- MIGRONDI:%s:%i --------------" "DOWN" timestamp

        let expectedUp =
            sprintf "-- ---------- MIGRONDI:%s:%i --------------" "UP" timestamp

        Assert.AreEqual(expectedDown, actualDownSeparator)
        Assert.AreEqual(expectedUp, actualUpSeparator)

    [<TestMethod>]
    member this.CreateNewMigrationFileTest() =
        let tempPath =
            Path.Combine(Path.GetTempPath(), "createNewMigrationFile")

        Directory.CreateDirectory tempPath |> ignore

        let file, content =
            createNewMigrationFile tempPath "createNewMigrationFile"

        let content = Encoding.UTF8.GetString content
        Assert.IsTrue(file.Name.Contains "createNewMigrationFile")
        Assert.IsTrue(file.Name.Contains "_")
        Assert.IsTrue(file.Name.Contains ".sql")
        Assert.IsTrue(content.Contains "MIGRONDI:UP")
        Assert.IsTrue(content.Contains "MIGRONDI:DOWN")

    [<TestMethod>]
    member this.CreateNewMigrationFile_WithComplexNameTest() =
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
        let tempPath = Path.Combine(Path.GetTempPath(), name)

        Directory.CreateDirectory tempPath |> ignore

        let file, content = createNewMigrationFile tempPath name

        let content = Encoding.UTF8.GetString content
        Assert.IsTrue(file.Name.Contains name)
        Assert.IsTrue(file.Name.Contains "_")
        Assert.IsTrue(file.Name.Contains ".sql")
        Assert.IsTrue(content.Contains "MIGRONDI:UP")
        Assert.IsTrue(content.Contains "MIGRONDI:DOWN")
