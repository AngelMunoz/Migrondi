namespace Migrondi.Tests.Migrondi

open System
open System.IO

open Microsoft.VisualStudio.TestTools.UnitTesting

open RepoDb

open FsToolkit.ErrorHandling

open Migrondi.Core
open Migrondi.Core.Serialization
open Migrondi.Core.FileSystem
open Migrondi.Core.Database
open Migrondi.Core.Migrondi

[<TestClass>]
type MigrondiUpServiceTests() =


  [<TestMethod>]
  member _.``RunUp should apply all pending migrations``() = ()

[<TestClass>]
type MigrondiDownTests() =


  [<TestMethod>]
  member _.``RunDown should rollback all migrations``() = ()