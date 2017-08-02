﻿namespace Foom.Network

open System
open System.Collections.Generic
open System.IO.Compression

type BasicChannelState =
    {
        sharedPacketPool :          PacketPool

        unreliableReceiver :        UnreliableReceiver
        unreliableSender :          UnreliableSender

        reliableOrderedReceiver :   ReliableOrderedReceiver
        reliableOrderedSender :     ReliableOrderedChannel

        reliableOrderedAckSender :  ReliableOrderedAckSender
    }

module BasicChannelState =

    let create packetPool receive send sendAck =
        let reliableOrderedAckSender = ReliableOrderedAckSender (packetPool, send)

        let sendAck = fun ack -> sendAck ack reliableOrderedAckSender.Send

        {
            sharedPacketPool = packetPool

            unreliableReceiver = UnreliableReceiver (packetPool, receive)
            unreliableSender = UnreliableSender (packetPool, send)

            reliableOrderedReceiver = ReliableOrderedReceiver (packetPool, sendAck, receive)
            reliableOrderedSender = ReliableOrderedChannel (packetPool, send)

            reliableOrderedAckSender = reliableOrderedAckSender
        }

    let send bytes startIndex size packetType state =
        match packetType with
        | PacketType.Unreliable ->
            state.unreliableSender.Send (bytes, startIndex, size)

        | PacketType.ReliableOrdered ->
            state.reliableOrderedSender.Send (bytes, startIndex, size)

        | PacketType.ReliableOrderedAck ->
            state.reliableOrderedAckSender.Send (bytes, startIndex, size)

        | _ -> failwith "packet type not supported"

    let receive time (packet : Packet) state =
        match packet.Type with

        | PacketType.Unreliable ->
            state.unreliableReceiver.Receive (time, packet)
            true

        | PacketType.ReliableOrdered ->
            state.reliableOrderedReceiver.Receive (time, packet)
            true

        | PacketType.ReliableOrderedAck ->
            packet.ReadAcks state.reliableOrderedSender.Ack
            state.sharedPacketPool.Recycle packet
            true

        | _ -> false

type SendStreamState =
    {
        sendStream :    ByteStream
        sendWriter :    ByteWriter
    }

