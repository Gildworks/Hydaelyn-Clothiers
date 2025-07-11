module SkeletalRenderer

open System
open System.Numerics
open System.Threading.Tasks

open Veldrid
open Veldrid.Utilities

open xivModdingFramework.General.Enums
open xivModdingFramework.Models.DataContainers
open xivModdingFramework.Models.ModelTextures
open xivModdingFramework.Materials.DataContainers
open xivModdingFramework.Materials.FileTypes

open CameraController
open Shared
open SkeletalData

let MaxBones = 256  // Adjust based on your needs

let createSkeletalResources (gd: GraphicsDevice) (textureLayout: ResourceLayout) : SkeletalRenderResources =
    let factory = gd.ResourceFactory
    
    // Create bone matrices buffer (256 bones * 64 bytes per matrix)
    let boneMatricesBuffer = factory.CreateBuffer(
        BufferDescription(uint32 (MaxBones * 64), BufferUsage.UniformBuffer ||| BufferUsage.Dynamic)
    )
    
    // Create bone matrices layout and resource set
    let boneMatricesLayout = factory.CreateResourceLayout(
        ResourceLayoutDescription(
            ResourceLayoutElementDescription("BoneMatrices", ResourceKind.UniformBuffer, ShaderStages.Vertex)
        )
    )
    let boneMatricesSet = factory.CreateResourceSet(
        ResourceSetDescription(boneMatricesLayout, boneMatricesBuffer)
    )
    
    // Create MVP buffer and resources (using your existing structure)
    let mvpBuffer = factory.CreateBuffer(
        BufferDescription(uint32 (sizeof<Matrix4x4> * 2), BufferUsage.UniformBuffer ||| BufferUsage.Dynamic)
    )
    let mvpLayout = factory.CreateResourceLayout(
        ResourceLayoutDescription(
            ResourceLayoutElementDescription("TransformsBuffer", ResourceKind.UniformBuffer, ShaderStages.Vertex)
        )
    )
    let mvpSet = factory.CreateResourceSet(ResourceSetDescription(mvpLayout, mvpBuffer))
    
    {
        BoneMatricesBuffer = boneMatricesBuffer
        BoneMatricesLayout = boneMatricesLayout
        BoneMatricesSet = boneMatricesSet
        SkeletalPipeline = None  // Will be created when we have character data
        MVPBuffer = mvpBuffer
        MVPLayout = mvpLayout
        MVPSet = mvpSet
        TextureLayout = textureLayout
        CharacterModel = None
        BoneTransforms = Array.create MaxBones Matrix4x4.Identity
    }

let createSkeletalPipeline (gd: GraphicsDevice) (resources: SkeletalRenderResources) (outputDesc: OutputDescription) : Pipeline =
    printfn "Creating skeletal pipeline..."
    let factory = gd.ResourceFactory
    
    try
        // Updated vertex layout for skinned vertices (matching your GLSL)
        printfn "Creating vertex layout..."
        let vertexLayout = VertexLayoutDescription([|
            VertexElementDescription("Position", VertexElementSemantic.Position, VertexElementFormat.Float3)
            VertexElementDescription("Color", VertexElementSemantic.Color, VertexElementFormat.Float4)
            VertexElementDescription("UV", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2)
            VertexElementDescription("Normal", VertexElementSemantic.Normal, VertexElementFormat.Float3)
            VertexElementDescription("Tangent", VertexElementSemantic.Normal, VertexElementFormat.Float3)
            VertexElementDescription("Bitangent", VertexElementSemantic.Normal, VertexElementFormat.Float3)
            VertexElementDescription("BoneIndices", VertexElementSemantic.Normal, VertexElementFormat.Float4)
            VertexElementDescription("BoneWeights", VertexElementSemantic.Normal, VertexElementFormat.Float4)
        |])
        
        // Use your existing GLSL shaders compiled to SPIR-V
        printfn "Loading skeletal shaders..."
        let shaders = ShaderUtils.getSkeletalShaderSet factory
        printfn $"Loaded {shaders.Length} shaders"
        
        let shaderSet = ShaderSetDescription([| vertexLayout |], shaders)
        
        printfn "Creating blend state..."
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
        
        printfn "Creating pipeline description..."
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
            [| resources.MVPLayout; resources.BoneMatricesLayout; resources.TextureLayout |],
            outputDesc
        )
        
        printfn "Creating graphics pipeline..."
        let pipeline = factory.CreateGraphicsPipeline(pipelineDesc)
        printfn "Graphics pipeline created successfully"
        
        pipeline
    with ex ->
        printfn $"ERROR creating skeletal pipeline: {ex.Message}"
        printfn $"Stack trace: {ex.StackTrace}"
        reraise()

