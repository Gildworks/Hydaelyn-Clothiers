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
    val Position        : Vector3
    val Color           : Vector4
    val Color2          : Vector4
    val UV              : Vector2
    val Normal          : Vector3
    new(pos, col, col2, uv, nor) = { Position = pos; Color = col; Color2 = col2; UV = uv; Normal = nor }

type LoadedTextures =
    {
        Diffuse         : TextureView
        Normal          : TextureView
        Mask            : TextureView
        Index           : TextureView
        Sampler         : Sampler
    }

type LoadedModel =
    {
        Vertices        : VertexPositionColorUv[]
        Indices         : uint16[]
        TextureSet      : ResourceSet       option
        TextureLayout   : ResourceLayout    option
        Textures        : LoadedTextures    option
        ColorSetBuffer  : DeviceBuffer      option
        RawModel        : XivMdl
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
        let colors2 = mesh.VertexData.Colors2 |> Seq.toArray |> Array.map SharpToNumerics.col

        printfn $"{mesh.VertexData.BoneWeights}"

        let vertexCount = positions.Length
        let vertices =
            Array.init vertexCount (fun i ->
                let pos = if i < positions.Length then positions[i] else Vector3.Zero
                let nor = if i < normals.Length then normals[i] else Vector3.UnitZ
                let uv = if i < uvs.Length then uvs[i] else Vector2.Zero
                let col = if i < colors.Length then colors[i] else Vector4.One
                let col2 = if i < colors2.Length then colors[i] else Vector4.Zero
          

                //if col.Z < 254.0f then
                //    printfn $"Vertex %d{i}: Color = {col}"
                //else
                //    printfn $"Opaque Alpha. Vertex %d{i}: Color = {col}; Position = {pos}; Normal = {nor}; UV = {uv} \n \n"

                VertexPositionColorUv(pos, col, col2, uv, nor)
            )

        let indices =
            mesh.VertexData.Indices
            |> Seq.map uint16
            |> Seq.toArray


        // === Get materials ===
        let! materials = loadAllModelMaterials xivMdl
        let fallWhite   = TextureUtils.oneByWhite gd
        let fallNorm    = TextureUtils.oneByNormal gd
        let fallBlack   = TextureUtils.oneByBlack gd
        let sampler = factory.CreateSampler(SamplerDescription.Linear)

        let colorSetOpt = materials |> List.tryPick (fun mat -> mat.ColorSet)

        let getTex usage fallback =
            materials
            |> List.tryPick (fun m -> m.Textures |> List.tryFind(fun t -> t.Usage = usage))
            |> Option.map (fun t -> TextureUtils.texViewFromBytes gd t)
            |> Option.defaultValue fallback

        let texViews =
            {
                Diffuse = getTex XivTexType.Diffuse fallWhite
                Normal = getTex XivTexType.Normal fallNorm
                Mask = getTex XivTexType.Mask fallWhite
                Index = getTex XivTexType.Index fallBlack
                Sampler = sampler
            }

        let colorSetBuffer =
            match colorSetOpt with
            | Some cs ->
                let buf = factory.CreateBuffer(BufferDescription(uint32 (cs.Length * 4), BufferUsage.UniformBuffer))
                gd.UpdateBuffer(buf, 0u, cs)
                Some buf
            | None -> None

        let layout = factory.CreateResourceLayout(ResourceLayoutDescription(
            ResourceLayoutElementDescription("tex_Diffuse", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
            ResourceLayoutElementDescription("tex_Normal", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
            ResourceLayoutElementDescription("tex_Mask", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
            ResourceLayoutElementDescription("tex_Index", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
            ResourceLayoutElementDescription("SharedSampler", ResourceKind.Sampler, ShaderStages.Fragment),
            ResourceLayoutElementDescription("ColorSetBuffer", ResourceKind.UniformBuffer, ShaderStages.Fragment)
        ))
        let set = factory.CreateResourceSet(ResourceSetDescription(
            layout,
            texViews.Diffuse,
            texViews.Normal,
            texViews.Mask,
            texViews.Index,
            texViews.Sampler,
            colorSetBuffer |> Option.defaultWith (fun () ->
                let empty = Array.init 4 (fun _ -> 0.0f)
                let buf = factory.CreateBuffer(BufferDescription(4u, BufferUsage.UniformBuffer))
                gd.UpdateBuffer(buf, 0u, empty)
                buf
            )
        ))

        return {
            Vertices = vertices
            Indices = indices
            TextureSet = Some set
            TextureLayout = Some layout
            Textures = Some texViews
            ColorSetBuffer = colorSetBuffer
            RawModel = xivMdl
        }
    }