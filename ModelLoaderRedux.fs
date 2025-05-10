module ModelLoaderRedux

open System.IO
open System.Numerics
open System.Runtime.InteropServices
open Veldrid
open xivModdingFramework.Models.FileTypes
open xivModdingFramework.Models.DataContainers
open xivModdingFramework.Mods
open xivModdingFramework.Textures.Enums
open MaterialLoader
open MaterialHelper
open ShaderBuilder
open Shared

let loadGameModel (gd: GraphicsDevice) (factory: ResourceFactory) (mdlPath: string) : Async<LoadedModel> =
    async{
        use tx = ModTransaction.BeginReadonlyTransaction()

        let! mdlStream = tx.GetFileStream(mdlPath, false, false) |> Async.AwaitTask
        use reader = new BinaryReader(mdlStream.BaseStream)
        let mdlBytes = reader.ReadBytes(int mdlStream.BaseStream.Length)

        let xivMdl = Mdl.GetXivMdl(mdlBytes, mdlPath)
        printfn $"LoDList Length: {xivMdl.LoDList.Count}; MeshDataList: {xivMdl.LoDList[0].MeshDataList.Count}"
        let mesh = xivMdl.LoDList[0].MeshDataList[0]

        let positions = mesh.VertexData.Positions |> Seq.toArray |> Array.map SharpToNumerics.vec3
        let normals = mesh.VertexData.Normals |> Seq.toArray |> Array.map SharpToNumerics.vec3
        let uvs = mesh.VertexData.TextureCoordinates0 |> Seq.toArray |> Array.map SharpToNumerics.vec2
        let colors = mesh.VertexData.Colors |> Seq.toArray |> Array.map SharpToNumerics.col
        let colors2 = mesh.VertexData.Colors2 |> Seq.toArray |> Array.map SharpToNumerics.col
        let tangent = mesh.VertexData.BiNormals |> Seq.toArray |> Array.map SharpToNumerics.vec3
        let stream1 = mesh.VertexData.FlowDirections |> Seq.toArray |> Array.map SharpToNumerics.vec3

        printfn $"{mesh.VertexData.BoneWeights}"

        let vertexCount = positions.Length
        let vertices =
            Array.init vertexCount (fun i ->
                let pos = if i < positions.Length then positions[i] else Vector3.Zero
                let nor = if i < normals.Length then normals[i] else Vector3.UnitZ
                let uv = if i < uvs.Length then uvs[i] else Vector2.Zero
                let col = if i < colors.Length then colors[i] else Vector4.One
                let col2 = if i < colors2.Length then colors2[i] else Vector4.One
                let bitan = if i < tangent.Length then tangent[i] else Vector3.Zero
                let un1 = if i < stream1.Length then stream1[i] else Vector3.Zero
               

                VertexPositionColorUv(pos, col, col2, uv, nor, bitan, un1)
            )

        let indices =
            mesh.VertexData.Indices
            |> Seq.map uint16
            |> Seq.toArray


        // === Load and interpret materials ===
        let! rawMaterials = loadAllRawMaterials xivMdl
        let interpreted =
            rawMaterials
            |> List.map (fun rm -> MaterialInterpreter.Interpreter.fromXivMtrl rm.Material rm.Textures)

        return {
            Vertices = vertices
            Indices = indices
            Materials = interpreted
            RawModel = xivMdl
        }
    }

let createRenderModel (gd: GraphicsDevice) (factory: ResourceFactory) (model: LoadedModel) (slot: EquipmentSlot): RenderModel =
    printfn "Creating buffers..."
    let vBuff = factory.CreateBuffer(BufferDescription(uint32 (model.Vertices.Length * Marshal.SizeOf<VertexPositionColorUv>()), BufferUsage.VertexBuffer))
    let iBuff = factory.CreateBuffer(BufferDescription(uint32 (model.Indices.Length * Marshal.SizeOf<uint32>()), BufferUsage.IndexBuffer))
    let mBuff = factory.CreateBuffer(BufferDescription(uint32 (Marshal.SizeOf<Matrix4x4>()), BufferUsage.UniformBuffer ||| BufferUsage.Dynamic))
    printfn "Buffers created!"

    printfn "Updating buffers..."
    gd.UpdateBuffer(vBuff, 0u, model.Vertices)
    gd.UpdateBuffer(iBuff, 0u, model.Indices)
    printfn "Buffers updated!"

    printfn "Preparing materials..."
    let preparedMaterials = model.Materials |> List.map (prepareMaterial gd factory)
    let matLayout, matSet =
        match preparedMaterials with
        | pm :: _ -> pm.ResourceLayout, pm.ResourceSet
        | []      -> failwith "No materials found for model."
    printfn "Materials prepared!"

    printfn "Creating MVP layout and set..."
    let mvpLayout = factory.CreateResourceLayout(ResourceLayoutDescription(ResourceLayoutElementDescription("MVPBuffer", ResourceKind.UniformBuffer, ShaderStages.Vertex)))
    let mvpSet = factory.CreateResourceSet(ResourceSetDescription(mvpLayout, mBuff))
    printfn "MVP Layout and Set created!"

    printfn "Creating Vertex Layout..."
    let vertexLayout = VertexLayoutDescription(
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
    printfn "Vertex Layout created!"

    //printfn "Creating pipeline..."
    //let pipeline = createDefaultPipeline gd factory vertexLayout gd.MainSwapchain.Framebuffer.OutputDescription mvpLayout matLayout
    //printfn "Pipeline created!"

    {
        Vertices = vBuff
        Indices = iBuff
        IndexCount = uint32 model.Indices.Length
        MVPBuffer = mBuff
        MVPSet = mvpSet
        MaterialSet = matSet
        MaterialLayout = matLayout
        Pipeline = None
        RawModel = model
        Slot = slot
    }