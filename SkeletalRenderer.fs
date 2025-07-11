module SkeletalRenderer

open System
open System.Numerics

open Veldrid
open Veldrid.Utilities

open xivModdingFramework.General.Enums
open xivModdingFramework.Models.DataContainers
open xivModdingFramework.Models.ModelTextures
open xivModdingFramework.Materials.DataContainers
open xivModdingFramework.Materials.FileTypes
open xivModdingFramework.Mods

open CameraController
open Shared
open SkeletalData
open MaterialBuilder

let MaxBones = 256
let MaxMaterials = 64

// Helper function to ensure all textures have the same dimensions
let findCommonTextureDimensions (materialTextures: MaterialTextureInfo array) : (int * int) =
    if materialTextures.Length = 0 then 
        (512, 512)  // Default size
    else
        // Log all texture dimensions for debugging
        materialTextures |> Array.iteri (fun i mat -> 
            printfn $"Material {i}: {mat.Width}x{mat.Height}")
        
        // Find the most common dimensions, or use a reasonable default
        let dimensions = materialTextures |> Array.map (fun mat -> (mat.Width, mat.Height))
        let dimensionCounts = dimensions |> Array.countBy id
        let sortedDimensions = dimensionCounts |> Array.sortByDescending snd
        
        if sortedDimensions.Length > 0 then
            let (width, height), count = sortedDimensions.[0]
            printfn $"Using most common dimensions: {width}x{height} (appears {count} times)"
            (width, height)
        else
            printfn "Using default dimensions: 512x512"
            (512, 512)

// Helper function to resize texture data to target dimensions
let resizeTextureData (sourceData: byte array) (sourceWidth: int) (sourceHeight: int) (targetWidth: int) (targetHeight: int) : byte array =
    if sourceWidth = targetWidth && sourceHeight = targetHeight then
        sourceData
    else
        // Simple nearest-neighbor resizing for now
        // In production, you'd want proper filtering
        let targetData = Array.zeroCreate (targetWidth * targetHeight * 4)
        let xScale = float sourceWidth / float targetWidth
        let yScale = float sourceHeight / float targetHeight
        
        for y in 0 .. targetHeight - 1 do
            for x in 0 .. targetWidth - 1 do
                let srcX = int (float x * xScale)
                let srcY = int (float y * yScale)
                let srcIndex = (srcY * sourceWidth + srcX) * 4
                let dstIndex = (y * targetWidth + x) * 4
                
                if srcIndex + 3 < sourceData.Length && dstIndex + 3 < targetData.Length then
                    targetData.[dstIndex + 0] <- sourceData.[srcIndex + 0]     // R
                    targetData.[dstIndex + 1] <- sourceData.[srcIndex + 1]     // G
                    targetData.[dstIndex + 2] <- sourceData.[srcIndex + 2]     // B
                    targetData.[dstIndex + 3] <- sourceData.[srcIndex + 3]     // A
        
        targetData

