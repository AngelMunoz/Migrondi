---
name: goo-fsharp
description: Author F# applications on the Goo retained GUI framework through the FunGoo bindings. Covers the Cell/Build/Blob model, the owner rule for state refresh, data-only props records with callbacks as parameters, local mutable state, stores for shared state, env-capability services with a PostUI marshaller, the composition-root skeleton, mount identity and keys, and the F#/Goo interop friction families. Use whenever you write, review, or debug F# code that opens Goo or FunGoo, subclasses Cell, overrides Build, mounts a child Cell, or constructs a Window — also when the user says Goo, FunGoo, Cell, Blob, mount, rebuild, or Goo window.
compatibility: Requires the .NET 10 SDK and a project referencing Goo. Goo is written in G# and compiled to ordinary .NET IL; F# consumes it directly. FunGoo supplies thin generated bindings.
---

# Goo from F#

Goo is a retained GUI tree with a React-style reconciler, rendered on Vulkan, laid out with
flexbox. F# code declares trees; Goo diffs, lays out, and renders them. This skill holds the
programming model and the application architecture. For the control library, read
`goo-widgets-fsharp` after this one. A complete worked application lives in
`samples/RaznorGoo` in this repository.

## Mental model

- A component is a `Cell`. Its `Build()` override returns a `Blob` tree (`Container`, `Text`,
  `Button`, `Shape`). `Build` runs only when the Cell is dirty — not every frame.
- Blobs are declarations. Goo diffs them against retained nodes, then discards them. A fresh
  tree per `Build` is the design, so declaring the tree allocates by design. Extra passes,
  closure churn, and per-row formatting are not part of that budget.
- The reconciler reuses a retained node when the kind and the key match. Unkeyed children
  match by sibling index. Never mix keyed and unkeyed siblings under one parent.
- The owner rule: a callback authored inside a Cell's `Build` runs, then Goo rebuilds that
  Cell. State owned by the Cell needs no manual refresh.
- `Rebuild()` refreshes a _different_ Cell. It throws when called during `Build`.
- One process-wide UI thread, no synchronization context. Background code returns through
  `Window.TryPost`.
- Layout is CSS flexbox as style properties: `FlexDirection`, `Gap`, `FlexGrow`, `Padding`,
  `Overflow`. There are no constraint objects.
- `Style` is a write-once declaration sink: each init-only property pushes an entry.
  `BasedOn` copies another style's entries; assign it before overrides.

## Authoring a Cell

Two independent choices: the authoring style, and whether the cell takes a typed input.

**Authoring style.** An object expression and a class are equal in power: the factory's
parameters play the role of constructor arguments, and closure variables play the role of
private fields — including mutable state. Neither is "more F#" or safer. Choose the class
form for one reason only: other code must call members on the cell.

```fsharp
// Object expression — the default. State lives in the closure.
let counterCell () : Cell =
    let mutable count = 0
    { new Cell() with
        override _.Build() : Blob =
            Container(Gap = 8)
                .Children(
                    Text(Content = $"Count: {count}"),
                    Button(OnClick = fun () -> count <- count + 1)
                        .Children(Text(Content = "Add one"))) }
```

```fsharp
// Class form — only because `Navigate` must be callable from outside.
type ShellCell(env: Env) =
    inherit Cell()
    let mutable route = Route.boot env.SignedIn

    member _.Navigate(target: Route) =
        route <- Route.guard env.SignedIn target
        // Navigate runs from this cell's own callbacks, so the owner
        // rule rebuilds it. Call this.Rebuild() only from outside.
```

**Typed input.** A plain cell owns all its state. A typed cell `Cell<'TInput>` receives an
immutable data snapshot from its parent and rebuilds when
`EqualityComparer<'TInput>.Default` reports the snapshot changed. This choice is
independent of the authoring style — the object expression takes it too:

```fsharp
type LogPageInput = { Log: LogKind; Commits: Commit[] }   // data only

let logCell () : Cell =
    { new Cell<LogPageInput>() with
        override _.Build(input: LogPageInput) : Blob = ... }
```

