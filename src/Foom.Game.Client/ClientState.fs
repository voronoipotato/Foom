﻿namespace Foom.Client

type ClientState = 
    {
        AlwaysUpdate: unit -> unit
        Update: (float32 * float32 -> bool)
        RenderUpdate: (float32 * float32 -> unit)
        ClientWorld: ClientWorld
    }
