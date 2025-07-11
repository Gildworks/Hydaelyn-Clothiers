module SkeletalData

open System.Numerics

open Veldrid

open xivModdingFramework.Models.DataContainers
open xivModdingFramework.Models.FileTypes

open Shared

let convertToSkinnedVertex (ttVertex: TTVertex) (boneNameMap: Map<string, int>) (meshBones: string list) : SkinnedVertex =
    // Convert bone IDs from mesh-local to global skeleton indices
    let convertBoneIndex (meshLocalIndex: byte) =
        if int meshLocalIndex < meshBones.Length then
            let boneName = meshBones.[int meshLocalIndex]
            match boneNameMap.TryFind(boneName) with
            | Some globalIndex -> float32 globalIndex
            | None -> 0.0f
        else 0.0f
    
    // Convert weights from bytes (0-255) to normalized floats (0-1)
    let convertWeight (weight: byte) = float32 weight / 255.0f
    
    // Normalize weights to ensure they sum to 1.0
    let weights = [| 
        convertWeight ttVertex.Weights.[0]
        convertWeight ttVertex.Weights.[1] 
        convertWeight ttVertex.Weights.[2]
        convertWeight ttVertex.Weights.[3]
    |]
    let totalWeight = weights |> Array.sum
    let normalizedWeights = 
        if totalWeight > 0.0f then 
            weights |> Array.map (fun w -> w / totalWeight)
        else [| 1.0f; 0.0f; 0.0f; 0.0f |]  // Default to first bone if no weights
    
    {
        Position = SharpToNumerics.vec3 ttVertex.Position
        Color = SharpToNumerics.convertColor ttVertex.VertexColor
        UV = SharpToNumerics.vec2 ttVertex.UV1
        Normal = SharpToNumerics.vec3 ttVertex.Normal
        Tangent = SharpToNumerics.vec3 ttVertex.Tangent
        Bitangent = SharpToNumerics.vec3 ttVertex.Binormal
        BoneIndices = Vector4(
            convertBoneIndex ttVertex.BoneIds.[0],
            convertBoneIndex ttVertex.BoneIds.[1],
            convertBoneIndex ttVertex.BoneIds.[2],
            convertBoneIndex ttVertex.BoneIds.[3]
        )
        BoneWeights = Vector4(
            normalizedWeights.[0],
            normalizedWeights.[1], 
            normalizedWeights.[2],
            normalizedWeights.[3]
        )
    }

