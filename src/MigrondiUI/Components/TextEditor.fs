module MigrondiUI.Components.TextEditor

open Avalonia.Controls

open AvaloniaEdit
open AvaloniaEdit.TextMate
open TextMateSharp.Grammars

open NXUI.Extensions

open FSharp.Data.Adaptive
open Navs.Avalonia
open System.Text
open Avalonia.Styling

let DefaultOptions() =
  TextEditorOptions(
    ConvertTabsToSpaces = true,
    ShowSpaces = true,
    ShowTabs = true
  )

let get() =
  let editor =
    TextEditor()
      .Classes("TextEditor")
      .ShowLineNumbers(true)
      .Options(DefaultOptions())
      .Encoding(Encoding.UTF8)
      .SyntaxHighlighting(
        Highlighting.HighlightingManager.Instance.GetDefinition("sql")
      )

  editor.WordWrap <- true

  match TopLevel.GetTopLevel(editor) with
  | null ->
    match Avalonia.Application.Current with
    | null ->
      let options = RegistryOptions(ThemeName.LightPlus)

      editor
        .InstallTextMate(options)
        .SetGrammar(options.GetScopeByExtension(".sql"))
    | app ->
      let options: RegistryOptions =
        if app.ActualThemeVariant = ThemeVariant.Dark then
          RegistryOptions(ThemeName.DarkPlus)
        elif app.ActualThemeVariant = ThemeVariant.Light then
          RegistryOptions(ThemeName.LightPlus)
        else
          RegistryOptions(ThemeName.Monokai)

      editor
        .InstallTextMate(options)
        .SetGrammar(options.GetScopeByExtension(".sql"))
  | topLevel ->
    let options: RegistryOptions =
      if topLevel.ActualThemeVariant = ThemeVariant.Dark then
        RegistryOptions(ThemeName.DarkPlus)
      elif topLevel.ActualThemeVariant = ThemeVariant.Light then
        RegistryOptions(ThemeName.LightPlus)
      else
        RegistryOptions(ThemeName.Monokai)

    editor
      .InstallTextMate(options)
      .SetGrammar(options.GetScopeByExtension(".sql"))

  editor

type TxtEditor =

  static member Readonly(text: string) =
    let editor = get()
    editor.Document.Text <- text
    editor.IsReadOnly <- true
    editor

  static member Readonly(text: string aval) =
    let editor = get()
    editor.IsReadOnly <- true
    text.AddCallback(fun text -> editor.Document.Text <- text) |> ignore
    editor

  static member ReadWrite(text: string cval, ?readonly: bool) =
    let editor = get()
    // set initial text
    editor.Document.Text <- AVal.force text
    readonly |> Option.iter(fun r -> editor.IsReadOnly <- r)
    // only update the text when the value changes from the editor
    editor.Document.UpdateFinished
    |> Observable.add(fun _ -> text.setValue editor.Document.Text)

    editor
