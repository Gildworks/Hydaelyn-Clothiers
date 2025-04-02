module MdlParser

open System
open System.IO
open System.Numerics
open System.Buffers.Binary

// Enums based on documentation
type VertexType =
    | Invalid = 0uy
    | Single3 = 2uy
    | Single4 = 3uy
    | UInt = 5uy
    | ByteFloat4 = 8uy
    | Half2 = 13uy
    | Half4 = 14uy

type VertexUsage =
    | Position = 0uy
    | BlendWeights = 1uy
    | BlendIndices = 2uy
    | Normal = 3uy
    | UV = 4uy
    | Tangent = 5uy
    | BiTangent = 6uy
    | Color = 7uy

// Vertex element (size: 8 bytes)
type VertexElement = {
    Stream: byte
    Offset: byte
    VertexType: VertexType
    VertexUsage: VertexUsage
    UsageIndex: byte
    Unknown: byte[] // size 3
}

// Header (fixed size: 56 bytes)
type ModelFileHeader = {
    Version: uint32
    StackSize: uint32
    RuntimeSize: uint32
    VertexDeclarationCount: uint16
    MaterialCount: uint16
    VertexOffsets: uint32[]
    IndexOffsets: uint32[]
    VertexBufferSizes: uint32[]
    IndexBufferSizes: uint32[]
    LodCount: byte
    IndexBufferStreamingEnabled: byte
    HasEdgeGeometry: byte
    Unknown1: byte
}

// Helper to read a struct from a byte span
let readUInt16LE (data: Span<byte>) offset =
    BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset, 2))

let readUInt32LE (data: Span<byte>) offset =
    BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4))

// Parse header from FileData
let parseHeader (data: byte[]) : ModelFileHeader =
    let span = data.AsSpan()
    let vertexOffsets =
        [| 
            readUInt32LE span 16
            readUInt32LE span 20
            readUInt32LE span 24
        |]

    let indexOffsets =
        [|
            readUInt32LE span 28
            readUInt32LE span 32
            readUInt32LE span 36
        |]

    let vertexBufferSizes =
        [|
            readUInt32LE span 40
            readUInt32LE span 44
            readUInt32LE span 48
        |]

    let indexBufferSizes =
        [|
            readUInt32LE span 52
            readUInt32LE span 56
            readUInt32LE span 60
        |]

    {
        Version = readUInt32LE span 0
        StackSize = readUInt32LE span 4
        RuntimeSize = readUInt32LE span 8
        VertexDeclarationCount = readUInt16LE span 12
        MaterialCount = readUInt16LE span 14
        VertexOffsets = vertexOffsets
        IndexOffsets = indexOffsets
        VertexBufferSizes = vertexBufferSizes
        IndexBufferSizes = indexBufferSizes
        LodCount = data[64]
        IndexBufferStreamingEnabled = data[65]
        HasEdgeGeometry = data[66]
        Unknown1 = data[67]
    }

// Parse vertex declaration stream
let parseVertexDeclarations (data: byte[]) (header: ModelFileHeader) : VertexElement list list * int =
    let mutable offset = 68 // right after the header
    let declarations = ResizeArray<VertexElement list>()

    for _ in 0 .. (int header.VertexDeclarationCount - 1) do
        let elements = ResizeArray<VertexElement>()

        let rec readElements () =
            let stream = data[offset]
            if stream = 0xFFuy then
                offset <- offset + 8 // consume end marker
            else
                let element = {
                    Stream = stream
                    Offset = data[offset + 1]
                    VertexType = LanguagePrimitives.EnumOfValue<byte, VertexType>(data[offset + 2])
                    VertexUsage = LanguagePrimitives.EnumOfValue<byte, VertexUsage>(data[offset + 3])
                    UsageIndex = data[offset + 4]
                    Unknown = data[offset + 5 .. offset + 7]
                }
                elements.Add element
                offset <- offset + 8
                readElements ()

        readElements ()

        declarations.Add(elements |> List.ofSeq)

        // Alignment: 17 + 8 - (elements + 1) * 8
        let padding = 17 + 8 - ((elements.Count + 1) * 8)
        offset <- offset + padding

    declarations |> List.ofSeq, offset

