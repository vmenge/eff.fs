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

module PathComponent =
  let toString (sep: string) =
    function
    | Prefix pre -> pre
    | RootDir -> sep
    | CurDir -> "."
    | ParentDir -> ".."
    | Normal str -> str

/// Lexical operations over `Path` values.
///
/// Functions in this module do not access the filesystem. They operate only on
/// the path value itself and are completely pure unlike the regular dotnet
/// library ones.
module Path =
  [<Literal>]
  let private WindowsSeparator = '\\'

  [<Literal>]
  let private UnixSeparator = '/'

  [<Literal>]
  let private Separator =
#if WINDOWS
    WindowsSeparator
#else
    UnixSeparator
#endif

  /// Returns the prefix (e.g. drive letter or UNC share) and root separator
  /// for the given path. Either or both may be None.
  let getPrefixAndRoot (Path p) : string option * string option =
    let isDrive (p: string) =
      let firstIsChar = p |> String.item 0 |> Option.exists Char.IsAsciiLetter
      let sndIsColon = p |> String.item 1 |> Option.exists ((=) ':')
      firstIsChar && sndIsColon

    match p with
    | _ when String.startsWith "/" p -> None, Some "/"
    | _ when String.startsWith "\\\\" p ->
      let hostAndRest =
        p
        |> String.splitOnce "\\\\"
        |> Option.map snd
        |> Option.bind (String.splitOnce "\\")

      let host, rest = Option.unzip hostAndRest

      let share, root =
        rest
        |> Option.bind (String.splitOnce "\\")
        |> Option.unzip
        |> fun (share, root) ->
            let share =
              share
              |> Option.orElse rest
              |> Option.reject ((=) "..")
              |> Option.reject ((=) ".")
              |> Option.reject String.isNullOrWhiteSpace

            share, Some "\\"


      let prefix =
        Option.zip host share
        |> Option.map (fun (host, share) -> $"\\\\{host}\\{share}")

      prefix, root

    | _ when String.startsWith "\\" p -> None, Some "\\"

    | _ when isDrive p ->
      let root =
        String.item 2 p |> Option.filter ((=) '\\') |> Option.map string

      let prefix = String.substring 0 2 p
      prefix, root

    | _ -> None, None

  let private prefixRootTail p =
    let prefix, root = getPrefixAndRoot p
    let (Path str) = p

    let prefixRootLen =
      let prefixLen = prefix |> Option.map String.len |> Option.defaultValue 0
      let rootLen = root |> Option.map String.len |> Option.defaultValue 0
      min (prefixLen + rootLen) (String.len str)

    let tail =
      str |> String.substringFrom prefixRootLen |> Option.defaultValue str

    prefix, root, tail

  /// Creates a new `Path`. A `Path` does not guarantee that the path exists, is valid for all host APIs,
  /// or refers to a file, directory, or symlink. Those questions belong to `Fs`.
  let make (str: string) = Path str

  /// Returns a reference to the underlying `string`
  let toString (Path str) = str

  /// Returns the parent directory of the path, or None if the path
  /// terminates in a root, prefix, or is empty.
  let parent p : Path Option =
    let prefix, root, tail = prefixRootTail p
    let tail = tail |> String.trimEndOf [| WindowsSeparator; UnixSeparator |]

    tail
    |> String.revSplitOnceOf [| string WindowsSeparator; string UnixSeparator |]
    |> Option.map fst
    |> Option.orElseWith (fun () ->
      Some tail |> Option.reject String.isNullOrEmpty |> Option.set ""
    )
    |> Option.map (fun prnt ->
      (Option.defaultValue "" prefix)
      + (Option.defaultValue "" root)
      + prnt
      |> Path
    )

  /// Returns true if the path has a root separator.
  let isAbsolute p = getPrefixAndRoot p |> snd |> Option.isSome
  /// Returns true if the path has no root separator.
  let isRelative p = getPrefixAndRoot p |> snd |> Option.isNone

  /// Returns true if the path is the empty string.
  let isEmpty (Path p) : bool = String.len p = 0

  /// Returns the length of the underlying string.
  let len (Path p) = String.len p

  /// Decomposes the path into its logical components without resolving
  /// dot-dot segments. Interior dot segments and repeated separators
  /// are normalized away.
  let components (p: Path) : PathComponent Vec =
    if isEmpty p then
      Vec()
    else
      let prefix, root, tail = prefixRootTail p

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

  let ofComponents (components: PathComponent seq) : Path = failwith "todo"


  /// Normalizes the path lexically using the given separator, collapsing
  /// dot and dot-dot segments without accessing the filesystem.
  /// Returns an error if normalization would leave leading dot-dot components.
  let normalizeLexicallyWith
    (separator: char)
    (p: Path)
    : Result<Path, PathErr> =
    if isEmpty p then
      Error(NormalizeErr "Cannot normalize a null or empty path.")
    else
      let prefix, root, tail = prefixRootTail p

      let segments =
        tail |> String.splitBy [| WindowsSeparator; UnixSeparator |]

      let normalized = Vec()

      let isAbsolute = isAbsolute p
      let isRelative = isRelative p

      for segment in segments do
        let prev = normalized |> Vec.last

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

  /// Normalizes the path lexically, collapsing dot and dot-dot segments
  /// without accessing the filesystem. Uses a separator chosen at compile
  /// time based on the target OS.
  /// Returns an error if normalization would leave leading dot-dot components.
  let normalizeLexically = normalizeLexicallyWith Separator

  /// Returns true if the path string ends with the given value.
  let endsWith (str: string) (Path p) : bool = String.endsWith str p

  /// Returns true if the path string starts with the given value.
  let startsWith (str: string) (Path p) : bool = String.startsWith str p

  /// Returns the file extension of the last component, without the leading dot.
  /// Returns None if there is no extension.
  let extension p : string Option =
    let _, _, tail = prefixRootTail p

    tail
    |> String.revSplitOnceOf [| string WindowsSeparator; string UnixSeparator |]
    |> Option.map snd
    |> Option.defaultValue tail
    |> String.revSplitOnce "."
    |> Option.bind (fun (preDot, postDot) ->
      if String.isNullOrWhiteSpace preDot then
        None
      else
        Some postDot
    )

  /// Appends the given extension to the path, separated by a dot.
  let withExtension (ext: string) (Path p) : Path = Path $"{p}.{ext}"

  /// Returns the final component of the path if it is a normal segment.
  /// Returns None if the path is empty, a root, or ends with `..`.
  let fileName p : string Option =
    components p
    |> Vec.last
    |> Option.bind (
      function
      | Normal v -> Some v
      | _ -> None
    )

  /// Strips the given prefix from the path using the specified separator,
  /// returning the remaining relative tail. Returns an error if the prefix
  /// does not match.
  let stripPrefixWith sep prefix path : Result<Path, PathErr> =
    let prefixComponents = components prefix
    let components = components path
    let prefixLen = Vec.len prefixComponents
    let cmpLen = Vec.len components

    if prefixLen > cmpLen then
      Error(
        StripPrefixErr "Prefix cannot have more segments then the given path"
      )

    else
      let mutable i = 0
      let mutable matches = true
      let mutable lastPfx = None
      let mutable lastCmp = None

      while matches && i < Vec.len prefixComponents && i < Vec.len components do
        lastPfx <- Vec.item i prefixComponents
        lastCmp <- Vec.item i components
        matches <- lastPfx = lastCmp

        if matches then
          i <- i + 1

      if not matches then
        let sep = string sep

        let prefix =
          lastPfx
          |> Option.map (PathComponent.toString sep)
          |> Option.defaultValue ""

        let cmp =
          lastCmp
          |> Option.map (PathComponent.toString sep)
          |> Option.defaultValue ""

        StripPrefixErr
          $"prefix \"{prefix}\" does not match actual segment \"{cmp}\""
        |> Error
      else
        components
        |> Seq.skip i
        |> Seq.map (PathComponent.toString (string sep))
        |> String.joinWith sep
        |> Path
        |> Ok

  /// Strips the given prefix from the path using the OS separator.
  /// Returns an error if the prefix does not match.
  let stripPrefix = stripPrefixWith Separator

  /// Returns the portion of the file name before the first dot.
  /// For dotfiles, the leading dot is considered part of the name.
  /// Returns None if there is no file name.
  let filePrefix path =
    fileName path
    |> Option.bind (fun fname ->
      let str, before =
        match String.item 0 fname with
        | Some '.' -> fname |> String.substringFrom 1, "."
        | _ -> Some fname, ""

      str
      |> Option.bind (String.splitOnce ".")
      |> Option.map fst
      |> Option.orElse str
      |> Option.map (fun x -> $"{before}{x}")
    )

  /// Returns the portion of the file name before the last dot.
  /// For dotfiles, the leading dot is considered part of the name.
  /// Returns None if there is no file name.
  let fileStem path =
    fileName path
    |> Option.bind (fun fname ->
      let str, before =
        match String.item 0 fname with
        | Some '.' -> fname |> String.substringFrom 1, "."
        | _ -> Some fname, ""

      str
      |> Option.bind (String.revSplitOnce ".")
      |> Option.map fst
      |> Option.orElse str
      |> Option.map (fun x -> $"{before}{x}")
    )

  /// Returns true if the path ends with a separator character.
  let hasTrailingSep (Path p) =
    p.EndsWith UnixSeparator || p.EndsWith WindowsSeparator

  /// Removes trailing separator characters from the path.
  let trimTrailingSep (Path p) =
    String.trimEndOf [| UnixSeparator; WindowsSeparator |] p

  let combine (segment: string) (Path p) : Path = failwith "todo"
  let join (Path p2) (Path p1) : Path = failwith "todo"
