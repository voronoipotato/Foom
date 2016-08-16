﻿namespace Foom.Wad.Level

open System.Numerics

open Foom.Wad.Geometry
open Foom.Wad.Level.Structures

type TextureAlignment =
    | UpperUnpegged of offsetY: int
    | LowerUnpegged

type Wall =
    {
        TextureName: string
        TextureOffsetX: int
        TextureOffsetY: int
        Vertices: Vector3 []
        TextureAlignment: TextureAlignment
    }

type Flat =
    {
        SectorId: int
        Triangles: Triangle2D []
    }

[<NoComparison; ReferenceEquality>]
type Level =
    internal {
        sectors: Sector []
    }

    member Sectors : Sector seq

[<CompilationRepresentationAttribute (CompilationRepresentationFlags.ModuleSuffix)>]
module Wall =

    val createUV : width: int -> height: int -> Wall -> Vector2 []

[<CompilationRepresentationAttribute (CompilationRepresentationFlags.ModuleSuffix)>]
module Flat =

    val createUV : width: int -> height: int -> Flat -> Vector2 []

    val createFlippedUV : width: int -> height: int -> Flat -> Vector2 []

[<CompilationRepresentationAttribute (CompilationRepresentationFlags.ModuleSuffix)>]
module Level =

    val createFlats : sectorId: int -> Level -> Flat seq

    val createWalls : Sector -> Level -> Wall seq