[<RequireQualifiedAccess>] 
module Foom.Client.Level

open System
open System.IO
open System.Numerics
open System.Collections.Generic

open Foom.Ecs
open Foom.Math
open Foom.Physics
open Foom.Renderer
open Foom.Geometry
open Foom.Wad
open Foom.Client.Sky
open Foom.Renderer.RendererSystem

open Foom.Game.Core
open Foom.Game.Assets
open Foom.Game.Sprite
open Foom.Game.Level
open Foom.Game.Wad
open Foom.Game.Gameplay.Doom

let mutable globalAM = Unchecked.defaultof<AssetManager>

let loadTexture path =
    globalAM.AssetLoader.LoadTextureFile path
    //let tex = Texture (TextureKind.Single path)
    //globalAM.LoadTexture tex
    //tex

let globalBatch = Dictionary<string, Vector3 ResizeArray * Vector2 ResizeArray * Vector4 ResizeArray> ()

let StandardMeshShader = CreateShader MeshInput 0 (CreateShaderPass (fun _ -> []) "TextureMesh")//CreateShader "TextureMesh" 0 ShaderPass.Depth MeshInput
let SkyMeshShader = CreateShader MeshInput 1 (CreateShaderPass (fun _ -> [ Stencil1 ]) "TextureMesh")//CreateShader "TextureMesh" 1 ShaderPass.Stencil1 MeshInput

let levelMaterialCache = Dictionary<string, MaterialDescription<MeshInput>> ()

let runGlobalBatch (em: EntityManager) =
    globalBatch
    |> Seq.iter (fun pair ->
        let isSky = pair.Key.Contains("F_SKY1")
        let texturePath = pair.Key
        let vertices, uv, color = pair.Value

        let ent = em.Spawn ()

        let texture = 
            match levelMaterialCache.TryGetValue (texturePath) with
            | true, x -> x
            | _ ->
                let textureKind = TextureKind.Single texturePath
                let shader = 
                    if isSky then 
                        SkyMeshShader 
                    else 
                        StandardMeshShader
                let material = MaterialDescription<MeshInput> (shader, textureKind)
                levelMaterialCache.[texturePath] <- material

                material

        let meshInfo : MeshInfo =
            {
                Position = vertices |> Seq.toArray
                Uv = uv |> Seq.toArray
                Color = color |> Seq.toArray
            }
        ()
     //   em.Add (ent, RendererSystem.MeshRendererComponent (0, texture, meshInfo))
    )

open System.Linq

let spawnMesh sector (vertices: IEnumerable<Vector3>) uv (texturePath: string) =
    let lightLevel = sector.lightLevel
    let color = Array.init (vertices.Count ()) (fun _ -> Vector4 (single lightLevel, single lightLevel, single lightLevel, 255.f))

    match globalBatch.TryGetValue(texturePath) with
    | true, (gVertices, gUv, gColor) ->
        gVertices.AddRange(vertices)
        gUv.AddRange(uv)
        gColor.AddRange(color)
    | _ ->
        globalBatch.Add (texturePath, (ResizeArray vertices, ResizeArray uv, ResizeArray color))

let spawnSectorGeometryMesh sector (geo: SectorGeometry) wad =
    geo.TextureName
    |> Option.iter (fun textureName ->
        let texturePath = textureName + "_flat.png"
        let t = loadTexture texturePath//new Bitmap(texturePath)
        spawnMesh sector geo.Vertices (SectorGeometry.createUV t.Width t.Height geo) texturePath
    )

let spawnWallPartMesh sector (part: WallPart) (vertices: Vector3 []) wad isSky =
    if vertices.Length >= 3 then
        if not isSky then
            part.TextureName
            |> Option.iter (fun textureName ->
                let texturePath = textureName + ".png"
                let t = loadTexture texturePath//new Bitmap(texturePath)
                spawnMesh sector vertices (WallPart.createUV vertices t.Width t.Height part) texturePath
            )
        else
            let texturePath = "F_SKY1" + "_flat.png"
            let t = loadTexture texturePath//new Bitmap(texturePath)
            spawnMesh sector vertices (WallPart.createUV vertices t.Width t.Height part) texturePath

