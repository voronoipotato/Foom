﻿namespace Foom.Network

open System
open System.IO
open System.Runtime.InteropServices

open FSharp.NativeInterop

// Disable native interop warnings
#nowarn "9"
#nowarn "51"

[<Struct; StructLayout (LayoutKind.Explicit)>]
type DoubleUnion =

    [<FieldOffset (0)>]
    val mutable Value : uint32

    [<FieldOffset (0)>]
    val mutable DoubleValue : double

[<Struct; StructLayout (LayoutKind.Explicit)>]
type SingleUnion =

    [<FieldOffset (0)>]
    val mutable Value : uint32

    [<FieldOffset (0)>]
    val mutable SingleValue : single

module LitteEndian =

    let inline write8 (data: byte []) offset bitOffset value =
        let offset = int offset
        if bitOffset = 0 then
            data.[offset] <- byte value
        else
            let offset1 = offset + 1
            data.[offset] <- data.[offset] ||| byte (value <<< bitOffset)
            data.[offset1] <- data.[offset1] ||| byte (value >>> (8 - bitOffset))

    let inline write16 (data: byte []) offset value =
        let offset = int offset
        data.[offset] <- byte value
        data.[offset + 1] <- byte (value >>> 8)

    let inline write32 (data: byte []) offset value =
        let offset = int offset
        data.[offset] <- byte value
        data.[offset + 1] <- byte (value >>> 8)
        data.[offset + 2] <- byte (value >>> 16)
        data.[offset + 3] <- byte (value >>> 24)

    let inline read8 (data: byte []) offset bitOffset =
        let offset = int offset
        if bitOffset = 0 then
            data.[offset]
        else
            let offset1 = offset + 1
            let value = data.[offset] <<< bitOffset
            value ||| (data.[offset1] >>> (8 - bitOffset))

    let read8byte (data: byte []) offset bitOffset =
        let offset = int offset
        if bitOffset = 0 then
            data.[offset]
        else
            let offset1 = offset + 1
            (data.[offset] >>> bitOffset) ||| (data.[offset1] <<< (8 - bitOffset))

    let inline read16 (data: byte []) offset =
        let offset = int offset
        (uint16 data.[offset]) |||
        ((uint16 data.[offset + 1]) <<< 8)

    let inline read32 (data: byte []) offset =
        let offset = int offset
        (uint32 data.[offset]) |||
        ((uint32 data.[offset + 1]) <<< 8) |||
        ((uint32 data.[offset + 2]) <<< 16) |||
        ((uint32 data.[offset + 3]) <<< 24)

type ByteStream (data : byte []) as this =
    inherit Stream ()

    let writer = ByteWriter this
    let reader = ByteReader this

    let mutable length = 0L
    let mutable position = 0L

    member this.Raw = data

    member this.Writer = writer

    member this.Reader = reader

    member val BitOffset = 0 with get, set

    // Stream Implementation

    override this.CanWrite = true

    override this.Length = length

    override this.SetLength value =
        if position > value then
            position <- value
        length <- value

    override this.Position
        with get () = position
        and set value = position <- value

    override this.Flush () = ()

    override this.CanRead = true

    override this.CanSeek = false

    override this.Read (bytes, offset, count) =
        let remaining = int (length - position)
        let count = if count > remaining then remaining else count
        Buffer.BlockCopy (data, int position, bytes, offset, count)
        position <- position + int64 count
        count

    override this.Seek (offset, origin) = raise <| NotImplementedException ()

    override this.Write (bytes, offset, count) =
        Buffer.BlockCopy (bytes, offset, data, int position, count)
        position <- position + int64 count
        if position > length then
            length <- position

