module Migrondi.UI.Views.ProjectSettings

open System

open Goo
open Goo.Widgets
open Goo.Widgets.Inputs
open Goo.Widgets.Layout
open FunGoo.Children
open FunGoo.Interop
open FunGoo.Style
open FunGoo.Widgets

open IcedTasks
open IcedTasks.Polyfill.Async.PolyfillBuilders

open Migrondi.Core
open MigrondiUI
open MigrondiUI.Services
open Migrondi.UI.Types
open Migrondi.UI.Views.Common

// Data-only input: a theme flip changes the snapshot, so the mounted
// settings section repaints with its form state intact.
type SettingsInput = { Dark: bool }

// The settings state: one mutable field per concern. The cell reads it
// inside Build; the transition functions below replace whole values.
type SettingsState = {
  mutable Name: string
  mutable Description: string
  mutable Connection: string
  mutable TableName: string
  mutable Driver: string
  mutable Expanded: bool
  mutable Notice: string
  mutable Busy: bool
}

// One event channel for the settings actions; the cell dispatches it.
type SettingsEvent =
  | Save
  | Delete
  | Export

let saved(state: SettingsState) =
  state.Notice <- "Project saved"
  state.Busy <- false

let failed(state: SettingsState, context: string, ex: exn) =
  state.Notice <- $"{context} failed: {ex.Message}"
  state.Busy <- false

