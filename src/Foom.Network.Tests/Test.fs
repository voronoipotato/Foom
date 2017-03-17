﻿namespace Foom.Network.Tests

open System

open NUnit.Framework

open Foom.Network

[<TestFixture>]
type Test() = 

    [<Test>]
    member x.StartClientAndServer () =
        use server = new DesktopServer () :> IServer
        use client = new DesktopClient () :> IClient

        let mutable str = ""
        server.ClientConnected.Add (fun s -> str <- s.Address)

        server.Start () 
        |> ignore

        client.Connect ("127.0.0.1") 
        |> Async.RunSynchronously
        |> ignore

        server.Heartbeat ()
        Assert.True (str.Contains("127.0.0.1"))

    [<Test>]
    member x.SendAndReceiveReliableString () =
        use server = new DesktopServer () :> IServer
        use client = new DesktopClient () :> IClient

        let mutable str = ""
        client.ServerPacketReceived.Add (fun (packet) ->
            str <- packet.ReadReliableString ()
            //if str.Equals ("reliablestring") then
            //    failwith "wut ups"
        )

        server.Start () 
        |> ignore

        client.Connect ("127.0.0.1") 
        |> Async.RunSynchronously
        |> ignore

        server.Heartbeat ()

        for i = 1 to 1000 do
            server.BroadcastReliableString ("wrong;")

        server.BroadcastReliableString ("reliablestring")

        client.Heartbeat ()

        let same = str = "reliablestring"
        Assert.True (same)