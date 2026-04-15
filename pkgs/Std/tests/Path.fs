namespace EffSharp.Std.Tests

open Expecto
open EffSharp.Std

module Path =
  let private normalize path = Path path |> Path.normalizeLexically

  let private normalizeWith sep path =
    Path path |> Path.normalizeLexicallyWith sep

  let private expectNormalized expected actual =
    match actual with
    | Ok normalized -> Expect.equal (Path.toString normalized) expected ""
    | Error err -> failtest $"expected Ok \"{expected}\", got Error %A{err}"

  let private expectNormalizeErr actual =
    match actual with
    | Ok normalized ->
      failtest $"expected Error, got Ok \"{Path.toString normalized}\""
    | Error(PathErr.NormalizeErr _) -> ()
    | Error err -> failtest $"expected NormalizeErr, got %A{err}"

  let private expectComponents expected path =
    let actual = path |> Path |> Path.components |> Seq.toList

    Expect.equal actual expected ""

  let private expectPrefixAndRoot expected path =
    let actual = path |> Path |> Path.getPrefixAndRoot

    Expect.equal actual expected ""

  let private getPrefixAndRoot =
    testList "getPrefixAndRoot" [
      testCase
        "returns no prefix and no root for a relative path"
        (fun () -> "a/b" |> expectPrefixAndRoot (None, None))

      testCase
        "returns no prefix and no root for a dot-relative path"
        (fun () -> "./a/b" |> expectPrefixAndRoot (None, None))

      testCase
        "returns no prefix and a unix root dir for a unix absolute path"
        (fun () -> "/a/b" |> expectPrefixAndRoot (None, Some "/"))

      testCase
        "returns no prefix and a root dir for a rooted path without a prefix"
        (fun () -> "\\a\\b" |> expectPrefixAndRoot (None, Some @"\"))

      testCase
        "returns a drive prefix without a root dir for a drive-relative path"
        (fun () -> @"C:a\b" |> expectPrefixAndRoot (Some "C:", None))

      testCase
        "returns a drive prefix and a root dir for a fully qualified drive path"
        (fun () -> @"C:\a\b" |> expectPrefixAndRoot (Some "C:", Some @"\"))

      testCase
        "returns a unc prefix and a root dir for a unc path"
        (fun () ->
          @"\\server\share\dir"
          |> expectPrefixAndRoot (Some @"\\server\share", Some @"\")
        )

      testCase
        "treats a malformed unc-like path without a share as a rooted path without a prefix"
        (fun () -> @"\\server" |> expectPrefixAndRoot (None, Some @"\"))

      testCase
        "treats a malformed unc-like path with an empty share as a rooted path without a prefix"
        (fun () -> @"\\server\" |> expectPrefixAndRoot (None, Some @"\"))

      testCase
        "identifies a unc prefix even without rootdir"
        (fun () ->
          @"\\server\share"
          |> expectPrefixAndRoot (Some "\\\\server\share", Some "\\")
        )
    ]

  let private isEmpty =
    testList "isEmpty" [
      testCase
        "returns true for the empty string"
        (fun () -> Path "" |> Path.isEmpty |> Expect.isTrue <| "")

      testCase
        "returns false for whitespace-only lexical paths"
        (fun () -> Path " " |> Path.isEmpty |> Expect.isFalse <| "")

      testCase
        "returns false for non-empty paths"
        (fun () -> Path "foo" |> Path.isEmpty |> Expect.isFalse <| "")
    ]

  let private normalizeLexically =
    testList "normalizeLexically" [
      testCase
        "removes dot-dot segments lexically"
        (fun () -> normalizeWith '/' "a/b/../c" |> expectNormalized "a/c")

      testCase
        "removes dot segments and repeated separators"
        (fun () -> normalizeWith '/' "./a//b/." |> expectNormalized "a/b")

      testCase
        "errors on a leading relative parent segment"
        (fun () -> normalizeWith '/' "../a" |> expectNormalizeErr)

      testCase
        "errors on multiple leading relative parent segments"
        (fun () -> normalizeWith '/' "../../a" |> expectNormalizeErr)

      testCase
        "errors when cancellation would leave a leading relative parent segment"
        (fun () -> normalizeWith '/' "a/../../b" |> expectNormalizeErr)

      testCase
        "clamps absolute paths at the root"
        (fun () -> normalizeWith '/' "/a/../../b" |> expectNormalized "/b")

      testCase
        "returns dot when a relative path collapses to empty"
        (fun () -> normalizeWith '/' "a/.." |> expectNormalized ".")

      testCase
        "treats whitespace-only paths as lexical paths instead of empty input"
        (fun () -> normalizeWith '/' " " |> expectNormalized " ")

      testCase
        "drops a trailing separator after lexical normalization"
        (fun () -> normalizeWith '/' "foo/bar/" |> expectNormalized "foo/bar")

      testCase
        "preserves a drive prefix and root when normalizing a fully qualified drive path"
        (fun () -> normalizeWith '\\' @"C:\a\..\b" |> expectNormalized @"C:\b")

      testCase
        "preserves a drive prefix when normalizing a drive-relative path"
        (fun () -> normalizeWith '\\' @"C:a\..\b" |> expectNormalized @"C:b")

      testCase
        "errors on a drive-relative path whose normalization would leave a leading parent segment"
        (fun () -> normalizeWith '\\' @"C:..\b" |> expectNormalizeErr)

      testCase
        "preserves a unc prefix and root when normalizing a unc path"
        (fun () ->
          normalizeWith '\\' @"\\server\share\dir\..\x"
          |> expectNormalized @"\\server\share\x"
        )

      testCase
        "treats a malformed unc-like path as rooted when normalizing lexically"
        (fun () ->
          normalizeWith '\\' @"\\server\..\x" |> expectNormalized @"\x"
        )

      testCase
        "windows rooted but not fully qualified paths should not preserve rooted-only semantics"
        (fun () -> normalizeWith '\\' "\\..\\a" |> expectNormalized "\\a")
    ]

  let private components =
    testList "components" [
      testCase
        "returns empty for the empty path"
        (fun () -> "" |> expectComponents [])

      testCase
        "returns CurDir for dot"
        (fun () -> "." |> expectComponents [ CurDir ])

      testCase
        "keeps a leading current-directory segment"
        (fun () -> "./a" |> expectComponents [ CurDir; Normal "a" ])

      testCase
        "keeps a parent-directory segment in a relative path"
        (fun () -> "../a" |> expectComponents [ ParentDir; Normal "a" ])

      testCase
        "splits ordinary relative segments into Normal components"
        (fun () ->
          "a/b/c" |> expectComponents [ Normal "a"; Normal "b"; Normal "c" ]
        )

      testCase
        "ignores repeated separators"
        (fun () -> "a//b" |> expectComponents [ Normal "a"; Normal "b" ])

      testCase
        "normalizes away interior dot segments"
        (fun () -> "a/./b" |> expectComponents [ Normal "a"; Normal "b" ])

      testCase
        "normalizes away a trailing separator"
        (fun () -> "a/b/" |> expectComponents [ Normal "a"; Normal "b" ])

      testCase
        "does not collapse parent-directory components"
        (fun () ->
          "a/b/../c"
          |> expectComponents [ Normal "a"; Normal "b"; ParentDir; Normal "c" ]
        )

      testCase
        "recognizes a unix root directory"
        (fun () ->
          "/a/b" |> expectComponents [ RootDir; Normal "a"; Normal "b" ]
        )

      testCase
        "recognizes a windows drive prefix and root separately"
        (fun () ->
          @"C:\a\b"
          |> expectComponents [ Prefix "C:"; RootDir; Normal "a"; Normal "b" ]
        )

      testCase
        "recognizes a windows drive prefix without a root"
        (fun () ->
          @"C:a\b" |> expectComponents [ Prefix "C:"; Normal "a"; Normal "b" ]
        )

      testCase
        "recognizes a windows unc prefix and root separately"
        (fun () ->
          @"\\server\share\dir"
          |> expectComponents [
            Prefix @"\\server\share"
            RootDir
            Normal "dir"
          ]
        )
    ]

  let private expectParent expected path =
    let actual = path |> Path |> Path.parent |> Option.map Path.toString
    Expect.equal actual expected ""

  let private parent =
    testList "parent" [
      testCase
        "returns the parent directory for a nested path"
        (fun () -> "a/b/c" |> expectParent (Some "a/b"))

      testCase
        "returns Some empty for a single-component relative path"
        (fun () -> "a" |> expectParent (Some ""))

      testCase
        "returns None for an empty path"
        (fun () -> "" |> expectParent None)

      testCase "returns None for root" (fun () -> "/" |> expectParent None)

      testCase
        "returns Some root for a file under root"
        (fun () -> "/a" |> expectParent (Some "/"))

      testCase
        "returns the parent for a deeper absolute path"
        (fun () -> "/a/b" |> expectParent (Some "/a"))

      testCase
        "strips a trailing separator before determining the parent"
        (fun () -> "a/b/" |> expectParent (Some "a"))

      testCase
        "returns None for a windows drive root"
        (fun () -> @"C:\" |> expectParent None)

      testCase
        "returns the drive root for a file under a drive"
        (fun () -> @"C:\a" |> expectParent (Some @"C:\"))

      testCase
        "returns the parent for a deeper windows path"
        (fun () -> @"C:\a\b" |> expectParent (Some @"C:\a"))

      testCase
        "returns None for a unc root"
        (fun () -> @"\\server\share\" |> expectParent None)

      testCase
        "returns the unc root for a file under a unc share"
        (fun () ->
          @"\\server\share\a" |> expectParent (Some @"\\server\share\")
        )

      testCase
        "returns None for double slash"
        (fun () -> "//" |> expectParent None)

      testCase
        "returns None for a drive prefix without root or file"
        (fun () -> "C:" |> expectParent None)

      testCase
        "returns Some empty for dot"
        (fun () -> "." |> expectParent (Some ""))

      testCase
        "returns Some empty for dot-dot"
        (fun () -> ".." |> expectParent (Some ""))

      testCase
        "strips multiple trailing separators before determining the parent"
        (fun () -> "a/b//" |> expectParent (Some "a"))

      testCase
        "returns Some empty for a whitespace-only path"
        (fun () -> " " |> expectParent (Some ""))
    ]

  let private expectExtension expected path =
    let actual = path |> Path |> Path.extension
    Expect.equal actual expected ""

  let private extension =
    testList "extension" [
      testCase
        "returns the extension for a simple file"
        (fun () -> "foo.txt" |> expectExtension (Some "txt"))

      testCase
        "returns the last extension for a double extension"
        (fun () -> "foo.tar.gz" |> expectExtension (Some "gz"))

      testCase
        "returns the extension for a file in a directory"
        (fun () -> "a/b/foo.txt" |> expectExtension (Some "txt"))

      testCase
        "returns None for a file without an extension"
        (fun () -> "foo" |> expectExtension None)

      testCase
        "returns None for a directory path without an extension"
        (fun () -> "a/b/c" |> expectExtension None)

      testCase
        "returns None for a dotfile"
        (fun () -> ".gitignore" |> expectExtension None)

      testCase
        "returns the extension for a dotfile with an extension"
        (fun () -> ".foo.txt" |> expectExtension (Some "txt"))

      testCase
        "returns None for an empty path"
        (fun () -> "" |> expectExtension None)

      testCase
        "returns None when dot is in a parent directory but not the file"
        (fun () -> "a.b/c" |> expectExtension None)

      testCase
        "returns the extension for a windows-style path"
        (fun () -> @"C:\a\b.txt" |> expectExtension (Some "txt"))

      testCase
        "returns Some empty for a path ending with a dot"
        (fun () -> "foo." |> expectExtension (Some ""))

      testCase
        "returns None for a trailing-separator path"
        (fun () -> "a/b.txt/" |> expectExtension None)
    ]

  let private withExtension =
    testList "withExtension" [
      testCase
        "appends an extension to a simple path"
        (fun () ->
          let result = Path "foo" |> Path.withExtension "txt" |> Path.toString

          Expect.equal result "foo.txt" ""
        )

      testCase
        "appends an extension to a path that already has one"
        (fun () ->
          let result =
            Path "foo.tar" |> Path.withExtension "gz" |> Path.toString

          Expect.equal result "foo.tar.gz" ""
        )

      testCase
        "appends an extension to a nested path"
        (fun () ->
          let result =
            Path "a/b/foo" |> Path.withExtension "txt" |> Path.toString

          Expect.equal result "a/b/foo.txt" ""
        )
    ]

  let private expectFileName expected path =
    let actual = path |> Path |> Path.fileName
    Expect.equal actual expected ""

  let private fileName =
    testList "fileName" [
      testCase
        "returns the file name for a simple path"
        (fun () -> "foo.txt" |> expectFileName (Some "foo.txt"))

      testCase
        "returns the file name for a nested path"
        (fun () -> "a/b/foo.txt" |> expectFileName (Some "foo.txt"))

      testCase
        "returns the directory name for a trailing separator"
        (fun () -> "/usr/bin/" |> expectFileName (Some "bin"))

      testCase
        "returns the file name when path ends with dot"
        (fun () -> "foo.txt/." |> expectFileName (Some "foo.txt"))

      testCase
        "returns the file name when path ends with dot and trailing separators"
        (fun () -> "foo.txt/.//" |> expectFileName (Some "foo.txt"))

      testCase
        "returns None when path ends with dot-dot"
        (fun () -> "foo.txt/.." |> expectFileName None)

      testCase "returns None for root" (fun () -> "/" |> expectFileName None)

      testCase
        "returns None for an empty path"
        (fun () -> "" |> expectFileName None)

      testCase
        "returns the name for a single component"
        (fun () -> "foo" |> expectFileName (Some "foo"))

      testCase
        "returns None for a windows drive root"
        (fun () -> @"C:\" |> expectFileName None)

      testCase
        "returns the file name for a windows path"
        (fun () -> @"C:\a\b.txt" |> expectFileName (Some "b.txt"))

      testCase
        "returns None for dot-dot only"
        (fun () -> ".." |> expectFileName None)

      testCase
        "returns the name for a dotfile"
        (fun () -> ".gitignore" |> expectFileName (Some ".gitignore"))
    ]

  let private expectFilePrefix expected path =
    let actual = path |> Path |> Path.filePrefix
    Expect.equal actual expected ""

  let private filePrefix =
    testList "filePrefix" [
      testCase
        "returns the prefix for a simple file"
        (fun () -> "foo.txt" |> expectFilePrefix (Some "foo"))

      testCase
        "returns the part before the first dot for a double extension"
        (fun () -> "foo.tar.gz" |> expectFilePrefix (Some "foo"))

      testCase
        "returns the prefix for a nested path"
        (fun () -> "a/b/foo.txt" |> expectFilePrefix (Some "foo"))

      testCase
        "returns the full name when there is no extension"
        (fun () -> "foo" |> expectFilePrefix (Some "foo"))

      testCase
        "returns the full name for a dotfile"
        (fun () -> ".gitignore" |> expectFilePrefix (Some ".gitignore"))

      testCase
        "returns the prefix for a dotfile with an extension"
        (fun () -> ".foo.txt" |> expectFilePrefix (Some ".foo"))

      testCase
        "returns the prefix for a dotfile with multiple extensions"
        (fun () -> ".foo.bar.txt" |> expectFilePrefix (Some ".foo"))

      testCase
        "returns None for an empty path"
        (fun () -> "" |> expectFilePrefix None)

      testCase "returns None for root" (fun () -> "/" |> expectFilePrefix None)

      testCase
        "returns None for dot-dot"
        (fun () -> ".." |> expectFilePrefix None)

      testCase
        "returns the prefix for a path ending with a dot"
        (fun () -> "foo." |> expectFilePrefix (Some "foo"))

      testCase
        "returns the prefix for a windows path"
        (fun () -> @"C:\a\b.txt" |> expectFilePrefix (Some "b"))
    ]

  let private expectFileStem expected path =
    let actual = path |> Path |> Path.fileStem
    Expect.equal actual expected ""

  let private fileStem =
    testList "fileStem" [
      testCase
        "returns the stem for a simple file"
        (fun () -> "foo.txt" |> expectFileStem (Some "foo"))

      testCase
        "returns everything before the last dot for a double extension"
        (fun () -> "foo.tar.gz" |> expectFileStem (Some "foo.tar"))

      testCase
        "returns the stem for a nested path"
        (fun () -> "a/b/foo.txt" |> expectFileStem (Some "foo"))

      testCase
        "returns the full name when there is no extension"
        (fun () -> "foo" |> expectFileStem (Some "foo"))

      testCase
        "returns the full name for a dotfile"
        (fun () -> ".gitignore" |> expectFileStem (Some ".gitignore"))

      testCase
        "returns the stem for a dotfile with an extension"
        (fun () -> ".foo.txt" |> expectFileStem (Some ".foo"))

      testCase
        "returns None for an empty path"
        (fun () -> "" |> expectFileStem None)

      testCase "returns None for root" (fun () -> "/" |> expectFileStem None)

      testCase
        "returns None for dot-dot"
        (fun () -> ".." |> expectFileStem None)

      testCase
        "returns the stem for a path ending with a dot"
        (fun () -> "foo." |> expectFileStem (Some "foo"))

      testCase
        "returns the stem for a windows path"
        (fun () -> @"C:\a\b.txt" |> expectFileStem (Some "b"))
    ]

  let private expectStripped sep expected prefix path =
    let result =
      Path.stripPrefixWith sep (Path prefix) (Path path)
      |> Result.map Path.toString

    Expect.equal result (Ok expected) ""

  let private expectStripErr sep prefix path =
    let result = Path.stripPrefixWith sep (Path prefix) (Path path)

    match result with
    | Error(PathErr.StripPrefixErr _) -> ()
    | Ok p -> failtest $"expected Error, got Ok \"{Path.toString p}\""
    | Error err -> failtest $"expected StripPrefixErr, got %A{err}"

  let private stripPrefix =
    testList "stripPrefix" [
      testCase
        "strips a root prefix"
        (fun () ->
          expectStripped '/' "test/haha/foo.txt" "/" "/test/haha/foo.txt"
        )

      testCase
        "strips a directory prefix"
        (fun () ->
          expectStripped '/' "haha/foo.txt" "/test" "/test/haha/foo.txt"
        )

      testCase
        "strips a prefix with trailing separator"
        (fun () ->
          expectStripped '/' "haha/foo.txt" "/test/" "/test/haha/foo.txt"
        )

      testCase
        "returns empty when prefix matches the full path"
        (fun () -> expectStripped '/' "" "/test/foo.txt" "/test/foo.txt")

      testCase
        "returns empty when prefix matches with trailing separator"
        (fun () -> expectStripped '/' "" "/test/foo.txt/" "/test/foo.txt")

      testCase
        "strips a relative prefix"
        (fun () -> expectStripped '/' "c" "a/b" "a/b/c")

      testCase
        "errors on partial component match"
        (fun () -> expectStripErr '/' "/te" "/test/foo.txt")

      testCase
        "errors when prefix is not a prefix of the path"
        (fun () -> expectStripErr '/' "/haha" "/test/foo.txt")

      testCase
        "errors when mixing relative prefix with absolute path"
        (fun () -> expectStripErr '/' "test" "/test/foo.txt")

      testCase
        "errors when prefix is longer than the path"
        (fun () -> expectStripErr '/' "/a/b/c" "/a")

      testCase
        "strips a windows drive prefix"
        (fun () -> expectStripped '\\' @"a\b" @"C:\" @"C:\a\b")

      testCase
        "errors on mismatched drive prefixes"
        (fun () -> expectStripErr '\\' @"C:\" @"D:\foo")

      testCase
        "strips a unc prefix"
        (fun () ->
          expectStripped '\\' "dir" @"\\server\share" @"\\server\share\dir"
        )
    ]

  let private expectCombined sep expected segment path =
    let actual = path |> Path |> Path.combineWith sep segment |> Path.toString

    Expect.equal actual expected ""

  let private combine =
    testList "combine" [
      testCase
        "appends a segment to a simple path"
        (fun () -> "a" |> expectCombined '/' "a/b" "b")

      testCase
        "appends a segment to a nested path"
        (fun () -> "a/b" |> expectCombined '/' "a/b/c" "c")

      testCase
        "appends a segment to root"
        (fun () -> "/" |> expectCombined '/' "/b" "b")

      testCase
        "does not double the separator when path has a trailing separator"
        (fun () -> "a/" |> expectCombined '/' "a/b" "b")

      testCase
        "appends to an empty path"
        (fun () -> "" |> expectCombined '/' "b" "b")

      testCase
        "returns the path unchanged when segment is empty"
        (fun () -> "a" |> expectCombined '/' "a" "")

      testCase
        "appends a segment to an absolute path"
        (fun () -> "/a/b" |> expectCombined '/' "/a/b/c" "c")

      testCase
        "appends a dotfile segment"
        (fun () -> "a" |> expectCombined '/' "a/.gitignore" ".gitignore")

      testCase
        "appends a segment with an extension"
        (fun () -> "a/b" |> expectCombined '/' "a/b/foo.txt" "foo.txt")

      testCase
        "appends a segment to a windows path"
        (fun () -> @"C:\a" |> expectCombined '\\' @"C:\a\b" "b")

      testCase
        "replaces the path when segment is absolute"
        (fun () -> "a/b" |> expectCombined '/' "/c" "/c")

      testCase
        "replaces the path with a dot segment"
        (fun () -> "a" |> expectCombined '/' "a/." ".")

      testCase
        "replaces the path with a parent segment"
        (fun () -> "a" |> expectCombined '/' "a/.." "..")

      testCase
        "returns empty when both are empty"
        (fun () -> "" |> expectCombined '/' "" "")
    ]

  let private expectJoined sep expected p2 p1 =
    let actual = p1 |> Path |> Path.joinWith sep (Path p2) |> Path.toString

    Expect.equal actual expected ""

  let private join =
    testList "join" [
      testCase
        "appends a relative path"
        (fun () -> "a" |> expectJoined '/' "a/b" "b")

      testCase
        "appends a nested relative path"
        (fun () -> "a" |> expectJoined '/' "a/b/c" "b/c")

      testCase
        "replaces when the second path is absolute"
        (fun () -> "a/b" |> expectJoined '/' "/c" "/c")

      testCase
        "does not double the separator when base has a trailing separator"
        (fun () -> "a/" |> expectJoined '/' "a/b" "b")

      testCase "appends to root" (fun () -> "/" |> expectJoined '/' "/b" "b")

      testCase
        "appends to an empty base"
        (fun () -> "" |> expectJoined '/' "b" "b")

      testCase
        "returns the base unchanged when joining empty"
        (fun () -> "a" |> expectJoined '/' "a" "")

      testCase
        "replaces when joining an absolute onto a windows path"
        (fun () -> @"C:\a" |> expectJoined '\\' @"D:\b" @"D:\b")

      testCase
        "keeps the prefix when joining a root-only path on windows"
        (fun () -> @"C:\a" |> expectJoined '\\' @"C:\b" @"\b")

      testCase
        "returns empty when both are empty"
        (fun () -> "" |> expectJoined '/' "" "")
    ]

  let tests =
    testList "Path" [
      getPrefixAndRoot
      isEmpty
      normalizeLexically
      components
      parent
      extension
      withExtension
      fileName
      filePrefix
      fileStem
      stripPrefix
      combine
      join
    ]
