﻿[<RequireQualifiedAccess>]
module Foom.Client.Client

open System.IO
open System.Drawing
open System.Numerics
open System.Collections.Generic

open Foom.Renderer
open Foom.Wad
open Foom.Wad.Geometry
open Foom.Wad.Level
open Foom.Wad.Level.Structures

type ClientState = 
    {
        Window: nativeint
        RenderUpdate: (float32 -> unit)
        Level: Level 
    }

// These are sectors to look for and test to ensure things are working as they should.
// 568 - map10 sunder
// 4 - map10  sunder
// 4371 - map14 sunder
// 28 - e1m1 doom
// 933 - map10 sunder
// 20 - map10 sunder
// 151 - map10 sunder
// 439 - map08 sunder
// 271 - map03 sunder
// 663 - map05 sunder
// 506 - map04 sunder
// 3 - map02 sunder
// 3450 - map11 sunder
// 1558 - map11 sunder
// 240 - map07 sunder
// 2021 - map11 sunder

open Foom.Ecs
open Foom.Ecs.World
open Foom.Renderer.EntitySystem
open Foom.Common.Components
open Foom.Renderer.Components

let init (world: World) =

    // Load up doom wads.

    let doom2Wad = Wad.create (System.IO.File.Open ("doom.wad", System.IO.FileMode.Open)) |> Async.RunSynchronously
    let wad = Wad.createFromWad doom2Wad (System.IO.File.Open ("sunder.wad", System.IO.FileMode.Open)) |> Async.RunSynchronously
    let lvl = Wad.findLevel "e1m1" doom2Wad |> Async.RunSynchronously


    // Extract all doom textures.

    Wad.flats doom2Wad
    |> Array.iter (fun tex ->
        let bmp = new Bitmap(64, 64, Imaging.PixelFormat.Format32bppArgb)

        for i = 0 to 64 - 1 do
            for j = 0 to 64 - 1 do
                let pixel = tex.Pixels.[i + (j * 64)]
                bmp.SetPixel (i, j, Color.FromArgb (255, int pixel.R, int pixel.G, int pixel.B))

        bmp.Save(tex.Name + ".bmp")
        bmp.Dispose ()
    )

    Wad.flats wad
    |> Array.iter (fun tex ->
        let bmp = new Bitmap(64, 64, Imaging.PixelFormat.Format32bppArgb)

        for i = 0 to 64 - 1 do
            for j = 0 to 64 - 1 do
                let pixel = tex.Pixels.[i + (j * 64)]
                bmp.SetPixel (i, j, Color.FromArgb (255, int pixel.R, int pixel.G, int pixel.B))

        bmp.Save(tex.Name + ".bmp")
        bmp.Dispose ()
    )

    // Calculate polygons

    let sectorPolygons =
        lvl.Sectors
        //[| lvl.Sectors.[343] |]
        |> Seq.mapi (fun i s -> 
            System.Diagnostics.Debug.WriteLine ("Sector " + string i)
            (Sector.polygonFlats s, s)
        )



    // Add entity system

    let app = Renderer.init ()
    let sys1 = Foom.Renderer.EntitySystem.create (app)
    let updateSys1 = world.AddSystem sys1

    let cameraEnt = world.EntityManager.Spawn ()
    world.EntityManager.AddComponent cameraEnt (CameraComponent ())
    world.EntityManager.AddComponent cameraEnt (TransformComponent (Matrix4x4.CreateTranslation (Vector3 (-3680.f, -6704.f, 64.f * 3.f))))
    world.EntityManager.AddComponent cameraEnt (CameraRotationComponent())

    let flatUnit = 64.f

    let mutable count = 0

    lvl.Sectors
    |> Seq.iter (fun sector ->
        lvl
        |> Level.createWalls sector
        |> Seq.iter (fun renderLinedef ->

        
            match Wad.tryFindTexture renderLinedef.TextureName doom2Wad with
            | None -> ()
            | Some tex ->

            let width = Array2D.length1 tex.Data
            let height = Array2D.length2 tex.Data

            let bmp = new Bitmap(width, height, Imaging.PixelFormat.Format32bppArgb)

            let mutable isTransparent = false
            tex.Data
            |> Array2D.iteri (fun i j pixel ->
                if pixel = Pixel.Cyan then
                    bmp.SetPixel (i, j, Color.FromArgb (0, 0, 0, 0))
                    isTransparent <- true
                else
                    bmp.SetPixel (i, j, Color.FromArgb (int pixel.R, int pixel.G, int pixel.B))
            )

            bmp.Save (tex.Name + ".bmp")
            bmp.Dispose ()

            let lightLevel = sector.LightLevel
            let lightLevel =
                if lightLevel > 255 then 255uy
                else byte lightLevel

            let ent = world.EntityManager.Spawn ()

            world.EntityManager.AddComponent ent (TransformComponent (Matrix4x4.Identity))
            world.EntityManager.AddComponent ent (MeshComponent (renderLinedef.Vertices, Wall.createUV width height renderLinedef))
            world.EntityManager.AddComponent ent (
                MaterialComponent (
                    "triangle.vertex",
                    "triangle.fragment",
                    tex.Name + ".bmp",
                    { R = lightLevel; G = lightLevel; B = lightLevel; A = 0uy },
                    isTransparent
                )
            )
        )
    )

    sectorPolygons
    |> Seq.iter (fun (polygons, sector) ->

        polygons
        |> Seq.iter (fun polygon ->
            let vertices =
                polygon
                |> Array.map (fun x -> [|x.X;x.Y;x.Z|])
                |> Array.reduce Array.append
                |> Array.map (fun x -> Vector3 (x.X, x.Y, single sector.FloorHeight))

            let uv = Array.zeroCreate vertices.Length

            let mutable i = 0
            while (i < vertices.Length) do
                let p1 = vertices.[i]
                let p2 = vertices.[i + 1]
                let p3 = vertices.[i + 2]

                uv.[i] <- Vector2 (p1.X / flatUnit, p1.Y / flatUnit * -1.f)
                uv.[i + 1] <- Vector2(p2.X / flatUnit, p2.Y / flatUnit * -1.f)
                uv.[i + 2] <- Vector2(p3.X / flatUnit, p3.Y / flatUnit * -1.f)

                i <- i + 3

            let lightLevel = sector.LightLevel
            let lightLevel =
                if lightLevel > 255 then 255uy
                else byte lightLevel

            count <- count + 1
            let ent = world.EntityManager.Spawn ()

            world.EntityManager.AddComponent ent (TransformComponent (Matrix4x4.Identity))
            world.EntityManager.AddComponent ent (MeshComponent (vertices, uv))
            world.EntityManager.AddComponent ent (
                MaterialComponent (
                    "triangle.vertex",
                    "triangle.fragment",
                    sector.FloorTextureName + ".bmp",
                    { R = lightLevel; G = lightLevel; B = lightLevel; A = 0uy },
                    false
                )
            )
        )
    )

    sectorPolygons
    |> Seq.iter (fun (polygons, sector) ->

        polygons
        |> Seq.iter (fun polygon ->
            let vertices =
                polygon
                |> Array.map (fun x -> [|x.Z;x.Y;x.X|])
                |> Array.reduce Array.append
                |> Array.map (fun x -> Vector3 (x.X, x.Y, single sector.CeilingHeight))

            let uv = Array.zeroCreate vertices.Length

            let mutable i = 0
            while (i < vertices.Length) do
                let p1 = vertices.[i]
                let p2 = vertices.[i + 1]
                let p3 = vertices.[i + 2]

                uv.[i] <- Vector2 (p1.X / flatUnit, p1.Y / flatUnit * -1.f)
                uv.[i + 1] <- Vector2(p2.X / flatUnit, p2.Y / flatUnit * -1.f)
                uv.[i + 2] <- Vector2(p3.X / flatUnit, p3.Y / flatUnit * -1.f)

                i <- i + 3

            let lightLevel = sector.LightLevel
            let lightLevel =
                if lightLevel > 255 then 255uy
                else byte lightLevel

            count <- count + 1
            let ent = world.EntityManager.Spawn ()

            world.EntityManager.AddComponent ent (TransformComponent (Matrix4x4.Identity))
            world.EntityManager.AddComponent ent (MeshComponent (vertices, uv))
            world.EntityManager.AddComponent ent (
                MaterialComponent (
                    "triangle.vertex",
                    "triangle.fragment",
                    sector.CeilingTextureName + ".bmp",
                    { R = lightLevel; G = lightLevel; B = lightLevel; A = 0uy },
                    false
                )
            )
        )
    )

    printfn "COUNT: %A" count

    { 
        Window = app.Window
        RenderUpdate = updateSys1
        Level = lvl
    }

let draw t (prev: ClientState) (curr: ClientState) =

    let stopwatch = System.Diagnostics.Stopwatch.StartNew ()

    curr.RenderUpdate t

    stopwatch.Stop ()

    //printfn "%A" stopwatch.Elapsed.TotalMilliseconds