// Create texture array from material texture data
let createTextureArray (factory: ResourceFactory) (gd: GraphicsDevice) (materialTextures: MaterialTextureInfo array) (getTextureData: MaterialTextureInfo -> byte array) (format: PixelFormat) (debugName: string) : Texture =
    if materialTextures.Length = 0 then
        // Create a dummy 1x1 texture array with one layer
        let desc = TextureDescription(
            1u, 1u,        // width, height
            1u,            // depth 
            1u,            // mip levels
            1u,            // ARRAY LAYERS - this makes it an array
            format, 
            TextureUsage.Sampled, 
            TextureType.Texture2D  // Still 2D, but with array layers
        )
        let textureArray = factory.CreateTexture(desc)
        let dummyData = [| RgbaByte(128uy, 128uy, 128uy, 255uy) |]
        gd.UpdateTexture(textureArray, dummyData, 0u, 0u, 0u, 1u, 1u, 1u, 0u, 0u)
        printfn $"Created dummy {debugName} texture array"
        textureArray
    else
        let (commonWidth, commonHeight) = findCommonTextureDimensions materialTextures
        let layerCount = uint32 materialTextures.Length
        
        printfn $"Creating {debugName} texture array: {commonWidth}x{commonHeight} with {layerCount} layers"
        
        // CRITICAL FIX: Create actual texture array with multiple layers
        let desc = TextureDescription(
            uint32 commonWidth, 
            uint32 commonHeight, 
            1u,                    // depth (always 1 for 2D)
            1u,                    // mip levels  
            layerCount,            // ARRAY LAYERS - this is the key!
            format, 
            TextureUsage.Sampled, 
            TextureType.Texture2D  // 2D texture with array layers
        )
        let textureArray = factory.CreateTexture(desc)
        
        // Upload each material's texture data as a separate array layer
        for i in 0 .. materialTextures.Length - 1 do
            let material = materialTextures.[i]
            let sourceData = getTextureData material
            
            printfn $"Processing material {i}: source={material.Width}x{material.Height}, target={commonWidth}x{commonHeight}, sourceDataLength={sourceData.Length}"
            
            // Check if the source data is valid
            let expectedSourceSize = material.Width * material.Height * 4
            if sourceData.Length <> expectedSourceSize then
                printfn $"WARNING: Material {i} has incorrect data size. Expected {expectedSourceSize}, got {sourceData.Length}"
                // Create dummy data for this layer
                let dummyData = Array.create (commonWidth * commonHeight) (RgbaByte(128uy, 128uy, 128uy, 255uy))
                gd.UpdateTexture(
                    textureArray,
                    dummyData,
                    0u, 0u, 0u,                    // x, y, z offset
                    uint32 commonWidth, uint32 commonHeight, 1u,  // width, height, depth
                    0u,                            // mip level
                    uint32 i                       // ARRAY LAYER - this is crucial!
                )
                printfn $"Used dummy data for material {i} due to size mismatch"
            else
                // Resize if necessary
                let resizedData = resizeTextureData sourceData material.Width material.Height commonWidth commonHeight
                
                // Verify the resized data size
                let expectedResizedSize = commonWidth * commonHeight * 4
                if resizedData.Length <> expectedResizedSize then
                    printfn $"ERROR: Resized data for material {i} has wrong size. Expected {expectedResizedSize}, got {resizedData.Length}"
                    let dummyData = Array.create (commonWidth * commonHeight) (RgbaByte(128uy, 128uy, 128uy, 255uy))
                    gd.UpdateTexture(
                        textureArray,
                        dummyData,
                        0u, 0u, 0u,
                        uint32 commonWidth, uint32 commonHeight, 1u,
                        0u,
                        uint32 i
                    )
                    printfn $"Used dummy data for material {i} due to resize error"
                else
                    // Convert to RgbaByte array
                    let rgbaData = 
                        resizedData
                        |> Array.chunkBySize 4
                        |> Array.map (fun chunk -> 
                            if chunk.Length = 4 then
                                RgbaByte(chunk.[0], chunk.[1], chunk.[2], chunk.[3])
                            else
                                RgbaByte(128uy, 128uy, 128uy, 255uy)
                        )
                    
                    // Final size check
                    let expectedPixelCount = commonWidth * commonHeight
                    if rgbaData.Length <> expectedPixelCount then
                        printfn $"ERROR: RgbaData for material {i} has wrong pixel count. Expected {expectedPixelCount}, got {rgbaData.Length}"
                        let dummyData = Array.create expectedPixelCount (RgbaByte(128uy, 128uy, 128uy, 255uy))
                        gd.UpdateTexture(
                            textureArray,
                            dummyData,
                            0u, 0u, 0u,
                            uint32 commonWidth, uint32 commonHeight, 1u,
                            0u,
                            uint32 i
                        )
                        printfn $"Used dummy data for material {i} due to pixel count mismatch"
                    else
                        // Upload to specific array layer
                        try
                            gd.UpdateTexture(
                                textureArray,
                                rgbaData,
                                0u, 0u, 0u,                    // x, y, z offset
                                uint32 commonWidth, uint32 commonHeight, 1u,  // width, height, depth
                                0u,                            // mip level
                                uint32 i                       // ARRAY LAYER - upload to layer i
                            )
                            printfn $"Successfully uploaded material {i} to array layer {i}"
                        with ex ->
                            printfn $"ERROR uploading material {i}: {ex.Message}"
                            let dummyData = Array.create expectedPixelCount (RgbaByte(255uy, 0uy, 255uy, 255uy))
                            gd.UpdateTexture(
                                textureArray,
                                dummyData,
                                0u, 0u, 0u,
                                uint32 commonWidth, uint32 commonHeight, 1u,
                                0u,
                                uint32 i
                            )
                            printfn $"Used magenta dummy data for material {i} after upload failure"
        
        printfn $"Created {debugName} texture array successfully with {layerCount} layers"
        textureArray

// Create material data buffer for GPU
let createMaterialDataBuffer (factory: ResourceFactory) (gd: GraphicsDevice) (materialTextures: MaterialTextureInfo array) : MaterialData array =
    let materialDataArray = Array.zeroCreate<MaterialData> MaxMaterials
    
    // Fill in material data
    for i in 0 .. min (materialTextures.Length - 1) (MaxMaterials - 1) do
        materialDataArray.[i] <- {
            DiffuseIndex = i      // Each material gets its own texture array layer
            NormalIndex = i
            SpecularIndex = i
            EmissiveIndex = i
            AlphaIndex = i
            RoughnessIndex = i
            MetalnessIndex = i
            OcclusionIndex = i
            SubsurfaceIndex = i
            Padding1 = 0  // Individual padding fields
            Padding2 = 0
            Padding3 = 0
            Padding4 = 0
            Padding5 = 0
            Padding6 = 0
            Padding7 = 0
        }
    
    printfn $"Created material data array with {materialTextures.Length} materials"
    materialDataArray

