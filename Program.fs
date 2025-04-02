namespace fs_mdl_viewer

open System
open Avalonia
open Avalonia.Controls.ApplicationLifetimes
open Avalonia.Reactive
open Lumina
open MdlParser

module Program =

    [<EntryPoint>]
    let main argv =
        let gameDataPath = @"F:\Games\SquareEnix\FINAL FANTASY XIV - A Realm Reborn\game\sqpack"
        let modelPath = "chara/human/c0101/obj/body/b0001/model/c0101b0001_top.mdl"

        let lumina = new Lumina.GameData(gameDataPath)
        let model = lumina.GetFile(modelPath)

        if isNull model then
            failwithf "Failed to load model at path: %s" modelPath

        model.LoadFile()

        let header = parseHeader model.Data
        let declarations, _ = parseVertexDeclarations model.Data header
        let rawBuffers = extractRawBuffers model.Data header

        printfn "Model Loaded: %s" modelPath
        printfn "Vertex decls: %d" declarations.Length
        printfn "Vertex 0 has %d elements" (List.length declarations[0])
        printfn "LOD count: %d" header.LodCount
        printfn "Vertex buffer 0 size: %d bytes" rawBuffers.VertexBuffers[0].Length
        printfn "Index buffer 0 size: %d bytes" rawBuffers.IndexBuffers[0].Length

        let vertices = decodeVertices declarations[0] rawBuffers.VertexBuffers[0]
        let indices = decodeIndices rawBuffers.IndexBuffers[0]

        printfn "Parsed %d vertices and %d indices" vertices.Length indices.Length
        printfn "First vertex: Position=(%f %f %f), UV=(%f %f)"
            vertices[0].Position.X vertices[0].Position.Y vertices[0].Position.Z
            vertices[0].UV.X vertices[0].UV.Y

        AppBuilder
            .Configure<App>()
            .UsePlatformDetect()
            .LogToTrace()
            .StartWithClassicDesktopLifetime(argv)