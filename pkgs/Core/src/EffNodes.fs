namespace EffFs.Core

module internal EffNodes =
  type Map<'src, 't, 'e, 'env>
    (source: Eff<'src, 'e, 'env>, mapper: 'src -> 't) =
    inherit Node<'t, 'e, 'env>()
    member _.Source = source
    member _.Mapper = mapper

    interface EffRuntime.INodeRuntime<'env> with
      member _.Enter(_, frames) =
        EffRuntime.Continue(
          (EffRuntime.BoxedEff<'src, 'e, 'env>(source) :> EffRuntime.BoxedEff<'env>),
          (EffRuntime.MapFrame<'src, 't, 'e, 'env>(mapper) :> EffRuntime.Frame<'env>)
          :: frames
        )

  type FlatMap<'src, 't, 'e, 'env>
    (source: Eff<'src, 'e, 'env>, cont: 'src -> Eff<'t, 'e, 'env>) =
    inherit Node<'t, 'e, 'env>()
    member _.Source = source
    member _.Cont = cont

    interface EffRuntime.INodeRuntime<'env> with
      member _.Enter(_, frames) =
        EffRuntime.Continue(
          (EffRuntime.BoxedEff<'src, 'e, 'env>(source) :> EffRuntime.BoxedEff<'env>),
          (EffRuntime.FlatMapFrame<'src, 't, 'e, 'env>(cont) :> EffRuntime.Frame<'env>)
          :: frames
        )

  type MapErr<'t, 'e1, 'e2, 'env>
    (body: Eff<'t, 'e1, 'env>, mapper: 'e1 -> 'e2) =
    inherit Node<'t, 'e2, 'env>()
    member _.Body = body
    member _.Mapper = mapper

    interface EffRuntime.INodeRuntime<'env> with
      member _.Enter(_, frames) =
        EffRuntime.Continue(
          (EffRuntime.BoxedEff<'t, 'e1, 'env>(body) :> EffRuntime.BoxedEff<'env>),
          (EffRuntime.MapErrFrame<'e1, 'e2, 'env>(mapper) :> EffRuntime.Frame<'env>)
          :: frames
        )

  type FlatMapErr<'t, 'e, 'env>
    (body: Eff<'t, 'e, 'env>, handler: 'e -> Eff<'t, 'e, 'env>) =
    inherit Node<'t, 'e, 'env>()
    member _.Body = body
    member _.Handler = handler

    interface EffRuntime.INodeRuntime<'env> with
      member _.Enter(_, frames) =
        EffRuntime.Continue(
          (EffRuntime.BoxedEff<'t, 'e, 'env>(body) :> EffRuntime.BoxedEff<'env>),
          (EffRuntime.FlatMapErrFrame<'t, 'e, 'env>(handler) :> EffRuntime.Frame<'env>)
          :: frames
        )

  type FlatMapExn<'t, 'e, 'env>
    (body: Eff<'t, 'e, 'env>, handler: exn -> Eff<'t, 'e, 'env>) =
    inherit Node<'t, 'e, 'env>()

    interface EffRuntime.INodeRuntime<'env> with
      member _.Enter(_, frames) =
        EffRuntime.Continue(
          (EffRuntime.BoxedEff<'t, 'e, 'env>(body) :> EffRuntime.BoxedEff<'env>),
          (EffRuntime.FlatMapExnFrame<'t, 'e, 'env>(handler) :> EffRuntime.Frame<'env>)
          :: frames
        )

  type Defer<'t, 'e, 'env>
    (body: Eff<'t, 'e, 'env>, cleanup: Eff<unit, 'e, 'env>) =
    inherit Node<'t, 'e, 'env>()
    member _.Body = body
    member _.Cleanup = cleanup

    interface EffRuntime.INodeRuntime<'env> with
      member _.Enter(_, frames) =
        EffRuntime.Continue(
          (EffRuntime.BoxedEff<'t, 'e, 'env>(body) :> EffRuntime.BoxedEff<'env>),
          (EffRuntime.DeferFrame<'e, 'env>(cleanup) :> EffRuntime.Frame<'env>)
          :: frames
        )

  type Bracket<'r, 't, 'e, 'env>
    (
      acquire: Eff<'r, 'e, 'env>,
      usefn: 'r -> Eff<'t, 'e, 'env>,
      release: 'r -> Eff<unit, 'e, 'env>
    ) =
    inherit Node<'t, 'e, 'env>()

    interface EffRuntime.INodeRuntime<'env> with
      member _.Enter(_, frames) =
        EffRuntime.Continue(
          (EffRuntime.BoxedEff<'r, 'e, 'env>(acquire) :> EffRuntime.BoxedEff<'env>),
          (EffRuntime.BracketAcquireFrame<'r, 't, 'e, 'env>(usefn, release)
           :> EffRuntime.Frame<'env>)
          :: frames
        )

  type ProvideFrom<'t, 'e, 'envOuter, 'envInner>
    (project: 'envOuter -> 'envInner, body: Eff<'t, 'e, 'envInner>) =
    inherit Node<'t, 'e, 'envOuter>()

    interface EffRuntime.INodeRuntime<'envOuter> with
      member _.Enter(stepper, frames) =
        let childStepper = stepper.Project project
        let childMachine =
          EffRuntime.TypedMachine<'envInner>(childStepper, childStepper.Step body [])
          :> EffRuntime.Machine

        EffRuntime.Switch(childMachine, fun exit -> stepper.Unwind exit frames)