Rules:

- Keep the input a data-only record so that comparison is cheap and precise.
- `Build` does no I/O, blocks nothing, calls no `Rebuild`.
- An annotated `Blob` return upcasts a `Container`; inside child lists a derived Blob needs
  no cast at member calls.

## Data in records, behavior in parameters

The central rule. Props, input, and store records carry data only — never functions.
A function field poisons record equality: two closures are never equal, so a typed-input
Cell with a callback field rebuilds on every parent build, and a store with functions is a
service in disguise.

| Placement           | Shape                                                                     | Why                                                                         |
| ------------------- | ------------------------------------------------------------------------- | --------------------------------------------------------------------------- |
| Inline build helper | `let inline clickable (id, child, [<InlineIfLambda>] onClick)`            | Erases into the caller. No closure, no delegate.                            |
| View function       | `let commitCard (p: CardProps, onEvent: CardEvent -> unit) : Blob`        | Tupled, two parameters. The callback value comes from a Cell field.         |
| Cell constructor    | `type LogCell(onEvent: CardEvent -> unit) = inherit Cell<LogPageInput>()` | Stable delegate identity for free, in a field.                              |
| Widget input struct | `MediaTransportInput(OnPlayPause = cachedDelegate)`                       | G#'s design. Cache the delegate outside `Build` (see `goo-widgets-fsharp`). |

Events travel as one DU over one callback. One channel beats a delegate zoo: extensible,
no arity churn, pattern-matched handlers.

```fsharp
type CardEvent =
    | Opened of Commit
    | ReplyToggled of Commit
    | MenuOpened of Commit

[<Struct; NoComparison>]
type CardProps = { Commit: Commit; ViewerId: string; Busy: bool }

let commitCard (p: CardProps, onEvent: CardEvent -> unit) : Blob = ...
```

- Arguments are tupled by default; curry only when a call site partially applies the
  function. Three parameters or fewer, counting `env`; past three, move the data into one
  named record. Mark callbacks `[<InlineIfLambda>]` wherever the helper is `inline`. The
  companion skills below carry the full rules.
- Some F# style guides default callbacks into record fields. This skill overrides that for
  props, input, and store records. Do not fix a data-only record by adding a callback
  field.
- Small short-lived props go `[<Struct; NoComparison>]`. Keep generated equality on mounted
  input records; drop it (`NoEquality`) only on records that are never compared.

## State ladder

| Tier | State kind                        | Lives in                               | Refresh                                |
| ---- | --------------------------------- | -------------------------------------- | -------------------------------------- |
| 1    | Ephemeral UI (open, hover, draft) | `let mutable` in the Cell              | Owner rule. Write nothing.             |
| 2    | Page state, one owner             | Cell private fields                    | Owner rule.                            |
| 3    | Shared domain state               | Store at the composition root          | Write the slice, rebuild reader cells. |
| 4    | Configuration                     | Validated settings capability in `env` | Read-only.                             |

- Local mutable state is fine. A Cell is a stateful class by design.
- State never lives in the Blob tree. Blobs are discarded after each diff.
- A handler may write any store slice. It must rebuild every _other_ cell that reads the
  changed slice. The owner rule covers its own cell.
- Cross-cell refresh flows through callback parameters injected at the composition root.
  Cells never hold sibling references:

  ```fsharp
  let bottomBar = BottomBar.create (env, player, fun () -> playlist.Rebuild())
  ```

- Rebuild the smallest subtree. One mounted Cell per refresh region.

## Stores

One immutable value per concern, in one mutable field per concern. Data only.

```fsharp
type Playlist = { Songs: Song array; Selected: SongId voption }

type Store =
    { mutable Playlist: Playlist
      mutable Session: Session }
```

- Write by replacing, never by mutating inside the value:
  `store.Playlist <- { store.Playlist with Songs = loaded }`.