let createSkeletalResources (gd: GraphicsDevice) (textureLayout: ResourceLayout) : SkeletalRenderResources =
    let factory = gd.ResourceFactory
    
    // ... existing buffer creation code stays the same ...
    
    // Create bone matrices buffer
    let boneMatricesBuffer = factory.CreateBuffer(
        BufferDescription(uint32 (MaxBones * 64), BufferUsage.UniformBuffer ||| BufferUsage.Dynamic)
    )
    let boneMatricesLayout = factory.CreateResourceLayout(
        ResourceLayoutDescription(
            ResourceLayoutElementDescription("BoneMatrices", ResourceKind.UniformBuffer, ShaderStages.Vertex)
        )
    )
    let boneMatricesSet = factory.CreateResourceSet(
        ResourceSetDescription(boneMatricesLayout, boneMatricesBuffer)
    )
    
    // Create MVP buffer and resources
    let mvpBuffer = factory.CreateBuffer(
        BufferDescription(uint32 (sizeof<Matrix4x4> * 2), BufferUsage.UniformBuffer ||| BufferUsage.Dynamic)
    )
    let mvpLayout = factory.CreateResourceLayout(
        ResourceLayoutDescription(
            ResourceLayoutElementDescription("TransformsBuffer", ResourceKind.UniformBuffer, ShaderStages.Vertex)
        )
    )
    let mvpSet = factory.CreateResourceSet(ResourceSetDescription(mvpLayout, mvpBuffer))
    
    // Create material data buffer
    let materialBuffer = factory.CreateBuffer(
        BufferDescription(uint32 (MaxMaterials * MaterialData.SizeInBytes), BufferUsage.UniformBuffer ||| BufferUsage.Dynamic)
    )
    let materialLayout = factory.CreateResourceLayout(
        ResourceLayoutDescription(
            ResourceLayoutElementDescription("MaterialBuffer", ResourceKind.UniformBuffer, ShaderStages.Fragment)
        )
    )
    let emptyMaterialData = Array.zeroCreate<MaterialData> MaxMaterials
    gd.UpdateBuffer(materialBuffer, 0u, emptyMaterialData)
    let materialSet = factory.CreateResourceSet(ResourceSetDescription(materialLayout, materialBuffer))
    
    // Create texture array layout
    let textureArrayLayout = factory.CreateResourceLayout(
        ResourceLayoutDescription([|
            ResourceLayoutElementDescription("tex_Diffuse", ResourceKind.TextureReadOnly, ShaderStages.Fragment)     // Regular 2D
            ResourceLayoutElementDescription("tex_Normal", ResourceKind.TextureReadOnly, ShaderStages.Fragment)      // Regular 2D
            ResourceLayoutElementDescription("tex_Specular", ResourceKind.TextureReadOnly, ShaderStages.Fragment)    // Regular 2D
            ResourceLayoutElementDescription("tex_Emissive", ResourceKind.TextureReadOnly, ShaderStages.Fragment)    // Regular 2D
            ResourceLayoutElementDescription("tex_Alpha", ResourceKind.TextureReadOnly, ShaderStages.Fragment)       // Regular 2D
            ResourceLayoutElementDescription("tex_Roughness", ResourceKind.TextureReadOnly, ShaderStages.Fragment)   // Regular 2D
            ResourceLayoutElementDescription("tex_Metalness", ResourceKind.TextureReadOnly, ShaderStages.Fragment)   // Regular 2D
            ResourceLayoutElementDescription("tex_Occlusion", ResourceKind.TextureReadOnly, ShaderStages.Fragment)   // Regular 2D
            ResourceLayoutElementDescription("tex_Subsurface", ResourceKind.TextureReadOnly, ShaderStages.Fragment)  // Regular 2D
            ResourceLayoutElementDescription("SharedSampler", ResourceKind.Sampler, ShaderStages.Fragment)
        |])
    )
    
    // Create regular 2D dummy textures
    let createDummy2DTexture (format: PixelFormat) (color: RgbaByte) =
        let desc = TextureDescription.Texture2D(64u, 64u, 1u, 1u, format, TextureUsage.Sampled)
        let texture = factory.CreateTexture(desc)
        let data = Array.create (64 * 64) color
        gd.UpdateTexture(texture, data, 0u, 0u, 0u, 64u, 64u, 1u, 0u, 0u)
        texture
    
    let dummyDiffuse = createDummy2DTexture PixelFormat.R8_G8_B8_A8_UNorm_SRgb (RgbaByte(180uy, 140uy, 120uy, 255uy))  // Clay color
    let dummyNormal = createDummy2DTexture PixelFormat.R8_G8_B8_A8_UNorm (RgbaByte(128uy, 128uy, 255uy, 255uy))        // Flat normal
    let dummySpecular = createDummy2DTexture PixelFormat.R8_G8_B8_A8_UNorm_SRgb (RgbaByte(64uy, 64uy, 64uy, 255uy))    // Low specular
    let dummyEmissive = createDummy2DTexture PixelFormat.R8_G8_B8_A8_UNorm_SRgb (RgbaByte(0uy, 0uy, 0uy, 255uy))       // No emission
    let dummyAlpha = createDummy2DTexture PixelFormat.R8_G8_B8_A8_UNorm (RgbaByte(255uy, 255uy, 255uy, 255uy))         // Full alpha
    let dummyRoughness = createDummy2DTexture PixelFormat.R8_G8_B8_A8_UNorm (RgbaByte(128uy, 128uy, 128uy, 255uy))     // Medium roughness
    let dummyMetalness = createDummy2DTexture PixelFormat.R8_G8_B8_A8_UNorm (RgbaByte(0uy, 0uy, 0uy, 255uy))           // Non-metallic
    let dummyOcclusion = createDummy2DTexture PixelFormat.R8_G8_B8_A8_UNorm (RgbaByte(255uy, 255uy, 255uy, 255uy))     // No occlusion
    let dummySubsurface = createDummy2DTexture PixelFormat.R8_G8_B8_A8_UNorm (RgbaByte(0uy, 0uy, 0uy, 255uy))          // No SSS
    
    let sampler = factory.CreateSampler(SamplerDescription.Linear)
    let textureArraySet = factory.CreateResourceSet(ResourceSetDescription(
        textureArrayLayout,
        dummyDiffuse :> BindableResource,      // All regular 2D textures now
        dummyNormal :> BindableResource,
        dummySpecular :> BindableResource,
        dummyEmissive :> BindableResource,
        dummyAlpha :> BindableResource,
        dummyRoughness :> BindableResource,
        dummyMetalness :> BindableResource,
        dummyOcclusion :> BindableResource,
        dummySubsurface :> BindableResource,
        sampler :> BindableResource
    ))
    
    {
        BoneMatricesBuffer = boneMatricesBuffer
        BoneMatricesLayout = boneMatricesLayout
        BoneMatricesSet = boneMatricesSet
        SkeletalPipeline = None
        MVPBuffer = mvpBuffer
        MVPLayout = mvpLayout
        MVPSet = mvpSet
        TextureLayout = textureLayout
        
        MaterialBuffer = materialBuffer
        MaterialLayout = materialLayout
        MaterialSet = materialSet
        
        // All texture arrays now have multiple layers
        DiffuseTextureArray = dummyDiffuse
        NormalTextureArray = dummyNormal
        SpecularTextureArray = dummySpecular
        EmissiveTextureArray = dummyEmissive
        AlphaTextureArray = dummyAlpha
        RoughnessTextureArray = dummyRoughness
        MetalnessTextureArray = dummyMetalness
        OcclusionTextureArray = dummyOcclusion
        SubsurfaceTextureArray = dummySubsurface
        TextureArrayLayout = textureArrayLayout
        TextureArraySet = textureArraySet
        
        CharacterModel = None
        BoneTransforms = Array.create MaxBones Matrix4x4.Identity
    }

