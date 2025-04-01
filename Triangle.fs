module Triangle

open System.Numerics
open Veldrid

[<Struct>]
type VertexPositionColor = 
    val Position    : Vector2
    val Color       : RgbaFloat
    new (position, color) = { Position = position; Color = color }

let vertices : VertexPositionColor[] =
    [|
        VertexPositionColor(Vector2(-0.75f, -0.75f), RgbaFloat.Red)
        VertexPositionColor(Vector2(0.0f, 0.75f), RgbaFloat.Green)
        VertexPositionColor(Vector2(0.75f, -0.75f), RgbaFloat.Blue)
    |]