module SharpToNumerics

open SharpDX
open System.Numerics

let vec2 (v: SharpDX.Vector2) = Vector2(v.X, v.Y)
let vec3 (v: SharpDX.Vector3) = Vector3(v.X, v.Y, v.Z)
let vec4 (v: SharpDX.Vector4) = Vector4(v.X, v.Y, v.Z, v.W)

let col (v: SharpDX.Color) =
    Vector4(
        float32 v.R / 255.0f, 
        float32 v.G / 255.0f, 
        float32 v.B / 255.0f, 
        float32 v.A / 255.0f
    )

let vec4col (v: SharpDX.Color) = Vector4(float32 v.R, float32 v.G, float32 v.B, float32 v.A)