let buildSkeletonFromTTModel (ttModel: TTModel) (race: xivModdingFramework.General.Enums.XivRace) : Async<CharacterSkeleton> =
    async {
        // Get skeleton data from xivModdingFramework
        let! skeletonData = 
            async {
                try
                    // Try with the same signature as your existing working code
                    let bones = ttModel.ResolveBoneHeirarchy(null, race, ttModel.Bones, null, null)
                    printfn $"Retrieved {bones.Count} bones from ResolveBoneHeirarchy"
                    return bones
                with ex ->
                    printfn $"Error calling ResolveBoneHeirarchy: {ex.Message}"
                    printfn $"TTModel.Bones count: {ttModel.Bones.Count}"
                    printfn $"Race: {race}"
                    return raise ex  // Fixed: return the raised exception
            }
        
        // Convert Dictionary to F# Map and handle potential null values
        let skeletonDataMap =
            if isNull skeletonData then
                printfn "ERROR: skeletonData is null!"
                Map.empty
            else
                skeletonData
                |> Seq.map (fun kvp -> 
                    if isNull kvp.Value then
                        printfn $"WARNING: Skeleton data for bone '{kvp.Key}' is null"
                        kvp.Key, Unchecked.defaultof<_>
                    else
                        kvp.Key, kvp.Value)
                |> Seq.filter (fun (_, skelData) -> not (isNull (box skelData)))
                |> Map.ofSeq
        
        // Check if we have any valid skeleton data - Fixed: use if-then-else
        let bones = 
            if skeletonDataMap.IsEmpty then
                printfn "ERROR: No valid skeleton data found!"
                [||]  // Return empty array instead of early return
            else
                printfn $"Processing {skeletonDataMap.Count} valid bones"
                
                // Convert to our format - FIXED MATRIX CONSTRUCTION with null checks
                skeletonDataMap
                |> Map.toArray
                |> Array.mapi (fun i (boneName, skelData) ->
                    try
                        // Check for null pose matrices
                        if isNull skelData.PoseMatrix || skelData.PoseMatrix.Length < 16 then
                            printfn $"WARNING: Invalid PoseMatrix for bone '{boneName}'"
                            {
                                Name = boneName
                                Index = i
                                ParentIndex = skelData.BoneParent
                                Children = []
                                BindPose = Matrix4x4.Identity
                                InverseBindPose = Matrix4x4.Identity
                            }
                        elif isNull skelData.InversePoseMatrix || skelData.InversePoseMatrix.Length < 16 then
                            printfn $"WARNING: Invalid InversePoseMatrix for bone '{boneName}'"
                            {
                                Name = boneName
                                Index = i
                                ParentIndex = skelData.BoneParent
                                Children = []
                                BindPose = Matrix4x4.Identity
                                InverseBindPose = Matrix4x4.Identity
                            }
                        else
                            // Correctly construct 4x4 matrices from float arrays (16 elements each)
                            let poseMatrix = Matrix4x4(
                                skelData.PoseMatrix.[0], skelData.PoseMatrix.[1], skelData.PoseMatrix.[2], skelData.PoseMatrix.[3],
                                skelData.PoseMatrix.[4], skelData.PoseMatrix.[5], skelData.PoseMatrix.[6], skelData.PoseMatrix.[7],
                                skelData.PoseMatrix.[8], skelData.PoseMatrix.[9], skelData.PoseMatrix.[10], skelData.PoseMatrix.[11],
                                skelData.PoseMatrix.[12], skelData.PoseMatrix.[13], skelData.PoseMatrix.[14], skelData.PoseMatrix.[15]
                            )
                            let inversePoseMatrix = Matrix4x4(
                                skelData.InversePoseMatrix.[0], skelData.InversePoseMatrix.[1], skelData.InversePoseMatrix.[2], skelData.InversePoseMatrix.[3],
                                skelData.InversePoseMatrix.[4], skelData.InversePoseMatrix.[5], skelData.InversePoseMatrix.[6], skelData.InversePoseMatrix.[7],
                                skelData.InversePoseMatrix.[8], skelData.InversePoseMatrix.[9], skelData.InversePoseMatrix.[10], skelData.InversePoseMatrix.[11],
                                skelData.InversePoseMatrix.[12], skelData.InversePoseMatrix.[13], skelData.InversePoseMatrix.[14], skelData.InversePoseMatrix.[15]
                            )
                            
                            {
                                Name = boneName
                                Index = i
                                ParentIndex = skelData.BoneParent
                                Children = []  // Will be filled below
                                BindPose = poseMatrix
                                InverseBindPose = inversePoseMatrix
                            }
                    with ex ->
                        printfn $"ERROR processing bone '{boneName}': {ex.Message}"
                        {
                            Name = boneName
                            Index = i
                            ParentIndex = -1
                            Children = []
                            BindPose = Matrix4x4.Identity
                            InverseBindPose = Matrix4x4.Identity
                        }
                )
        
        // Build bone name lookup
        let boneNameToIndex = 
            bones 
            |> Array.mapi (fun i bone -> bone.Name, i)
            |> Map.ofArray
        
        // Handle empty bones case
        let (updatedBones, rootBones) = 
            if bones.Length = 0 then
                ([||], [])
            else
                // Update parent indices and build children lists
                printfn "Building parent-child relationships..."
                let updatedBones = 
                    bones |> Array.mapi (fun i bone ->
                        try
                            printfn $"Processing bone {i}: '{bone.Name}' (original parent: {bone.ParentIndex})"
                            
                            let parentIndex = 
                                if bone.ParentIndex = -1 then 
                                    printfn $"  Bone '{bone.Name}' has no parent"
                                    -1
                                else
                                    // Find parent by bone number in original skeleton data
                                    printfn $"  Looking for parent with BoneNumber {bone.ParentIndex}"
                                    let parentBoneName = 
                                        skeletonDataMap 
                                        |> Map.tryFindKey (fun _ skelData -> 
                                            if isNull skelData then false
                                            else skelData.BoneNumber = bone.ParentIndex)
                                    
                                    match parentBoneName with
                                    | Some parentName ->
                                        printfn $"  Found parent bone name: '{parentName}'"
                                        match boneNameToIndex.TryFind(parentName) with
                                        | Some parentIdx -> 
                                            printfn $"  Parent index: {parentIdx}"
                                            parentIdx
                                        | None -> 
                                            printfn $"  WARNING: Parent bone '{parentName}' not found in bone name map"
                                            -1
                                    | None ->
                                        printfn $"  WARNING: No parent found with BoneNumber {bone.ParentIndex}"
                                        -1
                            
                            let children = 
                                bones 
                                |> Array.indexed
                                |> Array.choose (fun (idx, childBone) -> 
                                    if childBone.ParentIndex = i then Some idx else None)
                                |> Array.toList
                            let childrenString =
                                children
                                |> List.map string
                                |> String.concat("; ")
                            
                            printfn $"  Children: [{childrenString}]"
                            
                            { bone with ParentIndex = parentIndex; Children = children }
                        with ex ->
                            printfn $"ERROR processing bone relationships for '{bone.Name}': {ex.Message}"
                            { bone with ParentIndex = -1; Children = [] }
                    )
                
                printfn "Finding root bones..."
                let rootBones = 
                    updatedBones 
                    |> Array.indexed
                    |> Array.choose (fun (i, bone) -> 
                        if bone.ParentIndex = -1 then 
                            printfn $"  Root bone found: {i} ('{bone.Name}')"
                            Some i 
                        else None)
                    |> Array.toList
                
                printfn $"Found {rootBones.Length} root bones"
                (updatedBones, rootBones)
        
        printfn $"Skeleton building complete. Final bone count: {updatedBones.Length}, Root bones: {rootBones.Length}"
        
        return {
            Bones = updatedBones
            BoneNameToIndex = boneNameToIndex
            RootBoneIndices = rootBones
        }
    }

