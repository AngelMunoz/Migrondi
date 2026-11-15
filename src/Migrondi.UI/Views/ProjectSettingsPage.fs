module Migrondi.UI.Views.ProjectSettingsPage

open System
open System.IO

open Goo
open Goo.Widgets.Layout
open FunGoo.Children
open FunGoo.Interop
open FunGoo.Style

open FsToolkit.ErrorHandling

open IcedTasks
open IcedTasks.Polyfill.Async.PolyfillBuilders

open MigrondiUI
open Migrondi.UI.Types
open Migrondi.UI.Views.Common
open Migrondi.UI.Views.ProjectSettings

// Settings is its own screen: the project screen stays about migrations, and
// the one destructive action lives where it cannot be hit by accident.
let create<'env
  when 'env :> IProvideProjects
  and 'env :> IProvidePostUI
  and 'env :> IProvideTheme
  and 'env :> IProvideFeedback
  and 'env :> IProvideFilePicker
  and 'env :> IProvideNavigator>
  (env: 'env, project: Project)
  : Cell =
  let backUrl =
    match project with
    | Local p -> Routes.localProjectUrl p.id
    | Virtual p -> Routes.virtualProjectUrl p.id

  let title, path =
    match project with
    | Local p -> p.name, p.migrondiConfigPath
    | Virtual p -> p.name, p.connection

  // Created once, beside the cell it mounts: the settings section keeps its
  // form state across rebuilds and feeds on `SetDark` for theme flips.
  let settings =
    match project with
    | Virtual vp -> ValueSome(ProjectSettings.create(env, vp))
    | Local _ -> ValueNone

  let localDetails(local: LocalProject, palette: Palette) : Blob =
    let rows =
      match local.config with
      | None -> [|
          Text(
            Content = "No valid migrondi.json found",
            FontSize = 12,
            Color = palette.TextMuted
          )
          :> Blob
        |]
      | Some config ->
        let migrationsDir =
          option {
            let! root =
              local.migrondiConfigPath |> Path.GetDirectoryName |> Option.ofObj

            return Path.Combine(root, config.migrations)
          }
          |> Option.defaultValue config.migrations

        [|
          Text(
            Content = $"Connection: {config.connection}",
            FontSize = 12,
            Color = palette.TextMuted
          )
          :> Blob
          Text(
            Content = $"Driver: {config.driver}",
            FontSize = 12,
            Color = palette.TextMuted
          )
          :> Blob
          Text(
            Content = $"Migrations: {migrationsDir}",
            FontSize = 12,
            Color = palette.TextMuted
          )
          :> Blob
        |]

    Container(
      FlexDirection = FlexDirection.Column,
      Gap = 12,
      Padding = 20,
      Width = 640.0,
      BackgroundColor = palette.Panel,
      BorderRadius = 10
    )
      .Children
      [|
        yield! rows
        Container(FlexDirection = FlexDirection.Row, Gap = 8)
          .Children(
            actionButton(
              palette,
              "Open folder",
              fun () ->
                asyncEx {
                  match
                    local.migrondiConfigPath
                    |> Path.GetDirectoryName
                    |> Option.ofObj
                  with
                  | Some root ->
                    try
                      do! Migrondi.UI.OsOperations.OpenDirectory root
                    with ex ->
                      env.Feedback.Toast $"Open folder failed: {ex.Message}"
                  | None ->
                    env.Feedback.Toast "Could not resolve the project folder"
                }
                |> Async.Start
            )
          )
      |]

  {
    new Cell() with
      override this.Build() : Blob =
        let dark = env.Theme.Store.Dark
        let palette = Palette.forTheme dark

        let body =
          match settings, project with
          | ValueSome section, _ ->
            section.SetDark dark

            Cell.Mount(
              Func<Cell>(fun () -> section :> Cell),
              "project-settings"
            )
          | ValueNone, Local local -> localDetails(local, palette)
          | _ -> Container(Height = 0.0)

        let danger =
          Container(FlexDirection = FlexDirection.Row, Gap = 8)
            .Children(
              dangerButton(
                palette,
                "Delete project…",
                false,
                (fun () -> deleteProject(this, env, project))
              )
            )

        Container(
          Key = "project-settings-page",
          FlexDirection = FlexDirection.Column,
          Gap = 20,
          Padding = 24,
          Width = percent 100.0,
          Height = percent 100.0,
          BackgroundColor = palette.Canvas
        )
          .Children(
            pageHeader(
              palette,
              $"{title} · settings",
              path,
              [|
                actionButton(
                  palette,
                  "Back to project",
                  fun () -> Navigation.go(env.Navigator, backUrl)
                )
              |]
            ),
            body,
            danger
          )
  }
