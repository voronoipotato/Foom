﻿namespace Foom.Geometry

open System.Numerics

type Polygon2D =
    {
        Vertices: Vector2 []
    }

[<CompilationRepresentationAttribute (CompilationRepresentationFlags.ModuleSuffix)>]
module Polygon2D =

    let create vertices = { Vertices = vertices |> Array.ofSeq }

    let sign = function
        | x when x <= 0.f -> false
        | _ -> true

    let inline cross v1 v2 = (Vector3.Cross (Vector3(v1, 0.f), Vector3(v2, 0.f))).Z
        
    let isArrangedClockwise poly =
        let vertices = poly.Vertices
        let length = vertices.Length

        vertices
        |> Array.mapi (fun i y ->
            let x =
                match i with
                | 0 -> vertices.[length - 1]
                | _ -> vertices.[i - 1]
            cross x y)                
        |> Array.reduce ((+))
        |> sign

    // http://alienryderflex.com/polygon/
    let containsPoint (point: Vector2) poly =
        let vertices = poly.Vertices
        let mutable j = vertices.Length - 1
        let mutable c = false

        for i = 0 to vertices.Length - 1 do
            let xp1 = vertices.[i].X
            let xp2 = vertices.[j].X
            let yp1 = vertices.[i].Y
            let yp2 = vertices.[j].Y

            if
                ((yp1 > point.Y) <> (yp2 > point.Y)) &&
                (point.X < (xp2 - xp1) * (point.Y - yp1) / (yp2 - yp1) + xp1) then
                c <- not c
            else ()

            j <- i
        c

    let copyVertices poly =
        poly.Vertices |> Array.copy

    let iteri f poly =
        poly.Vertices
        |> Array.iteri f

    let maxBy f poly =
        poly.Vertices
        |> Array.maxBy f

    let item index poly =
        poly.Vertices.[index]

    let findIndex f poly =
        poly.Vertices
        |> Array.findIndex f

    let vertexCount poly =
        poly.Vertices.Length


        
