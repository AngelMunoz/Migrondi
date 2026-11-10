module PublicSurface

open System
open System.IO
open System.Reflection
open System.Runtime.Loader

let rec typeName (t: Type) =
    if t.IsGenericParameter then
        "'" + t.Name
    elif t.IsArray then
        typeName (t.GetElementType()) + "[" + String(',', t.GetArrayRank() - 1) + "]"
    elif t.IsByRef then
        "byref<" + typeName (t.GetElementType()) + ">"
    else
        let simple (x: Type) =
            let tick = x.Name.IndexOf '`'
            if tick < 0 then x.Name else x.Name.Substring(0, tick)
        let rec qualified (x: Type) =
            if x.IsNested then qualified x.DeclaringType + "+" + simple x
            elif String.IsNullOrEmpty x.Namespace then simple x
            else x.Namespace + "." + simple x
        if t.IsGenericType then
            let args = t.GetGenericArguments() |> Array.map typeName |> String.concat ", "
            $"{qualified t}<{args}>"
        else
            qualified t

let private parameters (ps: ParameterInfo array) =
    ps |> Array.map (fun p -> $"{p.Name}: {typeName p.ParameterType}") |> String.concat ", "

let private genericSuffix (m: MethodInfo) =
    if m.IsGenericMethodDefinition then
        "<" + (m.GetGenericArguments() |> Array.map typeName |> String.concat ", ") + ">"
    else
        ""

let private sourceName (m: MemberInfo) =
    m.GetCustomAttributesData()
    |> Seq.tryFind (fun a -> a.AttributeType.Name = "CompilationSourceNameAttribute")
    |> Option.map (fun a -> string a.ConstructorArguments[0].Value)
    |> Option.defaultValue m.Name

let private accessors (p: PropertyInfo) =
    let getter = p.GetGetMethod()
    let setter = p.GetSetMethod()
    match isNull getter, isNull setter with
    | false, false -> Some "get, set"
    | false, true -> Some "get"
    | true, false -> Some "set"
    | true, true -> None

let private describe (m: MemberInfo) =
    try
        match m with
        | :? ConstructorInfo as c -> Some $"new({parameters (c.GetParameters())})"
        | :? MethodInfo as mi ->
            let kind = if mi.IsStatic then "static " else ""
            Some $"{kind}{sourceName mi}{genericSuffix mi}({parameters (mi.GetParameters())}) : {typeName mi.ReturnType}"
        | :? PropertyInfo as p ->
            let isStatic =
                p.GetAccessors() |> Array.exists (fun a -> a.IsStatic)
            let index =
                match p.GetIndexParameters() with
                | [||] -> ""
                | ps -> $"[{parameters ps}]"
            accessors p
            |> Option.map (fun access ->
                let kind = if isStatic then "static " else ""
                $"{kind}{p.Name}{index} : {typeName p.PropertyType} with {access}")
        | :? FieldInfo as f when f.IsLiteral -> Some $"const {f.Name} : {typeName f.FieldType} = {f.GetRawConstantValue()}"
        | :? FieldInfo as f ->
            let kind = if f.IsStatic then "static " else ""
            Some $"{kind}field {f.Name} : {typeName f.FieldType}"
        | :? EventInfo as e -> Some $"event {e.Name} : {typeName e.EventHandlerType}"
        | _ -> None
    with ex ->
        Some $"{m.Name} (signature not loaded: {ex.GetType().Name})"

let private packageAssemblies (assemblyPath: string) =
    let depsPath = Path.ChangeExtension(assemblyPath, ".deps.json")
    let packageRoot =
        match Environment.GetEnvironmentVariable "NUGET_PACKAGES" with
        | null
        | "" -> Path.Combine(Environment.GetFolderPath Environment.SpecialFolder.UserProfile, ".nuget", "packages")
        | root -> root
    let found = Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    if File.Exists depsPath then
        use doc = Text.Json.JsonDocument.Parse(File.ReadAllText depsPath)
        let root = doc.RootElement
        let libraryPaths = Collections.Generic.Dictionary<string, string>()
        match root.TryGetProperty "libraries" with
        | true, libraries ->
            for library in libraries.EnumerateObject() do
                match library.Value.TryGetProperty "path" with
                | true, p -> libraryPaths[library.Name] <- p.GetString() |> string
                | _ -> ()
        | _ -> ()
        match root.TryGetProperty "targets" with
        | true, targets ->
            for target in targets.EnumerateObject() do
                for library in target.Value.EnumerateObject() do
                    match libraryPaths.TryGetValue library.Name, library.Value.TryGetProperty "runtime" with
                    | (true, libraryPath), (true, runtime) ->
                        for asset in runtime.EnumerateObject() do
                            let full = Path.Combine(packageRoot, libraryPath, asset.Name)
                            if File.Exists full then found[Path.GetFileNameWithoutExtension asset.Name] <- full
                    | _ -> ()
        | _ -> ()
    found

