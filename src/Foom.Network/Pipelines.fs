﻿[<AutoOpen>]
module Foom.Network.Pipelines

open System
open System.Collections.Generic

open Foom.Network
open Foom.Network.Pipeline

module NewPipeline =

    open NewPipeline

    let ReliableOrderedAckReceiver (packetPool : PacketPool) (ackManager : AckManager) ack =
        let mutable nextSeqId = 0us

        Filter (fun (packets : Packet seq) callback ->

            packets
            |> Seq.iter (fun packet ->
                if nextSeqId = packet.SequenceId then
                    callback packet
                    ack nextSeqId
                    nextSeqId <- nextSeqId + 1us
                else
                    ackManager.MarkCopy packet
                    packetPool.Recycle packet
            )

            ackManager.ForEachPending (fun seqId copyPacket ->
                if int nextSeqId = seqId then
                    let packet = packetPool.Get ()
                    copyPacket.CopyTo packet
                    ackManager.Ack seqId
                    ack nextSeqId
                    nextSeqId <- nextSeqId + 1us
                    callback packet
            )
        )

[<Struct>]
type Data = { bytes : byte []; startIndex : int; size : int }

let createMergeFilter (packetPool : PacketPool) =
        let packets = ResizeArray ()
        NewPipeline.Filter (fun data callback ->
            data
            |> Seq.iter (fun data ->
                if packets.Count = 0 then
                    let packet = packetPool.Get ()
                    if packet.LengthRemaining >= data.size then
                        packet.WriteRawBytes (data.bytes, data.startIndex, data.size)
                        packets.Add packet
                    else
                        let count = (data.size / packet.LengthRemaining) - (if data.size % packet.LengthRemaining > 0 then -1 else 0)
                        let mutable startIndex = data.startIndex
                        failwith "yopac"
                    
                else
                    let packet = packets.[packets.Count - 1]
                    if packet.LengthRemaining >= data.size then
                        packet.WriteRawBytes (data.bytes, data.startIndex, data.size)
                    else
                        let packet = packetPool.Get ()
                        if packet.LengthRemaining >= data.size then
                            packet.WriteRawBytes (data.bytes, data.startIndex, data.size)
                        else
                            failwith "too big"

                        packets.Add packet
            )

            packets |> Seq.iter callback
            packets.Clear ()
        )

let createClientReceiveFilter () =
    NewPipeline.Filter (fun (packets : Packet seq) callback ->
        packets |> Seq.iter callback
    )

let unreliableSender packetPool =
    let mergeFilter = createMergeFilter packetPool
    NewPipeline.createPipeline mergeFilter
    |> NewPipeline.build

let basicReceiver packetPool =
    let receiveFilter = createClientReceiveFilter ()
    NewPipeline.createPipeline receiveFilter
    |> NewPipeline.build