and [<Sealed>] ByteWriter (byteStream: ByteStream) =

    member this.WriteBit (value : bool) =
        let value = if value then 1uy else 0uy
        if byteStream.BitOffset = 0 then
            byteStream.Raw.[int byteStream.Position] <- 0uy
            byteStream.SetLength (byteStream.Position + 1L)

        byteStream.Raw.[int byteStream.Position] <- byteStream.Raw.[int byteStream.Position] ||| (value <<< byteStream.BitOffset)
        if byteStream.BitOffset = 7 then
            byteStream.BitOffset <- 0
            byteStream.Position <- byteStream.Position + 1L
        else
            byteStream.BitOffset <- byteStream.BitOffset + 1

    member this.WriteByte (value: byte) =
        let len = byteStream.Position + 1L + (if byteStream.BitOffset > 0 then 1L else 0L)
        if len > byteStream.Length then
            byteStream.SetLength len

        LitteEndian.write8 byteStream.Raw byteStream.Position byteStream.BitOffset value

        byteStream.Position <- len

    member this.WriteSByte (value: sbyte) =
        let len = byteStream.Position + 1L
        if len > byteStream.Length then
            byteStream.SetLength len

        LitteEndian.write8 byteStream.Raw byteStream.Position byteStream.BitOffset value

        byteStream.Position <- byteStream.Position + 1L

    member this.WriteInt16 (value: int16) =
        let len = byteStream.Position + 2L
        if len > byteStream.Length then
            byteStream.SetLength len

        LitteEndian.write16 byteStream.Raw byteStream.Position value

        byteStream.Position <- byteStream.Position + 2L

    member this.WriteUInt16 (value: uint16) =
        let len = byteStream.Position + 2L
        if len > byteStream.Length then
            byteStream.SetLength  len

        LitteEndian.write16 byteStream.Raw byteStream.Position value

        byteStream.Position <- byteStream.Position + 2L

    member this.WriteInt (value: int) =
        let len = byteStream.Position + 4L
        if len > byteStream.Length then
            byteStream.SetLength  len

        LitteEndian.write32 byteStream.Raw byteStream.Position value

        byteStream.Position <- byteStream.Position + 4L

    member this.WriteUInt32 (value: uint32) =
        let len = byteStream.Position + 4L
        if len > byteStream.Length then
            byteStream.SetLength len

        LitteEndian.write32 byteStream.Raw byteStream.Position value

        byteStream.Position <- byteStream.Position + 4L

    member this.WriteSingle (value: single) =
        let mutable s = SingleUnion ()
        s.SingleValue <- value
        this.WriteUInt32 (s.Value)

    member this.WriteInts (values: int [], startIndex, size) =
        this.WriteInt size
        let size = size * 4

        let len = byteStream.Position + int64 size
        if len > byteStream.Length then
            byteStream.SetLength len

        Buffer.BlockCopy (values, startIndex, byteStream.Raw, int byteStream.Position, size)

        byteStream.Position <- byteStream.Position + int64 size

    member this.Write<'T when 'T : unmanaged> (value: 'T) =
        let mutable value = value
        let size = sizeof<'T>

        let ptr = &&value |> NativePtr.toNativeInt

        let len = byteStream.Position + int64 size
        if len > byteStream.Length then
            byteStream.SetLength len

        Marshal.Copy (ptr, byteStream.Raw, int byteStream.Position, size)

        byteStream.Position <- byteStream.Position + int64 size

    member this.Write<'T when 'T : unmanaged> (value: byref<'T>) =
        let size = sizeof<'T>

        let ptr = &&value |> NativePtr.toNativeInt

        let len = byteStream.Position + int64 size
        if len > byteStream.Length then
            byteStream.SetLength len

        Marshal.Copy (ptr, byteStream.Raw, int byteStream.Position, size)

        byteStream.Position <- byteStream.Position + int64 size

and [<Sealed>] ByteReader (byteStream: ByteStream) =

    member this.ReadBit () : bool =
        let value = byteStream.Raw.[int byteStream.Position] &&& (1uy <<< byteStream.BitOffset)
        if byteStream.BitOffset = 7 then
            byteStream.BitOffset <- 0
            byteStream.Position <- byteStream.Position + 1L
        else
            byteStream.BitOffset <- byteStream.BitOffset + 1
        if value = 0uy then false else true

    member this.ReadByte () : byte =
        let value = LitteEndian.read8byte byteStream.Raw byteStream.Position byteStream.BitOffset |> byte
        byteStream.Position <- byteStream.Position + 1L
        value

    member this.ReadSByte () : sbyte =
        let value = LitteEndian.read8 byteStream.Raw byteStream.Position  byteStream.BitOffset |> sbyte
        byteStream.Position <- byteStream.Position + 1L
        value

    member this.ReadInt16 () : int16 =
        let value = LitteEndian.read16 byteStream.Raw byteStream.Position |> int16
        byteStream.Position <- byteStream.Position + 2L
        value

    member this.ReadUInt16 () : uint16 =
        let value = LitteEndian.read16 byteStream.Raw byteStream.Position |> uint16
        byteStream.Position <- byteStream.Position + 2L
        value

    member this.ReadInt () : int =
        let value = LitteEndian.read32 byteStream.Raw byteStream.Position |> int
        byteStream.Position <- byteStream.Position + 4L
        value

    member this.ReadUInt32 () : uint32 =
        let value = LitteEndian.read32 byteStream.Raw byteStream.Position |> uint32
        byteStream.Position <- byteStream.Position + 4L
        value

    member this.ReadSingle () : single =
        let value = this.ReadUInt32 ()
        let mutable s = SingleUnion ()
        s.Value <- value
        s.SingleValue

    member this.Read<'T when 'T : unmanaged> () : 'T =
        let size = sizeof<'T>

        let mutable value = Unchecked.defaultof<'T>
        let ptr = &&value |> NativePtr.toNativeInt
        Marshal.Copy (byteStream.Raw, int byteStream.Position, ptr, size)
        byteStream.Position <- byteStream.Position + int64 size
        value

    member this.Read<'T when 'T : unmanaged> (value: byref<'T>) =
        let size = sizeof<'T>
        let ptr = &&value |> NativePtr.toNativeInt
        Marshal.Copy (byteStream.Raw, int byteStream.Position, ptr, size)
        byteStream.Position <- byteStream.Position + int64 size

    member this.ReadInts (buffer: int []) =
        let len = this.ReadInt ()

        if buffer.Length < len then
            failwith "Buffer is too small for reading an array of ints."

        let size = len * 4
        Buffer.BlockCopy (byteStream.Raw, int byteStream.Position, buffer, 0, size)
        byteStream.Position <- byteStream.Position + int64 size
        len

    member this.IsEndOfStream = byteStream.Position = byteStream.Length

    member this.Position = byteStream.Position
    