module SendStreamState =

    let create () =
        let sendStream = ByteStream (Array.zeroCreate <| 1024 * 1024)
        {
            sendStream = sendStream
            sendWriter = ByteWriter sendStream
        }

    let write (msg : 'T) f (state : SendStreamState) =

        let sendStream = state.sendStream
        let sendWriter = state.sendWriter

        let startIndex = sendStream.Position

        match Network.lookup.TryGetValue typeof<'T> with
        | true, id ->
            let pickler = Network.FindTypeById id
            sendWriter.WriteByte (byte id)
            pickler.serialize (msg :> obj) sendWriter

            let size = sendStream.Position - startIndex

            f sendStream.Raw startIndex size

        | _ -> ()

type ReceiveStreamState =
    {
        receiveStream : ByteStream
        receiveWriter : ByteWriter
        receiveReader : ByteReader
    }

module ReceiveStreamState =

    let create () =
        let receiveStream = ByteStream (Array.zeroCreate <| 1024 * 1024)
        {
            receiveStream = receiveStream
            receiveWriter = ByteWriter receiveStream
            receiveReader = ByteReader receiveStream
        }

    let rec read' (subscriptions : Event<obj> []) (state : ReceiveStreamState) =
        let reader = state.receiveReader
        let typeId = reader.ReadByte () |> int

        if subscriptions.Length > typeId && typeId >= 0 then
            let pickler = Network.FindTypeById typeId
            let msg = pickler.ctor reader
            pickler.deserialize msg reader
            subscriptions.[typeId].Trigger msg
        else
            failwith "This shouldn't happen."

    let read subscriptions state =
        state.receiveStream.Position <- 0
        while not state.receiveReader.IsEndOfStream do
            read' subscriptions state

[<AutoOpen>]
module StateHelpers =

    let createSubscriptions () =
        Array.init 1024 (fun _ -> Event<obj> ())

type ClientState =
    {
        packetPool : PacketPool
        sendStreamState : SendStreamState
        receiveStreamState : ReceiveStreamState
        subscriptions : Event<obj> []
        udpClient : IUdpClient
        connectionTimeout : TimeSpan
        peerConnected : Event<IUdpEndPoint>
        peerDisconnected : Event<IUdpEndPoint>
        mutable lastReceiveTime : TimeSpan

        basicChannelState : BasicChannelState
    }

module ClientState =

    let create (udpClient : IUdpClient) connectionTimeout =

        let packetPool = PacketPool 1024
        let sendStreamState = SendStreamState.create ()
        let receiveStreamState = ReceiveStreamState.create ()

        let basicChannelState =
            BasicChannelState.create packetPool
                (fun packet ->
                    receiveStreamState.receiveWriter.WriteRawBytes (packet.Raw, sizeof<PacketHeader>, packet.DataLength)
                )
                (fun bytes size ->
                    udpClient.Send (bytes, size) |> ignore
                )
                (fun ack send ->
                    let startIndex = sendStreamState.sendStream.Position
                    sendStreamState.sendWriter.WriteUInt16 ack
                    let size = sendStreamState.sendStream.Position - startIndex
                    send (sendStreamState.sendStream.Raw, startIndex, size)
                )

        {
            packetPool = packetPool
            sendStreamState = sendStreamState
            receiveStreamState = receiveStreamState
            subscriptions = createSubscriptions ()
            udpClient = udpClient
            connectionTimeout = connectionTimeout
            peerConnected = Event<IUdpEndPoint> ()
            peerDisconnected = Event<IUdpEndPoint> ()
            basicChannelState = basicChannelState
            lastReceiveTime = TimeSpan.Zero
        }

    let receive time (packet : Packet) (state : ClientState) =
        match packet.Type with

        | PacketType.ConnectionAccepted ->
            state.peerConnected.Trigger (state.udpClient.RemoteEndPoint)
            state.packetPool.Recycle packet
            true

        | PacketType.Ping ->
            let sendPacket = state.packetPool.Get ()
            sendPacket.Type <- PacketType.Pong
            sendPacket.Writer.Write<TimeSpan> (packet.Reader.Read<TimeSpan>())
            state.udpClient.Send (packet.Raw, packet.Length) |> ignore
            state.packetPool.Recycle sendPacket
            state.packetPool.Recycle packet
            true

        | PacketType.Disconnect ->
            state.peerDisconnected.Trigger (state.udpClient.RemoteEndPoint)
            state.packetPool.Recycle packet
            true

        | _ -> false

type ConnectedClientState =
    {
        packetPool : PacketPool
        sendStreamState : SendStreamState
        receiveStreamState : ReceiveStreamState
        endPoint : IUdpEndPoint
        heartbeatInterval : TimeSpan
        mutable heartbeatTime : TimeSpan
        mutable pingTime : TimeSpan
        mutable lastReceiveTime : TimeSpan

        basicChannelState : BasicChannelState
    }

module ConnectedClientState =

    let create (udpServer : IUdpServer) endPoint heartbeatInterval time =

        let packetPool = PacketPool 1024
        let sendStreamState = SendStreamState.create ()
        let receiveStreamState = ReceiveStreamState.create ()

        let basicChannelState =
            BasicChannelState.create packetPool
                (fun packet ->
                    receiveStreamState.receiveWriter.WriteRawBytes (packet.Raw, sizeof<PacketHeader>, packet.DataLength)
                )
                (fun bytes size ->
                    udpServer.Send (bytes, size, endPoint) |> ignore
                )
                (fun ack send ->
                    let startIndex = sendStreamState.sendStream.Position
                    sendStreamState.sendWriter.WriteUInt16 ack
                    let size = sendStreamState.sendStream.Position - startIndex
                    send (sendStreamState.sendStream.Raw, startIndex, size)
                )

        {
            packetPool = packetPool
            sendStreamState = sendStreamState
            receiveStreamState = receiveStreamState
            endPoint = endPoint
            heartbeatInterval = heartbeatInterval
            heartbeatTime = time
            pingTime = TimeSpan.Zero
            lastReceiveTime = time
            basicChannelState = basicChannelState
        }

    let receive time (packet : Packet) state =
        match packet.Type with
        | PacketType.Pong ->

            state.pingTime <- time - packet.Reader.Read<TimeSpan> ()
            state.packetPool.Recycle packet
            true
        | _ -> false

type ServerState =
    {
        packetPool : PacketPool
        sendStreamState : SendStreamState
        subscriptions : Event<obj> []
        udpServer : IUdpServer
        connectionTimeout : TimeSpan
        peerLookup : Dictionary<IUdpEndPoint, ConnectedClientState>
        peerConnected : Event<IUdpEndPoint>
        peerDisconnected : Event<IUdpEndPoint>
    }

module ServerState =

    let create udpServer connectionTimeout =

        let packetPool = PacketPool 1024
        let sendStreamState = SendStreamState.create ()

        {
            packetPool = packetPool
            sendStreamState = sendStreamState
            subscriptions = createSubscriptions ()
            udpServer = udpServer
            connectionTimeout = connectionTimeout
            peerLookup = Dictionary ()
            peerConnected = Event<IUdpEndPoint> ()
            peerDisconnected = Event<IUdpEndPoint> ()
        }

    let receive time (packet : Packet) endPoint state =
        match packet.Type with
        | PacketType.ConnectionRequested ->
            let ccState = ConnectedClientState.create state.udpServer endPoint (TimeSpan.FromSeconds 1.) time

            state.peerLookup.Add (endPoint, ccState)

            let packet = Packet ()
            packet.Type <- PacketType.ConnectionAccepted
            state.udpServer.Send (packet.Raw, packet.Length, endPoint) |> ignore
            state.packetPool.Recycle packet
            state.peerConnected.Trigger endPoint
            true

        | PacketType.Disconnect ->
            
            state.peerLookup.Remove endPoint |> ignore
            state.packetPool.Recycle packet
            true
        | _ -> false

[<RequireQualifiedAccess>]
type Udp =
    | Client of ClientState
    | Server of ServerState

[<AbstractClass>]
type Peer (udp : Udp) =

    member this.Udp = udp

    member this.PacketPool = 
        match udp with
        | Udp.Client state -> state.packetPool
        | Udp.Server state -> state.packetPool

    member this.Connect (address, port) =
        match udp with
        | Udp.Client state ->
            if state.udpClient.Connect (address, port) then
                let packet = Packet ()
                packet.Type <- PacketType.ConnectionRequested
                state.udpClient.Send (packet.Raw, packet.Length) |> ignore
        | _ -> failwith "Clients can only connect."

    member this.Subscribe<'T> f =
        let subscriptions =
            match udp with
            | Udp.Client state -> state.subscriptions
            | Udp.Server state -> state.subscriptions

        match Network.lookup.TryGetValue typeof<'T> with
        | true, id ->
            let evt = subscriptions.[id]
            let pickler = Network.FindTypeById id

            evt.Publish.Add (fun msg -> f (msg :?> 'T))
        | _ -> ()

    member this.Send (bytes, startIndex, size, packetType) =
        match udp with
        | Udp.Server state ->
            state.peerLookup
            |> Seq.iter (fun pair ->
                let ccState = pair.Value
                BasicChannelState.send bytes startIndex size packetType ccState.basicChannelState
            )

        | Udp.Client state ->
            BasicChannelState.send bytes startIndex size packetType state.basicChannelState

    member private this.Send<'T> (msg : 'T, packetType) =
        match udp with
        | Udp.Client state -> state.sendStreamState
        | Udp.Server state -> state.sendStreamState
        |> SendStreamState.write msg (fun bytes startIndex size -> 
            this.Send (bytes, startIndex, size, packetType)
        )

    member this.SendUnreliable<'T> (msg : 'T) =
        this.Send<'T> (msg, PacketType.Unreliable)

    member this.SendReliableOrdered<'T> (msg : 'T) =
        this.Send<'T> (msg, PacketType.ReliableOrdered)

    member this.ReceivePacket (time, packet : Packet, endPoint : IUdpEndPoint) =
        match udp with
        | Udp.Client state -> 
            state.lastReceiveTime <- time

            match BasicChannelState.receive time packet state.basicChannelState with
            | false ->
                match ClientState.receive time packet state with
                | false -> state.packetPool.Recycle packet
                | _ -> ()
            | _ -> ()

        | Udp.Server state ->
            match state.peerLookup.TryGetValue endPoint with
            | (true, ccState) -> 
                ccState.lastReceiveTime <- time

                // TODO: Disconnect the client if there are no more packets available in their pool.
                // TODO: Disconnect the client if too many packets were received in a time frame.

                // Trade packets from server and client
                ccState.packetPool.Get ()
                |> state.packetPool.Recycle

                match BasicChannelState.receive time packet ccState.basicChannelState with
                | false ->
                    match ConnectedClientState.receive time packet ccState with
                    | false -> ccState.packetPool.Recycle packet
                    | _ -> ()
                | _ -> ()
                
            | _ ->

                match ServerState.receive time packet endPoint state with
                | false -> state.packetPool.Recycle packet
                | _ -> ()

    member this.Update time =
        match udp with
        | Udp.Client state ->

            state.receiveStreamState.receiveStream.Length <- 0

            let client = state.udpClient
            let packetPool = state.packetPool

            while client.IsDataAvailable do
                let packet = packetPool.Get ()
                let byteCount = client.Receive (packet.Raw, 0, packet.Raw.Length)
                if byteCount > 0 then
                    packet.Length <- byteCount
                    this.ReceivePacket (time, packet, client.RemoteEndPoint)
                else
                    packetPool.Recycle packet

            state.basicChannelState.unreliableReceiver.Update time
            state.basicChannelState.reliableOrderedReceiver.Update time

            ReceiveStreamState.read state.subscriptions state.receiveStreamState
            
            state.basicChannelState.unreliableSender.Update time
            state.basicChannelState.reliableOrderedAckSender.Update time
            state.basicChannelState.reliableOrderedSender.Update time

            state.sendStreamState.sendStream.Length <- 0

            if time > state.lastReceiveTime + state.connectionTimeout then
                state.peerDisconnected.Trigger state.udpClient.RemoteEndPoint

        | Udp.Server state ->

            let server = state.udpServer
            let packetPool = state.packetPool

            while server.IsDataAvailable do
                let packet = packetPool.Get ()
                let mutable endPoint = Unchecked.defaultof<IUdpEndPoint>
                let byteCount = server.Receive (packet.Raw, 0, packet.Raw.Length, &endPoint)
                // TODO: Check to see if endPoint is banned.
                if byteCount > 0 then
                    packet.Length <- byteCount
                    this.ReceivePacket (time, packet, endPoint)
                else
                    packetPool.Recycle packet

            let endPointRemovals = Queue ()
            // Check for connection timeouts
            state.peerLookup
            |> Seq.iter (fun pair ->
                let endPoint = pair.Key
                let ccState = pair.Value

                if time > ccState.lastReceiveTime + state.connectionTimeout then

                    let packet = packetPool.Get ()
                    packet.Type <- PacketType.Disconnect
                    server.Send (packet.Raw, packet.Length, endPoint) |> ignore
                    packetPool.Recycle packet

                    endPointRemovals.Enqueue endPoint

                if time > ccState.heartbeatTime + ccState.heartbeatInterval then

                    ccState.heartbeatTime <- time

                    let packet = packetPool.Get ()
                    packet.Type <- PacketType.Ping
                    packet.Writer.Write<TimeSpan> (time)
                    server.Send (packet.Raw, packet.Length, endPoint) |> ignore
                    packetPool.Recycle packet

                ccState.basicChannelState.unreliableReceiver.Update time
                ccState.basicChannelState.reliableOrderedReceiver.Update time

                ReceiveStreamState.read state.subscriptions ccState.receiveStreamState
            
                ccState.basicChannelState.unreliableSender.Update time
                ccState.basicChannelState.reliableOrderedAckSender.Update time
                ccState.basicChannelState.reliableOrderedSender.Update time

                ccState.sendStreamState.sendStream.Length <- 0
            )

            state.sendStreamState.sendStream.Length <- 0

            while endPointRemovals.Count > 0 do
                let endPoint = endPointRemovals.Dequeue ()
                state.peerLookup.Remove endPoint |> ignore
                state.peerDisconnected.Trigger endPoint


    interface IDisposable with

        member this.Dispose () =
            match udp with
            | Udp.Client state -> state.udpClient.Dispose ()
            | Udp.Server state-> state.udpServer.Dispose ()

