﻿namespace Foom.Common.Components

open System
open System.Numerics

open Foom.Ecs

[<Sealed>]
type TransformComponent (value: Matrix4x4) =

    let mutable transform = value

    member __.Transform : Matrix4x4 = transform

    member val TransformLerp : Matrix4x4 = transform with get, set

    member this.Position 
        with get () =
            transform.Translation
         
        and set value =
            transform.Translation <- value

    member this.ApplyYawPitchRoll (yaw: float32, pitch: float32, roll: float32) =
        let yaw = yaw * (float32 Math.PI / 180.f)
        let pitch = pitch * (float32 Math.PI / 180.f)
        let roll = roll * (float32 Math.PI / 180.f)
        let m = Matrix4x4.CreateFromYawPitchRoll (yaw, pitch, roll)
        transform <- m * transform

    member this.RotateX (degrees: float32) =
        let radians = degrees * (float32 Math.PI / 180.f)
        transform <- Matrix4x4.CreateRotationX (radians) * transform

    member this.RotateY (degrees: float32) =
        let radians = degrees * (float32 Math.PI / 180.f)
        transform <- Matrix4x4.CreateRotationY (radians) * transform

    member this.RotateZ (degrees: float32) =
        let radians = degrees * (float32 Math.PI / 180.f)
        transform <- Matrix4x4.CreateRotationZ (radians) * transform

    member this.Translate v =
        this.Position <- this.Position + v

    member this.Rotation () = Quaternion.CreateFromRotationMatrix (transform)

    interface IEntityComponent

[<Sealed>]
type CameraComponent () =

    interface IEntityComponent

[<Sealed>]
type CameraRotationComponent () =

    let mutable angle = Vector3.Zero

    member this.Angle
        with get () = angle
        and set value = angle <- value

    member val AngleLerp = angle with get, set

    member this.X 
        with get () = angle.X
        and set value = angle.X <- value

    member this.Y 
        with get () = angle.Y
        and set value = angle.Y <- value

    member this.Z 
        with get () = angle.Z
        and set value = angle.Z <- value

    interface IEntityComponent


    