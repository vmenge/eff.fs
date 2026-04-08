namespace EffSharp.Std.Tests

open Expecto
open EffSharp.Std

module Path =
  let private normalize path = Path.make path |> Path.normalizeLexically
  let private normalizeWith sep path = Path.make path |> Path.normalizeLexicallyWith sep

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
    let actual = path |> Path.make |> Path.components |> Seq.toList

    Expect.equal actual expected ""

  let private expectPrefixAndRoot expected path =
    let actual = path |> Path.make |> Path.getPrefixAndRoot

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
    ]

  let private isEmpty =
    testList "isEmpty" [
      testCase
        "returns true for the empty string"
        (fun () -> Path.make "" |> Path.isEmpty |> Expect.isTrue <| "")

      testCase
        "returns false for whitespace-only lexical paths"
        (fun () -> Path.make " " |> Path.isEmpty |> Expect.isFalse <| "")

      testCase
        "returns false for non-empty paths"
        (fun () -> Path.make "foo" |> Path.isEmpty |> Expect.isFalse <| "")
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
        (fun () -> normalizeWith '\\' @"\\server\..\x" |> expectNormalized @"\x")

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
    let actual = path |> Path.make |> Path.parent |> Option.map Path.toString
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

      testCase
        "returns None for root"
        (fun () -> "/" |> expectParent None)

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
        (fun () -> @"\\server\share\a" |> expectParent (Some @"\\server\share\"))

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

  let tests =
    testList "Path" [
      getPrefixAndRoot
      isEmpty
      normalizeLexically
      components
      parent
    ]
