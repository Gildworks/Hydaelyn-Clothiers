namespace fs_mdl_viewer

open System
open System.IO
open System.Numerics
open System.Threading.Tasks

open Avalonia
open Avalonia.Controls
open Avalonia.Input
open Avalonia.Input.Raw
open Avalonia.Threading

open AvaloniaRender.Veldrid

open Veldrid
open Veldrid.Utilities

open xivModdingFramework.Cache
open xivModdingFramework.Exd.Enums
open xivModdingFramework.Exd.FileTypes
open xivModdingFramework.Items.DataContainers
open xivModdingFramework.Items.Categories
open xivModdingFramework.General.Enums
open xivModdingFramework.Items.Interfaces
open xivModdingFramework.Models.FileTypes
open xivModdingFramework.Models.DataContainers
open xivModdingFramework.Models.Helpers
open xivModdingFramework.Models.ModelTextures
open xivModdingFramework.Mods

open MaterialBuilder
open ModelLoader
open CameraController
open ApplyFlags
open Shared

type VeldridView() as this =
    inherit VeldridRender()

    // === Model Resources ===
    let allSlots = [ Head; Body; Hands; Legs; Feet ]
    let mutable ttModelMap : Map<EquipmentSlot, InputModel> = Map.empty
        //allSlots |> List.map (fun slot -> slot, None) |> Map.ofList
    let mutable modelMap : Map<EquipmentSlot, RenderModel> = Map.empty
        //allSlots |> List.map (fun slot -> slot, None) |> Map.ofList

    let mutable gearItem        : IItemModel option             = None
    let mutable modelRace       : XivRace option                = None
    let mutable modelSlot       : EquipmentSlot option          = None
    let mutable assignModel     : bool                          = false

    // === Render Resources ===
    let mutable pipeline        : Pipeline option               = None
    let mutable mvpBuffer       : DeviceBuffer option           = None
    let mutable mvpLayout       : ResourceLayout option         = None
    let mutable mvpSet          : ResourceSet option            = None
    let mutable texLayout       : ResourceLayout option         = None

    let mutable device          : GraphicsDevice option         = None
    let mutable models          : RenderModel list              = []

    let mutable emptyPipeline   : Pipeline option               = None
    let mutable emptyMVPBuffer  : DeviceBuffer option           = None
    let mutable emptyMVPSet     : ResourceSet option            = None

    let disposeQueue = System.Collections.Generic.Queue<RenderModel * int>()

    // === Camera Resources ===
    let mutable camera          : CameraController              = CameraController()
    let mutable isDragging      : bool                          = false
    let mutable lastMPos        : Vector2                       = Vector2.Zero
    let mutable isResizing      : bool                          = false
    let mutable resizeTimer     : System.Timers.Timer option    = None
    let mutable firstRender     : bool                          = false
  
    let agent = MailboxProcessor.Start(fun inbox ->
        let rec loop () = async {
            let! (slot, item, race, dye1, dye2, colors, _mailboxAckReply: AsyncReplyChannel<unit>, taskCompletionSource: TaskCompletionSource<unit>) = inbox.Receive()
            try
                do! this.AssignGear(slot, item, race, dye1, dye2, colors, device.Value )
                taskCompletionSource.SetResult(())
            with ex ->
                taskCompletionSource.SetException(ex)
            
            _mailboxAckReply.Reply(())
            return! loop ()
        }
        loop ()
    )

    member this.ModelCount = models.Length

    override this.Prepare (gd: GraphicsDevice): unit = 
        base.Prepare(gd: GraphicsDevice)
        device <- Some gd

        let mvp = gd.ResourceFactory.CreateBuffer(
            BufferDescription(uint32 (sizeof<Matrix4x4> * 2), BufferUsage.UniformBuffer ||| BufferUsage.Dynamic)
        )
        let layout = gd.ResourceFactory.CreateResourceLayout(
            ResourceLayoutDescription(ResourceLayoutElementDescription("MVPBuffer", ResourceKind.UniformBuffer, ShaderStages.Vertex))
        )
        let set = gd.ResourceFactory.CreateResourceSet(ResourceSetDescription(layout, mvp))

        mvpBuffer <- Some mvp
        mvpLayout <- Some layout
        mvpSet    <- Some set

    override this.RenderFrame (gd: GraphicsDevice, cmdList: CommandList, swapchain: Swapchain): unit = 


        if isResizing then () else       

        let fb = swapchain.Framebuffer
        if fb.Width <> this.WindowWidth || fb.Height <> this.WindowHeight then
            gd.WaitForIdle()
            swapchain.Resize(this.WindowWidth, this.WindowHeight)

        let w = float32 fb.Width
        let h = float32 fb.Height

        if pipeline.IsNone && texLayout.IsSome && not assignModel then
            let vertexLayout = VertexLayoutDescription(
                [|
                    VertexElementDescription("Position", VertexElementSemantic.Position, VertexElementFormat.Float3)
                    VertexElementDescription("Color", VertexElementSemantic.Color, VertexElementFormat.Float4)
                    VertexElementDescription("UV", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2)
                    VertexElementDescription("Normal", VertexElementSemantic.Normal, VertexElementFormat.Float3)
                    VertexElementDescription("Tangent", VertexElementSemantic.Normal, VertexElementFormat.Float3)
                    VertexElementDescription("Bitangent", VertexElementSemantic.Normal, VertexElementFormat.Float3)
                |]
            )
            let shaders = ShaderUtils.getStandardShaderSet gd.ResourceFactory
            let shaderSet = ShaderSetDescription([| vertexLayout |], shaders)
            let blendState = BlendStateDescription(
                RgbaFloat(0.0f, 0.0f, 0.0f, 0.0f),
                BlendAttachmentDescription(
                    true,
                    BlendFactor.SourceAlpha,
                    BlendFactor.InverseSourceAlpha,
                    BlendFunction.Add,
                    BlendFactor.One,
                    BlendFactor.InverseSourceAlpha,
                    BlendFunction.Add
                )
            )
            let pipelineDesc = GraphicsPipelineDescription(
                blendState,
                DepthStencilStateDescription(
                    depthTestEnabled = true,
                    depthWriteEnabled = true,
                    comparisonKind = ComparisonKind.LessEqual
                ),
                RasterizerStateDescription(
                    cullMode = FaceCullMode.Front,
                    fillMode = PolygonFillMode.Solid,
                    frontFace = FrontFace.Clockwise,
                    depthClipEnabled = true,
                    scissorTestEnabled = true
                ),
                PrimitiveTopology.TriangleList,
                shaderSet,
                [| mvpLayout.Value; texLayout.Value|],
                fb.OutputDescription
            )
            let pipe = gd.ResourceFactory.CreateGraphicsPipeline(pipelineDesc)
            pipeline <- Some pipe

        if w > 0.0f && h > 0.0f then
            let aspect = w / h
            let view = camera.GetViewMatrix()
            let proj = camera.GetProjectionMatrix(aspect)
            let modelMatrix = Matrix4x4.CreateScale(-2.5f, 2.5f, 2.5f)
            
            let worldViewMatrix = modelMatrix * view
            let worldViewProjectionMatrix = worldViewMatrix * proj

            let transformsData = [| worldViewProjectionMatrix; worldViewMatrix |]

            cmdList.Begin()
            cmdList.SetFramebuffer(fb)
            cmdList.ClearColorTarget(0u, RgbaFloat.Grey)
            cmdList.ClearDepthStencil(1.0f)

            let visibleModels = modelMap |> Map.values |> Seq.toList

            if visibleModels.IsEmpty then
                if pipeline.IsNone then
                    this.CreateEmptyPipeline gd swapchain.Framebuffer.OutputDescription
                cmdList.SetPipeline(emptyPipeline.Value)
                cmdList.SetGraphicsResourceSet(0u, emptyMVPSet.Value)
            else
                for model in visibleModels do
                    for mesh in model.Meshes do
                        gd.UpdateBuffer(mvpBuffer.Value, 0u, transformsData)
                        cmdList.SetPipeline(pipeline.Value)
                        cmdList.SetGraphicsResourceSet(0u, mvpSet.Value)
                        cmdList.SetGraphicsResourceSet(1u, mesh.Material.ResourceSet)
                        cmdList.SetVertexBuffer(0u, mesh.VertexBuffer)
                        cmdList.SetIndexBuffer(mesh.IndexBuffer, IndexFormat.UInt16)
                        cmdList.DrawIndexed(uint32 mesh.IndexCount, 1u, 0u, 0, 0u)

            cmdList.End()
            gd.SubmitCommands(cmdList)
            gd.SwapBuffers(swapchain)

            if not firstRender then firstRender <- true

            let mutable count = disposeQueue.Count
            for _ in 0 .. count - 1 do
                let model, framesLeft = disposeQueue.Dequeue()
                if framesLeft <= 0 then
                    model.Dispose()
                else
                    disposeQueue.Enqueue((model, framesLeft - 1))

    override this.Dispose (gd: GraphicsDevice): unit =
        pipeline    |> Option.iter (fun p -> p.Dispose())
        mvpBuffer   |> Option.iter (fun b -> b.Dispose())
        mvpSet      |> Option.iter (fun s -> s.Dispose())
        mvpLayout   |> Option.iter (fun l -> l.Dispose())
        base.Dispose(gd: GraphicsDevice)

    member this.AssignGear(slot: EquipmentSlot, item: IItemModel, race: XivRace, dye1: int, dye2: int, colors: CustomModelColors, gd: GraphicsDevice) : Async<unit> =
        async {
            let tx = ModTransaction.BeginReadonlyTransaction()
            let eqp = new Eqp()       

            let textureLayout =
                gd.ResourceFactory.CreateResourceLayout(ResourceLayoutDescription(
                    ResourceLayoutElementDescription("tex_Diffuse", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
                    ResourceLayoutElementDescription("tex_Normal", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
                    ResourceLayoutElementDescription("tex_Specular", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
                    ResourceLayoutElementDescription("tex_Emissive", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
                    ResourceLayoutElementDescription("tex_Alpha", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
                    ResourceLayoutElementDescription("tex_Roughness", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
                    ResourceLayoutElementDescription("tex_Metalness", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
                    ResourceLayoutElementDescription("tex_Occlusion", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
                    ResourceLayoutElementDescription("tex_Subsurface", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
                    ResourceLayoutElementDescription("SharedSampler", ResourceKind.Sampler, ShaderStages.Fragment)
                ))

            let! ttModel =
                let loadModel (item: IItemModel) (race: XivRace) =
                    task {
                        let! model = Mdl.GetTTModel(item, race)
                        let _ = model.Source
                        return model
                    }

                async {    
                    match slot with
                    | EquipmentSlot.Face
                    | EquipmentSlot.Hair
                    | EquipmentSlot.Tail
                    | EquipmentSlot.Ear ->
                        let category, prefix, suffix  =
                            match slot with
                            | EquipmentSlot.Face -> "face", "f", "fac"
                            | EquipmentSlot.Hair -> "hair", "h", "hir"
                            | EquipmentSlot.Ear -> "zear", "z", "zer"
                            | EquipmentSlot.Tail -> "tail", "t", "til"
                            | _ -> "error", "error", "error"
                        let mdlPath = $"chara/human/c{item.ModelInfo.PrimaryID:D4}/obj/{category}/{prefix}{item.ModelInfo.SecondaryID:D4}/model/c{item.ModelInfo.PrimaryID:D4}{prefix}{item.ModelInfo.SecondaryID:D4}_{suffix}.mdl"
                        try
                            return! loadModel item race |> Async.AwaitTask
                        with ex ->
                            return raise ex
                    | _ ->
                        let rec resolveModelRace (item: IItemModel, race: XivRace, slot: EquipmentSlot, races: XivRace list) : Async<XivRace> =
                            let rec tryResolveRace (slot: string) (races: XivRace list) (originalRace: XivRace) (eqdp: Collections.Generic.Dictionary<XivRace, xivModdingFramework.Models.DataContainers.EquipmentDeformationParameter>) : Async<XivRace> =
                                async {
                                    match races with
                                    | [] -> 
                                        return originalRace
                                    | race::rest ->                                        
                                        match eqdp.TryGetValue(race) with
                                        | true, param when param.HasModel -> 
                                            return race
                                        | _ -> 
                                            return! tryResolveRace slot rest originalRace eqdp
                                }

                            let searchSlot = 
                                match slot with
                                | EquipmentSlot.Body -> "top"
                                | EquipmentSlot.Head -> "met"
                                | EquipmentSlot.Hands -> "glv"
                                | EquipmentSlot.Legs -> "dwn"
                                | EquipmentSlot.Feet -> "sho"
                                | _ -> ""
                            
                            
                            async {
                                let! eqdp = eqp.GetEquipmentDeformationParameters(item.ModelInfo.SecondaryID, searchSlot, false, false, false, tx) |> Async.AwaitTask
                                return! tryResolveRace searchSlot races race eqdp
                            }
                        
                        let priorityList = XivRaces.GetModelPriorityList(race) |> Seq.toList
                        let! resolvedRace = resolveModelRace(item, race, slot, priorityList)
                        
                        

                        let rec racialFallbacks (item: IItemModel) (races: XivRace list) (targetRace: XivRace): Async<TTModel> =
                            async {
                                match races with
                                | [] ->
                                    return raise (exn "Failed to load any model. Rage quitting.")
                                | race::rest ->
                                    try
                                        return! loadModel item race |> Async.AwaitTask
                                    with ex ->
                                        return! racialFallbacks item rest race
                            }

                        
                        try
                            return! loadModel item race |> Async.AwaitTask
                        with _ ->
                            
                            return! racialFallbacks item priorityList resolvedRace
                }
            do! ModelModifiers.RaceConvert(ttModel, race) |> Async.AwaitTask
            ModelModifiers.FixUpSkinReferences(ttModel, race)
            ttModelMap <- ttModelMap.Add(slot, {Model = ttModel; Item = item; Dye1 = dye1; Dye2 = dye2; Colors = colors})
            let! adjustedModels = applyFlags(ttModelMap) |> Async.AwaitTask
            ttModelMap <- adjustedModels
            for model in Map.toSeq adjustedModels do
                let materialBuilder = MaterialBuilder.materialBuilder gd.ResourceFactory gd textureLayout adjustedModels[slot].Dye1 adjustedModels[slot].Dye2 adjustedModels[slot].Colors
                let! renderModel =
                    async{
                        try
                            let fixedModel = adjustedModels[slot].Model
                            return! ModelLoader.loadRenderModelFromItem gd.ResourceFactory gd tx fixedModel item race materialBuilder |> Async.AwaitTask
                        with ex ->
                            return raise ex 
                    }
                match modelMap.TryFind(slot) with
                | Some oldModel when not (obj.ReferenceEquals(oldModel, renderModel)) -> disposeQueue.Enqueue((oldModel, 5))
                | None -> ()
                | _ -> ()

                modelMap <- modelMap.Add(slot, renderModel)
            texLayout <- Some textureLayout
            
        }

    member this.AssignTrigger (slot: EquipmentSlot, item: IItemModel, race: XivRace, dye1: int, dye2: int, colors: CustomModelColors) : Async<unit> =
        async {
          let tcs = TaskCompletionSource<unit>()
          do! agent.PostAndAsyncReply(fun mailboxAckReply -> (slot, item, race, dye1, dye2, colors, mailboxAckReply, tcs))

          do! Async.AwaitTask(tcs.Task)
        }
        

    member this.RequestResize (w: uint32, h: uint32) =
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
            if point.Properties.IsLeftButtonPressed then camera.StartOrbit(lastMPos)
            elif point.Properties.IsRightButtonPressed then camera.StartDolly(lastMPos)
            elif point.Properties.IsMiddleButtonPressed then camera.StartPan(lastMPos)
            isDragging <- true
        )

        control.PointerReleased.Add(fun _ ->
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

    member this.GetEquipment() : Async<XivGear list> =
        async {
            let gear = new Gear()
            let! gearList = gear.GetGearList() |> Async.AwaitTask
            return gearList |> List.ofSeq
        }

    member this.GetChara() : Async<XivCharacter list> =
        async{
            let chara = new Character()
            let! charaList = chara.GetCharacterList() |> Async.AwaitTask
            return charaList |> List.ofSeq
        }

    member this.ClearGearSlot(slot: EquipmentSlot) =
        modelMap <- modelMap.Remove(slot)
        ttModelMap <- ttModelMap.Remove(slot)

    member this.CreateEmptyPipeline (gd: GraphicsDevice) (outputDesc: OutputDescription) =
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

    member this.clearCharacter () =
        ttModelMap <-
            ttModelMap
            |> Map.remove Face
            |> Map.remove Hair
            |> Map.remove Tail
            |> Map.remove Ear

        modelMap <-
            modelMap
            |> Map.remove Face
            |> Map.remove Hair
            |> Map.remove Tail
            |> Map.remove Ear