// Calculate world-space bone matrices for rendering - FIXED VERSION
let calculateBoneMatrices (skeleton: CharacterSkeleton) (deformations: Map<string, Matrix4x4>) : Matrix4x4 array =
    let boneMatrices = Array.create skeleton.Bones.Length Matrix4x4.Identity
    
    // First pass: calculate world matrices for each bone
    let rec calculateBoneWorldMatrix (boneIndex: int) =
        if boneIndex < 0 || boneIndex >= skeleton.Bones.Length then
            Matrix4x4.Identity
        else
            // Check if already calculated
            if boneMatrices.[boneIndex] <> Matrix4x4.Identity then
                boneMatrices.[boneIndex]
            else
                let bone = skeleton.Bones.[boneIndex]
                
                // Get deformation matrix for this bone (if any)
                let deformMatrix = 
                    match deformations.TryFind(bone.Name) with
                    | Some matrix -> matrix
                    | None -> Matrix4x4.Identity
                
                // Calculate local transform (bind pose + deformation)
                let localMatrix = bone.BindPose * deformMatrix
                
                // Get parent world matrix
                let parentWorldMatrix = 
                    if bone.ParentIndex >= 0 && bone.ParentIndex < skeleton.Bones.Length then
                        calculateBoneWorldMatrix bone.ParentIndex
                    else
                        Matrix4x4.Identity
                
                // Calculate world matrix
                let worldMatrix = localMatrix * parentWorldMatrix
                boneMatrices.[boneIndex] <- worldMatrix
                worldMatrix
    
    // Calculate all bone world matrices
    for i in 0 .. skeleton.Bones.Length - 1 do
        calculateBoneWorldMatrix i |> ignore
    
    // FIXED: Apply inverse bind pose correctly for skinning
    skeleton.Bones 
    |> Array.mapi (fun i bone ->
        // Skinning matrix = World * InverseBindPose (not the other way around)
        boneMatrices.[i] * bone.InverseBindPose
    )

