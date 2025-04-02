module Vertex

open System.Numerics
open Veldrid

[<Struct>]
type Vertex =
    val mutable Position    : Vector2
    val mutable Color       : RgbaFloat
    
    new(pos, color) = { Position = pos; Color = color }

    static member VertexLayout : VertexLayoutDescription = 
        VertexLayoutDescription(
            [|
                VertexElementDescription("Position", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3)
                VertexElementDescription("Color", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float4)
                VertexElementDescription("UV", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2)
            |]
        )