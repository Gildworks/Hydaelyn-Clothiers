namespace fs_mdl_viewer

open System
open System.IO
open System.Numerics
open System.Threading.Tasks
open System.Runtime.InteropServices

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
    let mutable modelMap : Map<EquipmentSlot, RenderModel> = Map.empty

    let mutable boneTransforms: Matrix4x4[] = Array.empty<Matrix4x4>
    let mutable skeleton: List<SkeletonData> = []

    let mutable boneTransformBuffer     : DeviceBuffer option       = None
    let mutable boneTransformLayout     : ResourceLayout option     = None
    let mutable boneTransformSet        : ResourceSet option        = None

    let mutable currentCharacterModel   : RenderModel option        = None

    let mutable gearItem                : IItemModel option         = None
    let mutable modelRace               : XivRace option            = None
    let mutable modelSlot               : EquipmentSlot option      = None
    let mutable assignModel             : bool                      = false

    // === Render Resources ===
    let mutable pipeline                : Pipeline option           = None
    let mutable mvpBuffer               : DeviceBuffer option       = None
    let mutable mvpLayout               : ResourceLayout option     = None
    let mutable mvpSet                  : ResourceSet option        = None
    let mutable texLayout               : ResourceLayout option     = None

    let mutable device                  : GraphicsDevice option     = None
    let mutable models                  : RenderModel list          = []

    let mutable emptyPipeline           : Pipeline option           = None
    let mutable emptyMVPBuffer          : DeviceBuffer option       = None
    let mutable emptyMVPSet             : ResourceSet option        = None

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
        let factory = gd.ResourceFactory
        device <- Some gd

        let mvp = gd.ResourceFactory.CreateBuffer(
            BufferDescription(uint32 (sizeof<Matrix4x4> * 2), BufferUsage.UniformBuffer ||| BufferUsage.Dynamic)
        )
        let layout = gd.ResourceFactory.CreateResourceLayout(
            ResourceLayoutDescription(ResourceLayoutElementDescription("MVPBuffer", ResourceKind.UniformBuffer, ShaderStages.Vertex))
        )
        let set = gd.ResourceFactory.CreateResourceSet(ResourceSetDescription(layout, mvp))

        let boneBuffer = factory.CreateBuffer(BufferDescription(uint32 (256 * sizeof<Matrix4x4>), BufferUsage.UniformBuffer ||| BufferUsage.Dynamic))
        boneTransformBuffer <- Some boneBuffer
        let boneLayout = factory.CreateResourceLayout(
            ResourceLayoutDescription(
                ResourceLayoutElementDescription("BoneTransforms", ResourceKind.UniformBuffer, ShaderStages.Vertex)
            )
        )
        boneTransformLayout <- Some boneLayout

        let boneSet = factory.CreateResourceSet(ResourceSetDescription(boneLayout, boneBuffer))
        boneTransformSet <- Some boneSet

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
                    VertexElementDescription("Normal", VertexElementSemantic.Normal, VertexElementFormat.Float3)
                    VertexElementDescription("Color", VertexElementSemantic.Color, VertexElementFormat.Float4)
                    VertexElementDescription("UV", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2)
                    VertexElementDescription("Tangent", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3)
                    VertexElementDescription("Bitangent", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3)
                    // --- ADD THESE NEW LAYOUT ELEMENTS ---
                    VertexElementDescription("BoneIndices", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float4)
                    VertexElementDescription("BoneWeights", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float4)
            
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
                [| mvpLayout.Value; texLayout.Value; boneTransformLayout.Value |],
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

            let visibleModels = currentCharacterModel

            if visibleModels.IsNone then
                if pipeline.IsNone then
                    this.CreateEmptyPipeline gd swapchain.Framebuffer.OutputDescription
                cmdList.SetPipeline(emptyPipeline.Value)
                cmdList.SetGraphicsResourceSet(0u, emptyMVPSet.Value)
            else
                for mesh in visibleModels.Value.Meshes do
                    try
                        gd.UpdateBuffer(mvpBuffer.Value, 0u, transformsData)
                        cmdList.SetPipeline(pipeline.Value)
                        cmdList.SetGraphicsResourceSet(0u, mvpSet.Value)
                        cmdList.SetGraphicsResourceSet(1u, mesh.Material.ResourceSet)
                        cmdList.SetGraphicsResourceSet(2u, boneTransformSet.Value)
                        cmdList.SetVertexBuffer(0u, mesh.VertexBuffer)
                        cmdList.SetIndexBuffer(mesh.IndexBuffer, IndexFormat.UInt16)
                        cmdList.DrawIndexed(uint32 mesh.IndexCount, 1u, 0u, 0, 0u)
                    with ex ->
                        printfn $"Draw call failed: {ex.Message}"
                        printfn $"Error stack trace: {ex.StackTrace}"
                        reraise()

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

    member this.calculateBoneTransforms (skeleton: List<SkeletonData>) : Matrix4x4[] =
        let boneCount = skeleton.Length
        Array.create boneCount Matrix4x4.Identity
        //let worldMatrices = Array.create boneCount Matrix4x4.Identity
        //let finalTransforms = Array.create boneCount Matrix4x4.Identity

        //// First, calculate the world matrix for each bone
        //for i = 0 to boneCount - 1 do
        //    let bone = skeleton.[i]
        //    let parentIndex = bone.BoneParent
        //    let localMatrix = new Matrix4x4(
        //        bone.PoseMatrix.[0], bone.PoseMatrix.[1], bone.PoseMatrix.[2], bone.PoseMatrix.[3],
        //        bone.PoseMatrix.[4], bone.PoseMatrix.[5], bone.PoseMatrix.[6], bone.PoseMatrix.[7],
        //        bone.PoseMatrix.[8], bone.PoseMatrix.[9], bone.PoseMatrix.[10], bone.PoseMatrix.[11],
        //        bone.PoseMatrix.[12], bone.PoseMatrix.[13], bone.PoseMatrix.[14], bone.PoseMatrix.[15]
        //    )
        //    let transposedLocalMatrix = Matrix4x4.Transpose(localMatrix)
        
        //    if parentIndex > -1 then
        //        worldMatrices.[i] <- transposedLocalMatrix * worldMatrices.[parentIndex]
        //    else
        //        worldMatrices.[i] <- transposedLocalMatrix

        //// Then, calculate the final skinning matrix for each bone
        //for i = 0 to boneCount - 1 do
        //    let bone = skeleton.[i]
        //    let invBindMatrix = new Matrix4x4(
        //        bone.InversePoseMatrix.[0], bone.InversePoseMatrix.[1], bone.InversePoseMatrix.[2], bone.InversePoseMatrix.[3],
        //        bone.InversePoseMatrix.[4], bone.InversePoseMatrix.[5], bone.InversePoseMatrix.[6], bone.InversePoseMatrix.[7],
        //        bone.InversePoseMatrix.[8], bone.InversePoseMatrix.[9], bone.InversePoseMatrix.[10], bone.InversePoseMatrix.[11],
        //        bone.InversePoseMatrix.[12], bone.InversePoseMatrix.[13], bone.InversePoseMatrix.[14], bone.InversePoseMatrix.[15]
        //    )
        //    let transposedInvBindMatrix = Matrix4x4.Transpose(invBindMatrix)

        //    // The final transform sent to the shader is InverseBindPose * WorldTransformOfBone
        //    finalTransforms.[i] <- invBindMatrix * worldMatrices.[i]

        //finalTransforms

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

            try
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
                texLayout <- Some textureLayout
                do this.RebuildCharacterModel(gd, race)

                
            with ex ->
                return ()
            
        }

    member this.RebuildCharacterModel(gd: GraphicsDevice, race: XivRace) =
        async {
            let activeModels =
                ttModelMap
                |> Map.values
                |> Seq.toList
            if activeModels.IsEmpty then
                currentCharacterModel <- None
                return ()
            let tx = ModTransaction.BeginReadonlyTransaction()

            let modelPathsList = activeModels |> List.map (fun input -> input.Model.Source)
            let modelPaths = ResizeArray(modelPathsList)
            let masterBoneDict = TTModel.ResolveFullBoneHeirarchy(race, modelPaths, (fun isWarning msg -> printfn $"[SKLB] {msg}"), tx)
            skeleton <- masterBoneDict.Values |> Seq.sortBy (fun bone -> bone.BoneNumber) |> Seq.toList

            let masterBoneIndexLookup =
                skeleton
                |> List.indexed
                |> List.map (fun (i, bone) -> (bone.BoneName, i))
                |> dict

            let uniqueMaterialPaths =
                activeModels
                |> List.collect (fun input -> 
                    input.Model.Materials
                    |> Seq.toList
                )
                |> Set.ofList
            let! preparedMaterials = 
                uniqueMaterialPaths
                |> Set.toList
                |> List.map (fun matPath -> async {
                    let ownerInput = activeModels |> List.find (fun input -> input.Model.Materials.Contains(matPath))
                    let! mtrl = TTModelLoader.resolveMtrl ownerInput.Model matPath ownerInput.Item tx
                    let! prepared = MaterialBuilder.materialBuilder gd.ResourceFactory gd texLayout.Value ownerInput.Dye1 ownerInput.Dye2 ownerInput.Colors mtrl ownerInput.Item.Name |> Async.AwaitTask
                    return (matPath, prepared)
                })
                |> Async.Parallel
            let materialDict = dict preparedMaterials

            let geometryData = System.Collections.Generic.Dictionary<string, (ResizeArray<VertexPositionSkinned> * ResizeArray<uint16>)>()
            for matPath in uniqueMaterialPaths do
                geometryData.Add(matPath, (ResizeArray(), ResizeArray()))

            for inputModel in activeModels do
                let ttModel = inputModel.Model
                for meshGroup in ttModel.MeshGroups do

                    let boneRemapTable = System.Collections.Generic.Dictionary<int, int>()
                    for i = 0 to meshGroup.Bones.Count - 1 do
                        let localBoneName = meshGroup.Bones.[i]
                        if masterBoneIndexLookup.ContainsKey(localBoneName) then
                            let masterBoneIndex = masterBoneIndexLookup.[localBoneName]
                            boneRemapTable.Add(i, masterBoneIndex)
                    
                    let materialPath = meshGroup.Material
                    if materialPath <> null && geometryData.ContainsKey(materialPath) then
                        let (vertexList, indexList) = geometryData.[materialPath]
                        let vertexOffset = uint16 vertexList.Count
                        
                        for part in meshGroup.Parts do
                            for vertex in part.Vertices do
                                let boneIndices = Array.zeroCreate 4
                                let boneWeights = Array.zeroCreate 4

                                for i = 0 to 3 do
                                    let localBoneIndex = int vertex.BoneIds.[i]
                                    if boneRemapTable.ContainsKey(localBoneIndex) then
                                        boneIndices.[i] <- float32 boneRemapTable.[localBoneIndex]
                                        boneWeights.[i] <- (float32 vertex.Weights.[i]) / 255.0f

                                vertexList.Add(
                                    VertexPositionSkinned(
                                        SharpToNumerics.vec3 vertex.Position,
                                        SharpToNumerics.vec3 vertex.Normal,
                                        SharpToNumerics.convertColor vertex.VertexColor,
                                        SharpToNumerics.vec2 vertex.UV1,
                                        SharpToNumerics.vec3 vertex.Tangent,
                                        SharpToNumerics.vec3 vertex.Binormal,
                                        Vector4(boneIndices.[0], boneIndices.[1], boneIndices.[2], boneIndices.[3]),
                                        Vector4(boneWeights.[0], boneWeights.[1], boneWeights.[2], boneWeights.[3])
                                    )
                                )
                            for index in part.TriangleIndices do
                                indexList.Add(vertexOffset + (uint16 index))

            let finalMeshes = ResizeArray<RenderMesh>()
            let factory = gd.ResourceFactory

            for matPath in geometryData.Keys do
                let (vertexList, indexList) = geometryData.[matPath]

                if vertexList.Count > 0 && indexList.Count > 0 then
                    let vertices = vertexList.ToArray()
                    let indices = indexList.ToArray()

                    let vertexBuffer = factory.CreateBuffer(BufferDescription(
                        uint32 (vertices.Length * Marshal.SizeOf<VertexPositionSkinned>()),
                        BufferUsage.VertexBuffer
                    ))
                    let indexBuffer = factory.CreateBuffer(BufferDescription(
                        uint32 (indices.Length * sizeof<uint16>), 
                        BufferUsage.IndexBuffer
                    ))

                    gd.UpdateBuffer(vertexBuffer, 0u, vertices)
                    gd.UpdateBuffer(indexBuffer, 0u, indices)

                    let renderMesh = {
                        VertexBuffer = vertexBuffer
                        IndexBuffer = indexBuffer
                        IndexCount = indices.Length
                        Material = materialDict.[matPath]
                        RawModel = null
                    }
                    finalMeshes.Add(renderMesh)

            if finalMeshes.Count > 0 then
                let finalRenderModel = { Meshes = finalMeshes |> List.ofSeq; Original = null }
                printfn "Model added to render, you should see geometry."
                currentCharacterModel <- Some finalRenderModel
            else
                printfn "No model to add."
                currentCharacterModel <- None

            let finalTransforms = this.calculateBoneTransforms skeleton
            gd.UpdateBuffer(boneTransformBuffer.Value, 0u, finalTransforms)
        }
        |> Async.StartImmediate

    member this.AssignTrigger (slot: EquipmentSlot, item: IItemModel, race: XivRace, dye1: int, dye2: int, colors: CustomModelColors) : Async<unit> =
        async {
          let tcs = TaskCompletionSource<unit>()
          do! agent.PostAndAsyncReply(fun mailboxAckReply -> (slot, item, race, dye1, dye2, colors, mailboxAckReply, tcs))

          do! Async.AwaitTask(tcs.Task)
        }
        

    member this.RequestResize (w: uint32, h: uint32) =
        isResizing <- true
        resizeTimer |> Option.iter (fun t -> t.Stop(); t.Dispose())

        let timer = new System.Timers.Timer(250.0)
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

    member this.GetEquipment() : Async<FilterGear list> =
        let getJobEquip (row: Ex.ExdRow) : ClassJobEquip =
            let classJobEquip = {
                GLA = row.GetColumn(2) :?> bool
                PGL = row.GetColumn(3) :?> bool
                MRD = row.GetColumn(4) :?> bool
                LNC = row.GetColumn(5) :?> bool
                ARC = row.GetColumn(6) :?> bool
                CNJ = row.GetColumn(7) :?> bool
                THM = row.GetColumn(8) :?> bool
                CRP = row.GetColumn(9) :?> bool
                BSM = row.GetColumn(10) :?> bool
                ARM = row.GetColumn(11) :?> bool
                GSM = row.GetColumn(12) :?> bool
                LTW = row.GetColumn(13) :?> bool
                WVR = row.GetColumn(14) :?> bool
                ALC = row.GetColumn(15) :?> bool
                CUL = row.GetColumn(16) :?> bool
                MIN = row.GetColumn(17) :?> bool
                BTN = row.GetColumn(18) :?> bool
                FSH = row.GetColumn(19) :?> bool
                PLD = row.GetColumn(20) :?> bool
                MNK = row.GetColumn(21) :?> bool
                WAR = row.GetColumn(22) :?> bool
                DRG = row.GetColumn(23) :?> bool
                BRD = row.GetColumn(24) :?> bool
                WHM = row.GetColumn(25) :?> bool
                BLM = row.GetColumn(26) :?> bool
                ACN = row.GetColumn(27) :?> bool
                SMN = row.GetColumn(28) :?> bool
                SCH = row.GetColumn(29) :?> bool
                ROG = row.GetColumn(30) :?> bool
                NIN = row.GetColumn(31) :?> bool
                MCH = row.GetColumn(32) :?> bool
                DRK = row.GetColumn(33) :?> bool
                AST = row.GetColumn(34) :?> bool
                SAM = row.GetColumn(35) :?> bool
                RDM = row.GetColumn(36) :?> bool
                BLU = row.GetColumn(37) :?> bool
                GNB = row.GetColumn(38) :?> bool
                DNC = row.GetColumn(39) :?> bool
                RPR = row.GetColumn(40) :?> bool
                SGE = row.GetColumn(41) :?> bool
                VPR = row.GetColumn(42) :?> bool
                PCT = row.GetColumn(43) :?> bool
            }
            classJobEquip

        let getJobSet (cje: ClassJobEquip) : Set<Job> =
            let jobs = ResizeArray()
            if cje.GLA then jobs.Add(GLA)
            if cje.PGL then jobs.Add(PGL)
            if cje.MRD then jobs.Add(MRD)
            if cje.LNC then jobs.Add(LNC)
            if cje.ARC then jobs.Add(ARC)
            if cje.CNJ then jobs.Add(CNJ)
            if cje.THM then jobs.Add(THM)
            if cje.CRP then jobs.Add(CRP)
            if cje.BSM then jobs.Add(BSM)
            if cje.ARM then jobs.Add(ARM)
            if cje.GSM then jobs.Add(GSM)
            if cje.LTW then jobs.Add(LTW)
            if cje.WVR then jobs.Add(WVR)
            if cje.ALC then jobs.Add(ALC)
            if cje.CUL then jobs.Add(CUL)
            if cje.MIN then jobs.Add(MIN)
            if cje.BTN then jobs.Add(BTN)
            if cje.FSH then jobs.Add(FSH)
            if cje.PLD then jobs.Add(PLD)
            if cje.MNK then jobs.Add(MNK)
            if cje.WAR then jobs.Add(WAR)
            if cje.DRG then jobs.Add(DRG)
            if cje.BRD then jobs.Add(BRD)
            if cje.WHM then jobs.Add(WHM)
            if cje.BLM then jobs.Add(BLM)
            if cje.ACN then jobs.Add(ACN)
            if cje.SMN then jobs.Add(SMN)
            if cje.SCH then jobs.Add(SCH)
            if cje.ROG then jobs.Add(ROG)
            if cje.NIN then jobs.Add(NIN)
            if cje.MCH then jobs.Add(MCH)
            if cje.DRK then jobs.Add(DRK)
            if cje.AST then jobs.Add(AST)
            if cje.SAM then jobs.Add(SAM)
            if cje.RDM then jobs.Add(RDM)
            if cje.BLU then jobs.Add(BLU)
            if cje.GNB then jobs.Add(GNB)
            if cje.DNC then jobs.Add(DNC)
            if cje.RPR then jobs.Add(RPR)
            if cje.SGE then jobs.Add(SGE)
            if cje.VPR then jobs.Add(VPR)
            if cje.PCT then jobs.Add(PCT)
            Set.ofSeq jobs

        let getExdData (exd: XivEx) =
                async {
                    let ex = new Ex()
                    return! ex.ReadExData(exd) |> Async.AwaitTask
                }
        let exdToMap (exdDictionary: System.Collections.Generic.Dictionary<int, Ex.ExdRow>) : Map<int, Ex.ExdRow> =
            exdDictionary
            |> Seq.map (fun kvp -> kvp.Key, kvp.Value)
            |> Map.ofSeq

        async {
            

            let! gearListAsync =
                async {
                    let gear = new Gear()
                    return! gear.GetGearList() |> Async.AwaitTask
                } |> Async.StartChild
            let! itemExdAsync = getExdData(XivEx.item) |> Async.StartChild
            let! classJobCategoryAsync = getExdData(XivEx.classjobcategory) |> Async.StartChild
            let! recipeListAsync = getExdData(XivEx.recipe) |> Async.StartChild
            let! recipeLevelTableAsync = getExdData(XivEx.recipeleveltable) |> Async.StartChild
            let! recipeLookupTableAsync = getExdData(XivEx.recipelookup) |> Async.StartChild
            let! secretRecipeBookAsync = getExdData(XivEx.secretrecipebook) |> Async.StartChild

            let! gearList = gearListAsync
            let! itemExd = itemExdAsync
            let! classJobCategory = classJobCategoryAsync

            let! recipeExd = recipeListAsync
            let! recipeLevelExd = recipeLevelTableAsync
            let! recipeLookupExd = recipeLookupTableAsync
            let! secretRecipeBook = secretRecipeBookAsync

            let itemExdMap = exdToMap itemExd
            let classJobCategoryMap = exdToMap classJobCategory
            let recipeMap = exdToMap recipeExd
            let recipeLookupMap = exdToMap recipeLookupExd
            let recipeLevelMap = exdToMap recipeLevelExd
            let secretRecipeBookMap = exdToMap secretRecipeBook

            let filterGearItems =
                gearList
                |> List.ofSeq
                |> List.choose (fun gear ->
                    match Map.tryFind gear.ExdID itemExdMap with
                    | Some exdRow ->
                        let equipRestrictValue = exdRow.GetColumn(42) :?> byte |> int
                        let itemLevel = exdRow.GetColumn(11) :?> uint16 |> int
                        let equipLevel = exdRow.GetColumn(40) :?> byte |> int
                        let equipRestrictType = enum<EquipRestriction> equipRestrictValue
                        let cjCategory = exdRow.GetColumn(43) :?> byte |> int
                        let classJobs =
                            match Map.tryFind cjCategory classJobCategoryMap with
                            | Some catRow ->
                                getJobEquip catRow
                            | None -> ClassJobEquip.AllJobs
                        let craftRecipe =
                            match Map.tryFind gear.ExdID recipeLookupMap with
                            | Some lookupRow ->
                                let columnsToJobs = [ (0, "Carpenter"); (1, "Blacksmith"); (2, "Armorer"); (3, "Goldsmith"); (4, "Leatherworker"); (5, "Weaver"); (6, "Alchemist"); (7, "Culinarian")]
                                columnsToJobs
                                |> List.choose (fun (colIndex, jobName) ->
                                    let recipeId = lookupRow.GetColumn(colIndex) :?> uint16 |> int
                                    if recipeId > 0 then
                                        match Map.tryFind recipeId recipeMap with
                                        | Some recipeRow ->
                                            let recipeLevelTableId = recipeRow.GetColumn(2) :?> uint16 |> int
                                            let masterBookRowId = recipeRow.GetColumn(34) :?> uint16 |> int
                                            let masterBook: MasterBookItem =
                                                match Map.tryFind masterBookRowId secretRecipeBookMap with
                                                | Some bookRow ->
                                                    { Book = enum<MasterBook> (recipeRow.GetColumn(34) :?> uint16 |> int); DisplayName = bookRow.GetColumn(1) :?> string }
                                                | None ->
                                                    { Book = MasterBook.noBook; DisplayName = "" }

                                            let requiredLevel = 
                                                match Map.tryFind recipeLevelTableId recipeLevelMap with
                                                | Some levelRow -> 
                                                    levelRow.GetColumn(0) :?> byte |> int
                                                | None -> 0
                                            let recipeStars = 
                                                match Map.tryFind recipeLevelTableId recipeLevelMap with
                                                | Some levelRow ->
                                                    levelRow.GetColumn(1) :?> byte |> int
                                                | None -> 0

                                            Some {
                                                Job = jobName;
                                                RecipeLevel = requiredLevel;
                                                RecipeStars = recipeStars;
                                                MasterBook = masterBook
                                            }
                                        | None -> None
                                    else None
                                )
                            | None -> List.empty

                        Some {
                            Item = gear
                            ExdRow = exdRow
                            ItemLevel = itemLevel
                            EquipLevel = equipLevel
                            EquipRestriction = equipRestrictType
                            EquippableBy = getJobSet classJobs
                            CraftingDetails = craftRecipe
                        }
                    | None -> None
                )
            return filterGearItems
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


