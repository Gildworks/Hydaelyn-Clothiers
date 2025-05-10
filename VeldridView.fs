namespace fs_mdl_viewer

open System.IO
open System.Numerics
open System.Runtime.InteropServices
open System.Collections.Generic
open Avalonia
open AvaloniaRender.Veldrid
open Veldrid
open xivModdingFramework.General.Enums
open xivModdingFramework.Exd.Enums
open xivModdingFramework.Exd.FileTypes
open xivModdingFramework.Cache
open xivModdingFramework.Items.Categories
open xivModdingFramework.Items.DataContainers
open ModelLoaderRedux
open ShaderBuilder
open CameraController
open ShaderUtils
open Shared
open MaterialHelper

type VeldridView(initialModelPath: string option) as this =
    inherit VeldridRender()

    // --- Model List Resources ---
    let allSlots = [ Head; Body; Hands; Legs; Feet]
    let mutable modelMap    : Map<EquipmentSlot, RenderModel option> =
        allSlots |> List.map (fun slot -> slot, None) |> Map.ofList

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
    let mutable models      : RenderModel list              = []


    // --- Camera ---
    let mutable camera      : CameraController              = CameraController()
    let mutable isDragging  : bool                          = false
    let mutable lastMPos    : Vector2                       = Vector2.Zero

    let mutable isResizing  : bool                          = false
    let mutable resizeTimer : System.Timers.Timer   option  = None
    let mutable firstRender : bool                          = false

    // --- UI vars ---
    let mutable modelPath       : string            option  = None
    let mutable modelSlot       : EquipmentSlot     option  = None
    let mutable assignModel     : bool                      = false
    let mutable emptyPipeline   : Pipeline          option  = None
    let mutable emptyMVPBuffer  : DeviceBuffer      option  = None
    let mutable emptyMVPSet     : ResourceSet       option  = None

    // --- Constants ---
    let gdp                 = @"F:\Games\SquareEnix\FINAL FANTASY XIV - A Realm Reborn\game\sqpack\ffxiv"
    let defaultModel             = "chara/monster/m8299/obj/body/b0001/model/m8299b0001.mdl"
    let info = xivModdingFramework.GameInfo(DirectoryInfo(gdp), XivLanguage.English)
    
    member val InitialModelPath = initialModelPath with get, set

    member this.ModelCount = models.Length
       
    override this.Prepare(gd: GraphicsDevice) =
        printfn "Running Prepare"
        base.Prepare(gd)
        device <- Some gd
        ()
        try
            printfn ""
            models <- []
            loadedModel <- None
            preparedMats <- Some []
            tl <- None
            vb <- None
            ib <- None
            indexCount <- 0u

            let mBuff = gd.ResourceFactory.CreateBuffer(
                BufferDescription(uint32 (Marshal.SizeOf<Matrix4x4>()), BufferUsage.UniformBuffer ||| BufferUsage.Dynamic)
            )
            mb <- Some mBuff
            let mvpLayout = gd.ResourceFactory.CreateResourceLayout(
                ResourceLayoutDescription(
                    ResourceLayoutElementDescription("MVPBuffer", ResourceKind.UniformBuffer, ShaderStages.Vertex)
                )
            )
            let mvpSet = gd.ResourceFactory.CreateResourceSet(
                ResourceSetDescription(mvpLayout, mBuff)
            )
            ml <- Some mvpLayout
            ms <- Some mvpSet
                
        with ex ->
            failwith $"Error during Prepare: {ex.Message} \n {ex.StackTrace}"
        printfn "Finished Prepare"

    override this.RenderFrame (gd: GraphicsDevice, cmdList: CommandList, swapchain: Swapchain) =
        if assignModel then
            this.AssignGear(modelSlot.Value, modelPath.Value, gd)
            assignModel <- false
        
        if isResizing then () else

            // --- Update Camera/MVP ---
            let fb = swapchain.Framebuffer
            if fb.Width <> this.WindowWidth || fb.Height <> this.WindowHeight then
                gd.WaitForIdle()
                swapchain.Resize(this.WindowWidth, this.WindowHeight)

            let w = float32 fb.Width
            let h = float32 fb.Height

            if w > 0.0f && h > 0.0f then
                let aspect = w / h
                let view = camera.GetViewMatrix()
                let proj = camera.GetProjectionMatrix(aspect)

                cmdList.Begin()

                cmdList.SetFramebuffer(fb)
                cmdList.ClearColorTarget(0u, RgbaFloat.Grey)
                cmdList.ClearDepthStencil(1.0f)

                let visibleModels = modelMap |> Map.values |> Seq.choose id |> Seq.toList

                if visibleModels.Length = 0 then
                    if emptyPipeline.IsNone then
                        this.createEmptyPipeline gd swapchain.Framebuffer.OutputDescription
                    cmdList.SetPipeline(emptyPipeline.Value)
                    cmdList.SetGraphicsResourceSet(0u, emptyMVPSet.Value)
                else
                    for model in visibleModels do
                        let initModel =
                            match model.Pipeline with
                            | Some p -> Some p
                            | None ->
                                let vLayout = VertexLayoutDescription(
                                    [|
                                        VertexElementDescription("Position", VertexElementSemantic.Position, VertexElementFormat.Float3)
                                        VertexElementDescription("Color", VertexElementSemantic.Color, VertexElementFormat.Float4)
                                        VertexElementDescription("Color2", VertexElementSemantic.Color, VertexElementFormat.Float4)
                                        VertexElementDescription("UV", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2)
                                        VertexElementDescription("Normal", VertexElementSemantic.Normal, VertexElementFormat.Float3)
                                        VertexElementDescription("BiTangent", VertexElementSemantic.Normal, VertexElementFormat.Float3)
                                        VertexElementDescription("Unknown1", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3)
                                    |]
                                )
                                let mvpLayout = gd.ResourceFactory.CreateResourceLayout(ResourceLayoutDescription(ResourceLayoutElementDescription("MVPBuffer", ResourceKind.UniformBuffer, ShaderStages.Vertex)))
                                let pipe = createDefaultPipeline gd gd.ResourceFactory vLayout swapchain.Framebuffer.OutputDescription mvpLayout model.MaterialLayout
                                Some pipe
                        let pipeModel = { model with Pipeline = initModel }
                        modelMap <- modelMap.Add(pipeModel.Slot, Some pipeModel)

                        if pipeModel.MVPBuffer = null || pipeModel.MaterialSet = null then
                            printfn $"[RenderFrame] Missing pipeline or resources, skipping model: {pipeModel.RawModel.RawModel.MdlPath}"
                        else
                                let mvp = Matrix4x4.CreateScale(5.0f) * view * proj
                                gd.UpdateBuffer(pipeModel.MVPBuffer, 0u, mvp)

                                cmdList.SetPipeline(pipeModel.Pipeline.Value)
                                cmdList.SetVertexBuffer(0u, pipeModel.Vertices)
                                cmdList.SetIndexBuffer(pipeModel.Indices, IndexFormat.UInt16)
                                cmdList.SetGraphicsResourceSet(0u, pipeModel.MVPSet)
                                cmdList.SetGraphicsResourceSet(1u, pipeModel.MaterialSet)

                                cmdList.DrawIndexed(
                                    indexCount = pipeModel.IndexCount,
                                    instanceCount = 1u,
                                    indexStart = 0u,
                                    vertexOffset = 0,
                                    instanceStart = 0u
                                )

                cmdList.End()
                gd.SubmitCommands(cmdList)
                gd.SwapBuffers(swapchain)
                
                if not firstRender then firstRender <- true

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

    member this.GetMinions() : Async<MdlEntry list> =
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

    member this.createEmptyPipeline (gd: GraphicsDevice) (outputDesc: OutputDescription) =
        let factory = gd.ResourceFactory
        let vertexLayout = VertexLayoutDescription([||])
        let mvpLayout = factory.CreateResourceLayout(
            ResourceLayoutDescription(
                ResourceLayoutElementDescription("MVPBuffer", ResourceKind.UniformBuffer, ShaderStages.Vertex)
            )
        )
        let dummyMVP = factory.CreateBuffer(BufferDescription(64u, BufferUsage.UniformBuffer))
        gd.UpdateBuffer(dummyMVP, 0u, Matrix4x4.Identity)

        let mvpSet = factory.CreateResourceSet(ResourceSetDescription(mvpLayout, dummyMVP))

        let shaders = ShaderUtils.getEmptyShaderSet factory
        let shaderSet = ShaderSetDescription([| vertexLayout |], shaders)

        let pipeline = factory.CreateGraphicsPipeline(GraphicsPipelineDescription(
            BlendStateDescription.SingleOverrideBlend,
            DepthStencilStateDescription.Disabled,
            RasterizerStateDescription.CullNone,
            PrimitiveTopology.TriangleList,
            shaderSet,
            [| mvpLayout |],
            outputDesc
        ))

        emptyPipeline <- Some pipeline
        emptyMVPBuffer <- Some dummyMVP
        emptyMVPSet <- Some mvpSet

    member this.GetEquipment () : Async<XivGear list> =
        async {
            let gear = new Gear()
            let! gearList = gear.GetGearList() |> Async.AwaitTask
            return gearList |> List.ofSeq
        }

    member this.AssignTrigger(slot: EquipmentSlot, path: string) =
        modelPath <- Some path
        modelSlot <- Some slot
        assignModel <- true

    member this.AssignGear(slot: EquipmentSlot, path: string, gd: GraphicsDevice) =
        async {
            let! loaded = loadGameModel gd gd.ResourceFactory path
            let renderModel = createRenderModel gd gd.ResourceFactory loaded slot
            modelMap <- modelMap.Add(slot, Some renderModel)
        } |> Async.StartImmediate
    member this.ClearGearSlot (slot: EquipmentSlot) =
        modelMap <- modelMap.Add(slot, None)