// Quick struct to hold slices of the raw buffers
type RawBuffers = {
    VertexBuffers: byte[][]
    IndexBuffers: byte[][]
}

// Pull out vertex + index buffers as raw byte arrays
let extractRawBuffers (data: byte[]) (header: ModelFileHeader) : RawBuffers =
    let vertexBufs =
        [|
            let offset = int header.VertexOffsets[0]
            let size =int header.VertexBufferSizes[0]
            yield data[offset .. offset + size - 1]
        |]
        //[| 0..2 |]
        //|> Array.map (fun i ->
        //    let offset = int header.VertexOffsets[i]
        //    let size = int header.VertexBufferSizes[i]
        //    data[offset .. offset + size - 1])

    let indexBufs =
        [|
            let offset = int header.IndexOffsets[0]
            let size = int header.IndexBufferSizes[0]
            yield data[offset .. offset + size - 1]
        |]
        //[| 0..2 |]
        //|> Array.map (fun i ->
        //    let offset = int header.IndexOffsets[i]
        //    let size = int header.IndexBufferSizes[i]
        //    data[offset .. offset + size - 1])

    { VertexBuffers = vertexBufs; IndexBuffers = indexBufs }

type DecodedVertex = 
    {
        Position    : Vector3
        Color       : Vector4
        UV          : Vector2
    }

let decodeVertices (declaration: VertexElement list) (raw: byte[]) : DecodedVertex[] =
    let vertexStride =
        declaration
        |> List.map (fun el ->
            match el.VertexType with
            | VertexType.Single3 -> el.Offset + 12uy
            | VertexType.Single4 -> el.Offset + 16uy
            | VertexType.Half2   -> el.Offset + 4uy
            | VertexType.Half4   -> el.Offset + 8uy
            | _ -> el.Offset + 4uy
        )
        |> List.max
        |> int
    let count = raw.Length / vertexStride

    let tryReadVec3 offset =
        if offset + 12 <= raw.Length then
            Vector3(
                BitConverter.ToSingle(raw, offset),
                BitConverter.ToSingle(raw, offset + 4),
                BitConverter.ToSingle(raw, offset + 8)
            )
        else
            printfn "WARN: Skipped reading Vec3 at offset %d (buffer too small)" offset
            Vector3.Zero

    let tryReadVec2 offset =
        if offset + 8 <= raw.Length then
            Vector2(
                BitConverter.ToSingle(raw, offset),
                BitConverter.ToSingle(raw, offset + 4)
            )
        else
            printfn "WARN: Skipped reading Vec2 at offset %d (buffer too small)" offset
            Vector2.Zero

    let tryReadVec4 offset =
        if offset + 16 <= raw.Length then
            Vector4(
                BitConverter.ToSingle(raw, offset),
                BitConverter.ToSingle(raw, offset + 4),
                BitConverter.ToSingle(raw, offset + 8),
                BitConverter.ToSingle(raw, offset + 12)
            )
        else
            printfn "WARN: Skipped reading Vec4 at offset %d (buffer too small)" offset
            Vector4.One

    Array.init count (fun i ->
        let baseOffset = i * vertexStride

        let pos =
            match declaration |> List.tryFind (fun d -> d.VertexUsage = VertexUsage.Position) with
            | Some d -> tryReadVec3 (baseOffset + int d.Offset)
            | None -> Vector3.Zero

        let color =
            match declaration |> List.tryFind (fun d -> d.VertexUsage = VertexUsage.Color) with
            | Some d -> tryReadVec4 (baseOffset + int d.Offset)
            | None -> Vector4.One

        let uv =
            match declaration |> List.tryFind (fun d -> d.VertexUsage = VertexUsage.UV) with
            | Some d -> tryReadVec2 (baseOffset + int d.Offset)
            | None -> Vector2.Zero

        {
            Position = pos
            Color = color
            UV = uv
        }
    )

let decodeIndices (raw: byte[]) : uint16[] =
    Array.init (raw.Length / 2) (fun i ->
        BitConverter.ToUInt16(raw, i * 2)
    )