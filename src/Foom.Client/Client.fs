﻿[<RequireQualifiedAccess>]
module Foom.Client.Client

open Foom.Ecs
open Foom.Renderer

open Foom.Client.Sprite

type SpriteInput (program: ShaderProgram) =
    inherit MeshInput (program)

    member val Center = program.CreateVertexAttributeVector3 ("in_center")

    member val Positions = program.CreateInstanceAttributeVector3 ("instance_position")

module Pipelines =
    open Pipeline

    let skyWall =
        pipeline {
            do! runProgramWithMesh "TextureMesh" MeshInput (fun _ -> ()) (fun () input draw ->
                draw ()
            )
        }

    let sky =
        pipeline {
            do! runProgramWithMesh "Sky" MeshInput (fun _ -> ()) (fun (_: RendererSystem.Sky) input draw ->
                draw ()
            )
        }

    let skyPipeline =
        pipeline {
            do! setStencil skyWall 1
            do! useStencil sky 1
        }

    let worldPipeline =
        pipeline {
            do! runProgramWithMesh "TextureMesh" MeshInput (fun _ -> ()) (fun () input draw ->
                draw ()
            )

            do! runProgramWithMesh "Sprite" SpriteInput noOutput (fun (sprite: Sprite) input draw ->
                input.Center.Set sprite.Center
                draw ()
            )
        }

    let renderPipeline =
        pipeline {

            do! clear
            do! runSubPipeline "World"
           // do! runSubPipeline "Sky"

        }

type IsSky = IsSky of bool

let init (world: World) =
    let app = Backend.init ()
    let renderSystem = 
        app
        |> RendererSystem.create
            Pipelines.renderPipeline
            [
                ("World", Pipelines.worldPipeline)
            ]

    let renderSystemUpdate = world.AddBehavior (Behavior.merge [ renderSystem ])
    let inputUpdate = world.AddBehavior (Player.preUpdate app)

    let clientSubworld = world.CreateSubworld ()
    let clientWorld = ClientWorld.Create (clientSubworld, world.SpawnEntity ())
    let clientSystemUpdate = ClientSystem.create app clientWorld |> clientSubworld.AddBehavior

    world.Publish (ClientSystem.LoadWadAndLevelRequested ("freedoom1.wad", "e1m1"))
   // world.Publish (ClientSystem.LoadWadAndLevelRequested ("doom2.wad", "map10"))

    {
        Window = app.Window
        AlwaysUpdate = fun () -> inputUpdate ()
        Update = clientSystemUpdate
        RenderUpdate = renderSystemUpdate
        ClientWorld = clientWorld
    }

let draw currentTime t (prev: ClientState) (curr: ClientState) =
    curr.RenderUpdate (currentTime, t)