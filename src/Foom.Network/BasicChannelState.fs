﻿namespace Foom.Network

open System.Collections.Generic

type BasicChannelState =
    {
        sendStream :                ByteStream
        sharedPacketPool :          PacketPool

        unreliableReceiver :        Receiver
        unreliableSender :          Sender

        reliableOrderedReceiver :   ReceiverAck
        reliableOrderedSender :     SenderAck

        reliableOrderedAckSender :  Sender

        send :                      Packet -> unit
        receive :                   Packet -> unit
    }

    static member Create (packetPool, receive, send) =
        {
            sendStream = new ByteStream (Array.zeroCreate (1024 * 1024))
            sharedPacketPool = packetPool

            unreliableReceiver = Receiver.CreateUnreliable packetPool
            unreliableSender = Sender.CreateUnreliable packetPool

            reliableOrderedReceiver = ReceiverAck.CreateReliableOrdered packetPool
            reliableOrderedSender = SenderAck.CreateReliableOrdered packetPool

            reliableOrderedAckSender = Sender.CreateReliableOrderedAck packetPool

            send = send
            receive = receive
        }

    member this.SendUnreliable (bytes, startIndex, size) =
        this.unreliableSender.Enqueue (bytes, startIndex, size)

    member this.SendReliableOrdered (bytes, startIndex, size) =
        this.reliableOrderedSender.Enqueue (bytes, startIndex, size)

    member this.SendReliableOrderedAck ack =
        let s = this.sendStream.Position
        this.sendStream.Writer.WriteUInt16 ack
        this.reliableOrderedAckSender.Enqueue (this.sendStream.Raw, int s, 2)

    member this.ReceiveUnreliable packet =
        this.unreliableReceiver.Enqueue packet

    member this.Receive (packet : Packet) =
        match packet.Type with

        | PacketType.Unreliable ->
            this.unreliableReceiver.Enqueue packet
            true

        | PacketType.ReliableOrdered ->
            this.reliableOrderedReceiver.Enqueue packet
            true

        | PacketType.ReliableOrderedAck ->
            packet.ReadAcks this.reliableOrderedSender.Ack
            this.sharedPacketPool.Recycle packet
            true

        | _ -> false

    member this.Update time =
        // Receive
        this.unreliableReceiver.Flush time
        this.reliableOrderedReceiver.Flush time

        this.unreliableReceiver.Process this.receive
        this.reliableOrderedReceiver.Process (fun packet ->
            this.SendReliableOrderedAck packet.SequenceId
            this.receive packet
        )

        // Send
        this.unreliableSender.Flush time
        this.reliableOrderedAckSender.Flush time
        this.reliableOrderedSender.Flush time

        this.unreliableSender.Process this.send
        this.reliableOrderedAckSender.Process this.send
        this.reliableOrderedSender.Process this.send

        this.sendStream.SetLength 0L