let private sharedFrameworkAssembly (name: AssemblyName) =
    let runtimeDir = Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory()
    let shared = Directory.GetParent(Directory.GetParent(runtimeDir.TrimEnd('/', '\\')).FullName).FullName
    let requested = if isNull name.Version then Version() else name.Version
    let candidates =
        [ for framework in Directory.GetDirectories shared do
              for versionDir in Directory.GetDirectories framework do
                  match Version.TryParse(Path.GetFileName versionDir) with
                  | true, folderVersion ->
                      let file = Path.Combine(versionDir, name.Name + ".dll")
                      if File.Exists file then
                          let assemblyVersion = AssemblyName.GetAssemblyName(file).Version
                          yield folderVersion, assemblyVersion, file
                  | _ -> () ]
        |> List.sortBy (fun (folderVersion, _, _) -> folderVersion)
    match candidates |> List.tryFind (fun (_, assemblyVersion, _) -> assemblyVersion >= requested) with
    | Some(_, _, file) -> Some file
    | None -> candidates |> List.tryLast |> Option.map (fun (_, _, file) -> file)

let private load (path: string) =
    let full = Path.GetFullPath path
    let dir = Path.GetDirectoryName full
    let packages = packageAssemblies full
    let context = AssemblyLoadContext("public-surface", isCollectible = true)
    context.add_Resolving (fun _ name ->
        let local = Path.Combine(dir, name.Name + ".dll")
        let candidate =
            if File.Exists local then Some local
            else
                match packages.TryGetValue name.Name with
                | true, p -> Some p
                | _ -> sharedFrameworkAssembly name
        match candidate with
        | Some p -> context.LoadFromAssemblyPath p
        | None -> null)
    context.LoadFromAssemblyPath full

let lines (path: string) =
    let assembly = load path
    let types =
        let loaded (ex: ReflectionTypeLoadException) = ex.Types |> Array.filter (isNull >> not)
        try
            assembly.GetExportedTypes()
        with
        | :? ReflectionTypeLoadException as ex -> loaded ex
        | _ ->
            let all =
                try assembly.GetTypes() with :? ReflectionTypeLoadException as ex -> loaded ex
            all |> Array.filter (fun t -> try t.IsVisible with _ -> false)
    let flags =
        BindingFlags.Public ||| BindingFlags.Instance ||| BindingFlags.Static ||| BindingFlags.DeclaredOnly
    let describeType (t: Type) =
        let name = t.FullName
        [ if not (isNull t.BaseType) && t.BaseType <> typeof<obj> && t.BaseType <> typeof<ValueType> then
              yield $"{name} | inherits {typeName t.BaseType}"
          for i in t.GetInterfaces() do
              yield $"{name} | implements {typeName i}"
          let accessorMethods =
              Collections.Generic.HashSet<MethodInfo>(
                  Seq.append
                      (t.GetProperties flags |> Seq.collect (fun p -> p.GetAccessors()))
                      (t.GetEvents flags
                       |> Seq.collect (fun e -> [ e.GetAddMethod(); e.GetRemoveMethod(); e.GetRaiseMethod() ])
                       |> Seq.filter (isNull >> not))
              )
          for m in t.GetMembers flags do
              match m with
              | :? MethodInfo as mi when accessorMethods.Contains mi -> ()
              | _ ->
              match describe m with
              | Some line -> yield $"{name} | {line}"
              | None -> () ]
    [ for t in types do
          yield $"type {t.FullName}"
          let members =
              try describeType t
              with ex -> [ $"{t.FullName} | (members not loaded: {ex.GetType().Name}: {ex.Message})" ]
          yield! members ]
    |> List.distinct
    |> List.sortWith (fun a b -> String.CompareOrdinal(a, b))

let print (path: string) =
    if not (File.Exists path) then
        eprintfn "Assembly not found: %s" path
        exit 2
    for line in lines path do
        printfn "%s" line
