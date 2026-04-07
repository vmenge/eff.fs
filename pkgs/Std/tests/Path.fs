namespace EffSharp.Std.Tests

open System
open Expecto
open EffSharp.Std

module Path =
  let private normalize path = Path.make path |> Path.normalizeLexically

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
        (fun () -> normalize "a/b/../c" |> expectNormalized "a/c")

      testCase
        "removes dot segments and repeated separators"
        (fun () -> normalize "./a//b/." |> expectNormalized "a/b")

      testCase
        "errors on a leading relative parent segment"
        (fun () -> normalize "../a" |> expectNormalizeErr)

      testCase
        "errors on multiple leading relative parent segments"
        (fun () -> normalize "../../a" |> expectNormalizeErr)

      testCase
        "errors when cancellation would leave a leading relative parent segment"
        (fun () -> normalize "a/../../b" |> expectNormalizeErr)

      testCase
        "clamps absolute paths at the root"
        (fun () -> normalize "/a/../../b" |> expectNormalized "/b")

      testCase
        "returns dot when a relative path collapses to empty"
        (fun () -> normalize "a/.." |> expectNormalized ".")

      testCase
        "treats whitespace-only paths as lexical paths instead of empty input"
        (fun () -> normalize " " |> expectNormalized " ")

      testCase
        "drops a trailing separator after lexical normalization"
        (fun () -> normalize "foo/bar/" |> expectNormalized "foo/bar")

      testCase
        "windows rooted but not fully qualified paths should not preserve rooted-only semantics"
        (fun () ->
          if OperatingSystem.IsWindows() then
            normalize "\\..\\a" |> expectNormalized "\\a"
        )
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
          if not (OperatingSystem.IsWindows()) then
            "/a/b" |> expectComponents [ RootDir; Normal "a"; Normal "b" ]
        )

      testCase
        "recognizes a windows drive prefix and root separately"
        (fun () ->
          if OperatingSystem.IsWindows() then
            @"C:\a\b"
            |> expectComponents [
              Prefix "C:"
              RootDir
              Normal "a"
              Normal "b"
            ]
        )

      testCase
        "recognizes a windows drive prefix without a root"
        (fun () ->
          if OperatingSystem.IsWindows() then
            @"C:a\b"
            |> expectComponents [ Prefix "C:"; Normal "a"; Normal "b" ]
        )

      testCase
        "recognizes a windows unc prefix and root separately"
        (fun () ->
          if OperatingSystem.IsWindows() then
            @"\\server\share\dir"
            |> expectComponents [
              Prefix @"\\server\share"
              RootDir
              Normal "dir"
            ]
        )
    ]

  let tests = testList "Path" [ isEmpty; normalizeLexically; components ]
