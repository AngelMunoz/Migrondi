namespace Migrondi.Mcp

module McpResults =

  type LocalProjectSummary = {
    id: System.Guid
    name: string
    description: string
    configPath: string
    hasValidConfig: bool
  } with

    static member FromLocalProject:
      p: MigrondiUI.LocalProject -> LocalProjectSummary

    static member Encoder: JDeck.Encoder<LocalProjectSummary>

  type VirtualProjectSummary = {
    id: System.Guid
    name: string
    description: string
    driver: string
    tableName: string
  } with

    static member FromVirtualProject:
      p: MigrondiUI.VirtualProject -> VirtualProjectSummary

    static member Encoder: JDeck.Encoder<VirtualProjectSummary>

  type ListProjectsResult = {
    local: LocalProjectSummary list
    virtualProjects: VirtualProjectSummary list
  } with

    static member Empty: ListProjectsResult

    static member Encoder: JDeck.Encoder<ListProjectsResult>

  /// Config surface reported by the multi-project `get_project` tool for a
  /// local project. Hosts must never put connection strings in tool
  /// results; the CLI `get_project` already obeys this.
  type ProjectConfigOutput = {
    migrations: string
    tableName: string
    driver: string
  } with

    static member FromConfig:
      c: Migrondi.Core.MigrondiConfig -> ProjectConfigOutput

    static member Encoder: JDeck.Encoder<ProjectConfigOutput>

  type LocalProjectDetail = {
    id: System.Guid
    name: string
    description: string
    configPath: string
    config: ProjectConfigOutput option
    kind: string
  } with

    static member FromLocalProject:
      p: MigrondiUI.LocalProject -> LocalProjectDetail

    static member Encoder: JDeck.Encoder<LocalProjectDetail>

  /// Detail reported by the multi-project `get_project` tool for a virtual
  /// project. Hosts must never put connection strings in tool results; the
  /// CLI `get_project` already obeys this.
  type VirtualProjectDetail = {
    id: System.Guid
    name: string
    description: string
    driver: string
    tableName: string
    projectId: System.Guid
    kind: string
  } with

    static member FromVirtualProject:
      p: MigrondiUI.VirtualProject -> VirtualProjectDetail

    static member Encoder: JDeck.Encoder<VirtualProjectDetail>

  type GetProjectResult =
    | LocalProject of LocalProjectDetail
    | VirtualProject of VirtualProjectDetail
    | ProjectNotFound of string

    static member Encoder: JDeck.Encoder<GetProjectResult>

  type MigrationStatusOutput = {
    name: string
    timestamp: int64
    status: string
    fullName: string
  } with

    static member FromMigrationStatus:
      status: Migrondi.Core.MigrationStatus -> MigrationStatusOutput

    static member Encoder: JDeck.Encoder<MigrationStatusOutput>

  type ListMigrationsResult = {
    migrations: MigrationStatusOutput array
  } with

    static member FromMigrations:
      migrations:
        System.Collections.Generic.IReadOnlyList<Migrondi.Core.MigrationStatus> ->
        ListMigrationsResult

    static member Empty: ListMigrationsResult

    static member Encoder: JDeck.Encoder<ListMigrationsResult>

  type MigrationDetail = {
    id: System.Guid
    name: string
    timestamp: int64
    upContent: string
    downContent: string
    manualTransaction: bool
    projectId: System.Guid
    fullName: string
  } with

    static member FromVirtualMigration:
      m: MigrondiUI.VirtualMigration -> MigrationDetail

    static member Encoder: JDeck.Encoder<MigrationDetail>

  type GetMigrationResult =
    | MigrationFound of MigrationDetail
    | MigrationNotFound of string

    static member Encoder: JDeck.Encoder<GetMigrationResult>

  type MigrationPreview = {
    name: string
    timestamp: int64
    upContent: string
    downContent: string
    fullName: string
  } with

    static member FromMigration: m: Migrondi.Core.Migration -> MigrationPreview

    static member Encoder: JDeck.Encoder<MigrationPreview>

  type DryRunResult = {
    count: int
    migrations: MigrationPreview array
  } with

    static member FromMigrations:
      migrations:
        System.Collections.Generic.IReadOnlyList<Migrondi.Core.Migration> ->
        DryRunResult

    static member Empty: DryRunResult

    static member Encoder: JDeck.Encoder<DryRunResult>

  type SuccessResult = {
    success: bool
    message: string
  } with

    static member Error: message: string -> SuccessResult

    static member Ok: message: string -> SuccessResult

    static member Encoder: JDeck.Encoder<SuccessResult>

  type AppliedMigration = {
    name: string
    timestamp: int64
    fullName: string
  } with

    static member FromMigration: m: Migrondi.Core.Migration -> AppliedMigration

    static member FromMigrationRecord:
      r: Migrondi.Core.MigrationRecord -> AppliedMigration

    static member Encoder: JDeck.Encoder<AppliedMigration>

  type MigrationsResult = {
    success: bool
    message: string
    count: int
    migrations: AppliedMigration array
  } with

    static member Error: message: string -> MigrationsResult

    static member FromMigrationRecords:
      records:
        System.Collections.Generic.IReadOnlyList<Migrondi.Core.MigrationRecord> ->
        MigrationsResult

    static member FromMigrations:
      migrations:
        System.Collections.Generic.IReadOnlyList<Migrondi.Core.Migration> ->
        MigrationsResult

    static member Ok:
      count: int * migrations: AppliedMigration array -> MigrationsResult

    static member Encoder: JDeck.Encoder<MigrationsResult>

  type ErrorResult = {
    error: string
  } with

    static member Create: error: string -> ErrorResult

    static member Encoder: JDeck.Encoder<ErrorResult>

  type CreateMigrationResult =
    | MigrationCreated of
      {|
        fullName: string
        id: System.Guid
        name: string
        timestamp: int64
      |}
    | CreateMigrationError of string

    static member Encoder: JDeck.Encoder<CreateMigrationResult>

  type CreateProjectResult =
    | ProjectCreated of
      {|
        driver: string
        id: System.Guid
        name: string
        tableName: string
      |}
    | CreateProjectError of string

    static member Encoder: JDeck.Encoder<CreateProjectResult>

  type ExportResult =
    | ExportSuccess of {| path: string |}
    | ExportError of string

    static member Encoder: JDeck.Encoder<ExportResult>

  type ImportResult =
    | ImportSuccess of {| projectId: System.Guid |}
    | ImportError of string

    static member Encoder: JDeck.Encoder<ImportResult>

  /// Shape of the single-project `get_project` tool. It carries no binding
  /// knowledge: hosts fill the fields from their own resolved binding and
  /// must never put the connection string in any of them.
  type ProjectInfoResult = {
    name: string
    configPath: string
    driver: string
    migrationsFolder: string
    tableName: string
  } with

    static member Encoder: JDeck.Encoder<ProjectInfoResult>

  /// Result of the stateless `init_project` tool: the created or adopted
  /// project, or why the directory was refused.
  type InitProjectResult =
    | ProjectInitialized of
      {|
        directory: string
        configPath: string
        mode: string
      |}
    | InitError of string

    static member Encoder: JDeck.Encoder<InitProjectResult>

  /// Result of the stateless `update_project_config` tool.
  type UpdateConfigResult =
    | ConfigUpdated of {| configPath: string |}
    | UpdateError of string

    static member Encoder: JDeck.Encoder<UpdateConfigResult>

module McpResultMapper =

  val fromEncoder:
    encoder: JDeck.Encoder<'T> ->
    value: 'T ->
      ModelContextProtocol.Protocol.CallToolResult
