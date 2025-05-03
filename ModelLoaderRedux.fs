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
open Shared

[<Struct; StructLayout(LayoutKind.Sequential, Pack = 16)>]
type VertexPositionColorUv =
    val Position        : Vector3
    val Color           : Vector4
    val Color2          : Vector4
    val UV              : Vector2
    val Normal          : Vector3
    val BiTangent       : Vector3
    val Unknown1        : Vector3
    new(pos, col, col2, uv, nor, bitan, un1) = { Position = pos; Color = col; Color2 = col2; UV = uv; Normal = nor; BiTangent = bitan; Unknown1 = un1 }

type LoadedModel =
    {
        Vertices        : VertexPositionColorUv[]
        Indices         : uint16[]
        Materials       : InterpretedMaterial list
        RawModel        : XivMdl
    }



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