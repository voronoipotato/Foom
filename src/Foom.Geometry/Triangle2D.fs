﻿namespace Foom.Geometry

open System.Numerics

open Foom.Math

[<Struct>]
type Triangle2D =

    val mutable P1 : Vector2
    val mutable P2 : Vector2
    val mutable P3 : Vector2

    new (p1, p2, p3) = { P1 = p1; P2 = p2; P3 = p3 }

    member tri.Area () = 
        (tri.P1.X - tri.P2.X) * (tri.P2.Y - tri.P3.Y) - (tri.P2.X - tri.P3.X) * (tri.P1.Y - tri.P2.Y)

    member tri.BoundingBox () =
        let mutable minX = tri.P1.X
        let mutable maxX = tri.P1.X
        let mutable minY = tri.P1.Y
        let mutable maxY = tri.P1.Y

        if tri.P2.X < minX then minX <- tri.P2.X
        if tri.P2.X > maxX then maxX <- tri.P2.X
        if tri.P2.Y < minY then minY <- tri.P2.Y
        if tri.P2.Y > maxY then maxY <- tri.P2.Y

        if tri.P3.X < minX then minX <- tri.P3.X
        if tri.P3.X > maxX then maxX <- tri.P3.X
        if tri.P3.Y < minY then minY <- tri.P3.Y
        if tri.P3.Y > maxY then maxY <- tri.P3.Y

        AABB2D.ofMinAndMax (Vector2 (minX, minY)) (Vector2 (maxX, maxY))

    // This isn't efficient yet.
    member tri.Intersects (aabb: AABB2D) =
        let l0 = LineSegment2D (tri.P1, tri.P2)
        let l1 = LineSegment2D (tri.P2, tri.P3)
        let l2 = LineSegment2D (tri.P3, tri.P1)

        let min = aabb.Min ()
        let max = aabb.Max ()

        LineSegment2D.intersectsAABB aabb l0 ||
        LineSegment2D.intersectsAABB aabb l1 ||
        LineSegment2D.intersectsAABB aabb l2 ||
        AABB2D.containsPoint tri.P1 aabb ||
        AABB2D.containsPoint tri.P2 aabb ||
        AABB2D.containsPoint tri.P3 aabb ||
        tri.Contains min ||
        tri.Contains max

    // From book: Real-Time Collision Detection - Pages 47-48
    // Note: "If several points are tested against the same triangle, the terms d00, d01, d11, and
    //     denom only have to be computed once, as they are fixed for a given triangle."
    member tri.Contains (p : Vector2) =
        // ************************
        // Barycentric
        //     "bary" comes from Greek, meaning weight.
        // ************************
        let v0 = tri.P2 - tri.P1
        let v1 = tri.P3 - tri.P1
        let v2 = p - tri.P1

        let d00 = Vec2.dot v0 v0
        let d01 = Vec2.dot v0 v1
        let d11 = Vec2.dot v1 v1
        let d20 = Vec2.dot v2 v0
        let d21 = Vec2.dot v2 v1

        let denom = d00 * d11 - d01 * d01

        let v = (d11 * d20 - d01 * d21) / denom
        let w = (d00 * d21 - d01 * d20) / denom
        let u = 1.f - v - w
        // ************************

        0.f <= u && u <= 1.f && 0.f <= v && v <= 1.f && 0.f <= w && w <= 1.f

module Triangle2D =

    let inline area (tri : Triangle2D) = tri.Area ()

