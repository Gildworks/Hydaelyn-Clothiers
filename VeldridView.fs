namespace fs_mdl_viewer

open System
open System.IO
open System.Numerics

open Avalonia
open Avalonia.Controls
open Avalonia.Input
open Avalonia.Input.Raw
open Avalonia.Threading

open AvaloniaRender.Veldrid

open Veldrid

open xivModdingFramework.Cache
open xivModdingFramework.Exd.Enums
open xivModdingFramework.Exd.FileTypes
open xivModdingFramework.Items.DataContainers
open xivModdingFramework.Items.Categories
open xivModdingFramework.General.Enums
open xivModdingFramework.Items.Interfaces
open xivModdingFramework.Models.FileTypes
open xivModdingFramework.Mods

open MaterialBuilder
open ModelLoader
open CameraController
open Shared

type VeldridView() =
    inherit VeldridRender()

    // === Model Resources ===
    let allSlots = [ Head; Body; Hands; Legs; Feet ]
    let mutable modelMap : Map<EquipmentSlot, RenderModel option> =
        allSlots |> List.map (fun slot -> slot, None) |> Map.ofList

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

    // === Camera Resources ===
    let mutable camera          : CameraController              = CameraController()
    let mutable isDragging      : bool                          = false
    let mutable lastMPos        : Vector2                       = Vector2.Zero
    let mutable isResizing      : bool                          = false
    let mutable resizeTimer     : System.Timers.Timer option    = None
    let mutable firstRender     : bool                          = false
  
    let agent = MailboxProcessor.Start(fun inbox ->
        let rec loop () = async {
            let! (slot, item, race, reply: AsyncReplyChannel<unit>) = inbox.Receive()
            gearItem <- Some item
            modelSlot <- Some slot
            modelRace <- Some race
            assignModel <- true

            while assignModel do
                do! Async.Sleep 10
            
            reply. Reply(())
            return! loop ()
        }
        loop ()
    )

    member this.ModelCount = models.Length

    override this.Prepare (gd: GraphicsDevice): unit = 
        base.Prepare(gd: GraphicsDevice)
        device <- Some gd

        let mvp = gd.ResourceFactory.CreateBuffer(
            BufferDescription(uint32 (sizeof<Matrix4x4>), BufferUsage.UniformBuffer ||| BufferUsage.Dynamic)
        )
        let layout = gd.ResourceFactory.CreateResourceLayout(
            ResourceLayoutDescription(ResourceLayoutElementDescription("MVPBuffer", ResourceKind.UniformBuffer, ShaderStages.Vertex))
        )
        let set = gd.ResourceFactory.CreateResourceSet(ResourceSetDescription(layout, mvp))

        mvpBuffer <- Some mvp
        mvpLayout <- Some layout
        mvpSet    <- Some set

    override this.RenderFrame (gd: GraphicsDevice, cmdList: CommandList, swapchain: Swapchain): unit = 
        if assignModel then
            this.AssignGear(modelSlot.Value, gearItem.Value, modelRace.Value, gd)
            assignModel <- false

        if isResizing then () else

        

        let fb = swapchain.Framebuffer
        if fb.Width <> this.WindowWidth || fb.Height <> this.WindowHeight then
            gd.WaitForIdle()
            swapchain.Resize(this.WindowWidth, this.WindowHeight)

        let w = float32 fb.Width
        let h = float32 fb.Height

        if pipeline.IsNone && texLayout.IsSome && not assignModel then
            printfn "Creating standard pipeline..."
            let vertexLayout = VertexLayoutDescription(
                [|
                    VertexElementDescription("Position", VertexElementSemantic.Position, VertexElementFormat.Float3)
                    VertexElementDescription("Color", VertexElementSemantic.Color, VertexElementFormat.Float4)
                    //VertexElementDescription("Color2", VertexElementSemantic.Color, VertexElementFormat.Float4)
                    VertexElementDescription("UV", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2)
                    VertexElementDescription("Normal", VertexElementSemantic.Normal, VertexElementFormat.Float3)
                    //VertexElementDescription("BiTangent", VertexElementSemantic.Normal, VertexElementFormat.Float3)
                    //VertexElementDescription("Unknown1", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3)
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
                    cullMode = FaceCullMode.None,
                    fillMode = PolygonFillMode.Solid,
                    frontFace = FrontFace.Clockwise,
                    depthClipEnabled = false,
                    scissorTestEnabled = false
                ),
                PrimitiveTopology.TriangleList,
                shaderSet,
                [| mvpLayout.Value; texLayout.Value|],
                fb.OutputDescription
            )
            let pipe = gd.ResourceFactory.CreateGraphicsPipeline(pipelineDesc)
            pipeline <- Some pipe
            printfn "Standard pipeline created!"

        if w > 0.0f && h > 0.0f then
            let aspect = w / h
            let view = camera.GetViewMatrix()
            let proj = camera.GetProjectionMatrix(aspect)
            let mvpMatrix = Matrix4x4.CreateScale(2.5f) * view * proj

            cmdList.Begin()
            cmdList.SetFramebuffer(fb)
            cmdList.ClearColorTarget(0u, RgbaFloat.Grey)
            cmdList.ClearDepthStencil(1.0f)

            let visibleModels = modelMap |> Map.values |> Seq.choose id |> Seq.toList

            if visibleModels.IsEmpty then
                if pipeline.IsNone then
                    this.CreateEmptyPipeline gd swapchain.Framebuffer.OutputDescription
                cmdList.SetPipeline(emptyPipeline.Value)
                cmdList.SetGraphicsResourceSet(0u, emptyMVPSet.Value)
            else
                for model in visibleModels do
                    for mesh in model.Meshes do
                        gd.UpdateBuffer(mvpBuffer.Value, 0u, mvpMatrix)
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

    override this.Dispose (gd: GraphicsDevice): unit =
        pipeline    |> Option.iter (fun p -> p.Dispose())
        mvpBuffer   |> Option.iter (fun b -> b.Dispose())
        mvpSet      |> Option.iter (fun s -> s.Dispose())
        mvpLayout   |> Option.iter (fun l -> l.Dispose())
        base.Dispose(gd: GraphicsDevice)

    member this.AssignGear(slot: EquipmentSlot, item: IItemModel, race: XivRace, gd: GraphicsDevice) =
        printfn $"Loading model with the following parameters:"
        printfn $"Slot: {slot}"
        printfn $"Item: {item}"
        printfn $"Race: {race}"

        async {
            let tx = ModTransaction.BeginReadonlyTransaction()
            let eqp = new Eqp()       

            printfn "Creating texture layout..."
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
            printfn "Texture layout created!"
          
            printfn "Building material..."
            let materialBuilder = MaterialBuilder.materialBuilder gd.ResourceFactory gd textureLayout
            printfn "Material built!"
            printfn "Loading model..."
            //let! renderModel2 =
            //    try
            //        ModelLoader.loadRenderModelFromItem
            //            gd.ResourceFactory gd tx gearItem.Value XivRace.Hyur_Midlander_Female materialBuilder
            //        |> Async.AwaitTask
            //    with ex ->
            //        printfn $"Error loading model: {ex.Message}"
            //        raise ex

            let! renderModel =
                printfn "Reached renderModel loading!"
                async{    
                    match slot with
                    | EquipmentSlot.Face
                    | EquipmentSlot.Hair
                    | EquipmentSlot.Tail
                    | EquipmentSlot.Ear ->
                        printfn "Loading character model!"
                        let category, prefix, suffix  =
                            match slot with
                            | EquipmentSlot.Face -> "face", "f", "fac"
                            | EquipmentSlot.Hair -> "hair", "h", "hir"
                            | EquipmentSlot.Ear -> "zear", "z", "zer"
                            | EquipmentSlot.Tail -> "tail", "t", "til"
                            | _ -> "error", "error", "error"
                        let mdlPath = $"chara/human/c{item.ModelInfo.PrimaryID:D4}/obj/{category}/{prefix}{item.ModelInfo.SecondaryID:D4}/model/c{item.ModelInfo.PrimaryID:D4}{prefix}{item.ModelInfo.SecondaryID:D4}_{suffix}.mdl"
                        try
                            return! ModelLoader.loadRenderModelFromPart gd.ResourceFactory gd tx mdlPath race materialBuilder |> Async.AwaitTask
                        with ex ->
                            printfn $"[Character Part Loading | Path: {mdlPath}] Error loading model: {ex.Message}"
                            return raise ex
                    | _ ->
                        printfn "Loading gear model!"
                        let rec resolveModelRace (item: IItemModel, race: XivRace, slot: EquipmentSlot) : Async<XivRace> =
                            let swapRaceGender (race: XivRace) : XivRace option =
                                let name = race.ToString()
                                if name.EndsWith("_Male") then
                                    let swapped = name.Replace("_Male", "_Female")
                                    match Enum.TryParse<XivRace>(swapped) with
                                    | true, r -> Some r
                                    | _ -> None
                                elif name.EndsWith("_Female") then
                                    let swapped = name.Replace("_Female", "_Male")
                                    match Enum.TryParse<XivRace>(swapped) with
                                    | true, r -> Some r
                                    | _ -> None
                                else None

                            let searchSlot = 
                                match slot with
                                | EquipmentSlot.Body -> "top"
                                | EquipmentSlot.Head -> "met"
                                | EquipmentSlot.Hands -> "glv"
                                | EquipmentSlot.Legs -> "dwn"
                                | EquipmentSlot.Feet -> "sho"
                                | _ -> ""
                            async{
                                let! eqdp = eqp.GetEquipmentDeformationParameters(item.ModelInfo.SecondaryID, searchSlot, false, false, false, tx) |> Async.AwaitTask
                                match eqdp.TryGetValue(race) with
                                | true, param when param.HasModel ->
                                    printfn $"For this item:"
                                    for kvp in eqdp do
                                        printfn $"Race: {kvp.Key} | HasModel: {kvp.Value.HasModel}"
                                    return race
                                | _ ->
                                    match swapRaceGender race with
                                    | Some alt ->
                                        match eqdp.TryGetValue(alt) with
                                        | true, param when param.HasModel -> return alt
                                        | _ -> return race
                                    | _ ->
                                        let parent = XivRaceTree.GetNode(race).Parent
                                        if parent <> null && parent.Race <> XivRace.All_Races then
                                            return! resolveModelRace (item, parent.Race, slot)
                                        else
                                            return XivRace.Hyur_Midlander_Male
                            }
                    
                        try
                            let! resolvedRace = resolveModelRace(item, race, slot)
                            printfn $"Attempting to load model with {resolvedRace.ToString()}"
                            return! ModelLoader.loadRenderModelFromItem gd.ResourceFactory gd tx gearItem.Value resolvedRace materialBuilder |> Async.AwaitTask
                        with _ ->
                            let! resolvedRace = resolveModelRace(item, race, slot)
                            match resolvedRace with
                            | XivRace.Lalafell_Female ->
                                try
                                    return! ModelLoader.loadRenderModelFromItem gd.ResourceFactory gd tx gearItem.Value XivRace.Lalafell_Male materialBuilder |> Async.AwaitTask
                                with ex ->
                                    printfn $"Error loading model: {ex.Message}"
                                    return raise ex
                            | _ -> 
                                try
                                    return! ModelLoader.loadRenderModelFromItem gd.ResourceFactory gd tx gearItem.Value XivRace.Hyur_Midlander_Female materialBuilder |> Async.AwaitTask
                                with _ ->
                                    printfn "Apparently everything else failed, defaulting to Hyur Midlander Male and hoping for the best."
                                    try
                                        return! ModelLoader.loadRenderModelFromItem gd.ResourceFactory gd tx gearItem.Value XivRace.Hyur_Midlander_Male materialBuilder |> Async.AwaitTask
                                    with ex ->
                                        printfn "If you're seeing this, everything has failed and I'm not even sure the item you're trying to view actually exists. Why are you doing this to me?"
                                        return raise ex
                }
            
            modelMap <- modelMap.Add(slot, Some renderModel)
            printfn "Model loaded!"
            texLayout <- Some textureLayout
        } |> Async.StartImmediate

    member this.AssignTrigger (slot: EquipmentSlot, item: IItemModel, race: XivRace) : Async<unit> =
        agent.PostAndAsyncReply(fun reply -> (slot, item, race, reply))
        

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
            printfn $"c{charaList[0].ModelInfo.PrimaryID:D4} | v{charaList[0].ModelInfo.ImcSubsetID:D4} | {charaList[0].ModelInfo.SecondaryID:D4}"
            return charaList |> List.ofSeq
        }

    member this.ClearGearSlot(slot: EquipmentSlot) =
        modelMap <- modelMap.Add(slot, None)

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