- The store knows no Cell, no Window, no rebuild.
- Cells read it inside `Build`.
- All background wiring lives in the composition root subscription block, in one order:
  marshal, write, rebuild.

```fsharp
playback.Subscribe
    { OnLoaded = fun songs ->
        env.PostUI.Post(fun () ->
            store.Playlist <- { store.Playlist with Songs = songs }
            mediaList.Rebuild()) }
```

## Services: env capabilities plus PostUI

Dependencies travel as capabilities. One worker interface holds the operations; one
accessor interface named `IProvideX` exposes the worker as a single property. Functions
take `env` first and name only the accessors they use; composite functions leave `env`
unannotated so inference collects the union of capabilities. A test env implements only the
capabilities a call tree names. Validated settings are one more capability. The full
pattern is `fsharp-env-capabilities` in the companion skills below.

One Goo addition. The Window is not a service; the marshaller is:

```fsharp
type IPostUI =
    abstract Post: (unit -> unit) -> unit

type UiMarshaller() =
    let mutable window: Window voption = ValueNone

    let post(action: unit -> unit) =
        window
        |> ValueOption.iter(fun window ->
            window.TryPost(fun () -> action()) |> ignore)

    member _.Attach(w: Window) = window <- ValueSome w

    member _.Post(action: unit -> unit) = post action

    interface IPostUI with
        member _.Post action = post action
```

- Every service event and every background task returns through `env.PostUI.Post`.
- The mutable window slot is the app's single seam. It lives inside the marshaller and
  nowhere else.
- No capability exposes a `Window`, a `Cell`, or a `Blob`.
- A factory returns one interface. A tuple of `worker * looseFunction` leaks half the
  service's surface out of its contract — consumers hold two artifacts for one service,
  and no other component can reach the function through `env`. Fold the function in as a
  member.
- A service that raises events on foreign threads takes the marshaller at creation and
  delivers its handlers on the UI thread, so no subscriber can forget.
  `Subscribe(handlers)` is a member of the worker interface; the handlers record is an
  argument carrier wired once at the composition root, not a props record.
- When one value must satisfy several accessors at once — a widget factory whose `env`
  needs two capabilities — declare a bundle interface (`type IAppEnv = inherit
IProvidePlayback inherit IProvidePostUI`) and build the production env as that
  interface. An object expression's type is its first interface, so a value implementing
  several capabilities one-by-one fails such constraints.

## Composition root

Every app reads the same, in this order:

1. `Window.ConfigureApplication(name, version, identifier)`.
2. Build the services and the `UiMarshaller`.
3. Parse settings; stop the app on error.
4. Build `env` (capabilities, including `IPostUI`).
5. Build the stores.
6. Create the cells: `create (env, store, callbacks)` — `env` first.
7. Build the root view Cell; mount children with stable keys.
8. Create `Window(Title = ..., Root = root)`; `marshaller.Attach(window)`.
9. Subscribe to service events: handler → `PostUI.Post` → store write → target `Rebuild()`.
10. `window.Run()`.

Step 8 resolves the one circularity: `Root` is init-only, so the window is built last and
attached to the marshaller the cells already hold.

## Mounting and keys

- Mount a child Cell by factory plus key: `Cell.Mount<Cell>(playlistFactory, "playlist")`.
- Mount a typed cell with the two-generic form:
  `Cell.Mount<LogPageInput, LogCell>("page-log", input)`.
- Identity is the declared type plus the sibling key. A changed key unmounts and remounts.
- Dynamic lists need stable keys: `Key = $"song-{song.Id}"`. Index keys break on reorder.
- Create the factory once, beside the cell it builds. A factory written inside the parent
  `Build` allocates a fresh closure every rebuild:

  ```fsharp
  let playlistFactory = Func<Cell>(fun () -> playlist)   // once, composition root
  // inside the parent Build:
  Cell.Mount<Cell>(playlistFactory, "playlist")
  ```

## The interop boundary

F# friction families and their FunGoo answers:

| Friction                                                             | Rule                                                                                                                                                                                 |
| -------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| Child collections                                                    | Use the `.Children(...)` ParamArray extension (`FunGoo.Children`); it erases to `Add` calls. Never hand-convert an F# list to `IList<Blob>`.                                         |
| `op_Implicit` (int → `Length`, `Length` → `Edges`, string → `Color`) | F# applies no implicit conversions. Use named factories `Length.Pixels`, `Length.Percent`, `Color.Rgb`, or `FunGoo` converters `lengthFromFloat`, `edgesFromFloat`, `percent 100.0`. |
| Nullable value fields and arguments                                  | Pass by name: `Padding = Nullable 20.`. Nullable struct fields take generated optional setters that accept `'T voption`.                                                             |
| `Try*` with `out`                                                    | Returns a tuple: `match handle.TryGetTextPositionAt(p, space) with true, pos -> ...`. FunGoo.Interop wraps these as `voption`.                                                       |
| Window events                                                        | Explicit accessors: `window.add_FocusChanged handler`, `window.remove_FocusChanged handler`.                                                                                         |
| Mutable class setters                                                | Fluent `FunGoo.Setters` extensions.                                                                                                                                                  |

Pass-through, no wrapper needed: constructors with named properties, CLR enums, `Color`
factories, `Cell`, `Window.Run`. In the FunGoo repo, wrappers are generated by
`codegen/Generate.fsx` — extend the generator, never the generated files.

## Domain modeling and performance

- Domain types import nothing from Goo. Model routes and transitions as pure functions:
  a `Route` DU plus a total `guard` function. Unit-test them with no window.
- Props records translate domain to view. Keep them small; `[<Struct>]` when they are
  short-lived carriers, reference records when large.
- `voption` on the F# side. `Nullable` only at the Goo boundary.
- Bulk data: arrays with stable string keys. No `seq` materialized per build.
- Per-row builders: one pass, `inline` + `[<InlineIfLambda>]`, cached delegates, no
  formatting pipeline when the domain already holds the string.
- Budget is the measurement: `GC.GetAllocatedBytesForCurrentThread()` around one `Build`,
  asserted under a byte ceiling. Goo.Widgets ships such contract tests; hold app cells to
  the same discipline.
- `Build()` runs without a window, so views test headless. Reading children out of a
  returned Blob sometimes needs a downcast (`:?>`) — keep casts in tests, never in
  production code — and fail with a message, not a printed flag:

```fsharp
let tree = cell.Build() :?> Container
let first = tree.Children[0] :?> Text

if first.Content <> "Count: 0" then
    invalidOp $"headless check failed, got: {first.Content}"
```

## Checklist for review

List each of these in the diff:

- A function field in a props, input, or store record.
- A curried view or `create` function with no partial-application call site.
- A delegate created inside `Build` for a mounted widget input.
- `Rebuild()` called on the cell that authored the handler — the owner rule already ran.
- A tree read, write, or `Rebuild()` on a background thread without `TryPost`.
- I/O inside `Build`.
- Keyed and unkeyed children mixed under one parent.
- An F# list hand-converted for `Children`.
- A formatting pipeline per row in a builder.
- A cell holding a sibling cell instead of an injected callback.

Each item: fix it, or state why it stays.

## Verify

In the target project:

1. `dotnet build` — the shapes above must compile as written.
2. Call each `Build()` headless and assert on the returned Blob tree, the way
   `Goo/samples/interop/FSharp` does under `--check`.
3. Measure one `Build()` per cell with `GC.GetAllocatedBytesForCurrentThread()` in a
   Release build, and record the number next to the cell.

## Companion skills

This skill is self-contained, but its parameter, environment, and performance conventions
come from `AngelMunoz/opinionated-fsharp-skills`, which in turn builds on
`TheAngryByrd/fsharp-skills`. When the user's machine lacks them, suggest installing:

```sh
npx skills add TheAngryByrd/fsharp-skills
npx skills add AngelMunoz/opinionated-fsharp-skills
```

Do not assume those skills are installed.
