﻿namespace fs_mdl_viewer

open System
open System.IO
open System.Numerics
open System.Threading.Tasks
open System.Runtime.InteropServices

open Serilog

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
open xivModdingFramework.General
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
            let! (slot, item, race, dye1, dye2, colors, customizations, _mailboxAckReply: AsyncReplyChannel<unit>, taskCompletionSource: TaskCompletionSource<unit>) = inbox.Receive()
            try
                do! this.AssignGear(slot, item, race, dye1, dye2, colors, customizations, device.Value )
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
                        Log.Error(ex, "Frame render failed.")
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
        pipeline                |> Option.iter (fun p -> p.Dispose())
        mvpBuffer               |> Option.iter (fun b -> b.Dispose())
        mvpSet                  |> Option.iter (fun s -> s.Dispose())
        mvpLayout               |> Option.iter (fun l -> l.Dispose())
        boneTransformBuffer     |> Option.iter (fun b -> b.Dispose())
        boneTransformSet        |> Option.iter (fun s -> s.Dispose())
        boneTransformLayout     |> Option.iter (fun l -> l.Dispose())

        match currentCharacterModel with
        | Some model -> this.DisposeRenderModel(model)
        | None -> ()

        base.Dispose(gd: GraphicsDevice)

    member this.calculateBoneTransforms (skeleton: List<SkeletonData>) (customizations: CharacterCustomizations) : Matrix4x4[] =
        let boneCount = skeleton.Length
        if boneCount = 0 then
            [| |]
        else
            let worldMatrices = Array.create boneCount Matrix4x4.Identity
            let finalTransforms = Array.create boneCount Matrix4x4.Identity
            let bustScale = this.handleBustScaling(customizations.BustSize)

            // Build world matrices WITHOUT custom scaling first
            for i = 0 to boneCount - 1 do
                let bone = skeleton.[i]
                let parentIndex = bone.BoneParent
        
                let localMatrixOriginal = new Matrix4x4(
                    bone.PoseMatrix.[0], bone.PoseMatrix.[1], bone.PoseMatrix.[2], bone.PoseMatrix.[3],
                    bone.PoseMatrix.[4], bone.PoseMatrix.[5], bone.PoseMatrix.[6], bone.PoseMatrix.[7],
                    bone.PoseMatrix.[8], bone.PoseMatrix.[9], bone.PoseMatrix.[10], bone.PoseMatrix.[11],
                    bone.PoseMatrix.[12], bone.PoseMatrix.[13], bone.PoseMatrix.[14], bone.PoseMatrix.[15]
                )
                let localMatrix = Matrix4x4.Transpose(localMatrixOriginal)
    
                if parentIndex > -1 then
                    worldMatrices.[i] <- localMatrix * worldMatrices.[parentIndex]
                else
                    worldMatrices.[i] <- localMatrix

            // Apply custom scaling in the final skinning matrix calculation
            for i = 0 to boneCount - 1 do
                let bone = skeleton.[i]
                let invBindMatrixOriginal = new Matrix4x4(
                    bone.InversePoseMatrix.[0], bone.InversePoseMatrix.[1], bone.InversePoseMatrix.[2], bone.InversePoseMatrix.[3],
                    bone.InversePoseMatrix.[4], bone.InversePoseMatrix.[5], bone.InversePoseMatrix.[6], bone.InversePoseMatrix.[7],
                    bone.InversePoseMatrix.[8], bone.InversePoseMatrix.[9], bone.InversePoseMatrix.[10], bone.InversePoseMatrix.[11],
                    bone.InversePoseMatrix.[12], bone.InversePoseMatrix.[13], bone.InversePoseMatrix.[14], bone.InversePoseMatrix.[15]
                )
                let invBindMatrix = Matrix4x4.Transpose(invBindMatrixOriginal)
        
                let mutable skinningMatrix = worldMatrices.[i] * invBindMatrixOriginal
            
                finalTransforms.[i] <- Matrix4x4.Transpose(skinningMatrix)

            finalTransforms

    member this.AssignGear(slot: EquipmentSlot, item: IItemModel, race: XivRace, dye1: int, dye2: int, colors: CustomModelColors, customizations: CharacterCustomizations, gd: GraphicsDevice) : Async<unit> =
        try
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
                                let! model = 
                                    try
                                        Mdl.GetTTModel(item, race)
                                    with ex ->
                                        Log.Fatal("Failed to complete GetTTModel for {Item}: {Message}", item.Name, ex.Message)
                                        raise(ex)
                                let _ =
                                    try
                                        model.Source
                                    with ex ->
                                        Log.Error("Could not read model source for {Item}: {Message}", item.Name, ex.Message)
                                        raise ex
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
                                            Log.Fatal("Failed to load any model for item {ItemName} across all racial fallbacks", item.Name)
                                            return raise (exn "Failed to load any model after all fallback attempts")
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
                    let nullCustomizations =
                        {
                            Height = 800.0f
                            BustSize = 0.0f
                            FaceScale = 1.0f
                            MuscleDefinition = 1.0f
                        }
                    do! this.RebuildCharacterModel(gd, race, customizations)

                
                with ex ->
                    Log.Error("Failed to load TTModel for item {ItemName}: {message}", item.Name, ex.Message)
                    raise ex
            
            }
        with ex ->
            Log.Error(ex, "AssignGear failed for slot {Slot} with item {ItemName}", slot, item.Name)
            reraise()

    member this.RebuildCharacterModel(gd: GraphicsDevice, race: XivRace, customizations: CharacterCustomizations) =
        try
            async {
                let! activeModels =
                    async {
                        let! flaggedModels = applyFlags ttModelMap |> Async.AwaitTask
                    return 
                        flaggedModels
                        |> Map.values
                        |> Seq.toList
                    }
                    
                if activeModels.IsEmpty then
                    match currentCharacterModel with
                    | Some oldModel ->
                        disposeQueue.Enqueue((oldModel, 5))
                    | None -> ()
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

                // Build unified geometry with proper bone index mapping
                let geometryData = System.Collections.Generic.Dictionary<string, ResizeArray<VertexPositionSkinned> * ResizeArray<uint16>>()

                // Build material dictionary
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

        
                for input in activeModels do
                    let mutable totalVertices = 0.0f
                    let mutable averageX = 0.0f
                    let mutable averageY = 0.0f
                    let mutable averageZ = 0.0f

                    for mesh in input.Model.MeshGroups do                
                        for part in mesh.Parts do
                            for vertex in part.Vertices do
                                averageX <- averageX + vertex.Position.X
                                averageY <- averageY + vertex.Position.Y
                                averageZ <- averageZ + vertex.Position.Z
                                totalVertices <- totalVertices + 1.0f

                    let centerX = averageX / totalVertices
                    let centerY = averageY / totalVertices
                    let centerZ = averageZ / totalVertices

                    for mesh in input.Model.MeshGroups do
                        let materialPath = mesh.Material
                        if not (geometryData.ContainsKey(materialPath)) then
                            geometryData.[materialPath] <- (ResizeArray<VertexPositionSkinned>(), ResizeArray<uint16>())
                
                        let (vertexList, indexList) = geometryData.[materialPath]

                        for part in mesh.Parts do
                            let vertexOffset = vertexList.Count
                            for vertex in part.Vertices do
                                let mutable scaledPosition = vertex.Position

                                let bustInfluence =
                                    [0..7]
                                    |> List.sumBy (fun i ->
                                        let localBoneIndex = int vertex.BoneIds.[i]
                                        if localBoneIndex < mesh.Bones.Count then
                                            let boneName = mesh.Bones.[localBoneIndex]
                                            if boneName.Contains("j_mune") then
                                                (float32 vertex.Weights.[i] / 255.0f)
                                            else 0.0f
                                        else 0.0f
                                    )
                                
                                if bustInfluence > 0.0f then
                                    let bustScale = this.handleBustScaling(customizations.BustSize)
                                    let centerPoint = SharpDX.Vector3(centerX, centerY, centerZ)

                                    let offsetFromCenter = scaledPosition - centerPoint
                                    
                                    let effectiveScale = SharpDX.Vector3(
                                        1.0f + (bustScale.Z - 1.0f) * bustInfluence,
                                        1.0f + (bustScale.Z - 1.0f) * bustInfluence,
                                        1.0f + (bustScale.Z - 1.0f) * bustInfluence
                                    )

                                    let scaledOffset = SharpDX.Vector3(
                                        offsetFromCenter.X * effectiveScale.X,
                                        offsetFromCenter.Y * effectiveScale.Y,
                                        offsetFromCenter.Z * effectiveScale.Z
                                    )
                                    scaledPosition <- centerPoint + scaledOffset

                                let heightScale = this.handleBustScaling(customizations.Height)
                                scaledPosition <- SharpDX.Vector3(
                                    scaledPosition.X * heightScale.Y,
                                    scaledPosition.Y * heightScale.Y,
                                    scaledPosition.Z * heightScale.Y
                                )

                                let mutable boneIndices = Array.create 4 0.0f
                                let mutable boneWeights = Array.create 4 0.0f

                                // Map mesh-local bone indices to global skeleton indices
                                for i = 0 to 3 do
                                    let localBoneIndex = int vertex.BoneIds.[i]
                                    if localBoneIndex < mesh.Bones.Count then
                                        let boneName = mesh.Bones.[localBoneIndex]
                                        if masterBoneIndexLookup.ContainsKey(boneName) then
                                            boneIndices.[i] <- float32 masterBoneIndexLookup.[boneName]
                                            boneWeights.[i] <- (float32 vertex.Weights.[i]) / 255.0f

                                vertexList.Add(
                                    VertexPositionSkinned(
                                        SharpToNumerics.vec3 scaledPosition,
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
                                let test = uint16 (vertexOffset + index)
                                indexList.Add(test)

                // Create unified render meshes (single draw call per material type)
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
                    match currentCharacterModel with
                    | Some oldModel ->
                        disposeQueue.Enqueue((oldModel, 5))
                    | None -> ()

                    let finalRenderModel = { Meshes = finalMeshes |> List.ofSeq; Original = null }
                    currentCharacterModel <- Some finalRenderModel
            
                    // Calculate bone transforms with customizations and update GPU buffer
                    let customizedTransforms = this.calculateBoneTransforms skeleton customizations
                    gd.UpdateBuffer(boneTransformBuffer.Value, 0u, customizedTransforms)
                else
                    match currentCharacterModel with
                    | Some oldModel -> disposeQueue.Enqueue((oldModel, 5))
                    | None -> ()
                    currentCharacterModel <- None
            }
        with ex ->
            Log.Error("Failed to build skeletal model: {Message}", ex.Message)
            reraise()

    member this.AssignTrigger (slot: EquipmentSlot, item: IItemModel, race: XivRace, dye1: int, dye2: int, colors: CustomModelColors, customizations: CharacterCustomizations) : Async<unit> =
        async {
          let tcs = TaskCompletionSource<unit>()
          do! agent.PostAndAsyncReply(fun mailboxAckReply -> (slot, item, race, dye1, dye2, colors, customizations, mailboxAckReply, tcs))

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

    member this.DisposeRenderModel(renderModel: RenderModel) : unit =
        try
            for mesh in renderModel.Meshes do
                try
                    mesh.VertexBuffer.Dispose()
                with ex -> printfn $"Failed to dispose vertex buffer {ex.Message}"
                try
                    mesh.IndexBuffer.Dispose()
                with ex -> printfn $"Failed to dispose index buffer: {ex.Message}"
                try
                    mesh.Material.Dispose()
                with ex -> printfn $"Failed to dispose material: {ex.Message}"
        with ex ->
            printfn $"Error disposing render model: {ex.Message}"

    member this.ClearGearSlot(slot: EquipmentSlot) =
        modelMap <- modelMap.Remove(slot)
        ttModelMap <- ttModelMap.Remove(slot)

    member this.handleBustScaling(inputPercent: float32) : Vector3 =
        let clampedPercent = Math.Clamp(inputPercent, 0.0f, 300.0f)

        let scaleX = (0.92f + (inputPercent * 0.0016f))
        let scaleY = (0.816f + (inputPercent * 0.00368f))
        let scaleZ = (0.8f + (inputPercent * 0.004f))

        Vector3(scaleX, scaleY, scaleZ)

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


