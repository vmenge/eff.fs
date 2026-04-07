namespace EffSharp.Gen

type Parameter = {
  Name: string
  TypeName: string
}

type ParameterGroup =
  | Single of Parameter
  | Tupled of Parameter list

type ReturnShape =
  | Plain of valueType: string
  | Result of okType: string * errorType: string
  | Task of valueType: string
  | TaskResult of okType: string * errorType: string
  | Async of valueType: string
  | AsyncResult of okType: string * errorType: string
  | ValueTask of valueType: string
  | ValueTaskResult of okType: string * errorType: string
  | Eff of okType: string * errorType: string * environmentType: string
  | Unsupported of rawType: string

type EffectMethod = {
  SourceName: string
  WrapperName: string
  DeclarationLine: int
  DeclarationColumn: int
  ParameterGroups: ParameterGroup list
  ReturnShape: ReturnShape
}

type EffectInterface = {
  Namespace: string option
  SourceFile: string
  Mode: Mode
  ServiceName: string
  ServiceTypeName: string
  EnvironmentName: string
  PropertyName: string
  DeclarationLine: int
  DeclarationColumn: int
  InheritedEnvironments: string list
  Methods: EffectMethod list
}

type GeneratedFile = {
  SourceFile: string
  OutputPath: string
  Contents: string
}

type EffectDiagnostic = {
  Code: string
  Message: string
  FilePath: string
  Line: int
  Column: int
}

type DiscoveryResult = {
  Interfaces: EffectInterface list
  Diagnostics: EffectDiagnostic list
}
