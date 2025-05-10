module ModelLoader

open System
open System.IO
open System.Threading.Tasks
open Veldrid
open xivModdingFramework.Cache
open xivModdingFramework.Models.FileTypes
open xivModdingFramework.Models.DataContainers
open xivModdingFramework.Models.Helpers
open xivModdingFramework.Models.ModelTextures
open xivModdingFramework.Mods
open xivModdingFramework.Materials.DataContainers
open xivModdingFramework.Materials.FileTypes

open Shared
open MaterialBuilder

let loadRenderModelFromPath
    (factory    : ResourceFactory)
    (gd         : GraphicsDevice)
    (tx         : ModTransaction)
    (mdlPath    : string)
    (mtlBuilder : XivMtrl -> Task<PreparedMaterial>)
    : Task<RenderModel> =
    task {
        let! xivMdl = Mdl.GetXivMdl(mdlPath, true, tx)
        let! ttModel = TTModel.FromRaw(xivMdl)

        let materialMap =
            ttModel.Materials
            |> Seq.distinct
            |> Seq.map (fun path -> task {
                let! mtrl = Mtrl.GetXivMtrl(path, true, tx)
                let! prepared = mtlBuilder mtrl
                return path, prepared
            })
            |> Task.WhenAll

        let! materialAssoc = materialMap
        let materialDict = materialAssoc |> dict

        let renderMeshes =
            ttModel.MeshGroups
            |> List.ofSeq
            |> List.collect ( fun group ->
                group.Parts
                |> List.ofSeq
                |> List.map (fun part ->
                    let verts = part.Vertices |> Array.ofSeq
                    let indices = part.TriangleIndices |> Array.ofSeq

                    let vertexBuffer = factory.CreateBuffer(BufferDescription(uint32 (verts.Length * sizeof<float32> * 8), BufferUsage.VertexBuffer))
                    let indexBuffer = factory.CreateBuffer(BufferDescription(uint32 (indices.Length * sizeof<uint16>), BufferUsage.IndexBuffer))

                    let material =
                        match materialDict.TryGetValue(group.Material) with
                        | true, mat -> mat
                        | _ -> failwith $"Material {group.Material} not found." 

                    {
                        VertexBuffer = vertexBuffer
                        IndexBuffer = indexBuffer
                        IndexCount = indices.Length
                        Material = material
                    }
                )
            )
        return {
            Meshes = renderMeshes
            Original = ttModel
        }

    }