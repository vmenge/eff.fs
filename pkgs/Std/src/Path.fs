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
/// the path value itself.
module Path =
  /// Creates a new `Path`. A `Path` does not guarantee that the path exists, is valid for all host APIs,
  /// or refers to a file, directory, or symlink. Those questions belong to `Fs`.
  let make (str: string) = Path str
  /// Returns a reference to the underlying `string`
  let toString (Path str) = str

  let parent (Path p) : Path Option = failwith "todo"

  let isAbsolute (Path p) : bool = Path.IsPathFullyQualified p
  let isRelative (Path p) : bool = not <| Path.IsPathFullyQualified p

  let getRoot (Path p) : string ValueOption =
    Path.GetPathRoot p
    |> ValueOption.ofObj
    |> ValueOption.reject String.isNullOrWhiteSpace

  let isEmpty (Path p) : bool = String.len p = 0

  let len (Path p) = String.len p

  let components (p: Path) : PathComponent seq =
    if isEmpty p then
      Seq.empty
    else
      let (Path str) = p
      let root = getRoot p

      let tail =
        match root with
        | ValueSome root -> String.substringFrom (String.len root) str
        | ValueNone -> str

      let segments =
        tail
        |> String.splitBy [|
          System.IO.Path.DirectorySeparatorChar
          System.IO.Path.AltDirectorySeparatorChar
        |]

      let normalized = ResizeArray<PathComponent>()

      root
      |> ValueOption.iter (fun root ->
        if root <> "/" && root <> @"\" then
          normalized.Add(Prefix root)

        normalized.Add RootDir
      )

      let isAbsolute = isAbsolute p

      for segment in segments do
        let prev = normalized |> Seq.tryLast

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
  let normalizeLexically (p: Path) : Result<Path, PathErr> =
    if isEmpty p then
      Error(NormalizeErr "Cannot normalize a null or empty path.")
    else
      let (Path str) = p
      let root = getRoot p

      let tail =
        match root with
        | ValueSome root -> String.substringFrom (String.len root) str
        | ValueNone -> str

      let segments =
        tail
        |> String.splitBy [|
          System.IO.Path.DirectorySeparatorChar
          System.IO.Path.AltDirectorySeparatorChar
        |]

      let normalized = ResizeArray<string>()

      let isAbsolute = isAbsolute p
      let isRelative = isRelative p

      for segment in segments do
        let prev = normalized |> Seq.tryItem (normalized.Count - 1)

        match (segment, prev) with
        | "", _
        | ".", _ -> ()
        | "..", None when isAbsolute -> ()
        | "..", Some ".." -> normalized.Add(segment)
        | "..", Some _ -> normalized.RemoveAt(normalized.Count - 1)
        | _ -> normalized.Add(segment)

      let joined = normalized |> String.joinWith Path.DirectorySeparatorChar

      let path =
        match root with
        | ValueSome root -> root + joined
        | ValueNone -> if String.len joined > 0 then joined else "."

      if String.startsWith ".." path && isRelative then
        "Relative paths cannot start with '..'" |> NormalizeErr |> Error
      else
        Ok(Path path)

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
