namespace EffSharp.Std

open System
open System.IO

/// Represents a filesystem path as a lexical value.
///
/// A `Path` does not guarantee that the path exists, is valid for all host APIs,
/// or refers to a file, directory, or symlink. Those questions belong to `Fs`.
[<Struct>]
type Path = private Path of string

[<Struct>]
type PathErr =
  | NormalizeErr of string
  | StripPrefixErr of string

[<Struct>]
type PathComponent =
  | Prefix of string
  | RootDir
  | CurDir
  | ParentDir
  | Normal of string

/// Lexical operations over `Path` values.
///
/// Functions in this module do not access the filesystem. They operate only on
/// the path value itself and are completely pure unlike the regular dotnet
/// library ones.
module Path =
  [<AutoOpen>]
  module private Helpers =
    [<Literal>]
    let WindowsSeparator = '\\'

    [<Literal>]
    let UnixSeparator = '/'

    [<Literal>]
    let Separator =
#if WINDOWS
      WindowsSeparator
#else
      UnixSeparator
#endif

  /// Creates a new `Path`. A `Path` does not guarantee that the path exists, is valid for all host APIs,
  /// or refers to a file, directory, or symlink. Those questions belong to `Fs`.
  let make (str: string) = Path str
  /// Returns a reference to the underlying `string`
  let toString (Path str) = str

  let parent (Path p) : Path Option = failwith "todo"

  let getPrefixAndRoot (Path p) : string option * string option =
    let isDrive (p: string) =
      let firstIsChar = p |> Seq.tryItem 0 |> Option.exists Char.IsAsciiLetter
      let sndIsColon = p |> Seq.tryItem 1 |> Option.exists ((=) ':')
      firstIsChar && sndIsColon

    match p with
    | _ when String.startsWith "/" p -> None, Some "/"
    | _ when String.startsWith "\\\\" p ->
      let hostAndRest =
        p
        |> String.splitOnce "\\\\"
        |> Option.map snd
        |> Option.bind (String.splitOnce "\\")

      let host = hostAndRest |> Option.map fst

      let share =
        hostAndRest
        |> Option.map snd
        |> Option.bind (String.splitOnce "\\")
        |> Option.map fst
        |> Option.reject ((=) "..")
        |> Option.reject ((=) ".")

      let prefix =
        Option.zip host share
        |> Option.map (fun (host, share) -> $"\\\\{host}\\{share}")

      prefix, Some "\\"

    | _ when String.startsWith "\\" p -> None, Some "\\"

    | _ when isDrive p ->
      let root =
        Seq.tryItem 2 p |> Option.filter ((=) '\\') |> Option.map string

      let prefix = String.substring 0 2 p
      prefix, root

    | _ -> None, None

  let isAbsolute p = getPrefixAndRoot p |> snd |> Option.isSome
  let isRelative p = getPrefixAndRoot p |> snd |> Option.isNone

  let isEmpty (Path p) : bool = String.len p = 0

  let len (Path p) = String.len p

  let components (p: Path) : PathComponent seq =
    if isEmpty p then
      Seq.empty
    else
      let (Path str) = p
      let prefix, root = getPrefixAndRoot p

      let prefixRootLen =
        let prefixLen = prefix |> Option.map String.len |> Option.defaultValue 0
        let rootLen = root |> Option.map String.len |> Option.defaultValue 0
        prefixLen + rootLen

      let tail =
        str |> String.substringFrom prefixRootLen |> Option.defaultValue str

      let segments =
        tail |> String.splitBy [| WindowsSeparator; UnixSeparator |]

      let normalized = Vec()

      prefix |> Option.map Prefix |> Option.iter normalized.Add
      root |> Option.set RootDir |> Option.iter normalized.Add

      let isAbsolute = isAbsolute p

      for segment in segments do
        let prev = Vec.last normalized

        match (segment, prev) with
        | "", _ -> ()
        | ".", None -> normalized.Add CurDir
        | ".", Some _ -> ()
        | "..", None when isAbsolute -> ()
        | "..", _ -> normalized.Add ParentDir
        | _ -> normalized.Add(Normal segment)

      normalized

  /// Normalize a path, including .. without traversing the filesystem.
  /// Returns an error if normalization would leave leading .. components.
  let normalizeLexicallyWith
    (separator: char)
    (p: Path)
    : Result<Path, PathErr> =
    if isEmpty p then
      Error(NormalizeErr "Cannot normalize a null or empty path.")
    else
      let (Path str) = p
      let prefix, root = getPrefixAndRoot p

      let prefixRootLen =
        let prefixLen = prefix |> Option.map String.len |> Option.defaultValue 0
        let rootLen = root |> Option.map String.len |> Option.defaultValue 0
        prefixLen + rootLen

      let tail =
        str |> String.substringFrom prefixRootLen |> Option.defaultValue str

      let segments =
        tail |> String.splitBy [| WindowsSeparator; UnixSeparator |]

      let normalized = Vec()

      let isAbsolute = isAbsolute p
      let isRelative = isRelative p

      for segment in segments do
        let prev = normalized |> Seq.tryItem (normalized.Count - 1)

        match (segment, prev) with
        | "", _
        | ".", _ -> ()
        | "..", None when isAbsolute -> ()
        | "..", Some ".." -> normalized.Add segment
        | "..", Some _ -> normalized.RemoveAt(normalized.Count - 1)
        | _ -> normalized.Add segment

      let joined = normalized |> String.joinWith separator

      let prefixRoot =
        match prefix, root with
        | Some p, Some r -> Some(p + r)
        | Some p, None -> Some p
        | None, Some r -> Some r
        | None, None -> None

      let path =
        match prefixRoot with
        | Some pr -> pr + joined
        | None -> if String.len joined > 0 then joined else "."

      if String.startsWith ".." joined && isRelative then
        "Relative paths cannot start with '..'" |> NormalizeErr |> Error
      else
        Ok(Path path)

  let normalizeLexically = normalizeLexicallyWith Separator

  let endsWith (str: string) (Path p) : bool = failwith "todo"
  let startsWith (str: string) (Path p) : bool = failwith "todo"

  let extension (Path p) : string Option = failwith "todo"
  let withExtension (ext: string) (Path p) : Path = failwith "todo"

  let fileName (Path p) : string Option = failwith "todo"
  let withFileName (ext: string) (Path p) : Path = failwith "todo"

  let stripPrefix (prefix: Path) (Path p) : Result<Path, PathErr> =
    failwith "todo"

  let filePrefix (Path p) : string Option = failwith "todo"
  let fileStem (Path p) : string Option = failwith "todo"

  let hasTrailingSep (Path p) : bool = failwith "todo"
  let trimTrailingSep (Path p) : Path = failwith "todo"
  let withTrailingSep (ext: string) (Path p) : Path = failwith "todo"

  let combine (segment: string) (Path p) : Path = failwith "todo"
  let join (Path p2) (Path p1) : Path = failwith "todo"
