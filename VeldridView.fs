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
open xivModdingFramework.Exd
open xivModdingFramework.Exd.Enums
open xivModdingFramework.Exd.FileTypes
open ModelLoaderRedux
open ShaderBuilder
open CameraController
open Shared
open MaterialHelper

[<Struct; StructLayout(LayoutKind.Sequential)>]
type VertexPositionColorUv = 
    val Position    : Vector3
    val Color       : Vector4
    val Color2      : Vector4
    val UV          : Vector2
    val Normal      : Vector3
    val BiTangent   : Vector3
    val Unknown1    : Vector3
    new(pos,col,col2,uv,nor,bitan, un1) = { Position = pos; Color = col; Color2 = col2; UV = uv; Normal = nor; BiTangent = bitan; Unknown1 = un1 }

type MinionEntry = {
    Name: string
    MdlPath: string
}

type VeldridView(initialModelPath: string option) as this =
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
    let mutable device      : GraphicsDevice    option      = None
    let mutable preparedMats: PreparedMaterial list option  = None 

    // --- Camera ---
    let mutable camera      : CameraController              = CameraController()
    let mutable isDragging  : bool                          = false
    let mutable lastMPos    : Vector2                       = Vector2.Zero

    let mutable isResizing  : bool                          = false
    let mutable resizeTimer : System.Timers.Timer   option  = None
    let mutable firstRender : bool                          = false

    // --- UI vars ---
    let mutable modelPath       : string            option  = None
    let mutable modelHasChanged : bool                      = false

    // --- Constants ---
    let gdp                 = @"F:\Games\SquareEnix\FINAL FANTASY XIV - A Realm Reborn\game\sqpack\ffxiv"
    let defaultModel             = "chara/monster/m8299/obj/body/b0001/model/m8299b0001.mdl"
    let info = xivModdingFramework.GameInfo(DirectoryInfo(gdp), XivLanguage.English)
    
    member val InitialModelPath = initialModelPath with get, set
  

   
    override this.Prepare(gd: GraphicsDevice) =
        base.Prepare(gd)

        device <- Some gd
        ()

        try
            
            //if modelPath.IsNone then
            //    modelPath <- Some defaultModel
            printfn $"Attempting to load model {this.InitialModelPath}"
            match this.InitialModelPath with
            | Some path -> 
                if path.Length > 0 then
                    let initModel = loadGameModel gd gd.ResourceFactory path |> Async.RunSynchronously
                    loadedModel <- Some initModel
                    printfn "Model loaded!"
                else printfn "Failed to load model"
            | None -> printfn "Initial model path not set"
            //let initModel = loadGameModel gd gd.ResourceFactory modelPath.Value |> Async.RunSynchronously
            //loadedModel <- Some initModel
            //ts <- initModel.TextureSet
            //tl <- initModel.TextureLayout
            let prepared = 
                loadedModel.Value.Materials
                |> List.map (prepareMaterial gd gd.ResourceFactory)
            preparedMats <- Some prepared

            match prepared with
            | pm :: _ ->
                tl <- Some pm.ResourceLayout
            | [] ->
                printfn "Warning: No materials found for model."

            // --- Create Buffers ---
            let vBuff = gd.ResourceFactory.CreateBuffer(
                BufferDescription(
                    uint32 (loadedModel.Value.Vertices.Length * Marshal.SizeOf<VertexPositionColorUv>()),
                    BufferUsage.VertexBuffer
                )
            )
            let iBuff = gd.ResourceFactory.CreateBuffer(
                BufferDescription(
                    uint32 (loadedModel.Value.Indices.Length * Marshal.SizeOf<uint16>()),
                    BufferUsage.IndexBuffer
                )
            )
            let mBuff = gd.ResourceFactory.CreateBuffer(
                BufferDescription(
                    uint32 (Marshal.SizeOf<Matrix4x4>()),
                    BufferUsage.UniformBuffer ||| BufferUsage.Dynamic
                )
            )

            gd.UpdateBuffer(vBuff, 0u, loadedModel.Value.Vertices)
            gd.UpdateBuffer(iBuff, 0u, loadedModel.Value.Indices)
            vb              <- Some vBuff
            ib              <- Some iBuff
            mb              <- Some mBuff
            indexCount      <- uint32 loadedModel.Value.Indices.Length

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
        with ex ->
            failwith $"Error during VeldridView.Prepare: {ex.Message}\n{ex.StackTrace}"

    override this.RenderFrame (gd: GraphicsDevice, cmdList: CommandList, swapchain: Swapchain) =
        if modelHasChanged then
            this.UpdateRender(gd)
            modelHasChanged <- false
        if pl.IsNone then
            match ml, tl with
            |Some mvpL, Some texL ->
                // --- Create Vertex Layout ---
                let vertexLayout = VertexLayoutDescription(
                    [|
                        VertexElementDescription("Position", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3)
                        VertexElementDescription("Color", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float4)
                        VertexElementDescription("Color2", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float4)
                        VertexElementDescription("UV", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2)
                        VertexElementDescription("Normal", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3)
                        VertexElementDescription("BiTangent", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3)
                        VertexElementDescription("Unknown1", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3)
                    |]
                )
                try
                    let pipeline = createDefaultPipeline gd gd.ResourceFactory vertexLayout swapchain.Framebuffer.OutputDescription mvpL texL
                    pl <- Some pipeline
                with ex ->
                    printfn $"Pipeline creation failed: {ex.Message}"

            | _ ->
                printfn "Failed to match MVP and Texture layouts"
                ()

        // --- Skip rendering if resize is in progress
        if isResizing then
            ()
        else
            // --- Update Camera/MVP ---
            let fb = swapchain.Framebuffer
            if fb.Width <> this.WindowWidth || fb.Height <> this.WindowHeight then
                gd.WaitForIdle()
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
                match vb, ib, mb, ms, preparedMats, pl with
                | Some vBuff, Some iBuff, Some mBuff, Some mSet, Some (pm :: _), Some pipe -> 
                    cmdList.Begin()

               

                    cmdList.SetFramebuffer(swapchain.Framebuffer)
                    cmdList.ClearColorTarget(0u, RgbaFloat.Grey)
                    cmdList.ClearDepthStencil(1.0f)

                    cmdList.SetPipeline(pipe)
                    cmdList.SetVertexBuffer(0u, vBuff)
                    cmdList.SetIndexBuffer(iBuff, IndexFormat.UInt16)
                    cmdList.SetGraphicsResourceSet(0u, mSet)
                    cmdList.SetGraphicsResourceSet(1u, pm.ResourceSet)

                    cmdList.DrawIndexed(
                        indexCount = indexCount,
                        instanceCount = 1u,
                        indexStart = 0u,
                        vertexOffset = 0,
                        instanceStart = 0u
                    )

                    gd.UpdateBuffer(mBuff, 0u, mvp)

                    cmdList.End()

                    gd.SubmitCommands(cmdList)
                    gd.SwapBuffers(swapchain)
                    if not firstRender then
                        firstRender <- true
                | _ -> printfn "Renderfram: Pipeline or layout missing."
            else
                printfn ""
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


    member this.DisposeAllResources() =
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

    member this.RequestResize(w: uint32, h: uint32) =
        isResizing <- true
        resizeTimer |> Option.iter (fun t -> t.Stop(); t.Dispose())
            
        let timer = new System.Timers.Timer(100.0)
        timer.AutoReset <- false
        timer.Elapsed.Add(fun _ ->
            this.Resize(w, h)
            isResizing <- false
        )
        timer.Start()
        resizeTimer <- Some timer

    member this.AttachInputHandlers(control: Controls.Control) =
        control.PointerPressed.Add(fun args ->
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
            camera.Zoom(scroll * 0.25f)
        )

    member this.IsFirstRenderComplete = firstRender

    member this.GetMinions() : Async<MinionEntry list> =
        async {
            let exReader    = Ex()
            let! compExd    = exReader.ReadExData(XivEx.companion)  |> Async.AwaitTask
            let! charaExd   = exReader.ReadExData(XivEx.modelchara) |> Async.AwaitTask
            let results =
                compExd.Values
                |> Seq.choose (fun row ->
                    try
                        let name = row.GetColumnByName("Name") :?> string
                        let charaId = row.GetColumnByName("ModelCharaId") :?> uint16
                        
                        if charaId > 0us && charaExd.ContainsKey(int charaId) then
                            let mdlInfo = xivModdingFramework.General.XivModelChara.GetModelInfo(charaExd[int charaId])
                            let mId = mdlInfo.PrimaryID
                            let path = $"chara/monster/m{mId}/obj/body/b0001/model/m{mId}b0001.mdl"
                            Some { Name = name; MdlPath = path }
                        else None
                    with _ -> None
                )
                |> Seq.sortBy (fun x -> x.Name)
                |> Seq.toList

            return results
        }

    member this.ChangeModel(path: string) =
        modelPath <- Some path
        modelHasChanged <- true

    member this.UpdateRender(gd: GraphicsDevice) =
        gd.WaitForIdle()
        this.DisposeAllResources()

        try
            let initModel = loadGameModel gd gd.ResourceFactory modelPath.Value |> Async.RunSynchronously

            loadedModel <- Some initModel
            //ts <- initModel.TextureSet
            //tl <- initModel.TextureLayout
            let prepared =
                initModel.Materials
                |> List.map (prepareMaterial gd gd.ResourceFactory)
            preparedMats <- Some prepared

            match prepared with
            | pm :: _ ->
                tl <- Some pm.ResourceLayout
            | [] ->
                printfn "Warning: No materials found for model."

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
        with ex ->
            failwith $"Error during VeldridView.Prepare: {ex.Message}\n{ex.StackTrace}"
