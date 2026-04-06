namespace EffFs.EffectGen

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
  | Unsupported of rawType: string

type EffectMethod = {
  SourceName: string
  WrapperName: string
  ParameterGroups: ParameterGroup list
  ReturnShape: ReturnShape
}

type EffectInterface = {
  Namespace: string option
  SourceFile: string
  ServiceName: string
  EnvironmentName: string
  PropertyName: string
  Methods: EffectMethod list
}

type GeneratedFile = {
  SourceFile: string
  OutputPath: string
  Contents: string
}