// Soft-delete with confirmation. Call from a Build-authored callback: the
// confirm flow runs its continuation on the UI thread. Works for local and
// virtual projects; the landing list refreshes after the navigation.
let deleteProject<'env
  when 'env :> IProvideProjects
  and 'env :> IProvidePostUI
  and 'env :> IProvideFeedback
  and 'env :> IProvideNavigator>
  (cell: Cell, env: 'env, project: Project)
  : unit =
  env.Feedback.Confirm(
    $"Delete {project.Name}?",
    "Delete",
    fun () ->
      asyncEx {
        let projectId =
          match project with
          | Local p -> p.id
          | Virtual p -> p.projectId

        let! result = env.Projects.DeleteProject(projectId, DeleteKind.Soft)

        env.PostUI.Post(fun () ->
          match result with
          | Ok() ->
            env.Feedback.Toast $"Deleted {project.Name}"
            Navigation.go(env.Navigator, "/")
          | Error ProjectDeleteError.NotFound ->
            env.Feedback.Toast "Project not found"

          | Error ProjectDeleteError.HasAppliedMigrations ->
            env.Feedback.Toast(
              "Cannot delete a project with applied migrations"
            )

          cell.Rebuild())
      }
      |> Async.Start
  )

let saveSettings<'env
  when 'env :> IProvideProjects
  and 'env :> IProvidePostUI
  and 'env :> IProvideFeedback>
  (cell: Cell, env: 'env, state: SettingsState, project: VirtualProject)
  : unit =
  asyncEx {
    state.Busy <- true

    try
      let description = state.Description.Trim()

      let updated: VirtualProject = {
        project with
            name = state.Name.Trim()
            description =
              if String.IsNullOrWhiteSpace description then
                None
              else
                Some description
            connection = state.Connection.Trim()
            tableName = state.TableName.Trim()
            driver = MigrondiDriver.FromString state.Driver
      }

      do! env.Projects.UpdateVirtual updated

      env.PostUI.Post(fun () ->
        saved(state)
        env.Feedback.Toast $"Saved {updated.name}"
        cell.Rebuild())
    with ex ->
      env.PostUI.Post(fun () ->
        failed(state, "Saving the project", ex)
        env.Feedback.Toast $"Save failed: {ex.Message}"
        cell.Rebuild())
  }
  |> Async.Start

let exportProject<'env
  when 'env :> IProvideProjects
  and 'env :> IProvidePostUI
  and 'env :> IProvideFeedback
  and 'env :> IProvideFilePicker>
  (cell: Cell, env: 'env, project: VirtualProject)
  : unit =
  asyncEx {
    let! picked = env.FilePicker.PickDirectory()

    match picked with
    | Some folder ->
      let! rootPath = env.Projects.Export(project.id, folder)

      env.PostUI.Post(fun () ->
        env.Feedback.Toast $"Exported to {rootPath}"
        cell.Rebuild())
    | None -> ()
  }
  |> Async.StartImmediate

// A settings section the details page mounts: the abstract member keeps the
// cell an object expression while the page feeds the appearance through
// `SetDark` and the form state survives theme flips.
[<AbstractClass>]
type ProjectSettingsCell() =
  inherit Cell()

  /// Repaints the section for the appearance; the form state survives.
  abstract SetDark: bool -> unit

// The collapsible management section for virtual projects. The fields own
// their drafts, so typing a connection string never repaints the section.
let create<'env
  when 'env :> IProvideProjects
  and 'env :> IProvidePostUI
  and 'env :> IProvideTheme
  and 'env :> IProvideFeedback
  and 'env :> IProvideFilePicker
  and 'env :> IProvideNavigator>
  (env: 'env, project: VirtualProject)
  : ProjectSettingsCell =
  let state = {
    Name = project.name
    Description = project.description |> Option.defaultValue ""
    Connection = project.connection
    TableName = project.tableName
    Driver = project.driver.AsString
    Expanded = false
    Notice = ""
    Busy = false
  }

  // The appearance the section paints with; the details page keeps it in
  // sync through `SetDark`.
  let mutable dark = env.Theme.Store.Dark

  // Created once at cell birth: the mounted driver picker input carries the
  // same delegate instance on every Build, and the fields keep their drafts.
  let onDriverSelect = fun id -> state.Driver <- id

  let nameField =
    textField("Project name", state.Name, fun value -> state.Name <- value)

  let descriptionField =
    textField(
      "Description (optional)",
      state.Description,
      fun value -> state.Description <- value
    )

  let connectionField =
    textField(
      "Connection string",
      state.Connection,
      fun value -> state.Connection <- value
    )

  let tableField =
    textField(
      "Table name",
      state.TableName,
      fun value -> state.TableName <- value
    )

  {
    new ProjectSettingsCell() with
      override this.SetDark(next: bool) =
        if dark <> next then
          dark <- next
          this.Rebuild()

      override this.Build() : Blob =
        let palette = Palette.forTheme dark

        let onEvent =
          function
          | Save -> saveSettings(this, env, state, project)
          | Delete -> deleteProject(this, env, Virtual project)
          | Export -> exportProject(this, env, project)

        let form =
          Container(
            Key = "project-settings-form",
            FlexDirection = FlexDirection.Column,
            Gap = 8,
            Padding = 12,
            BackgroundColor = palette.Panel,
            BorderRadius = 8,
            BorderWidth = edgesFromFloat 1.0,
            BorderColor = palette.Border
          )
            .Children(
              Container(FlexDirection = FlexDirection.Row, Gap = 8)
                .Children(
                  Container(FlexGrow = 1.0, MinWidth = 0)
                    .Children(
                      mountField("settings-name", nameField, state.Name)
                    ),
                  actionButton(palette, "Save", fun () -> onEvent Save)
                ),
              mountField(
                "settings-description",
                descriptionField,
                state.Description
              ),
              mountField(
                "settings-connection",
                connectionField,
                state.Connection
              ),
              mountField("settings-table", tableField, state.TableName),
              Cell.Mount<ComboBoxInput, ComboBox>(
                "project-settings-driver",
                ComboBoxInput(
                  Items = [|
                    ComboBoxOption(Id = "sqlite", Label = "sqlite")
                    ComboBoxOption(Id = "mssql", Label = "mssql (SQL Server)")
                    ComboBoxOption(Id = "postgresql", Label = "postgresql")
                    ComboBoxOption(Id = "mysql", Label = "mysql")
                  |],
                  SelectedId = state.Driver,
                  OnSelect = onDriverSelect
                )
              ),
              Container(FlexDirection = FlexDirection.Row, Gap = 8)
                .Children(
                  Text(
                    Content = state.Notice,
                    FontSize = 12,
                    Color = palette.TextMuted,
                    FlexGrow = 1.0
                  ),
                  actionButton(palette, "Export...", fun () -> onEvent Export),
                  actionButton(
                    palette,
                    "Delete project",
                    fun () -> onEvent Delete
                  )
                )
            )

        let content = form :> Blob

        Disclosure(
          Label = "Project settings",
          Expanded = state.Expanded,
          OnExpandedChange = (fun expanded -> state.Expanded <- expanded),
          BackgroundColor = Nullable palette.Row,
          TextColor = Nullable palette.Text,
          BorderColor = Nullable palette.Border,
          ContentPadding = Nullable 0.0,
          Content = content
        )
          .Build()
  }