let debugCreateSkeletalPipeline (gd: GraphicsDevice) (resources: SkeletalRenderResources) (outputDesc: OutputDescription) : Pipeline =
    printfn "=== DEBUG: Creating skeletal pipeline ==="
    let factory = gd.ResourceFactory
    
    try
        printfn "DEBUG: Creating vertex layout..."
        let vertexLayout = VertexLayoutDescription([|
            VertexElementDescription("Position", VertexElementSemantic.Position, VertexElementFormat.Float3)
            VertexElementDescription("Color", VertexElementSemantic.Color, VertexElementFormat.Float4)
            VertexElementDescription("UV", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2)
            VertexElementDescription("Normal", VertexElementSemantic.Normal, VertexElementFormat.Float3)
            VertexElementDescription("Tangent", VertexElementSemantic.Normal, VertexElementFormat.Float3)
            VertexElementDescription("Bitangent", VertexElementSemantic.Normal, VertexElementFormat.Float3)
            VertexElementDescription("BoneIndices", VertexElementSemantic.Normal, VertexElementFormat.Float4)
            VertexElementDescription("BoneWeights", VertexElementSemantic.Normal, VertexElementFormat.Float4)
            VertexElementDescription("MaterialIndex", VertexElementSemantic.Normal, VertexElementFormat.Float1)
        |])
        printfn "DEBUG: Vertex layout created"
        
        printfn "DEBUG: Loading shaders..."
        let shaders = 
            try
                ShaderUtils.getSkeletalShaderSet factory
            with ex ->
                printfn $"DEBUG: Shader loading failed: {ex.Message}"
                reraise()
        printfn $"DEBUG: Loaded {shaders.Length} shaders"
        
        printfn "DEBUG: Creating shader set..."
        let shaderSet = ShaderSetDescription([| vertexLayout |], shaders)
        
        printfn "DEBUG: Creating blend state..."
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
        
        printfn "DEBUG: Creating pipeline description..."
        let resourceLayouts = [| resources.MVPLayout; resources.BoneMatricesLayout; resources.MaterialLayout; resources.TextureArrayLayout |]
        printfn $"DEBUG: Using {resourceLayouts.Length} resource layouts"
        
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
            resourceLayouts,
            outputDesc
        )
        
        printfn "DEBUG: Creating graphics pipeline..."
        let pipeline = 
            try
                factory.CreateGraphicsPipeline(pipelineDesc)
            with ex ->
                printfn $"DEBUG: Graphics pipeline creation failed: {ex.Message}"
                reraise()
        
        printfn "=== DEBUG: Pipeline created successfully ==="
        pipeline
    with ex ->
        printfn $"=== DEBUG: Pipeline creation error: {ex.Message} ==="
        reraise()

let createSkeletalPipeline (gd: GraphicsDevice) (resources: SkeletalRenderResources) (outputDesc: OutputDescription) : Pipeline =
    printfn "Creating skeletal pipeline with material arrays..."
    let factory = gd.ResourceFactory
    
    try
        // Updated vertex layout for skinned vertices WITH material index
        printfn "Creating vertex layout with material index..."
        let vertexLayout = VertexLayoutDescription([|
            VertexElementDescription("Position", VertexElementSemantic.Position, VertexElementFormat.Float3)
            VertexElementDescription("Color", VertexElementSemantic.Color, VertexElementFormat.Float4)
            VertexElementDescription("UV", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2)
            VertexElementDescription("Normal", VertexElementSemantic.Normal, VertexElementFormat.Float3)
            VertexElementDescription("Tangent", VertexElementSemantic.Normal, VertexElementFormat.Float3)
            VertexElementDescription("Bitangent", VertexElementSemantic.Normal, VertexElementFormat.Float3)
            VertexElementDescription("BoneIndices", VertexElementSemantic.Normal, VertexElementFormat.Float4)
            VertexElementDescription("BoneWeights", VertexElementSemantic.Normal, VertexElementFormat.Float4)
            VertexElementDescription("MaterialIndex", VertexElementSemantic.Normal, VertexElementFormat.Float1)  // NEW
        |])
        
        // Use your existing GLSL shaders compiled to SPIR-V
        printfn "Loading skeletal shaders..."
        let shaders = ShaderUtils.getSkeletalShaderSet factory
        printfn $"Loaded {shaders.Length} shaders"
        
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
            // UPDATED: Now we have 4 resource layouts instead of 3
            [| resources.MVPLayout; resources.BoneMatricesLayout; resources.MaterialLayout; resources.TextureArrayLayout |],
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

// Calculate world-space bone matrices for rendering (same as before)
let calculateBoneMatrices (skeleton: CharacterSkeleton) (deformations: Map<string, Matrix4x4>) : Matrix4x4 array =
    let boneMatrices = Array.create skeleton.Bones.Length Matrix4x4.Identity
    
    let rec calculateBoneWorldMatrix (boneIndex: int) =
        if boneIndex < 0 || boneIndex >= skeleton.Bones.Length then
            Matrix4x4.Identity
        else
            if boneMatrices.[boneIndex] <> Matrix4x4.Identity then
                boneMatrices.[boneIndex]
            else
                let bone = skeleton.Bones.[boneIndex]
                
                let deformMatrix = 
                    match deformations.TryFind(bone.Name) with
                    | Some matrix -> matrix
                    | None -> Matrix4x4.Identity
                
                let localMatrix = bone.BindPose * deformMatrix
                
                let parentWorldMatrix = 
                    if bone.ParentIndex >= 0 && bone.ParentIndex < skeleton.Bones.Length then
                        calculateBoneWorldMatrix bone.ParentIndex
                    else
                        Matrix4x4.Identity
                
                let worldMatrix = localMatrix * parentWorldMatrix
                boneMatrices.[boneIndex] <- worldMatrix
                worldMatrix
    
    for i in 0 .. skeleton.Bones.Length - 1 do
        calculateBoneWorldMatrix i |> ignore
    
    skeleton.Bones 
    |> Array.mapi (fun i bone ->
        boneMatrices.[i] * bone.InverseBindPose
    )

