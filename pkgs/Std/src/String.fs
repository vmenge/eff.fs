namespace EffSharp.Std

open System

module String =
  /// Returns true if the string is null or empty.
  let isNullOrEmpty (str: string) = String.IsNullOrEmpty str
  /// Returns true if the string is null, empty, or consists only of whitespace.
  let isNullOrWhiteSpace (str: string) = String.IsNullOrWhiteSpace str
  /// Returns the length of the string.
  let len (str: string) = str.Length

  /// Extracts a substring of the given length starting at the given index.
  /// Returns None if the index or length is out of range.
  let substring (start: int) (len: int) (str: string) : string option =
    try
      str.Substring(start, len) |> Some
    with _ ->
      None

  /// Extracts the substring from the given index to the end.
  /// Returns None if the index is out of range.
  let substringFrom (start: int) (str: string) : string option =
    try
      str.Substring(start) |> Some
    with _ ->
      None

  /// Splits the string on any of the given separator characters.
  let splitBy (separators: char array) (str: string) = str.Split(separators)

  /// Splits the string on the first occurrence of the separator.
  /// Returns a tuple of the part before and after the separator, or None if not found.
  let splitOnce (separator: string) (str: string) : (string * string) option =
    let i = str.IndexOf separator

    if i < 0 then
      None
    else
      let left = str.Substring(0, i)
      let right = str.Substring(i + separator.Length)

      Some(left, right)

  /// Splits the string on the last occurrence of the separator.
  /// Returns a tuple of the part before and after the separator, or None if not found.
  let revSplitOnce
    (separator: string)
    (str: string)
    : (string * string) option =
    let i = str.LastIndexOf separator

    if i < 0 then
      None
    else
      let left = str.Substring(0, i)
      let right = str.Substring(i + separator.Length)

      Some(left, right)

  /// Splits the string on the last occurrence of any of the given separators.
  /// Returns a tuple of the part before and after the separator, or None if not found.
  let revSplitOnceOf
    (separators: string array)
    (str: string)
    : (string * string) option =
    let mutable bestIdx = -1
    let mutable bestLen = 0

    for sep in separators do
      let i = str.LastIndexOf(sep)

      if i > bestIdx then
        bestIdx <- i
        bestLen <- sep.Length

    if bestIdx < 0 then
      None
    else
      Some(str.Substring(0, bestIdx), str.Substring(bestIdx + bestLen))

  /// Joins a sequence of strings with the given character separator.
  let joinWith (separator: char) (values: string seq) =
    String.Join(separator, values)

  /// Joins a sequence of strings with the given string separator.
  let joinWithString (separator: string) (values: string seq) =
    String.Join(separator, values)

  /// Returns true if the string starts with the given value.
  let startsWith (value: string) (str: string) = str.StartsWith(value)

  let item (i: int) (str: string) : char option =
    if i >= 0 && i < str.Length then Some str[i] else None
