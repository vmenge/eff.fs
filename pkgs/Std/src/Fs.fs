namespace EffSharp.Std

open EffSharp.Core
open EffSharp.Gen

[<Struct>]
type FsErr =
  | NotFound of notfoundexn: System.IO.FileNotFoundException
  | PermissionDenied of permissionexn: System.Security.SecurityException
  | AlreadyExists of alreadyexists: string
  | IOError of exn: exn

module FsErr =
  let ofExn (e: exn) : FsErr =
    match e with
    | :? System.IO.FileNotFoundException as ex -> NotFound(ex)
    | :? System.Security.SecurityException as ex -> PermissionDenied(ex)
    | _ -> IOError e

// TODO
type DirEntry = struct end

// TODO
type Metadata = struct end

// TODO
type Permissions = struct end

// TODO
type FileTimes = struct end

// TODO
type FileHandle = struct end

[<Struct>]
type FileAccess =
  | Read
  | Write
  | ReadWrite
  | Append

[<Struct>]
type FileCreate =
  | Open
  | Create
  | CreateNew
  | OpenOrCreate
  | Truncate

[<Struct>]
type FileOpen = {
  Access: FileAccess
  Create: FileCreate
}

[<Effect(Mode.Wrap)>]
type Fs =
  abstract readText: Path -> Eff<string, FsErr, unit>
  abstract read: Path -> Eff<byte array, FsErr, unit>

  abstract writeText: Path -> string -> Eff<unit, FsErr, unit>
  abstract write: Path -> byte array -> Eff<unit, FsErr, unit>

  abstract appendText: Path -> string -> Eff<unit, FsErr, unit>
  abstract append: Path -> byte array -> Eff<unit, FsErr, unit>

  abstract readDir: Path -> Eff<DirEntry array, FsErr, unit>
  abstract createDir: Path -> Eff<unit, FsErr, unit>
  abstract createDirAll: Path -> Eff<unit, FsErr, unit>
  abstract removeDir: Path -> Eff<unit, FsErr, unit>
  abstract removeDirAll: Path -> Eff<unit, FsErr, unit>
  abstract removeFile: Path -> Eff<unit, FsErr, unit>
  abstract copy: Path -> Path -> Eff<unit, FsErr, unit>
  abstract rename: Path -> Path -> Eff<unit, FsErr, unit>

  abstract metadata: Path -> Eff<Metadata, FsErr, unit>
  abstract exists: Path -> Eff<bool, FsErr, unit>
  abstract isFile: Path -> Eff<bool, FsErr, unit>
  abstract isDir: Path -> Eff<bool, FsErr, unit>
  abstract canonicalize: Path -> Eff<Path, FsErr, unit>

  abstract hardLink: link: Path -> original: Path -> Eff<unit, FsErr, unit>
  abstract symlinkFile: link: Path -> original: Path -> Eff<unit, FsErr, unit>
  abstract symlinkDir: link: Path -> original: Path -> Eff<unit, FsErr, unit>
  abstract symlinkMetadata: link: Path -> Eff<Metadata, FsErr, unit>
  abstract readLink: link: Path -> Eff<Path, FsErr, unit>

  abstract setPermissions: Path -> Permissions -> Eff<unit, FsErr, unit>
  abstract setTimes: Path -> FileTimes -> Eff<unit, FsErr, unit>

  abstract withFile:
    Path ->
    FileOpen ->
    (FileHandle -> Eff<'t, FsErr, unit>) ->
      Eff<'t, FsErr, unit>

type FsProvider internal () =
  interface Fs with
    member _.append (arg1: Path) (arg2: byte array) : Eff<unit, FsErr, unit> =
      failwith "Not Implemented"

    member _.appendText (arg1: Path) (arg2: string) : Eff<unit, FsErr, unit> =
      failwith "Not Implemented"

    member _.canonicalize(arg1: Path) : Eff<Path, FsErr, unit> =
      failwith "Not Implemented"

    member _.copy (arg1: Path) (arg2: Path) : Eff<unit, FsErr, unit> =
      failwith "Not Implemented"

    member _.createDir(arg1: Path) : Eff<unit, FsErr, unit> =
      failwith "Not Implemented"

    member _.createDirAll(arg1: Path) : Eff<unit, FsErr, unit> =
      failwith "Not Implemented"

    member _.exists(arg1: Path) : Eff<bool, FsErr, unit> =
      failwith "Not Implemented"

    member _.hardLink (link: Path) (original: Path) : Eff<unit, FsErr, unit> =
      failwith "Not Implemented"

    member _.isDir(arg1: Path) : Eff<bool, FsErr, unit> =
      failwith "Not Implemented"

    member _.isFile(arg1: Path) : Eff<bool, FsErr, unit> =
      failwith "Not Implemented"

    member _.metadata(arg1: Path) : Eff<Metadata, FsErr, unit> =
      failwith "Not Implemented"

    member _.read(arg1: Path) : Eff<byte array, FsErr, unit> =
      fun () -> System.IO.File.ReadAllBytesAsync(Path.toString arg1)
      |> Eff.tryTask
      |> Eff.mapErr FsErr.ofExn

    member _.readDir(arg1: Path) : Eff<DirEntry array, FsErr, unit> =
      failwith "Not Implemented"

    member _.readLink(link: Path) : Eff<Path, FsErr, unit> =
      failwith "Not Implemented"

    member _.readText(arg1: Path) : Eff<string, FsErr, unit> =
      fun () -> System.IO.File.ReadAllTextAsync(Path.toString arg1)
      |> Eff.tryTask
      |> Eff.mapErr FsErr.ofExn

    member _.removeDir(arg1: Path) : Eff<unit, FsErr, unit> =
      failwith "Not Implemented"

    member _.removeDirAll(arg1: Path) : Eff<unit, FsErr, unit> =
      failwith "Not Implemented"

    member _.removeFile(arg1: Path) : Eff<unit, FsErr, unit> =
      failwith "Not Implemented"

    member _.rename (arg1: Path) (arg2: Path) : Eff<unit, FsErr, unit> =
      failwith "Not Implemented"

    member _.setPermissions
      (arg1: Path)
      (arg2: Permissions)
      : Eff<unit, FsErr, unit> =
      failwith "Not Implemented"

    member _.setTimes (arg1: Path) (arg2: FileTimes) : Eff<unit, FsErr, unit> =
      failwith "Not Implemented"

    member _.symlinkDir (link: Path) (original: Path) : Eff<unit, FsErr, unit> =
      failwith "Not Implemented"

    member _.symlinkFile
      (link: Path)
      (original: Path)
      : Eff<unit, FsErr, unit> =
      failwith "Not Implemented"

    member _.symlinkMetadata(link: Path) : Eff<Metadata, FsErr, unit> =
      failwith "Not Implemented"

    member _.withFile
      (arg1: Path)
      (arg2: FileOpen)
      (arg3: FileHandle -> Eff<'t, FsErr, unit>)
      : Eff<'t, FsErr, unit> =
      failwith "Not Implemented"

    member _.write (arg1: Path) (arg2: byte array) : Eff<unit, FsErr, unit> =
      failwith "Not Implemented"

    member _.writeText (arg1: Path) (arg2: string) : Eff<unit, FsErr, unit> =
      failwith "Not Implemented"

[<AutoOpen>]
module FsExt =
  type Fs with
    static member Provider() = FsProvider()