// Simplified material creation for testing (clay render)
let createSimpleMaterial (factory: ResourceFactory) (gd: GraphicsDevice) (textureLayout: ResourceLayout) : PreparedMaterial =
    // Create simple 1x1 textures for testing
    let createSolidTexture (color: RgbaByte) (format: PixelFormat) =
        let desc = TextureDescription.Texture2D(1u, 1u, 1u, 1u, format, TextureUsage.Sampled)
        let tex = factory.CreateTexture(desc)
        gd.UpdateTexture(tex, [| color |], 0u, 0u, 0u, 1u, 1u, 1u, 0u, 0u)
        tex
    
    // Simple clay colors
    let clayDiffuse = createSolidTexture (RgbaByte(180uy, 140uy, 120uy, 255uy)) PixelFormat.R8_G8_B8_A8_UNorm_SRgb
    let flatNormal = createSolidTexture (RgbaByte(128uy, 128uy, 255uy, 255uy)) PixelFormat.R8_G8_B8_A8_UNorm  // Flat normal
    let blackTexture = createSolidTexture (RgbaByte(0uy, 0uy, 0uy, 255uy)) PixelFormat.R8_G8_B8_A8_UNorm
    let whiteTexture = createSolidTexture (RgbaByte(255uy, 255uy, 255uy, 255uy)) PixelFormat.R8_G8_B8_A8_UNorm
    
    let sampler = factory.CreateSampler(SamplerDescription.Linear)
    
    let resourceSet = factory.CreateResourceSet(ResourceSetDescription(
        textureLayout,
        clayDiffuse :> BindableResource,      // tex_Diffuse
        flatNormal :> BindableResource,       // tex_Normal  
        blackTexture :> BindableResource,     // tex_Specular
        blackTexture :> BindableResource,     // tex_Emissive
        whiteTexture :> BindableResource,     // tex_Alpha
        whiteTexture :> BindableResource,     // tex_Roughness
        blackTexture :> BindableResource,     // tex_Metalness
        whiteTexture :> BindableResource,     // tex_Occlusion
        blackTexture :> BindableResource,     // tex_Subsurface
        sampler :> BindableResource           // SharedSampler
    ))
    
    {
        DiffuseTexture = clayDiffuse
        NormalTexture = flatNormal
        SpecularTexture = blackTexture
        EmissiveTexture = blackTexture
        AlphaTexture = whiteTexture
        RoughnessTexture = whiteTexture
        MetalnessTexture = blackTexture
        OcclusionTexture = whiteTexture
        SubsurfaceTexture = blackTexture
        ResourceSet = resourceSet
        Mtrl = Unchecked.defaultof<XivMtrl>  // Dummy for now
    }

