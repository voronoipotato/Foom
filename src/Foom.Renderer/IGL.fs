﻿namespace Foom.Renderer

open System
open System.Numerics
open System.Collections.Generic
open System.Runtime.InteropServices

type IGL =

    abstract BindBuffer : int -> unit

    abstract CreateBuffer : unit -> int

    abstract DeleteBuffer : int -> unit

    abstract BufferData : Vector2 [] * count: int * bufferId: int -> unit

    abstract BufferData : Vector3 [] * count: int * bufferId: int -> unit

    abstract BufferData : Vector4 [] * count: int * bufferId: int -> unit


    abstract BindTexture : int -> unit

    abstract CreateTexture : width: int * height: int * data: nativeint -> int

    abstract CreateTextureFromFile : filePath: string -> int

    abstract DeleteTexture : int -> unit


    abstract BindFramebuffer : int -> unit

    abstract CreateFramebuffer : unit -> int

    abstract CreateFramebufferTexture : width: int * height: int * data: nativeint -> int

    abstract SetFramebufferTexture : int -> unit

    abstract CreateRenderBuffer : width: int * height: int -> int


    abstract Clear : unit -> unit

[<AbstractClass>]
type TextureFile () =
    
    abstract Width : int

    abstract Height : int

    abstract IsTransparent : bool

    abstract UseData : Action<nativeint> -> unit

    abstract Dispose : unit -> unit

    interface IDisposable with

        member this.Dispose () = this.Dispose ()