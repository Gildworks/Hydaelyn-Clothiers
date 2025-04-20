module CustomVeldridControl

open System.IO
open System.Numerics
open System.Runtime.InteropServices
open Avalonia.Controls
open Veldrid
open Veldrid.StartupUtilities
open Veldrid.Sdl2
open xivModdingFramework.Cache
open xivModdingFramework.General.Enums
open CameraController
open ModelLoader
open ShaderBuilder

[<Struct; StructLayout(LayoutKind.Sequential)>]
type VertexPositionColorUv = 
    val Position    : Vector3
    val Color       : Vector4
    val UV          : Vector2
    val Normal      : Vector3
    new(pos,col,uv,nor) = { Position = pos; Color = col; UV = uv; Normal = nor }

type CustomVeldridControl() as this =
    inherit NativeControlHost()

    let mutable graphicsDevice : GraphicsDevice option = None
    let mutable commandList : CommandList option = None
    let mutable factory : ResourceFactory option = None
    let mutable isInitialized = false

    let mutable sdlWindow : Sdl2Window option = None

    let mutable vertexBuffer : DeviceBuffer option = None
    let mutable indexBuffer : DeviceBuffer option = None
    let mutable indexCount = 0

    let mutable pipeline : Pipeline option = None

    let mutable isLeftDown = false
    let mutable isRightDown = false
    let mutable isMiddleDown = false
    let mutable lastMousePos = Vector2.Zero

    let mutable mvpb : DeviceBuffer option = None
    let mutable ts = null

    let cameraComtroller = CameraController()

    do
        
        this.AttachedToVisualTree.Add(fun _ -> this.InitializeVeldrid())

    member private this.InitializeVeldrid() =
        if isInitialized then () else

        isInitialized <- true

        let windowCI = WindowCreateInfo(
            X = 100,
            Y = 100,
            WindowWidth = 800,
            WindowHeight = 600,
            WindowTitle = "Veldrid Test"
        )
        let flags = SDL_WindowFlags.OpenGL ||| SDL_WindowFlags.InputFocus ||| SDL_WindowFlags.AllowHighDpi ||| SDL_WindowFlags.Shown

        let sdl = new Sdl2Window(windowCI.WindowTitle, windowCI.X, windowCI.Y, windowCI.WindowWidth,windowCI.WindowHeight, flags, true)
        
        let scSrc = VeldridStartup.GetSwapchainSource(sdl)
        let scDsc = SwapchainDescription(
            scSrc,
            uint32 sdl.Bounds.Width,
            uint32 sdl.Bounds.Height,
            PixelFormat.D32_Float_S8_UInt,
            true
        )

        // Add mouse button state
        sdl.add_MouseDown(fun (e: MouseEvent) ->
            match e.MouseButton with
            | MouseButton.Left -> isLeftDown <- true; cameraComtroller.StartOrbit(lastMousePos)
            | MouseButton.Right -> isRightDown <- true; cameraComtroller.StartDolly(lastMousePos)
            | MouseButton.Middle -> isMiddleDown <- true; cameraComtroller.StartPan(lastMousePos)
            | _ -> ()
        )

        sdl.add_MouseUp(fun (e: MouseEvent) ->
            match e.MouseButton with
            | MouseButton.Left -> isLeftDown <- false; cameraComtroller.Stop()
            | MouseButton.Right -> isRightDown <- false; cameraComtroller.Stop()
            | MouseButton.Middle -> isMiddleDown <- false; cameraComtroller.Stop()
            | _ -> ()
        )

        // Add camera controls
        sdl.add_MouseMove(fun (e: MouseMoveEventArgs) ->
            let pos = Vector2(float32 e.MousePosition.X, float32 e.MousePosition.Y)
            lastMousePos <- pos
            cameraComtroller.MouseMove(pos)
        )

        sdl.Visible <- true
        async {
            while sdl.Exists do
                Sdl2Events.ProcessEvents()

                do! Async.Sleep(10)
        } |> Async.Start

        sdlWindow <- Some sdl

        if not sdl.Exists then
            failwith "SDL window does not exist!"

        let gd = GraphicsDevice.CreateVulkan(
            GraphicsDeviceOptions(
                debug = true,
                swapchainDepthFormat = PixelFormat.D32_Float_S8_UInt,
                syncToVerticalBlank = true,
                ResourceBindingModel = ResourceBindingModel.Improved                
            ),
            scDsc
        )
        graphicsDevice <- Some gd
        factory <- Some gd.ResourceFactory

        let cl = gd.ResourceFactory.CreateCommandList()

        let modelPath = "chara/equipment/e0755/model/c0101e0755_top.mdl"
        let gameDataPath = @"F:\Games\SquareEnix\FINAL FANTASY XIV - A Realm Reborn\game\sqpack\ffxiv"
        let gameInfo = xivModdingFramework.GameInfo(DirectoryInfo(gameDataPath), XivLanguage.English)
        XivCache.SetGameInfo(gameInfo) |> ignore

        let loadedModel = loadGameModel gd factory.Value modelPath |> Async.RunSynchronously
        ts <- loadedModel.TextureSet

        let vb = gd.ResourceFactory.CreateBuffer(
            BufferDescription(
                uint32 (loadedModel.Vertices.Length * Marshal.SizeOf<VertexPositionColorUv>()),
                BufferUsage.VertexBuffer
            )
        )
        gd.UpdateBuffer(vb, 0u, loadedModel.Vertices)
        vertexBuffer <- Some vb

        let ib = gd.ResourceFactory.CreateBuffer(
            BufferDescription(
                uint32 (loadedModel.Indices.Length * Marshal.SizeOf<uint16>()),
                BufferUsage.IndexBuffer
            )
        )
        gd.UpdateBuffer(ib, 0u, loadedModel.Indices)
        indexBuffer <- Some ib
        indexCount <- loadedModel.Indices.Length
        

        // === Create MVP matrix and buffer ===
        let aspectRatio = float32 sdl.Width / float32 sdl.Height

        let mvpBuffer = gd.ResourceFactory.CreateBuffer(
            BufferDescription(uint32 sizeof<Matrix4x4>, BufferUsage.UniformBuffer ||| BufferUsage.Dynamic)
        )
        mvpb <- Some mvpBuffer


        let layout = gd.ResourceFactory.CreateResourceLayout(
            ResourceLayoutDescription(
                ResourceLayoutElementDescription("MVPBuffer", ResourceKind.UniformBuffer, ShaderStages.Vertex)
            )
        )

        let resourceSet = gd.ResourceFactory.CreateResourceSet(
            ResourceSetDescription(layout, mvpb.Value)
        )

        // === Shader setup ===
        let vertexLayout = 
            VertexLayoutDescription(
                [|
                    VertexElementDescription("Position", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3)
                    VertexElementDescription("Color", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float4)
                    VertexElementDescription("UV", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2)
                    VertexElementDescription("Normal", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3)
                |]
            )

        let pl = createDefaultPipeline gd factory.Value vertexLayout gd.SwapchainFramebuffer.OutputDescription layout loadedModel.TextureLayout.Value

        pipeline <- Some pl

        commandList <- Some cl

        // Pass resourceSet into a member if needed, or use in RenderLoop
        this.RenderLoop(resourceSet)


    member private this.RenderLoop(resourceSet: ResourceSet) =
        match graphicsDevice, commandList, pipeline, vertexBuffer with
        | Some gd, Some cl, Some pl, Some vb ->
            cl.Begin()
            cl.SetFramebuffer(gd.SwapchainFramebuffer)

            cl.ClearColorTarget(0u, RgbaFloat.Grey)
            cl.ClearDepthStencil(1.0f)

            cl.SetPipeline(pl)
            cl.SetGraphicsResourceSet(0u, resourceSet)
            cl.SetGraphicsResourceSet(1u, ts.Value)
            cl.SetVertexBuffer(0u, vb)
            cl.SetIndexBuffer(indexBuffer.Value, IndexFormat.UInt16)

            cl.DrawIndexed(
                indexCount = uint32 indexCount,
                instanceCount = 1u,
                indexStart = 0u,
                vertexOffset = 0,
                instanceStart = 0u
            )
            let model = Matrix4x4.CreateScale(5.0f)
            let view = cameraComtroller.GetViewMatrix()
            let projection = cameraComtroller.GetProjectionMatrix(
                float32 gd.SwapchainFramebuffer.Width / float32 gd.SwapchainFramebuffer.Height
            )

            let mvp = model * (view * projection)
            gd.UpdateBuffer(mvpb.Value, 0u, mvp)
            cl.End()

            gd.SubmitCommands(cl)
            gd.SwapBuffers()
            gd.WaitForIdle()

            async {
                do! Async.Sleep(16)
                this.RenderLoop(resourceSet)
            } |> Async.StartImmediate
        | _ -> ()