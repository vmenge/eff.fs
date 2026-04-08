namespace EffSharp.Std.Tests

open Expecto
open EffSharp.Std

module String =
  let private splitOnce =
    testList "splitOnce" [
      testCase
        "splits on the first occurrence of the separator"
        (fun () ->
          let result = String.splitOnce ":" "a:b:c"
          Expect.equal result (Some("a", "b:c")) ""
        )

      testCase
        "returns None when the separator is not found"
        (fun () ->
          let result = String.splitOnce ":" "abc"
          Expect.equal result None ""
        )

      testCase
        "returns empty left when the separator is at the start"
        (fun () ->
          let result = String.splitOnce ":" ":abc"
          Expect.equal result (Some("", "abc")) ""
        )

      testCase
        "returns empty right when the separator is at the end"
        (fun () ->
          let result = String.splitOnce ":" "abc:"
          Expect.equal result (Some("abc", "")) ""
        )

      testCase
        "returns two empty strings when the input is just the separator"
        (fun () ->
          let result = String.splitOnce ":" ":"
          Expect.equal result (Some("", "")) ""
        )

      testCase
        "returns None for an empty input"
        (fun () ->
          let result = String.splitOnce ":" ""
          Expect.equal result None ""
        )

      testCase
        "handles a multi-character separator"
        (fun () ->
          let result = String.splitOnce "::" "a::b::c"
          Expect.equal result (Some("a", "b::c")) ""
        )
    ]

  let private revSplitOnce =
    testList "revSplitOnce" [
      testCase
        "splits on the last occurrence of the separator"
        (fun () ->
          let result = String.revSplitOnce ":" "a:b:c"
          Expect.equal result (Some("a:b", "c")) ""
        )

      testCase
        "returns None when the separator is not found"
        (fun () ->
          let result = String.revSplitOnce ":" "abc"
          Expect.equal result None ""
        )

      testCase
        "returns empty right when the separator is at the end"
        (fun () ->
          let result = String.revSplitOnce ":" "abc:"
          Expect.equal result (Some("abc", "")) ""
        )

      testCase
        "returns empty left when the separator is at the start"
        (fun () ->
          let result = String.revSplitOnce ":" ":abc"
          Expect.equal result (Some("", "abc")) ""
        )

      testCase
        "returns two empty strings when the input is just the separator"
        (fun () ->
          let result = String.revSplitOnce ":" ":"
          Expect.equal result (Some("", "")) ""
        )

      testCase
        "returns None for an empty input"
        (fun () ->
          let result = String.revSplitOnce ":" ""
          Expect.equal result None ""
        )

      testCase
        "handles a multi-character separator"
        (fun () ->
          let result = String.revSplitOnce "::" "a::b::c"
          Expect.equal result (Some("a::b", "c")) ""
        )
    ]

  let private revSplitOnceOf =
    testList "revSplitOnceOf" [
      testCase
        "splits on the last occurrence among multiple separators"
        (fun () ->
          let result = String.revSplitOnceOf [| "/"; "\\" |] "a/b\\c"
          Expect.equal result (Some("a/b", "c")) ""
        )

      testCase
        "returns None when no separator is found"
        (fun () ->
          let result = String.revSplitOnceOf [| "/"; "\\" |] "abc"
          Expect.equal result None ""
        )

      testCase
        "picks the rightmost match when separators appear at different positions"
        (fun () ->
          let result = String.revSplitOnceOf [| "."; "/" |] "a.b/c.d"
          Expect.equal result (Some("a.b/c", "d")) ""
        )

      testCase
        "handles a multi-character separator"
        (fun () ->
          let result = String.revSplitOnceOf [| "::" |] "a::b::c"
          Expect.equal result (Some("a::b", "c")) ""
        )

      testCase
        "returns None for an empty input"
        (fun () ->
          let result = String.revSplitOnceOf [| "/" |] ""
          Expect.equal result None ""
        )

      testCase
        "returns None for an empty separators array"
        (fun () ->
          let result = String.revSplitOnceOf [||] "a/b"
          Expect.equal result None ""
        )
    ]

  let tests = testList "String" [ splitOnce; revSplitOnce; revSplitOnceOf ]
