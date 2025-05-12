module ModelLoader

open System
open System.IO
open System.Threading.Tasks
open Veldrid
open xivModdingFramework.Cache
open xivModdingFramework.General.Enums
open xivModdingFramework.Models.FileTypes
open xivModdingFramework.Models.DataContainers
open xivModdingFramework.Models.Helpers
open xivModdingFramework.Models.ModelTextures
open xivModdingFramework.Items.DataContainers
open xivModdingFramework.Items.Interfaces
open xivModdingFramework.Mods
open xivModdingFramework.Materials.DataContainers
open xivModdingFramework.Materials.FileTypes

open Shared

let loadRenderModelFromItem
    (factory    : ResourceFactory)
    (gd         : GraphicsDevice)
    (tx         : ModTransaction)
    (item       : IItemModel)
    (race       : XivRace)
    (mtlBuilder : XivMtrl -> Task<PreparedMaterial>)
    : Task<RenderModel> =
    task {
        printfn "Loading raw model..."
        let! ttModel = Mdl.GetTTModel(item, race)
        

        for mat in ttModel.Materials do
            printfn $"Material path: {mat}"

        printfn "Generating material map..."
        let materialMap =
            ttModel.Materials
            |> Seq.distinct
            |> Seq.map (fun path -> task {
                try
                    printfn $"Getting mtrl from path {path}..."
                    printfn $"Finding model path for skin edge case... {ttModel.Source}"
            
                    let finalPath =
                        try
                            let material = Mtrl.GetXivMtrl(path, item, true, tx)
                            material.Result.MTRLPath
                        with
                        | _ ->
                            Mtrl.GetMtrlPath(ttModel.Source, path)
                    printfn $"Full internal path: {finalPath}"
                    let! mtrl = Mtrl.GetXivMtrl(finalPath, true, tx)
                    printfn "Material received!"
                    printfn $"Material properties: {mtrl.MTRLPath}; {mtrl.Textures.Count}"
                    printfn "Preparing material..."
                    let! prepared = mtlBuilder mtrl
                    printfn "Material prepared!"
                    return path, prepared
                with ex ->
                    let! finalPath = Mtrl.GetXivMtrl(path, item, true, tx)
                    printfn $"Failed to load material for {finalPath.MTRLPath}: {ex.Message}"
                    return raise ex
            })
            |> Task.WhenAll
        printfn "Material map generated!"
        printfn $"MaterialMap: {materialMap.Result}"

        printfn "Define materailAssoc..."
        let! materialAssoc =
            try
                materialMap
            with ex ->
                printfn $"Task.WhenAll failed: {ex.Message}"
                raise ex
        printfn "materialAssoc defined!"
        printfn "Create materialDict..."
        let materialDict = materialAssoc |> dict
        printfn "materialDict created!"

        printfn "Making render meshes..."
        let renderMeshes =
            ttModel.MeshGroups
            |> List.ofSeq
            |> List.collect ( fun group ->
                group.Parts
                |> List.ofSeq
                |> List.map (fun part ->
                    let verts = part.Vertices |> Array.ofSeq
                    let convertedVerts =
                        part.Vertices
                        |> Seq.map (fun vtx ->
                            VertexPositionColorUv(
                                SharpToNumerics.vec3 vtx.Position,
                                SharpToNumerics.convertColor vtx.VertexColor,
                                SharpToNumerics.vec2 vtx.UV1,
                                SharpToNumerics.vec3 vtx.Normal
                            )
                        )
                        |> Seq.toArray
                    let indices = part.TriangleIndices |> Seq.map uint16 |> Array.ofSeq

                    printfn "Creating buffers"
                    let vertexBuffer = factory.CreateBuffer(BufferDescription(uint32 (verts.Length * sizeof<float32> * 12), BufferUsage.VertexBuffer))
                    let indexBuffer = factory.CreateBuffer(BufferDescription(uint32 (indices.Length * sizeof<uint16>), BufferUsage.IndexBuffer))
                    printfn "Buffers created, updating buffers..."
                    gd.UpdateBuffer(vertexBuffer, 0u, convertedVerts)
                    printfn "vertex buffer updated, updating index buffer..."
                    gd.UpdateBuffer(indexBuffer, 0u, indices)
                    printfn "Buffers updated!"

                    printfn $"[ModelLoader] Mesh vertices: {verts.Length}; Expected strid: {12 * sizeof<float32>} bytes"
                    printfn "Attaching material..."
                    let material =
                        printfn $"Trying to get material for: {group.Material}..."
                        let keyList = materialDict.Keys |> Seq.map (fun k -> $"'{k}'") |> String.concat ", "
                        printfn $"Available material keys: {keyList}"
                        match materialDict.TryGetValue(group.Material) with
                        | true, mat -> mat
                        | _ -> failwith $"Material {group.Material} not found."
                    printfn "Material attached!"

                    {
                        VertexBuffer = vertexBuffer
                        IndexBuffer = indexBuffer
                        IndexCount = indices.Length
                        Material = material
                    }
                )
            )
        printfn "Render mesh created!"
        printfn $"Total mesh parts: {ttModel.MeshGroups.Count}"
        
        return {
            Meshes = renderMeshes
            Original = ttModel
        }

    }