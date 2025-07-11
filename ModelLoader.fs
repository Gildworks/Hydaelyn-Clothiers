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
    (factory           : ResourceFactory)
    (gd                : GraphicsDevice)
    (tx                : ModTransaction)
    (ttModel           : TTModel)
    (item              : IItemModel)
    (race              : XivRace)
    (mtlBuilder : XivMtrl -> string -> Task<PreparedMaterial>)
    : Task<RenderModel> =
    task {
        let materialMap =
            ttModel.Materials
            |> Seq.distinct
            |> Seq.map (fun path -> task {
                try
                    let finalPath =
                        try
                            printfn $"Passed path: {path}"
                            let material = Mtrl.GetXivMtrl(path, false)
                            material.Result.MTRLPath
                        with
                        | _ ->
                            printfn "Using fallback logic for material."
                            Mtrl.GetMtrlPath(ttModel.Source, path)
                    printfn $"Final path: {finalPath}"
                    let! mtrl = Mtrl.GetXivMtrl(finalPath, true, tx)
                    
                    let! prepared = mtlBuilder mtrl item.Name
                    return path, prepared
                with ex ->
                    let finalPath = Mtrl.GetMtrlPath(ttModel.Source, path)
                    return raise ex
            })
            |> Task.WhenAll
        let! materialAssoc =
            try
                materialMap
            with ex ->
                raise ex
        let materialDict = materialAssoc |> dict

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
                                SharpToNumerics.vec3 vtx.Normal,
                                SharpToNumerics.vec3 vtx.Tangent,
                                SharpToNumerics.vec3 vtx.Binormal
                            )
                        )
                        |> Seq.toArray
                    let indices = part.TriangleIndices |> Seq.map uint16 |> Array.ofSeq

                    let vertexBuffer = factory.CreateBuffer(BufferDescription(uint32 (verts.Length * sizeof<float32> * sizeof<VertexPositionColorUv>), BufferUsage.VertexBuffer))
                    let indexBuffer = factory.CreateBuffer(BufferDescription(uint32 (indices.Length * sizeof<uint16>), BufferUsage.IndexBuffer))
                    gd.UpdateBuffer(vertexBuffer, 0u, convertedVerts)
                    gd.UpdateBuffer(indexBuffer, 0u, indices)

                    let material =
                        let keyList = materialDict.Keys |> Seq.map (fun k -> $"'{k}'") |> String.concat ", "
                        match materialDict.TryGetValue(group.Material) with
                        | true, mat -> mat
                        | _ -> failwith $"Material {group.Material} not found."

                    {
                        VertexBuffer = vertexBuffer
                        IndexBuffer = indexBuffer
                        IndexCount = indices.Length
                        Material = material
                        RawModel = ttModel
                    }
                )
            )
        
        return {
            Meshes = renderMeshes
            Original = ttModel
        }

    }

let loadRenderModelFromPart
    (factory    : ResourceFactory)
    (gd         : GraphicsDevice)
    (tx         : ModTransaction)
    (path       : string)
    (race       : XivRace)
    (mtlBuilder : XivMtrl -> Task<PreparedMaterial>)
    : Task<RenderModel> =
    task {
        let! ttModel = Mdl.GetTTModel(path)

        let materialMap =
            ttModel.Materials
            |> Seq.distinct
            |> Seq.map (fun path -> task {
                try            
                    let finalPath =
                        try
                            let material = Mtrl.GetXivMtrl(path, true, tx)
                            material.Result.MTRLPath
                        with
                        | _ ->
                            Mtrl.GetMtrlPath(ttModel.Source, path)
                    let! mtrl = Mtrl.GetXivMtrl(finalPath, true, tx)
                    let! prepared = mtlBuilder mtrl
                    return path, prepared
                with ex ->
                    let! finalPath = Mtrl.GetXivMtrl(path, true, tx)
                    return raise ex
            })
            |> Task.WhenAll
        let! materialAssoc =
            try
                materialMap
            with ex ->
                raise ex
        let materialDict = materialAssoc |> dict

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
                                SharpToNumerics.vec3 vtx.Normal,
                                SharpToNumerics.vec3 vtx.Tangent,
                                SharpToNumerics.vec3 vtx.Binormal
                            )
                        )
                        |> Seq.toArray
                    let indices = part.TriangleIndices |> Seq.map uint16 |> Array.ofSeq

                    let vertexBuffer = factory.CreateBuffer(BufferDescription(uint32 (verts.Length * sizeof<float32> * 12), BufferUsage.VertexBuffer))
                    let indexBuffer = factory.CreateBuffer(BufferDescription(uint32 (indices.Length * sizeof<uint16>), BufferUsage.IndexBuffer))
                    gd.UpdateBuffer(vertexBuffer, 0u, convertedVerts)
                    gd.UpdateBuffer(indexBuffer, 0u, indices)
                    let material =
                        let keyList = materialDict.Keys |> Seq.map (fun k -> $"'{k}'") |> String.concat ", "
                        match materialDict.TryGetValue(group.Material) with
                        | true, mat -> mat
                        | _ -> failwith $"Material {group.Material} not found."
                    {
                        VertexBuffer = vertexBuffer
                        IndexBuffer = indexBuffer
                        IndexCount = indices.Length
                        Material = material
                        RawModel = ttModel
                    }
                )
            )
        return {
            Meshes = renderMeshes
            Original = ttModel
        }

    }