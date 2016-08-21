﻿namespace Foom.Level.Components

open System
open System.IO
open System.Numerics

open Foom.Ecs
open Foom.Wad
open Foom.Wad.Geometry
open Foom.Wad.Level

type LevelTexture =
    {
        IsFlat: bool
        TextureName: string
        CreateUV: int -> int -> Vector2 []
    }

type LevelStaticGeometry =
    {
        Texture: LevelTexture option
        LightLevel: byte
        Vertices: Vector3 []
    }

type LoadLevelRequested (name: string) =

    let calculateStaticGeometry (level: Level) =
        lazy
            level.Sectors
            |> Seq.mapi (fun i sector ->
                let flats = Level.createFlats i level
                let walls = Level.createWalls sector level

                let floorGeometry =
                    flats
                    |> Seq.map (fun flat ->
                        let texture =
                            match flat.FloorTextureName with
                            | Some name ->
                                {
                                    IsFlat = true
                                    TextureName = name
                                    CreateUV = fun width height -> Flat.createUV width height flat 
                                } |> Some
                            | _ -> None
                        {
                            Texture = texture
                            LightLevel = Level.lightLevelBySectorId flat.SectorId level
                            Vertices = 
                                flat.Triangles
                                |> Seq.map (fun tri ->
                                    [|
                                        Vector3 (tri.X.X, tri.X.Y, single flat.FloorHeight)
                                        Vector3 (tri.Y.X, tri.Y.Y, single flat.FloorHeight)
                                        Vector3 (tri.Z.X, tri.Z.Y, single flat.FloorHeight)
                                    |]
                                )
                                |> Seq.reduce Array.append
                        }
                    )

                let ceilingGeometry =
                    flats
                    |> Seq.map (fun flat ->
                        let texture =
                            match flat.CeilingTextureName with
                            | Some name ->
                                {
                                    IsFlat = true
                                    TextureName = name
                                    CreateUV = fun width height -> Flat.createFlippedUV width height flat 
                                } |> Some
                            | _ -> None
                        {
                            Texture = texture
                            LightLevel = Level.lightLevelBySectorId flat.SectorId level
                            Vertices = 
                                flat.Triangles
                                |> Seq.map (fun tri ->
                                    [|
                                        Vector3 (tri.Z.X, tri.Z.Y, single flat.CeilingHeight)
                                        Vector3 (tri.Y.X, tri.Y.Y, single flat.CeilingHeight)
                                        Vector3 (tri.X.X, tri.X.Y, single flat.CeilingHeight)
                                    |]
                                )
                                |> Seq.reduce Array.append
                        }
                    )

                let wallGeometry =
                    walls
                    |> Seq.map (fun wall ->
                        let texture =
                            match wall.TextureName with
                            | Some name ->
                                {
                                    IsFlat = false
                                    TextureName = name
                                    CreateUV = fun width height -> Wall.createUV width height wall
                                } |> Some
                            | _ -> None
                        {
                            Texture = texture
                            LightLevel = Level.lightLevelBySectorId wall.SectorId level
                            Vertices = wall.Vertices
                        }
                    )

                [|
                    floorGeometry
                    ceilingGeometry
                    wallGeometry
                |]
                |> Array.reduce Seq.append
            )
            |> Seq.reduce Seq.append

    member this.StaticGeometry (level: Level) = (calculateStaticGeometry level).Force()

    member this.Name = name

    interface IEntitySystemEvent

type WadComponent (wad: Wad) =

    member this.Wad = wad

    interface IEntityComponent

type LoadWadRequested (name: string) =

    member this.Name = name

    interface IEntitySystemEvent

module Sys =

    let handleLoadWadRequests (openWad: string -> Stream) =
        eventQueue (fun entityManager _ (evt: LoadWadRequested) ->
            let wad = Wad.create (openWad (evt.Name))
            let ent = entityManager.Spawn ()

            entityManager.AddComponent ent (WadComponent (wad))
        )

    let handleWadLoaded f =
        eventQueue (fun entityManager _ (evt: Events.ComponentAdded<WadComponent>) ->
            entityManager.TryGet<WadComponent> (evt.Entity)
            |> Option.iter (fun wadComp ->
                f entityManager wadComp.Wad
            )
        )

    let handleLoadLevelRequests f =
        eventQueue (fun entityManager _ (evt: LoadLevelRequested) ->
            match entityManager.TryFind<WadComponent> (fun _ _ -> true) with
            | Some (_, wadComp) ->
                Wad.findLevel evt.Name wadComp.Wad
                |> evt.StaticGeometry
                |> Seq.iter (f entityManager wadComp.Wad)
            | _ -> ()
        )