// Load character model with texture arrays (MAJOR UPDATE)
let loadCharacterModel 
    (factory: ResourceFactory)
    (gd: GraphicsDevice) 
    (ttModelMap: Map<EquipmentSlot, TTModel>)
    (race: XivRace)
    (textureLayout: ResourceLayout)
    : Async<SkinnedCharacterModel * MaterialTextureInfo array> =  // CHANGED: Now returns tuple
    
    async {
        printfn $"Loading character model with {ttModelMap.Count} equipment pieces"
        
        let referenceTTModel = 
            ttModelMap 
            |> Map.toSeq 
            |> Seq.head 
            |> snd
        
        printfn "Building skeleton..."
        let! skeleton = buildSkeletonFromTTModel referenceTTModel race
        printfn $"Skeleton built with {skeleton.Bones.Length} bones"
        
        // Load all materials and extract texture data
        printfn "Loading materials and extracting texture data..."
        let! materialTextureInfos = 
            async {
                // ... existing material loading logic stays the same ...
                let materialPathsWithSource = 
                    ttModelMap
                    |> Map.toArray
                    |> Array.collect (fun (slot, ttModel) ->
                        ttModel.MeshGroups
                        |> Seq.collect (fun meshGroup ->
                            meshGroup.Parts
                            |> Seq.map (fun part -> (meshGroup.Material, ttModel.Source))
                        )
                        |> Seq.toArray
                    )
                    |> Array.distinctBy fst
                
                let materialLoadTasks = 
                    materialPathsWithSource
                    |> Array.mapi (fun index (materialPath, ttModelSource) ->
                        async {
                            try
                                printfn $"Loading material {index}: {materialPath}"
                                
                                let tx = ModTransaction.BeginReadonlyTransaction()
                                let finalPath = Mtrl.GetMtrlPath(ttModelSource, materialPath)
                                let! mtrl = Mtrl.GetXivMtrl(finalPath, true, tx) |> Async.AwaitTask
                                
                                let dye1 = 0
                                let dye2 = 0
                                let colors = ModelTexture.GetCustomColors()
                                
                                // Get raw texture data (don't need PreparedMaterial anymore)
                                let! modelTex = ModelTexture.GetModelMaps(mtrl, true, colors) |> Async.AwaitTask
                                
                                return Some {
                                    MaterialIndex = index
                                    DiffuseData = modelTex.Diffuse
                                    NormalData = modelTex.Normal
                                    SpecularData = modelTex.Specular
                                    EmissiveData = modelTex.Emissive
                                    AlphaData = modelTex.Alpha
                                    RoughnessData = modelTex.Roughness
                                    MetalnessData = modelTex.Metalness
                                    OcclusionData = modelTex.Occlusion
                                    SubsurfaceData = modelTex.Subsurface
                                    Width = modelTex.Width
                                    Height = modelTex.Height
                                }
                            with ex ->
                                printfn $"Failed to load material {materialPath}: {ex.Message}"
                                return None
                        }
                    )
                
                let! results = Async.Parallel materialLoadTasks
                return results |> Array.choose id
            }
        
        printfn $"Loaded {materialTextureInfos.Length} materials with texture data"
        
        // Combine models to skinned mesh
        printfn "Combining models to skinned mesh..."
        let ttModelArray = ttModelMap |> Map.toArray
        let unifiedMesh = debugCombineModelsToSkinnedMesh factory gd ttModelArray skeleton
        
        let characterModel = {
            Skeleton = skeleton
            UnifiedMesh = unifiedMesh
            Materials = [||]  // We don't use individual materials anymore
        }
        
        return (characterModel, materialTextureInfos)  // CHANGED: Return both
    }

