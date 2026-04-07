namespace EffSharp.Std

open System

module String =
  let isNullOrEmpty (str: string) = String.IsNullOrEmpty str
  let isNullOrWhiteSpace (str: string) = String.IsNullOrWhiteSpace str
  let len (str: string) = str.Length

  let substring (start: int) (len: int) (str: string) =
    str.Substring(start, len)

  let substringFrom (start: int) (str: string) = str.Substring(start)

  let splitBy (separators: char array) (str: string) = str.Split(separators)

  let joinWith (separator: char) (values: string seq) =
    String.Join(separator, values)

  let joinWithString (separator: string) (values: string seq) =
    String.Join(separator, values)

  let startsWith (value: string) (str: string) = str.StartsWith(value)