let spawnWallMesh (level : Level) (wall: Wall) wad =
    let (
        (upperFront, middleFront, lowerFront),
        (upperBack, middleBack, lowerBack)) = level.CreateWallGeometry wall

    match wall.FrontSide with
    | Some frontSide ->

        let isSky =
            match wall.BackSide with
            | Some backSide ->
                let sector = level.GetSector backSide.SectorId
                sector.ceilingTextureName.Equals("F_SKY1")
            | _ -> false
        
        let sector = level.GetSector frontSide.SectorId

        spawnWallPartMesh sector frontSide.Upper upperFront wad isSky
        spawnWallPartMesh sector frontSide.Middle middleFront wad false
        spawnWallPartMesh sector frontSide.Lower lowerFront wad false

    | _ -> ()

    match wall.BackSide with
    | Some backSide ->

        let isSky =
            match wall.FrontSide with
            | Some frontSide ->
                let sector = level.GetSector frontSide.SectorId
                sector.ceilingTextureName.Equals("F_SKY1")
            | _ -> false

        let sector = level.GetSector backSide.SectorId

        spawnWallPartMesh sector backSide.Upper upperBack wad isSky
        spawnWallPartMesh sector backSide.Middle middleBack wad false
        spawnWallPartMesh sector backSide.Lower lowerBack wad false

    | _ -> ()