let debugUpdateWithCharacterModel (gd: GraphicsDevice) (resources: SkeletalRenderResources) (characterModel: SkinnedCharacterModel) (materialTextureInfos: MaterialTextureInfo array) (outputDesc: OutputDescription) : SkeletalRenderResources =
    printfn "=== DEBUG: Starting updateWithCharacterModel ==="
    
    try
        let factory = gd.ResourceFactory
        
        printfn "DEBUG: About to create texture arrays..."
        printfn $"DEBUG: MaterialTextureInfos count: {materialTextureInfos.Length}"
        
        // Try creating just one texture array first to isolate the issue
        printfn "DEBUG: Creating diffuse texture array..."
        let diffuseArray = 
            try
                createTextureArray factory gd materialTextureInfos (fun m -> m.DiffuseData) PixelFormat.R8_G8_B8_A8_UNorm_SRgb "Diffuse"
            with ex ->
                printfn $"DEBUG: Diffuse texture array creation failed: {ex.Message}"
                reraise()
        
        printfn "DEBUG: Diffuse array created successfully"
        
        // If diffuse works, try the others
        printfn "DEBUG: Creating normal texture array..."
        let normalArray = createTextureArray factory gd materialTextureInfos (fun m -> m.NormalData) PixelFormat.R8_G8_B8_A8_UNorm "Normal"
        
        printfn "DEBUG: Creating remaining texture arrays..."
        let specularArray = createTextureArray factory gd materialTextureInfos (fun m -> m.SpecularData) PixelFormat.R8_G8_B8_A8_UNorm_SRgb "Specular"
        let emissiveArray = createTextureArray factory gd materialTextureInfos (fun m -> m.EmissiveData) PixelFormat.R8_G8_B8_A8_UNorm_SRgb "Emissive"
        let alphaArray = createTextureArray factory gd materialTextureInfos (fun m -> m.AlphaData) PixelFormat.R8_G8_B8_A8_UNorm "Alpha"
        let roughnessArray = createTextureArray factory gd materialTextureInfos (fun m -> m.RoughnessData) PixelFormat.R8_G8_B8_A8_UNorm "Roughness"
        let metalnessArray = createTextureArray factory gd materialTextureInfos (fun m -> m.MetalnessData) PixelFormat.R8_G8_B8_A8_UNorm "Metalness"
        let occlusionArray = createTextureArray factory gd materialTextureInfos (fun m -> m.OcclusionData) PixelFormat.R8_G8_B8_A8_UNorm "Occlusion"
        let subsurfaceArray = createTextureArray factory gd materialTextureInfos (fun m -> m.SubsurfaceData) PixelFormat.R8_G8_B8_A8_UNorm "Subsurface"
        
        printfn "DEBUG: All texture arrays created successfully"
        
        // Create material data and update buffer
        printfn "DEBUG: Creating material data buffer..."
        let materialDataArray = createMaterialDataBuffer factory gd materialTextureInfos
        
        printfn "DEBUG: Updating material buffer..."
        gd.UpdateBuffer(resources.MaterialBuffer, 0u, materialDataArray)
        printfn "DEBUG: Material buffer updated successfully"
        
        // Create new texture array resource set
        printfn "DEBUG: Creating sampler and resource set..."
        let sampler = factory.CreateSampler(SamplerDescription.Linear)
        
        printfn "DEBUG: Creating texture array resource set..."
        let newTextureArraySet = 
            try
                factory.CreateResourceSet(ResourceSetDescription(
                    resources.TextureArrayLayout,
                    diffuseArray :> BindableResource,
                    normalArray :> BindableResource,
                    specularArray :> BindableResource,
                    emissiveArray :> BindableResource,
                    alphaArray :> BindableResource,
                    roughnessArray :> BindableResource,
                    metalnessArray :> BindableResource,
                    occlusionArray :> BindableResource,
                    subsurfaceArray :> BindableResource,
                    sampler :> BindableResource
                ))
            with ex ->
                printfn $"DEBUG: Resource set creation failed: {ex.Message}"
                reraise()
        
        printfn "DEBUG: Resource set created successfully"
        
        // Create skeletal pipeline
        printfn "DEBUG: Creating skeletal pipeline..."
        let skeletalPipeline = 
            try
                debugCreateSkeletalPipeline gd resources outputDesc
            with ex ->
                printfn $"DEBUG: Pipeline creation failed: {ex.Message}"
                reraise()
        
        printfn "DEBUG: Pipeline created successfully"
        
        let result = 
            { resources with 
                CharacterModel = Some characterModel
                SkeletalPipeline = Some skeletalPipeline
                DiffuseTextureArray = diffuseArray
                NormalTextureArray = normalArray
                SpecularTextureArray = specularArray
                EmissiveTextureArray = emissiveArray
                AlphaTextureArray = alphaArray
                RoughnessTextureArray = roughnessArray
                MetalnessTextureArray = metalnessArray
                OcclusionTextureArray = occlusionArray
                SubsurfaceTextureArray = subsurfaceArray
                TextureArraySet = newTextureArraySet
            }
        
        printfn "=== DEBUG: updateWithCharacterModel completed successfully ==="
        result
    with ex ->
        printfn $"=== DEBUG: ERROR in updateWithCharacterModel: {ex.Message} ==="
        printfn $"=== DEBUG: Stack trace: {ex.StackTrace} ==="
        reraise()

// Update the rendering resources with character model and texture arrays
let updateWithCharacterModel (gd: GraphicsDevice) (resources: SkeletalRenderResources) (characterModel: SkinnedCharacterModel) (materialTextureInfos: MaterialTextureInfo array) (outputDesc: OutputDescription) : SkeletalRenderResources =
    printfn "Starting updateWithCharacterModel with texture arrays..."
    
    try
        let factory = gd.ResourceFactory
        
        // Create texture arrays (same as before)
        printfn "Creating texture arrays..."
        let diffuseArray = createTextureArray factory gd materialTextureInfos (fun m -> m.DiffuseData) PixelFormat.R8_G8_B8_A8_UNorm_SRgb "Diffuse"
        let normalArray = createTextureArray factory gd materialTextureInfos (fun m -> m.NormalData) PixelFormat.R8_G8_B8_A8_UNorm "Normal"
        let specularArray = createTextureArray factory gd materialTextureInfos (fun m -> m.SpecularData) PixelFormat.R8_G8_B8_A8_UNorm_SRgb "Specular"
        let emissiveArray = createTextureArray factory gd materialTextureInfos (fun m -> m.EmissiveData) PixelFormat.R8_G8_B8_A8_UNorm_SRgb "Emissive"
        let alphaArray = createTextureArray factory gd materialTextureInfos (fun m -> m.AlphaData) PixelFormat.R8_G8_B8_A8_UNorm "Alpha"
        let roughnessArray = createTextureArray factory gd materialTextureInfos (fun m -> m.RoughnessData) PixelFormat.R8_G8_B8_A8_UNorm "Roughness"
        let metalnessArray = createTextureArray factory gd materialTextureInfos (fun m -> m.MetalnessData) PixelFormat.R8_G8_B8_A8_UNorm "Metalness"
        let occlusionArray = createTextureArray factory gd materialTextureInfos (fun m -> m.OcclusionData) PixelFormat.R8_G8_B8_A8_UNorm "Occlusion"
        let subsurfaceArray = createTextureArray factory gd materialTextureInfos (fun m -> m.SubsurfaceData) PixelFormat.R8_G8_B8_A8_UNorm "Subsurface"
        
        // Create material data and update buffer - FIXED
        printfn "Creating and updating material data buffer..."
        let materialDataArray = createMaterialDataBuffer factory gd materialTextureInfos
        gd.UpdateBuffer(resources.MaterialBuffer, 0u, materialDataArray)  // Now updates with the array, not a buffer
        
        // Create new texture array resource set
        let sampler = factory.CreateSampler(SamplerDescription.Linear)
        let newTextureArraySet = factory.CreateResourceSet(ResourceSetDescription(
            resources.TextureArrayLayout,
            diffuseArray :> BindableResource,
            normalArray :> BindableResource,
            specularArray :> BindableResource,
            emissiveArray :> BindableResource,
            alphaArray :> BindableResource,
            roughnessArray :> BindableResource,
            metalnessArray :> BindableResource,
            occlusionArray :> BindableResource,
            subsurfaceArray :> BindableResource,
            sampler :> BindableResource
        ))
        
        // Create skeletal pipeline
        printfn "Creating skeletal pipeline..."
        let skeletalPipeline = createSkeletalPipeline gd resources outputDesc
        
        let result = 
            { resources with 
                CharacterModel = Some characterModel
                SkeletalPipeline = Some skeletalPipeline
                DiffuseTextureArray = diffuseArray
                NormalTextureArray = normalArray
                SpecularTextureArray = specularArray
                EmissiveTextureArray = emissiveArray
                AlphaTextureArray = alphaArray
                RoughnessTextureArray = roughnessArray
                MetalnessTextureArray = metalnessArray
                OcclusionTextureArray = occlusionArray
                SubsurfaceTextureArray = subsurfaceArray
                TextureArraySet = newTextureArraySet
            }
        
        printfn "updateWithCharacterModel completed successfully"
        result
    with ex ->
        printfn $"ERROR in updateWithCharacterModel: {ex.Message}"
        printfn $"Stack trace: {ex.StackTrace}"
        reraise()

