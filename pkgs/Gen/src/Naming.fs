namespace EffSharp.Gen

open System

module Naming =
  let private stripInterfacePrefix (name: string) =
    if name.Length > 1 && name[0] = 'I' && Char.IsUpper(name[1]) then
      name.Substring(1)
    else
      name

  let wrappedEnvironmentName serviceName = $"E{stripInterfacePrefix serviceName}"

  let environmentName mode serviceName =
    if mode = Mode.Wrap then
      wrappedEnvironmentName serviceName
    else
      serviceName

  let propertyName serviceName = stripInterfacePrefix serviceName

  let wrapperName (memberName: string) =
    if String.IsNullOrEmpty(memberName) then
      memberName
    else
      $"{Char.ToLowerInvariant(memberName[0])}{memberName.Substring(1)}"