// Load character model with skeletal animation (adapted for your system)
let loadCharacterModel 
    (factory: ResourceFactory)
    (gd: GraphicsDevice) 
    (ttModelMap: Map<EquipmentSlot, TTModel>)
    (race: XivRace)
    (textureLayout: ResourceLayout)
    : Async<SkinnedCharacterModel> =
    
    async {
        printfn $"Loading character model with {ttModelMap.Count} equipment pieces"
        
        // Get any TTModel to build skeleton from (they should all have compatible skeletons)
        let referenceTTModel = 
            ttModelMap 
            |> Map.toSeq 
            |> Seq.head 
            |> snd
        
        printfn $"Using reference model with {referenceTTModel.Bones.Count} bones"
        
        // Build unified skeleton
        printfn "Building skeleton..."
        let! skeleton = buildSkeletonFromTTModel referenceTTModel race
        printfn $"Skeleton built with {skeleton.Bones.Length} bones"
        
        if isNull (box skeleton.Bones) then
            printfn "ERROR: skeleton.Bones is null!"
            failwith "Skeleton bones array is null"
        
        if isNull (box skeleton.BoneNameToIndex) then
            printfn "ERROR: skeleton.BoneNameToIndex is null!"
            failwith "Skeleton bone name map is null"
        
        // Combine all equipment models into unified mesh
        printfn "Combining models to skinned mesh..."
        try
            let ttModelArray = ttModelMap |> Map.toArray
            printfn $"TTModel array length: {ttModelArray.Length}"
            
            let unifiedMesh = combineModelsToSkinnedMesh factory gd ttModelArray skeleton
            printfn $"Unified mesh created with {unifiedMesh.Vertices.Length} vertices"
            
            // Create materials for each piece of equipment
            printfn "Loading materials for equipment pieces..."
            let! materials = 
                async {
                    // Get all unique material paths from all equipment pieces
                    let materialPaths = 
                        ttModelArray
                        |> Array.collect (fun (slot, ttModel) ->
                            ttModel.Materials
                            |> Seq.distinct
                            |> Seq.toArray
                        )
                        |> Array.distinct
                    
                    printfn $"Found {materialPaths.Length} unique materials to load"
                    
                    // Load each material (following your ModelLoader pattern)
                    let materialLoadTasks = 
                        materialPaths
                        |> Array.map (fun materialPath ->
                            async {
                                try
                                    printfn $"Loading material: {materialPath}"
                                    
                                    // Get the actual XivMtrl (following your ModelLoader pattern)
                                    let tx = xivModdingFramework.Mods.ModTransaction.BeginReadonlyTransaction()
                                    let! mtrl = 
                                        async {
                                            // Try to get material path - simplified version of your logic
                                            let finalPath = 
                                                try
                                                    // For skeletal models, we might need to resolve the path differently
                                                    // For now, assume the path is already correct
                                                    materialPath
                                                with _ ->
                                                    printfn $"Material path empty, will probably fall back to clay."
                                                    materialPath
                                            

                                            let! finalMat = Mtrl.GetXivMtrl(finalPath, true, tx) |> Async.AwaitTask
                                            printfn $"Material results: {finalMat.MTRLPath}"
                                            return! Mtrl.GetXivMtrl(finalPath, true) |> Async.AwaitTask
                                        }
                                    
                                    // Use default colors/dyes for now
                                    let dye1 = 0
                                    let dye2 = 0
                                    let colors = ModelTexture.GetCustomColors()
                                    
                                    let! material = 
                                        MaterialBuilder.materialBuilder 
                                            factory gd textureLayout 
                                            dye1 dye2 colors 
                                            mtrl
                                            materialPath
                                        |> Async.AwaitTask
                                    
                                    return Some (materialPath, material)
                                with ex ->
                                    printfn $"Failed to load material {materialPath}, falling back to clay: {ex.Message}"
                                    // Fall back to simple clay material
                                    let clayMaterial = createSimpleMaterial factory gd textureLayout
                                    return Some (materialPath, clayMaterial)
                            }
                        )
                    
                    // Execute all material loading tasks
                    let! loadedMaterialResults = Async.Parallel materialLoadTasks
                    
                    // Create material dictionary
                    let materialDict = 
                        loadedMaterialResults
                        |> Array.choose id
                        |> dict
                    
                    // Create array of materials in the order they appear in mesh groups
                    let materialArray = 
                        ttModelArray
                        |> Array.collect (fun (slot, ttModel) ->
                            ttModel.MeshGroups
                            |> Seq.collect (fun meshGroup ->
                                meshGroup.Parts
                                |> Seq.map (fun part ->
                                    match materialDict.TryGetValue(meshGroup.Material) with
                                    | true, mat -> mat
                                    | false, _ -> 
                                        printfn $"WARNING: Material {meshGroup.Material} not found, using clay"
                                        createSimpleMaterial factory gd textureLayout
                                )
                            )
                            |> Array.ofSeq
                        )
                    
                    return materialArray
                }
            
            printfn $"Loaded {materials.Length} materials"
            
            return {
                Skeleton = skeleton
                UnifiedMesh = unifiedMesh
                Materials = materials
            }
        with ex ->
            printfn $"ERROR in mesh combining or material creation: {ex.Message}"
            printfn $"Stack trace: {ex.StackTrace}"
            return raise ex
    }

// Update the rendering resources with character model
let updateWithCharacterModel (gd: GraphicsDevice) (resources: SkeletalRenderResources) (characterModel: SkinnedCharacterModel) (outputDesc: OutputDescription) : SkeletalRenderResources =
    printfn "Starting updateWithCharacterModel..."
    
    try
        if isNull gd then
            printfn "ERROR: GraphicsDevice is null"
            failwith "GraphicsDevice is null"
        
        printfn $"OutputDescription color attachments: {outputDesc.ColorAttachments.Length}"
        printfn $"OutputDescription has depth: {outputDesc.DepthAttachment.HasValue}"
        
        printfn "Creating skeletal pipeline..."
        let skeletalPipeline = createSkeletalPipeline gd resources outputDesc
        printfn "Skeletal pipeline created successfully"
        
        let result = 
            { resources with 
                CharacterModel = Some characterModel
                SkeletalPipeline = Some skeletalPipeline
            }
        
        printfn "updateWithCharacterModel completed successfully"
        result
    with ex ->
        printfn $"ERROR in updateWithCharacterModel: {ex.Message}"
        printfn $"Stack trace: {ex.StackTrace}"
        reraise()

