module ModelLoader

open System.IO
open System.Numerics
open System.Runtime.InteropServices
open Veldrid
open xivModdingFramework.Models.FileTypes
open xivModdingFramework.Models.DataContainers
open xivModdingFramework.Mods
open xivModdingFramework.Textures.Enums
open MaterialLoader

[<Struct; StructLayout(LayoutKind.Sequential, Pack = 16)>]
type VertexPositionColorUv =
    val Position: Vector3
    val Color: Vector4
    val UV: Vector2
    val Normal: Vector3
    new(pos, col, uv, nor) = { Position = pos; Color = col; UV = uv; Normal = nor }

type LoadedModel =
    {
        Vertices: VertexPositionColorUv[]
        Indices: uint16[]
        TextureSet: ResourceSet option
        TextureLayout: ResourceLayout option
        RawModel: XivMdl
    }

let loadGameModel (gd: GraphicsDevice) (factory: ResourceFactory) (mdlPath: string) : Async<LoadedModel> =
    async{
        use tx = ModTransaction.BeginReadonlyTransaction()

        let! mdlStream = tx.GetFileStream(mdlPath, false, false) |> Async.AwaitTask
        use reader = new BinaryReader(mdlStream.BaseStream)
        let mdlBytes = reader.ReadBytes(int mdlStream.BaseStream.Length)

        let xivMdl = Mdl.GetXivMdl(mdlBytes, mdlPath)
        let mesh = xivMdl.LoDList[0].MeshDataList[0]

        let positions = mesh.VertexData.Positions |> Seq.toArray |> Array.map SharpToNumerics.vec3
        let normals = mesh.VertexData.Normals |> Seq.toArray |> Array.map SharpToNumerics.vec3
        let uvs = mesh.VertexData.TextureCoordinates0 |> Seq.toArray |> Array.map SharpToNumerics.vec2
        let colors = mesh.VertexData.Colors |> Seq.toArray |> Array.map SharpToNumerics.col

        let vertexCount = positions.Length
        let vertices =
            Array.init vertexCount (fun i ->
                let pos = if i < positions.Length then positions[i] else Vector3.Zero
                let nor = if i < normals.Length then normals[i] else Vector3.UnitZ
                let uv = if i < uvs.Length then uvs[i] else Vector2.Zero
                let col = if i < colors.Length then colors[i] else Vector4.One
                VertexPositionColorUv(pos, col, uv, nor)
            )

        let indices =
            mesh.VertexData.Indices
            |> Seq.map uint16
            |> Seq.toArray


        // === Get materials ===
        let! materials = loadAllModelMaterials xivMdl
        let firstTextureOpt =
            materials
            |> List.tryPick (fun m -> m.Textures |> List.tryFind (fun t -> t.Usage = XivTexType.Diffuse || t.Usage = XivTexType.Normal))

        let texSet, texLayout =
            match firstTextureOpt with
            | Some tex ->
                let rgba32Array =
                    Array.init (tex.Data.Length / 4) (fun i ->
                        let idx = i * 4
                        RgbaByte(tex.Data[idx], tex.Data[idx + 1], tex.Data[idx + 2], tex.Data[idx + 3])
                    )
                let texObj = factory.CreateTexture(TextureDescription(
                    uint32 tex.Width, uint32 tex.Height, 1u, 1u, 1u,
                    PixelFormat.R8_G8_B8_A8_UNorm,
                    TextureUsage.Sampled,
                    TextureType.Texture2D
                ))

                let texView = factory.CreateTextureView(texObj)
                let sampler = factory.CreateSampler(SamplerDescription.Linear)

                gd.UpdateTexture(texObj, rgba32Array, 0u, 0u, 0u, uint32 tex.Width, uint32 tex.Height, 1u, 0u, 0u)

                let layout = factory.CreateResourceLayout(ResourceLayoutDescription(
                
                    ResourceLayoutElementDescription("tex_Diffuse", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
                    ResourceLayoutElementDescription("sampler_Diffuse", ResourceKind.Sampler, ShaderStages.Fragment)
                ))

                let set = factory.CreateResourceSet(ResourceSetDescription(layout, texView, sampler))
                Some set, Some layout
            | None -> None, None




        return {
            Vertices = vertices
            Indices = indices
            TextureSet = texSet
            TextureLayout = texLayout
            RawModel = xivMdl
        }
    }