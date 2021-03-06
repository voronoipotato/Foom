﻿namespace rec Foom.Droid

open System
open System.Threading.Tasks
open System.Collections.Concurrent

open Android.App
open Android.Content
open Android.OS
open Android.Runtime
open Android.Views
open Android.Widget
open Android.Opengl
open Android.Content.PM
open Javax.Microedition.Khronos.Opengles;
open Java.Lang
open OpenTK.Graphics
open OpenTK.Graphics.ES30
open OpenTK.Platform.Android

open Foom.Renderer
open Foom.Input

type GLRenderer (surfaceView: GLSurfaceView, input) =
    inherit Java.Lang.Object ()

    let mutable vaoId = 0

    let mutable PreUpdate = id

    let mutable Update = (fun _ _ -> true)

    let mutable Render = (fun _ _ -> ())

    let mutable gameLoop = GameLoop.create 30.

    interface GLSurfaceView.IRenderer with

        member x.OnDrawFrame _ =
            gameLoop <- GameLoop.tick PreUpdate Update Render gameLoop

        member x.OnSurfaceChanged (gl, width, height) =
            GL.Viewport (0, 0, width, height)

        member x.OnSurfaceCreated (gl, config) =
            GL.GenVertexArrays (1, &vaoId)
            GL.BindVertexArray (vaoId)

            let gl = OpenTKGL (fun () -> ())

            let (preUpdate, update, render) = Foom.Program.start input gl (new Task (fun () -> ()) |> ref)
            PreUpdate <- preUpdate
            Update <- update
            Render <- render

type InputLookView (context, inputEvents : ConcurrentQueue<InputEvent>) as this =
    inherit View (context)

    let mutable lastTouchX = 0.f
    let mutable lastTouchY = 0.f
    let mutable isLooking = false

    do
        this.Touch.Add (fun args ->
            let ev = args.Event
            let touchX = ev.GetX ()
            let touchY = ev.GetY ()

            match args.Event.Action with

            | MotionEventActions.Up ->
                isLooking <- true

            | MotionEventActions.Down ->
                isLooking <- false
                lastTouchX <- touchX
                lastTouchY <- touchY

            | _ -> ()

            match args.Event.Action with

            | MotionEventActions.Move ->
                let xrel = touchX - lastTouchX
                let yrel = touchY - lastTouchY
                inputEvents.Enqueue (MouseMoved (0, 0, int xrel, int yrel))

            | _ -> ()
        )

type InputMovementView (context, inputEvents : ConcurrentQueue<InputEvent>) as this =
    inherit View (context)

    let mutable lastTouchX = 0.f
    let mutable lastTouchY = 0.f

    do
        this.Touch.Add (fun args ->
            let ev = args.Event
            let touchX = ev.GetX ()
            let touchY = ev.GetY ()

            match args.Event.Action with

            | MotionEventActions.Up ->
                inputEvents.Enqueue (KeyReleased ('w'))

            | _ -> ()

            match args.Event.Action with

            | MotionEventActions.Down ->
                inputEvents.Enqueue (KeyPressed ('w'))

            | MotionEventActions.Up ->
                inputEvents.Enqueue (KeyReleased ('w'))

            | _ -> ()
        )

type Runnable (f: unit -> unit) =
    inherit Java.Lang.Object ()

    interface IRunnable with

        member this.Run () = f ()

type GLView (context) as x =
    inherit RelativeLayout (context)

    let surfaceView = new GLSurfaceView (context)
    let renderer = new GLRenderer (surfaceView, x)

    let inputEvents = ConcurrentQueue ()

    do
        surfaceView.SetEGLContextClientVersion (3)
        surfaceView.SetEGLConfigChooser (8, 8, 8, 8, 24, 8)
        surfaceView.SetRenderer (renderer)
        surfaceView.PreserveEGLContextOnPause <- true

        x.AddView surfaceView

        x.Post (
            new Runnable (fun () ->
                    let layoutParams = new LinearLayout.LayoutParams (ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
                    let layout = new LinearLayout (context)
                    layout.LayoutParameters <- layoutParams

                    let view1 = new InputLookView (context, inputEvents)
                    let view2 = new InputMovementView (context, inputEvents)
                    let l1 = new LinearLayout.LayoutParams(x.Width / 2, x.Height)
                    let l2 = new LinearLayout.LayoutParams(x.Width / 2, x.Height)

                    layout.AddView (view1, l1)
                    layout.AddView (view2, l2)
                    x.AddView layout
            )
        )
        |> ignore

    interface IInput with

        member x.PollEvents () =
            ()

        member x.GetMousePosition () =
            MousePosition ()

        member x.GetState () =
            let events = ResizeArray ()
            let mutable e = Unchecked.defaultof<InputEvent>
            while inputEvents.TryDequeue (&e) do
                events.Add e
            { Events = events |> Seq.distinct |> Seq.toList }

[<Activity (
    Label = "Foom", 
    MainLauncher = true, 
    Icon = "@mipmap/icon", 
    ConfigurationChanges = (ConfigChanges.KeyboardHidden ||| ConfigChanges.Keyboard ||| ConfigChanges.Orientation ||| ConfigChanges.ScreenSize), 
    ScreenOrientation = ScreenOrientation.SensorLandscape
)>]
type MainActivity () =
    inherit Activity ()

    let mutable glView = Unchecked.defaultof<GLView>

    override this.OnCreate (bundle) =

        base.OnCreate (bundle)

        this.Window.SetFlags (WindowManagerFlags.KeepScreenOn, WindowManagerFlags.KeepScreenOn)

        glView <- new GLView (this :> Context)
       
        this.SetContentView (glView)