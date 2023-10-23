namespace App

open System

[<RequireQualifiedAccess>]
module Observable =

  let tap
    (observe: 'Type -> unit)
    (observable: IObservable<'Type>)
    : IObservable<'Type> =
    Observable.map
      (fun value ->
        observe value
        value
      )
      observable