let combineModelsToSkinnedMesh
    (factory: ResourceFactory)
    (gd: GraphicsDevice)
    (ttModels: (EquipmentSlot * TTModel) array)
    (skeleton: CharacterSkeleton)
    : SkinnedMesh =

    printfn $"Combining {ttModels.Length} models into skinned mesh"
    printfn $"Skeleton has {skeleton.Bones.Length} bones"

    let allVertices = ResizeArray<SkinnedVertex>()
    let allIndices = ResizeArray<uint16>()
    let materialIndices = ResizeArray<int>()

    let mutable currentVertexOffset = 0us
    let mutable materialIndex = 0

    for (slot, ttModel) in ttModels do
        printfn $"Processing slot {slot} with {ttModel.MeshGroups.Count} mesh groups"
        
        if isNull ttModel.MeshGroups then
            printfn $"ERROR: MeshGroups is null for slot {slot}"
            failwith $"MeshGroups is null for slot {slot}"
        
        for meshGroup in ttModel.MeshGroups do
            printfn $"  Processing mesh group with {meshGroup.Parts.Count} parts and {meshGroup.Bones.Count} bones"
            
            if isNull meshGroup.Parts then
                printfn $"ERROR: Parts is null for mesh group"
                failwith "Parts is null for mesh group"
            
            if isNull meshGroup.Bones then
                printfn $"ERROR: Bones is null for mesh group"
                failwith "Bones is null for mesh group"
            
            for part in meshGroup.Parts do
                printfn $"    Processing part with {part.Vertices.Count} vertices and {part.TriangleIndices.Count} indices"
                
                if isNull part.Vertices then
                    printfn $"ERROR: Vertices is null for part"
                    failwith "Vertices is null for part"
                
                if isNull part.TriangleIndices then
                    printfn $"ERROR: TriangleIndices is null for part"
                    failwith "TriangleIndices is null for part"
                
                // Convert vertices
                let meshBonesList = meshGroup.Bones |> Seq.toList
                printfn $"    Converting vertices with {meshBonesList.Length} mesh bones"
                
                let skinnedVerts =
                    part.Vertices
                    |> Seq.mapi (fun i ttVertex ->
                        try
                            convertToSkinnedVertex ttVertex skeleton.BoneNameToIndex meshBonesList
                        with ex ->
                            printfn $"ERROR converting vertex {i}: {ex.Message}"
                            reraise()
                    )
                    |> Array.ofSeq

                printfn $"    Converted {skinnedVerts.Length} vertices"
                allVertices.AddRange(skinnedVerts)

                // Convert indices with offset
                let offsetIndices =
                    part.TriangleIndices
                    |> Seq.map (fun i -> uint16 i + currentVertexOffset)
                    |> Array.ofSeq

                allIndices.AddRange(offsetIndices)

                // Track material for each triangle
                let triangleCount = offsetIndices.Length / 3
                for _ in 0 .. triangleCount - 1 do
                    materialIndices.Add(materialIndex)

                currentVertexOffset <- currentVertexOffset + uint16 skinnedVerts.Length
            materialIndex <- materialIndex + 1

    // Create GPU buffers
    let vertices = allVertices.ToArray()
    let indices = allIndices.ToArray()
    
    printfn $"Creating GPU buffers for {vertices.Length} vertices and {indices.Length} indices"

    let vertexBuffer = factory.CreateBuffer(
        BufferDescription(uint32 (vertices.Length * sizeof<SkinnedVertex>), BufferUsage.VertexBuffer)
    )
    let indexBuffer = factory.CreateBuffer(
        BufferDescription(uint32 (indices.Length * sizeof<uint16>), BufferUsage.IndexBuffer)
    )

    gd.UpdateBuffer(vertexBuffer, 0u, vertices)
    gd.UpdateBuffer(indexBuffer, 0u, indices)
    
    printfn "GPU buffers created successfully"

    {
        Vertices = vertices
        Indices = indices
        VertexBuffer = vertexBuffer
        IndexBuffer = indexBuffer
        MaterialIndices = materialIndices.ToArray()
    }