let debugRenderSkeletalCharacter 
    (gd: GraphicsDevice)
    (cmdList: CommandList) 
    (resources: SkeletalRenderResources)
    (camera: CameraController)
    (aspectRatio: float32)
    : unit =
    
    printfn "=== DEBUG: Starting renderSkeletalCharacter ==="
    
    match resources.CharacterModel, resources.SkeletalPipeline with
    | Some characterModel, Some pipeline ->
        printfn "DEBUG: Character model and pipeline available"
        
        // Calculate MVP matrices
        let view = camera.GetViewMatrix()
        let proj = camera.GetProjectionMatrix(aspectRatio)
        let modelMatrix = Matrix4x4.CreateScale(-2.5f, 2.5f, 2.5f)
        let worldViewMatrix = modelMatrix * view
        let worldViewProjectionMatrix = worldViewMatrix * proj
        let transformsData = [| worldViewProjectionMatrix; worldViewMatrix |]
        
        printfn "DEBUG: MVP matrices calculated"
        
        // Update MVP buffer
        try
            gd.UpdateBuffer(resources.MVPBuffer, 0u, transformsData)
            printfn "DEBUG: MVP buffer updated successfully"
        with ex ->
            printfn $"DEBUG: MVP buffer update failed: {ex.Message}"
            reraise()
        
        // Calculate bone matrices
        let boneMatrices = Array.create MaxBones Matrix4x4.Identity
        let paddedBoneMatrices = 
            if boneMatrices.Length < MaxBones then
                Array.append boneMatrices (Array.create (MaxBones - boneMatrices.Length) Matrix4x4.Identity)
            else
                boneMatrices |> Array.take MaxBones
        
        // Update bone matrices buffer
        try
            gd.UpdateBuffer(resources.BoneMatricesBuffer, 0u, paddedBoneMatrices)
            printfn "DEBUG: Bone matrices buffer updated successfully"
        with ex ->
            printfn $"DEBUG: Bone matrices buffer update failed: {ex.Message}"
            reraise()
        
        // Check vertex buffer info
        printfn $"DEBUG: Vertex buffer size: {characterModel.UnifiedMesh.VertexBuffer.SizeInBytes}"
        printfn $"DEBUG: Vertex count: {characterModel.UnifiedMesh.Vertices.Length}"
        printfn $"DEBUG: Expected vertex size: {SkinnedVertex.SizeInBytes}"
        printfn $"DEBUG: Expected total size: {characterModel.UnifiedMesh.Vertices.Length * SkinnedVertex.SizeInBytes}"
        
        // Check index buffer info
        printfn $"DEBUG: Index buffer size: {characterModel.UnifiedMesh.IndexBuffer.SizeInBytes}"
        printfn $"DEBUG: Index count: {characterModel.UnifiedMesh.Indices.Length}"
        printfn $"DEBUG: Expected index size: {characterModel.UnifiedMesh.Indices.Length * sizeof<uint16>}"
        
        // Set pipeline
        try
            cmdList.SetPipeline(pipeline)
            printfn "DEBUG: Pipeline set successfully"
        with ex ->
            printfn $"DEBUG: Pipeline set failed: {ex.Message}"
            reraise()
        
        // Set resource sets one by one to isolate issues
        try
            cmdList.SetGraphicsResourceSet(0u, resources.MVPSet)
            printfn "DEBUG: MVP resource set (0) bound successfully"
        with ex ->
            printfn $"DEBUG: MVP resource set binding failed: {ex.Message}"
            reraise()
        
        try
            cmdList.SetGraphicsResourceSet(1u, resources.BoneMatricesSet)
            printfn "DEBUG: Bone matrices resource set (1) bound successfully"
        with ex ->
            printfn $"DEBUG: Bone matrices resource set binding failed: {ex.Message}"
            reraise()
        
        try
            cmdList.SetGraphicsResourceSet(2u, resources.MaterialSet)
            printfn "DEBUG: Material resource set (2) bound successfully"
        with ex ->
            printfn $"DEBUG: Material resource set binding failed: {ex.Message}"
            reraise()
        
        try
            cmdList.SetGraphicsResourceSet(3u, resources.TextureArraySet)
            printfn "DEBUG: Texture array resource set (3) bound successfully"
        with ex ->
            printfn $"DEBUG: Texture array resource set binding failed: {ex.Message}"
            reraise()
        
        // Set vertex buffer
        try
            cmdList.SetVertexBuffer(0u, characterModel.UnifiedMesh.VertexBuffer)
            printfn "DEBUG: Vertex buffer set successfully"
        with ex ->
            printfn $"DEBUG: Vertex buffer set failed: {ex.Message}"
            reraise()
        
        // Set index buffer
        try
            cmdList.SetIndexBuffer(characterModel.UnifiedMesh.IndexBuffer, IndexFormat.UInt16)
            printfn "DEBUG: Index buffer set successfully"
        with ex ->
            printfn $"DEBUG: Index buffer set failed: {ex.Message}"
            reraise()
        
        // The moment of truth - draw call
        try
            printfn $"DEBUG: About to draw {characterModel.UnifiedMesh.Indices.Length} indices"
            cmdList.DrawIndexed(uint32 characterModel.UnifiedMesh.Indices.Length, 1u, 0u, 0, 0u)
            printfn "DEBUG: Draw call completed successfully!"
        with ex ->
            printfn $"DEBUG: Draw call failed: {ex.Message}"
            reraise()
        
    | _ -> 
        printfn "DEBUG: No character or pipeline available"

