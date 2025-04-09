module MdlParser

open System
open System.IO
open System.Numerics
open System.Buffers.Binary
open System.Runtime.InteropServices

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
        Normal      : Vector3
    }

let decodeVerticesFromDeclaration (declaration: VertexElement list) (rawBuffers: byte[][]) =
    let tryMax list =
        if List.isEmpty list then None
        else Some (List.max list)

    let getElement usage expectedStream =
            declaration
            |> List.tryFind (fun el -> el.VertexUsage = usage && el.Stream = expectedStream)

    let posEl = getElement VertexUsage.Position 0uy
    let norEl = getElement VertexUsage.Normal 1uy
    let colEl = getElement VertexUsage.Color 1uy
    let uvEl  = getElement VertexUsage.UV 1uy

    // Get raw buffer slices
    let posBuf = if rawBuffers.Length > 0 then Some rawBuffers[0] else None
    let attrBuf = if rawBuffers.Length > 1 then Some rawBuffers[1] else None

    // Estimate strides per stream
    let strideOf stream =
        declaration
        |> List.filter (fun el -> el.Stream = stream)
        |> List.map (fun el ->
            match el.VertexType with
            | VertexType.Single3 -> int el.Offset + 12
            | VertexType.Single4 -> int el.Offset + 16
            | VertexType.Half4   -> int el.Offset + 8
            | VertexType.ByteFloat4 -> int el.Offset + 4
            | VertexType.Half2   -> int el.Offset + 4
            | _ -> int el.Offset + 4
        )
        |> tryMax
        |> Option.defaultValue 0

    let posStride = strideOf 0uy
    let attrStride = strideOf 1uy

    // Determine how many vertices exist (based on position stream)
    let count =
        match posBuf with
        | Some buf -> buf.Length / posStride
        | None -> 0

    let halfToFloat (half: uint16) : float32 =
        let s = (half >>> 15) &&& 0x0001us
        let e = (half >>> 10) &&& 0x001Fus
        let f = half &&& 0x03FFus
        let result =
            if e = 0us then float32 f * (2.0f ** -24.0f)
            elif e = 31us then if f = 0us then (if s = 0us then Single.PositiveInfinity else Single.NegativeInfinity) else Single.NaN
            else
                let exponent = float32 (int e - 15)
                let mantissa = 1.0f + float32 f / 1024.0f
                mantissa * (2.0f ** exponent)
        if s = 1us then -result else result

    let tryReadVec3 (buffer: byte[]) offset =
        Vector3(
            BitConverter.ToSingle(buffer, offset),
            BitConverter.ToSingle(buffer, offset + 4),
            BitConverter.ToSingle(buffer, offset + 8)
        )

    let tryReadVec2 (buffer: byte[]) offset =
        Vector2(
            BitConverter.ToSingle(buffer, offset),
            BitConverter.ToSingle(buffer, offset + 4)
        )

    let tryReadHalfVec3 (buffer: byte[]) offset =
        Vector3(
            halfToFloat (BitConverter.ToUInt16(buffer, offset)),
            halfToFloat (BitConverter.ToUInt16(buffer, offset + 2)),
            halfToFloat (BitConverter.ToUInt16(buffer, offset + 4))
        )

    let tryReadByteFloat4 (buffer: byte[]) offset =
        Vector4(
            float32 buffer[offset] / 255.0f,
            float32 buffer[offset + 1] / 255.0f,
            float32 buffer[offset + 2] / 255.0f,
            float32 buffer[offset + 3] / 255.0f
        )

    let tryReadVec4 (buffer: byte[]) offset =
        Vector4(
            BitConverter.ToSingle(buffer, offset),
            BitConverter.ToSingle(buffer, offset + 4),
            BitConverter.ToSingle(buffer, offset + 8),
            BitConverter.ToSingle(buffer, offset + 12)
        )

    Array.init count (fun i ->
        let mutable pos = Vector3.Zero
        let mutable nor = Vector3.UnitZ
        let mutable col = Vector4.One
        let mutable uv  = Vector2.Zero

        match posBuf, posEl with
        | Some buf, Some el ->
            let offset = i * posStride + int el.Offset
            if offset + 12 <= buf.Length then
                pos <- tryReadVec3 buf offset
        | _ -> ()

        match attrBuf, norEl with
        | Some buf, Some el ->
            let offset = i * attrStride + int el.Offset
            if offset + 6 <= buf.Length then
                nor <- tryReadHalfVec3 buf offset
        | _ -> ()

        match attrBuf, colEl with
        | Some buf, Some el ->
            let offset = i * attrStride + int el.Offset
            if offset + 4 <= buf.Length then
                col <- tryReadByteFloat4 buf offset
        | _ -> ()

        match attrBuf, uvEl with
        | Some buf, Some el ->
            let offset = i * attrStride + int el.Offset
            if offset + 8 <= buf.Length then
                uv <- tryReadVec2 buf offset
        | _ -> ()

        {
            Position = pos
            Normal = nor
            Color = col
            UV = uv
        }
    )


let decodeIndices (raw: byte[]) : uint16[] =
    let count = raw.Length / 2
    printfn "Raw index buffer size: %d bytes" raw.Length
    printfn "Interpreted as %d 16-bit indices" count

    for i in 0 .. min 19 (count - 1) do
        let index = BitConverter.ToUInt16(raw, i * 2)
        printfn "Index %d: %d" i index
    
    Array.init count (fun i ->
        BitConverter.ToUInt16(raw, i * 2)
    )