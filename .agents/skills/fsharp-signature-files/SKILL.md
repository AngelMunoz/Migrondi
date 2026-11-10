---
name: fsharp-signature-files
description: Add F# signature files (.fsi) that expose only the API that callers use, and hide type representations so callers must use constructor functions. Covers generating a starting point with --allsigs or --sig, removing helpers from the .fsi, abstract types, attribute and interface mismatches (FS3888, FS0050), signature errors (FS0034, FS0193, FS0238), XML docs, InternalsVisibleTo for tests, and a public-surface check of the built assembly. Use when the user asks for signature files, a smaller public API, encapsulation of an F# library, or hidden constructors.
compatibility: The proof and the public-surface script require the .NET 10 SDK and PowerShell 7.4 or later. Results were verified with SDK 10.0.401 on win-x64. FS3888 comes from the F# 10 compiler.
---

# F# signature files

A `.fsi` file is the contract of its `.fs` file. A value or type that is not in the
`.fsi` is hidden from every later file and every other assembly. A type that the `.fsi`
declares with no body (`type Email`) hides its fields and cases.

The compiler can write a `.fsi` for you, but that output repeats every public member.
It hides nothing. The work is to remove what callers do not use.

Build time can also improve. Later files type-check against the `.fsi`, not the
`.fs`, so graph-based type checking can check them in parallel with the implementation.
Measure this before you claim a speed-up. See
[optimize-fsharp-typecheck-graph](../optimize-fsharp-typecheck-graph/SKILL.md).

## Steps

1. **Record the public surface before the change.** Build the library and run:

   ```powershell
   pwsh ./scripts/public-surface.ps1 -Assembly <bin>/<Lib>.dll -OutFile before.txt
   ```

2. **Generate a starting point.** Build once with `--allsigs`. It writes one `.fsi`
   next to each `.fs`, with doc comments and attributes:

   ```powershell
   dotnet build <Lib>.fsproj -p:OtherFlags=--allsigs
   ```

   Build the library project file, not the solution. The global property also reaches
   each project that the library references, and it replaces an `OtherFlags` value
   that the project sets. When the project sets `OtherFlags`, add `--allsigs` to that
   value for one build instead. `--sig:<file>` writes all signatures into one file,
   which you must then split by hand. Remove the flag after this build. Delete each
   `.fsi` that the build wrote outside the library, and the `.fsi` files under `obj/`
   for generated files such as `AssemblyAttributes.fs`.
3. **Add each `.fsi` to the project directly before its `.fs`:**

   ```xml
   <Compile Include="Email.fsi" />
   <Compile Include="Email.fs" />
   ```

   A `.fsi` after its `.fs` gives FS0238 and FS0248.
4. **Decide what is truly public.** Search the other projects, the tests, and the
   documented entry points for each member in the `.fsi`. Delete each `val`, type, and
   member that no other project uses. Do not mark it `private` in the `.fs` instead: the
   omission from the `.fsi` is the hiding.
   - A member that only tests use: write `val internal f: ...` in the `.fsi` and add
     `<InternalsVisibleTo Include="<Tests assembly name>" />` to the library project.
     Do not make it public for the tests.
   - The generated `.fsi` also lists `val private` values. Delete them.
5. **Hide representations where callers must not build values.** Replace the record or
   union body in the `.fsi` with the bare type name and keep the constructor function:

   ```fsharp
   /// An email address that contains one '@'.
   type Email

   module Email =
       val create: input: string -> Result<Email, string>
       val value: email: Email -> string
   ```

   Callers then get FS0039 for `{ Value = ... }` or a union case. Keep a record public
   when every combination of its field values is valid. For the choice of type and
   constructor, use [parse-dont-validate](../parse-dont-validate/SKILL.md).
6. **Fix the traps.** Read [references/TRAPS.md](references/TRAPS.md) for the
   diagnostics. The most important rules:
   - Copy each attribute on a type or value to the `.fsi`: `[<RequireQualifiedAccess>]`,
     `[<Struct>]`, `[<Literal>]`. Do not add an attribute that the `.fs` does not have:
     `[<Sealed>]` on a class in the `.fsi` only gives FS0297. A missing `RequireQualifiedAccess` gives
     only warning FS3888. The build passes, and callers can use the cases without the
     type name.
   - List each implemented interface: `interface System.IDisposable`. Without it, you
     get warning FS0050, and `use` in a caller fails with FS0193.
   - Put XML doc comments in the `.fsi`. When a `.fsi` exists, a doc comment that is
     only in the `.fs` is not in the XML documentation file.
   - Write each `val` with the same generic arity and `inline` flag as the `.fs`.
7. **Check the result.**
   - Build every project and run the tests. Treat FS3888 and FS0050 warnings as
     errors to fix.
   - Run `public-surface.ps1` again and compare with `before.txt`. Each removed line
     must be a member that no caller uses. No line may be added.
   - Write a small probe file in a consumer that builds a hidden record or union
     directly. It must fail to compile. Then delete it.

## Verify

Run the bundled proof from the skill root:

```powershell
pwsh ./scripts/run.ps1
```

The runner builds [scripts/Sample](scripts/Sample) and checks its public surface, its XML
documentation, and an `InternalsVisibleTo` test project. It builds
[scripts/Generate](scripts/Generate) with `--allsigs` and checks the generated `.fsi`.
It then builds each folder in [scripts/breaks](scripts/breaks) and checks the diagnostic
codes in its `// expect:` lines.

These rules come from the F# documentation and are not bundled proofs: a `val inline`
in a `.fsi` makes the function body part of the contract, so callers must build again
when the body changes. Use `inline` in a `.fsi` only when the function needs it, for
example for statically resolved type parameters.