// Render the skeletal character with SINGLE draw call
let renderSkeletalCharacter 
    (gd: GraphicsDevice)
    (cmdList: CommandList) 
    (resources: SkeletalRenderResources)
    (camera: CameraController)
    (aspectRatio: float32)
    : unit =
    
    match resources.CharacterModel, resources.SkeletalPipeline with
    | Some characterModel, Some pipeline ->
        
        // Calculate MVP matrices
        let view = camera.GetViewMatrix()
        let proj = camera.GetProjectionMatrix(aspectRatio)
        let modelMatrix = Matrix4x4.CreateScale(-2.5f, 2.5f, 2.5f)
        let worldViewMatrix = modelMatrix * view
        let worldViewProjectionMatrix = worldViewMatrix * proj
        let transformsData = [| worldViewProjectionMatrix; worldViewMatrix |]
        
        // Update MVP buffer
        gd.UpdateBuffer(resources.MVPBuffer, 0u, transformsData)
        
        // Calculate bone matrices (TEMPORARY: use identity for testing)
        let boneMatrices = Array.create MaxBones Matrix4x4.Identity
        let paddedBoneMatrices = 
            if boneMatrices.Length < MaxBones then
                Array.append boneMatrices (Array.create (MaxBones - boneMatrices.Length) Matrix4x4.Identity)
            else
                boneMatrices |> Array.take MaxBones
        
        // Update bone matrices buffer
        gd.UpdateBuffer(resources.BoneMatricesBuffer, 0u, paddedBoneMatrices)
        
        // Set pipeline and ALL resource sets
        cmdList.SetPipeline(pipeline)
        cmdList.SetGraphicsResourceSet(0u, resources.MVPSet)           // set = 0: TransformsBuffer
        cmdList.SetGraphicsResourceSet(1u, resources.BoneMatricesSet)  // set = 1: BoneMatrices
        cmdList.SetGraphicsResourceSet(2u, resources.MaterialSet)      // set = 2: MaterialBuffer
        cmdList.SetGraphicsResourceSet(3u, resources.TextureArraySet)  // set = 3: Texture Arrays
        
        // Set vertex and index buffers
        cmdList.SetVertexBuffer(0u, characterModel.UnifiedMesh.VertexBuffer)
        cmdList.SetIndexBuffer(characterModel.UnifiedMesh.IndexBuffer, IndexFormat.UInt16)
        
        // SINGLE draw call for entire character!
        cmdList.DrawIndexed(uint32 characterModel.UnifiedMesh.Indices.Length, 1u, 0u, 0, 0u)
        
        printfn $"Rendered entire character in single draw call: {characterModel.UnifiedMesh.Indices.Length} indices"
        
    | _ -> ()

// Dispose resources (updated for texture arrays)
let disposeSkeletalResources (resources: SkeletalRenderResources) : unit =
    resources.BoneMatricesBuffer.Dispose()
    resources.BoneMatricesLayout.Dispose()
    resources.BoneMatricesSet.Dispose()
    resources.MVPBuffer.Dispose()
    resources.MVPLayout.Dispose()
    resources.MVPSet.Dispose()
    
    // Dispose material resources
    resources.MaterialBuffer.Dispose()
    resources.MaterialLayout.Dispose()
    resources.MaterialSet.Dispose()
    
    // Dispose texture arrays
    resources.DiffuseTextureArray.Dispose()
    resources.NormalTextureArray.Dispose()
    resources.SpecularTextureArray.Dispose()
    resources.EmissiveTextureArray.Dispose()
    resources.AlphaTextureArray.Dispose()
    resources.RoughnessTextureArray.Dispose()
    resources.MetalnessTextureArray.Dispose()
    resources.OcclusionTextureArray.Dispose()
    resources.SubsurfaceTextureArray.Dispose()
    resources.TextureArrayLayout.Dispose()
    resources.TextureArraySet.Dispose()
    
    match resources.SkeletalPipeline with
    | Some pipeline -> pipeline.Dispose()
    | None -> ()
    
    match resources.CharacterModel with
    | Some model ->
        model.UnifiedMesh.VertexBuffer.Dispose()
        model.UnifiedMesh.IndexBuffer.Dispose()
    | None -> ()