let updates openWad exportTextures am (physics : PhysicsEngine) (clientWorld: ClientWorld) =
    globalAM <- am
    [
           // (fun name -> System.IO.File.Open (name, FileMode.Open) :> Stream)
            //(fun wad _ ->
            //    wad |> exportFlatTextures
            //    wad |> exportTextures
            //    wad |> exportSpriteTextures
            //)

        Behavior.wadLevelLoading
        |> Behavior.contramap (fun _ -> Behavior.Context (openWad, exportTextures, fun wad level em ->
            let lvl = WadLevel.toLevel level

            let sectorCount = lvl.SectorCount

            let sectorWalls =
                Array.init sectorCount (fun _ -> ResizeArray ())

            lvl.ForEachSector (fun i sector ->

                (i, level)
                ||> Level.iterLinedefBySectorId (fun linedef ->
                    let isImpassible = (linedef.Flags.HasFlag(LinedefFlags.BlocksPlayersAndMonsters))
                    let isUpper = linedef.Flags.HasFlag (LinedefFlags.UpperTextureUnpegged)
                    let staticWall =
                        {
                            LineSegment = LineSegment2D (linedef.Start, linedef.End)

                            IsTrigger = (linedef.FrontSidedef.IsSome && linedef.BackSidedef.IsSome) //&& not isImpassible && isUpper

                        }

                    let rBody = RigidBody (StaticWall staticWall, Vector3.Zero)

                    physics
                    |> PhysicsEngine.addRigidBody rBody

                    if isImpassible then
                        let staticWall =
                            {
                                LineSegment = LineSegment2D (linedef.End, linedef.Start)

                                IsTrigger = false

                            }

                        let rBody = RigidBody (StaticWall staticWall, Vector3.Zero)

                        physics
                        |> PhysicsEngine.addRigidBody rBody                        
                )

                WadLevel.createSectorGeometry i lvl
                |> Seq.iter (fun (ceiling, floor) ->
                    spawnSectorGeometryMesh sector ceiling wad
                    spawnSectorGeometryMesh sector floor wad

                    let mutable j = 0
                    while j < floor.Vertices.Length do
                        let v0 = floor.Vertices.[j]
                        let v1 = floor.Vertices.[j + 1]
                        let v2 = floor.Vertices.[j + 2]

                        physics
                        |> PhysicsEngine.addTriangle
                            (Triangle2D (
                                    Vector2 (v0.X, v0.Y),
                                    Vector2 (v1.X, v1.Y),
                                    Vector2 (v2.X, v2.Y)
                                )
                            )
                            sector // data to store for physics

                        j <- j + 3
                )
            )
            
            lvl.ForEachWall (fun wall ->
                spawnWallMesh lvl wall wad
            )

            level
            |> Foom.Wad.Level.iterThing (fun thing ->
                match thing with
                | Thing.Doom thing when thing.Flags.HasFlag (DoomThingFlags.SkillLevelFourAndFive) ->

                    let position = Vector3 (single thing.X, single thing.Y, 0.f)

                    match thing.Type with

                    | ThingType.ArmorBonus -> ArmorBonus.spawn position |> em.Spawn |> ignore 

                    | ThingType.GreenArmor -> GreenArmor.spawn position em |> ignore

                    | ThingType.BlueArmor -> BlueArmor.spawn position em |> ignore

                    | ThingType.SoulSphere -> SoulSphere.spawn position em |> ignore

                    | ThingType.BloodyMess
                    | ThingType.BloodyMess2 -> GibbedMarine.spawn position em |> ignore

                    | ThingType.ShotgunGuy -> ShotgunGuy.spawn position em |> ignore

                    | _ ->

                    let mutable image = None

                    match thing.Type with
                    | ThingType.HealthBonus -> image <- Some "BON1A0.png"
                    | ThingType.DeadPlayer -> image <- Some "PLAYN0.png"
                    | ThingType.Stimpack -> image <- Some "STIMA0.png"
                    | ThingType.Medkit -> image <- Some "MEDIA0.png"
                    | ThingType.Barrel -> image <- Some "BAR1A0.png"
                    | ThingType.TallTechnoPillar -> image <- Some "ELECA0.png"
                    | ThingType.Player1Start -> image <- Some "PLAYA1.png"
                    | ThingType.AmmoClip -> image <- Some "CLIPA0.png"
                    | _ -> ()

                    let pos = Vector2 (single thing.X, single thing.Y)
                    let sector = physics |> PhysicsEngine.findWithPoint pos

                    match image with
                    | Some texturePath when sector <> null ->
                        let sector = sector :?> Foom.Game.Level.Sector
                        let pos = Vector3 (pos, single sector.floorHeight)

                        let textureKind : TextureKind = 
                            let materialDesc =
                                match levelMaterialCache.TryGetValue (texturePath) with
                                | true, x -> x
                                | _ ->
                                    let textureKind = TextureKind.Single texturePath
                                    let material = MaterialDescription<MeshInput> (StandardMeshShader, textureKind)
                                    levelMaterialCache.[texturePath] <- material

                                    material
                            materialDesc.TextureKind

                        let ent = em.Spawn ()
                        em.Add (ent, TransformComponent (Matrix4x4.CreateTranslation(pos)))
                        em.Add (ent, SpriteComponent (0, textureKind, sector.lightLevel))
                    | _ -> ()

                | _ -> ()
            )

            runGlobalBatch em

            level
            |> Foom.Wad.Level.tryFindPlayer1Start
            |> Option.iter (function
                | Doom doomThing ->
                    let sector =
                        physics
                        |> PhysicsEngine.findWithPoint (Vector2 (single doomThing.X, single doomThing.Y)) :?> Foom.Game.Level.Sector

                    let position = Vector3 (single doomThing.X, single doomThing.Y, single sector.floorHeight + 28.f)

                    let transformComp = TransformComponent (Matrix4x4.CreateTranslation (position))

                    let cameraEnt = em.Spawn ()
                    em.Add (cameraEnt, CameraComponent (Matrix4x4.CreatePerspectiveFieldOfView (56.25f * 0.0174533f, ((16.f + 16.f * 0.25f) / 9.f), 16.f, 100000.f)))
                    em.Add (cameraEnt, TransformComponent (Matrix4x4.CreateTranslation (position)))
                    em.Add (cameraEnt, CharacterControllerComponent (position, 15.f, 56.f))
                    em.Add (cameraEnt, PlayerComponent ())

                    let skyEnt = em.Spawn ()
                    let textureKind = TextureKind.Single "milky2.jpg"
                    em.Add (skyEnt, SkyRendererComponent textureKind)

                | _ -> ()
            )
        ))
    ]
