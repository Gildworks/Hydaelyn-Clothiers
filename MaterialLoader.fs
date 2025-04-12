module MaterialLoader

open System
open System.IO
open xivModdingFramework.Materials.FileTypes
open xivModdingFramework.Materials.DataContainers
open xivModdingFramework.Models.DataContainers
open xivModdingFramework.Mods
open xivModdingFramework.Textures.Enums
open xivModdingFramework.Textures.FileTypes

type LoadedTexture =
    {
        Usage: XivTexType
        Path: string
        Data: byte[]
        Width: int
        Height: int
    }

type LoadedMaterial =
    {
        MaterialPath: string
        Textures: LoadedTexture list
        ColorSet: byte[] option
        Flags: Map<string, obj>
    }


let loadMaterial (mtrlPath: string) : Async<LoadedMaterial> =
    async {
        use tx = ModTransaction.BeginReadonlyTransaction()

        let! mtrlStream = tx.GetFileStream(mtrlPath, false, false) |> Async.AwaitTask
        use reader = new BinaryReader(mtrlStream.BaseStream)
        let mtrlBytes = reader.ReadBytes(int mtrlStream.BaseStream.Length)
        let mtrl = Mtrl.GetXivMtrl(mtrlBytes, mtrlPath)

        let! textures =
            mtrl.Textures
            |> Seq.map (fun tex ->
                async {
                    try
                        let! xivTex = Tex.GetXivTex(tex, false, tx) |> Async.AwaitTask
                        let! raw = xivTex.GetRawPixels(0) |> Async.AwaitTask
                        return Some {
                            Usage = tex.Usage
                            Path = tex.TexturePath
                            Data = raw
                            Width = xivTex.Width
                            Height = xivTex.Height
                        }
                    with ex ->
                        printfn $"Warning: Could not load texture {tex.TexturePath}: {ex.Message}"
                        return None
                }
            )
            |> Seq.toArray
            |> Async.Parallel

        return {
            MaterialPath = mtrlPath
            Textures = textures |> Array.choose id |> Array.toList
            ColorSet = None
            Flags = Map.empty
        }
    }

let inferMtrlPath (mdlPath: string) (materialEntry: string) =
    let basePath = Path.GetDirectoryName(mdlPath).Replace("model", "material").Replace("\\", "/")
    let versionFolder = "v0001"
    let trimmedEntry = materialEntry.TrimStart('/')

    $"{basePath}/{versionFolder}/{trimmedEntry}"

let loadAllModelMaterials (mdl: XivMdl) : Async<LoadedMaterial list> =
    async {
        let materialPaths =
            mdl.PathData.MaterialList
            |> Seq.map (inferMtrlPath mdl.MdlPath)
            |> Seq.toArray

        let! loadedMaterials =
            materialPaths
            |> Array.map loadMaterial
            |>Async.Parallel

        return loadedMaterials |> Array.toList
    }