type ServerPeer (udpServer, connectionTimeout) =
    inherit Peer (Udp.Server (ServerState.create udpServer connectionTimeout))

    member this.ClientConnected =
        match this.Udp with
        | Udp.Server state -> state.peerConnected.Publish
        | _ -> failwith "should not happen"

    member this.ClientDisconnected =
        match this.Udp with
        | Udp.Server state -> state.peerDisconnected.Publish
        | _ -> failwith "should not happen"

    member this.ClientPacketPoolMaxCount =
        match this.Udp with
        | Udp.Server server -> server.peerLookup |> Seq.sumBy (fun pair1 -> pair1.Value.packetPool.MaxCount)
        | _ -> failwith "nope"

    member this.ClientPacketPoolCount =
        match this.Udp with
        | Udp.Server server -> server.peerLookup |> Seq.sumBy (fun pair1 -> pair1.Value.packetPool.Count)
        | _ -> failwith "nope"

type ClientPeer (udpClient) =
    inherit Peer (Udp.Client (ClientState.create udpClient (TimeSpan.FromSeconds 5.)))

    member this.Connected =
        match this.Udp with
        | Udp.Client state -> state.peerConnected.Publish
        | _ -> failwith "should not happen"

    member this.Disconnected =
        match this.Udp with
        | Udp.Client state -> state.peerDisconnected.Publish
        | _ -> failwith "should not happen"