// Render the skeletal character (adapted for your render loop)
let renderSkeletalCharacter 
    (gd: GraphicsDevice)
    (cmdList: CommandList) 
    (resources: SkeletalRenderResources)
    (camera: CameraController)
    (aspectRatio: float32)
    : unit =
    
    match resources.CharacterModel, resources.SkeletalPipeline with
    | Some characterModel, Some pipeline ->
        
        // Calculate MVP matrices (matching your existing logic)
        let view = camera.GetViewMatrix()
        let proj = camera.GetProjectionMatrix(aspectRatio)
        let modelMatrix = Matrix4x4.CreateScale(-2.5f, 2.5f, 2.5f)
        let worldViewMatrix = modelMatrix * view
        let worldViewProjectionMatrix = worldViewMatrix * proj
        let transformsData = [| worldViewProjectionMatrix; worldViewMatrix |]
        
        // Update MVP buffer (matching your TransformsBuffer)
        gd.UpdateBuffer(resources.MVPBuffer, 0u, transformsData)
        
        // Calculate bone matrices (TEMPORARY: use identity for testing)
        let deformations = Map.empty<string, Matrix4x4>  // No deformations for now
        
        // TEMPORARY FIX: Use identity matrices to test basic rendering
        let boneMatrices = Array.create MaxBones Matrix4x4.Identity
        // Uncomment this line once bone calculation is fixed:
        // let boneMatrices = calculateBoneMatrices characterModel.Skeleton deformations
        
        // Pad to MaxBones if needed
        let paddedBoneMatrices = 
            if boneMatrices.Length < MaxBones then
                Array.append boneMatrices (Array.create (MaxBones - boneMatrices.Length) Matrix4x4.Identity)
            else
                boneMatrices |> Array.take MaxBones
        
        // Update bone matrices buffer
        gd.UpdateBuffer(resources.BoneMatricesBuffer, 0u, paddedBoneMatrices)
        
        // Set pipeline and shared resources
        cmdList.SetPipeline(pipeline)
        cmdList.SetGraphicsResourceSet(0u, resources.MVPSet)          // set = 0: TransformsBuffer
        cmdList.SetGraphicsResourceSet(1u, resources.BoneMatricesSet) // set = 1: BoneMatrices
        
        // Set vertex and index buffers
        cmdList.SetVertexBuffer(0u, characterModel.UnifiedMesh.VertexBuffer)
        cmdList.SetIndexBuffer(characterModel.UnifiedMesh.IndexBuffer, IndexFormat.UInt16)
        
        // For now, render everything with the first material
        // TODO: Later you can split this by material indices
        if characterModel.Materials.Length > 0 then
            cmdList.SetGraphicsResourceSet(2u, characterModel.Materials.[0].ResourceSet) // set = 2: Textures
            cmdList.DrawIndexed(uint32 characterModel.UnifiedMesh.Indices.Length, 1u, 0u, 0, 0u)
        else
            printfn "WARNING: No materials available for rendering"
        
    | _ -> ()  // No character or pipeline to render

// Dispose resources
let disposeSkeletalResources (resources: SkeletalRenderResources) : unit =
    resources.BoneMatricesBuffer.Dispose()
    resources.BoneMatricesLayout.Dispose()
    resources.BoneMatricesSet.Dispose()
    resources.MVPBuffer.Dispose()
    resources.MVPLayout.Dispose()
    resources.MVPSet.Dispose()
    
    match resources.SkeletalPipeline with
    | Some pipeline -> pipeline.Dispose()
    | None -> ()
    
    match resources.CharacterModel with
    | Some model ->
        model.UnifiedMesh.VertexBuffer.Dispose()
        model.UnifiedMesh.IndexBuffer.Dispose()
        for material in model.Materials do
            material.Dispose()
    | None -> ()