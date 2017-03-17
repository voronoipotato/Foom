﻿namespace Foom.Network

open System

type IPacket = 

    abstract ReadReliableString : unit -> string

type IConnectedClient =

    abstract Address : string

type IServer =
    inherit IDisposable

    abstract Start : unit -> unit

    abstract Stop : unit -> unit

    abstract Heartbeat : unit -> unit

    abstract ClientConnected : IEvent<IConnectedClient>

    abstract ClientPacketReceived : IEvent<IConnectedClient * IPacket>

    abstract BroadcastReliableString : string -> unit

type IClient =
    inherit IDisposable

    abstract Connect : string -> Async<bool>

    abstract Heartbeat : unit -> unit

    abstract ServerPacketReceived : IEvent<IPacket>