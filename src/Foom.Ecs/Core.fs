﻿namespace Foom.Ecs

open System
open System.Diagnostics
open System.Reflection
open System.Collections.Generic
open System.Collections.Concurrent
open System.Threading.Tasks
open System.Runtime.InteropServices

open Foom.Collections

#nowarn "9"

[<Struct; StructLayout (LayoutKind.Explicit)>]
type Entity =

    [<FieldOffset (0)>]
    val Index : int

    [<FieldOffset (4)>]
    val Version : uint32

    [<FieldOffset (0); DefaultValue>]
    val Id : uint64

    new (index, version) = { Index = index; Version = version }

    member this.IsZero = this.Id = 0UL

    override this.ToString () = String.Format ("(Entity #{0}.{1})", this.Index, this.Version)

[<AbstractClass>]
type Component () =

    member val Owner = Entity (0, 0u) with get, set

type IEvent = interface end

module Events =

    [<Sealed>]
    type ComponentRemoved<'T when 'T :> Component> (ent: Entity) = 

        member this.Entity = ent

        interface IEvent

    [<Sealed>]
    type EntitySpawned (ent: Entity) =

        member this.Entity = ent

        interface IEvent

    [<Sealed>]
    type EntityDestroyed (ent: Entity) =

        member this.Entity = ent

        interface IEvent

open Events

[<ReferenceEquality>]
type EventAggregator  =
    {
        Lookup: ConcurrentDictionary<Type, obj>

        ComponentAddedLookup: Dictionary<Type, obj * (obj -> unit)>
    }

    static member Create () =
        {
            Lookup = ConcurrentDictionary<Type, obj> ()

            ComponentAddedLookup = Dictionary ()
        }

    member this.Publish (event: 'T when 'T :> IEvent and 'T : not struct) =
        let mutable value = Unchecked.defaultof<obj>
        if this.Lookup.TryGetValue (typeof<'T>, &value) then
            (value :?> Event<'T>).Trigger event

    member this.GetEvent<'T when 'T :> IEvent> () =
       this.Lookup.GetOrAdd (typeof<'T>, valueFactory = (fun _ -> Event<'T> () :> obj)) :?> Event<'T>

    member this.GetComponentAddedEvent<'T when 'T :> Component> () =
        let t = typeof<'T>
        let mutable o = Unchecked.defaultof<obj * (obj -> unit)>
        if (this.ComponentAddedLookup.TryGetValue (t, &o)) then
            let (event, trigger) = o
            (event :?> Event<'T>)
        else
            let e = Event<'T> ()
            let trigger = (fun (o : obj) ->
                match o with
                | :? 'T as o -> e.Trigger o
                | _ -> ()
            )
            this.ComponentAddedLookup.[t] <- (e :> obj, trigger)
            e

    member this.TryGetComponentAddedTrigger (t : Type, [<Out>] trigger : byref<obj -> unit>) =
        let mutable o = Unchecked.defaultof<obj * (obj -> unit)>
        if (this.ComponentAddedLookup.TryGetValue (t, &o)) then
            let (_, trigger') = o
            trigger <- trigger'
            true
        else
            false