namespace fs_mdl_viewer

open System
open System.IO
open System.Numerics
open System.Runtime.InteropServices
open Avalonia
open Avalonia.Input
open AvaloniaRender.Veldrid
open Veldrid
open Veldrid.Vk
open xivModdingFramework.Cache
open xivModdingFramework.General.Enums
open ModelLoader
open ShaderBuilder
open CameraController

[<Struct; StructLayout(LayoutKind.Sequential)>]
type VertexPositionColorUv = 
    val Position    : Vector3
    val Color       : Vector4
    val UV          : Vector2
    val Normal      : Vector3
    new(pos,col,uv,nor) = { Position = pos; Color = col; UV = uv; Normal = nor }

type VeldridView() as this =
    inherit VeldridRender()

    // --- Veldrid Resources ---
    let mutable pl          : Pipeline          option      = None
    let mutable vb          : DeviceBuffer      option      = None
    let mutable ib          : DeviceBuffer      option      = None
    let mutable mb          : DeviceBuffer      option      = None
    let mutable ml          : ResourceLayout    option      = None
    let mutable ms          : ResourceSet       option      = None
    let mutable tl          : ResourceLayout    option      = None
    let mutable ts          : ResourceSet       option      = None
    let mutable loadedModel : LoadedModel       option      = None
    let mutable indexCount  : uint32                        = 0u

    // --- Camera ---
    let mutable camera      : CameraController              = CameraController()
    let mutable isDragging  : bool                          = false
    let mutable lastMPos    : Vector2                       = Vector2.Zero

    let mutable isResizing  : bool                          = false
    let mutable resizeTimer : System.Timers.Timer   option  = None


    // --- Constants ---
    let gdp                 = @"F:\Games\SquareEnix\FINAL FANTASY XIV - A Realm Reborn\game\sqpack\ffxiv"
    let mdlPath             = "chara/monster/m8299/obj/body/b0001/model/m8299b0001.mdl"

  

   
    override this.Prepare(gd: GraphicsDevice) =
        base.Prepare(gd)
        printfn "MainSwapchain is not yet ready, skipping Prepare()"
        ()

        try
            let info = xivModdingFramework.GameInfo(DirectoryInfo(gdp), XivLanguage.English)
            XivCache.SetGameInfo(info) |> ignore
            let initModel = loadGameModel gd gd.ResourceFactory mdlPath |> Async.RunSynchronously
            printfn $"Sample vertex: {initModel.Vertices[0].Position}"
            loadedModel <- Some initModel
            ts <- initModel.TextureSet
            tl <- initModel.TextureLayout

            // --- Create Buffers ---
            let vBuff = gd.ResourceFactory.CreateBuffer(
                BufferDescription(
                    uint32 (initModel.Vertices.Length * Marshal.SizeOf<VertexPositionColorUv>()),
                    BufferUsage.VertexBuffer
                )
            )
            let iBuff = gd.ResourceFactory.CreateBuffer(
                BufferDescription(
                    uint32 (initModel.Indices.Length * Marshal.SizeOf<uint16>()),
                    BufferUsage.IndexBuffer
                )
            )
            let mBuff = gd.ResourceFactory.CreateBuffer(
                BufferDescription(
                    uint32 (Marshal.SizeOf<Matrix4x4>()),
                    BufferUsage.UniformBuffer ||| BufferUsage.Dynamic
                )
            )

            printfn "Updating buffers..."
            gd.UpdateBuffer(vBuff, 0u, initModel.Vertices)
            gd.UpdateBuffer(iBuff, 0u, initModel.Indices)
            vb              <- Some vBuff
            ib              <- Some iBuff
            mb              <- Some mBuff
            indexCount      <- uint32 initModel.Indices.Length

            // --- Create resource layout and set for MVP matrix ---
            let mvpLayout = gd.ResourceFactory.CreateResourceLayout(
                ResourceLayoutDescription(
                    ResourceLayoutElementDescription("MVPBuffer", ResourceKind.UniformBuffer, ShaderStages.Vertex)
                )
            )
            let mvpSet = gd.ResourceFactory.CreateResourceSet(
                ResourceSetDescription(
                    mvpLayout,
                    mBuff
                )
            )
            ml <- Some mvpLayout
            ms <- Some mvpSet

                

            printfn "VeldridView: Preparation complete!"
        with ex ->
            failwith $"Error during VeldridView.Prepare: {ex.Message}\n{ex.StackTrace}"

    override this.RenderFrame (gd: GraphicsDevice, cmdList: CommandList, swapchain: Swapchain) =
        if pl.IsNone then
            printfn "Pipeline is empty, creating..."
            match ml, tl with
            |Some mvpL, Some texL ->
                // --- Create Vertex Layout ---
                let vertexLayout = VertexLayoutDescription(
                    [|
                        VertexElementDescription("Position", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3)
                        VertexElementDescription("Color", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float4)
                        VertexElementDescription("UV", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2)
                        VertexElementDescription("Normal", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3)
                    |]
                )
                try
                    printfn "Attempting to create the pipeline..."
                    let pipeline = createDefaultPipeline gd gd.ResourceFactory vertexLayout swapchain.Framebuffer.OutputDescription mvpL texL
                    printfn "Pipeline created successfully!"
                    pl <- Some pipeline
                with ex ->
                    printfn $"Pipeline creation failed: {ex.Message}"

            | _ ->
                printfn "Failed to match MVP and Texture layouts"
                ()

        // --- Skip rendering if resize is in progress
        if isResizing then
            printfn "Skipping frame due to resize."
            ()
        else
            // --- Update Camera/MVP ---
            let fb = swapchain.Framebuffer
            if fb.Width <> this.WindowWidth || fb.Height <> this.WindowHeight then
                gd.WaitForIdle()
                printfn $"Resizing Swapchain: [{fb.Width} x {fb.Height}] to [{this.WindowWidth} x {this.WindowHeight}]"
                swapchain.Resize(this.WindowWidth, this.WindowHeight)
            let w = float32 fb.Width
            let h = float32 fb.Height
            if w > 0.0f && h > 0.0f then
                let aspect = w / h
                let model = Matrix4x4.CreateScale(5.0f)
                let view = camera.GetViewMatrix()
                let proj = camera.GetProjectionMatrix(aspect)
                let mutable mvp : Matrix4x4     = model * (view * proj)

                // --- Record commands ---
                cmdList.Begin()

               

                cmdList.SetFramebuffer(swapchain.Framebuffer)
                cmdList.ClearColorTarget(0u, RgbaFloat.Grey)
                cmdList.ClearDepthStencil(1.0f)

                cmdList.SetPipeline(pl.Value)
                cmdList.SetVertexBuffer(0u, vb.Value)
                cmdList.SetIndexBuffer(ib.Value, IndexFormat.UInt16)
                cmdList.SetGraphicsResourceSet(0u, ms.Value)
                cmdList.SetGraphicsResourceSet(1u, ts.Value)

                cmdList.DrawIndexed(
                    indexCount = indexCount,
                    instanceCount = 1u,
                    indexStart = 0u,
                    vertexOffset = 0,
                    instanceStart = 0u
                )

                gd.UpdateBuffer(mb.Value, 0u, mvp)

                cmdList.End()

                gd.SubmitCommands(cmdList)
                gd.SwapBuffers(swapchain)
            else
                ()

    override this.Dispose (gd: GraphicsDevice): unit = 
        pl |> Option.iter (fun p -> p.Dispose())
        ms |> Option.iter (fun s -> s.Dispose())
        ml |> Option.iter (fun l -> l.Dispose())
        ts |> Option.iter (fun s -> s.Dispose())
        tl |> Option.iter (fun l -> l.Dispose())
        mb |> Option.iter (fun b -> b.Dispose())
        ib |> Option.iter (fun b -> b.Dispose())
        vb |> Option.iter (fun b -> b.Dispose())

        pl <- None; ms <- None; ml <- None; ts <- None; tl <- None
        mb <- None; ib <- None; vb <- None; loadedModel <- None

        base.Dispose(gd: GraphicsDevice)


    member this.RequestResize(w: uint32, h: uint32) =
        isResizing <- true
        resizeTimer |> Option.iter (fun t -> t.Stop(); t.Dispose())
            
        let timer = new System.Timers.Timer(100.0)
        timer.AutoReset <- false
        timer.Elapsed.Add(fun _ ->
            this.Resize(w, h)
            isResizing <- false
            printfn "Applied resize."
        )
        timer.Start()
        resizeTimer <- Some timer

    member this.AttachInputHandlers(control: Controls.Control) =
        control.PointerPressed.Add(fun args ->
            printfn "Mouse clicked!"
            let pos = args.GetPosition(control)
            lastMPos <- Vector2(float32 pos.X, float32 pos.Y)

            let point = args.GetCurrentPoint(control)
            if point.Properties.IsLeftButtonPressed then
                camera.StartOrbit(lastMPos)
            else if point.Properties.IsRightButtonPressed then
                camera.StartDolly(lastMPos)
            else if point.Properties.IsMiddleButtonPressed then
                camera.StartPan(lastMPos)

            isDragging <- true
        )

        control.PointerReleased.Add(fun args ->
            printfn "Mouse released!"
            isDragging <- false
            camera.Stop()
        )

        control.PointerMoved.Add(fun args ->
            if isDragging then                
                let pos = args.GetPosition(control)
                let newMouse = Vector2(float32 pos.X, float32 pos.Y)
                camera.MouseMove(newMouse)
       
                
        )

        control.PointerWheelChanged.Add(fun args ->
            let scroll = float32 args.Delta.Y
            camera.Zoom(scroll * -0.25f)
        )