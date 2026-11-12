module MigrondiUI.OsOperations

open System.Runtime.InteropServices

open CliWrap
open CliWrap.Buffered

open System.Threading.Tasks

open IcedTasks



type TargetPlatform =
  | Windows
  | Linux
  | MacOS


let GetTarget() =
  if RuntimeInformation.IsOSPlatform(OSPlatform.Windows) then
    Windows
  elif RuntimeInformation.IsOSPlatform(OSPlatform.Linux) then
    Linux
  elif RuntimeInformation.IsOSPlatform(OSPlatform.OSX) then
    MacOS
  else
    failwith "Unsupported OS platform"


let OpenDirectory(path: string) = asyncEx {
  let command =
    match GetTarget() with
    | Windows ->
      Cli
        .Wrap("explorer.exe")
        .WithArguments(path)
        .WithValidation(CommandResultValidation.None)
        .ExecuteBufferedAsync()
    | Linux ->
      Cli
        .Wrap("xdg-open")
        .WithArguments(path)
        .WithValidation(CommandResultValidation.None)
        .ExecuteBufferedAsync()
    | MacOS ->
      Cli
        .Wrap("open")
        .WithArguments(path)
        .WithValidation(CommandResultValidation.None)
        .ExecuteBufferedAsync()

  do! command.Task :> Task
}
