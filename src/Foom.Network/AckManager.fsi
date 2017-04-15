﻿namespace Foom.Network

open System

[<Sealed>]
type AckManager =

    member ForEachPending : (int -> Packet -> unit) -> unit

    member Ack : int -> unit

    member MarkCopy : Packet -> unit

    new : unit -> AckManager
