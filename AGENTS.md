# AGENTS.md

Instructions for agentic coding assistants working on the Migrondi codebase.

## Project Overview

SQL migrations tool built in F# targeting .NET 8.0 and .NET 9.0. Uses F# with .fsi signature files, MSTest for testing, and Fantomas for formatting.

## Build and Test Commands

### Build
```bash
# Full build with FsMake
dotnet fsi build.fsx

# Direct dotnet build
dotnet build src/Migrondi/Migrondi.fsproj
dotnet build src/Migrondi.Core/Migrondi.Core.fsproj

# Build for specific runtime
dotnet fsi build.fsx build:runtime -- linux-x64
```

### Test
```bash
# Run all tests
dotnet test src/Migrondi.Tests --no-restore

# Run tests using FsMake
dotnet fsi build.fsx test

# Run single test
dotnet test src/Migrondi.Tests --filter "FullyQualifiedName=Namespace.ClassName.MethodName"

# Run tests for specific framework
dotnet test src/Migrondi.Tests -f net8.0 --no-restore
```

### Format and Lint
```bash
# Format all F# source files
dotnet fsi build.fsx format
# Or directly
dotnet fantomas format
```

### Restore
```bash
dotnet restore
```

### Run CLI
```bash
dotnet run --project src/Migrondi -- <command>
```

## Code Style

### Formatting (.editorconfig)
- Indentation: 2 spaces
- Max line length: 80 characters
- Trim trailing whitespace, LF (Unix) line endings
- Stroustrup-style multiline brackets
- Multiline lambdas close on new line

### Namespace/Module Conventions
- Core: `namespace Migrondi.Core` or `namespace Migrondi.Core.*`
- CLI: `namespace Migrondi.*`
- Tests: `namespace Migrondi.Tests.*`
- Internal modules: `module internal ModuleName`
- Private modules: `module private ModuleName`
- Auto-open: `[<AutoOpen>] module ModuleName`
- Qualified access: `[<RequireQualifiedAccess>]` on types/modules

### Import Order
1. System namespaces
2. Microsoft namespaces
3. Third-party (FsToolkit, IcedTasks, Thoth.Json)
4. Project namespaces (Migrondi.*)

### Type Conventions
- Records: PascalCase fields, anonymous records lowercase
- DUs: `[<RequireQualifiedAccess>]`, PascalCase cases
- Functions: camelCase (private), PascalCase (public API)
- Async: `IcedTasks`, `Async` suffix, optional `?cancellationToken`

### Error Handling
Use `FsToolkit.ErrorHandling`:
- `Validation<'T, 'E>` for multiple errors
- `Result<'T, 'E>` for single error
- Return errors as string lists

Define exceptions in `Library.fs`: `exception MyError of Context: string * Reason: string`

### Public API Guidelines
**Critical:** No F#-specific types for C#/VB interop (use `seq` not `list`, `T option` not `'T option`). Document with XML comments. Provide sync and async versions.

### Signature Files (.fsi)
Core library uses .fsi files before .fs in project order. Include full type signatures and XML docs.

### Testing (MSTest)
```fsharp
[<TestClass>]
type MyTests() =
  [<TestInitialize>]
  member _.Setup() = // setup code

  [<TestCleanup>]
  member _.Cleanup() = // cleanup code

  [<TestMethod>]
  [<DataRow("input1", "expected1")>]
  member _.``Test description``(input, expected) =
    Assert.AreEqual(expected, input)
```
- Descriptive names with backticks
- `[<DataRow>]` for parameterized tests
- `task { ... }` for async tests

### File Organization
**Migrondi.Core.fsproj:** Library.fsi/fs, Serialization.fsi/fs, FileSystem.fsi/fs, Database.fsi/fs, Migrondi.fsi/fs

**Migrondi.Tests.fsproj:** Library.fs, Serialization.fs, FileSystem.fs, Database.fs, Database.Async.fs, Migrondi.fs, Main.fs

### Common Patterns
- **URIs for paths:** Use `Uri(path, UriKind.Relative/Absolute)` internally
- **Logging:** `Microsoft.Extensions.Logging`, inject `ILogger<T>`, structured logging
- **Transactions:** Database transactions for migrations, rollback on failure

## CI/CD

GitHub Actions: `dotnet restore`, `dotnet build src/Migrondi -f net9.0 --configuration Release`, `dotnet test src/Migrondi.Tests --no-restore`

## Resources
- Fantomas: https://github.com/fsprojects/fantomas
- FsToolkit.ErrorHandling: https://github.com/fsprojects/FsToolkit.ErrorHandling
- IcedTasks: https://github.com/TheAngryByrd/IcedTasks
- MSTest: https://learn.microsoft.com/en-us/dotnet/core/testing/unit-testing-with-mstest
