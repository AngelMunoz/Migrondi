namespace Migrondi.Core.Configuration

open Migrondi.Core

type ConfigurationEnv =
  abstract member ParseConfiguration: string -> MigrondiConfig