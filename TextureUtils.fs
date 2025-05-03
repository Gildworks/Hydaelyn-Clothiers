module TextureUtils

open Veldrid
open xivModdingFramework.Textures.Enums
open Shared

let oneByWhite (gd: GraphicsDevice) =
    let tex = gd.ResourceFactory.CreateTexture(
        TextureDescription.Texture2D(1u, 1u, 1u, 1u, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Sampled)
    )
    let pixel = RgbaByte(255uy, 255uy, 255uy, 255uy)
    gd.UpdateTexture(tex, [| pixel |], 0u, 0u, 0u, 1u, 1u, 1u, 0u, 0u)
    gd.ResourceFactory.CreateTextureView(tex)

let oneByNormal (gd: GraphicsDevice) =
    let tex = gd.ResourceFactory.CreateTexture(
        TextureDescription.Texture2D(1u, 1u, 1u, 1u, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Sampled)
    )
    let pixel = RgbaByte(128uy, 128uy, 255uy, 255uy)
    gd.UpdateTexture(tex, [| pixel |], 0u, 0u, 0u, 1u, 1u, 1u, 0u, 0u)
    gd.ResourceFactory.CreateTextureView(tex)

let oneByBlack (gd: GraphicsDevice) =
    let tex = gd.ResourceFactory.CreateTexture(
        TextureDescription.Texture2D(1u, 1u, 1u, 1u, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Sampled)
    )
    let pixel = RgbaByte(0uy, 0uy, 0uy, 0uy)
    gd.UpdateTexture(tex, [| pixel |], 0u, 0u, 0u, 1u, 1u, 1u, 0u, 0u)
    gd.ResourceFactory.CreateTextureView(tex)

let texViewFromBytes (gd: GraphicsDevice) (tex: LoadedTexture) =
    let factory = gd.ResourceFactory
    let rgba32Array =
        Array.init (tex.Data.Length / 4) (fun i ->
            let idx = i * 4
            RgbaByte(tex.Data[idx], tex.Data[idx + 1], tex.Data[idx + 2], tex.Data[idx + 3])
        )

    let texObj = factory.CreateTexture(TextureDescription(
        uint32 tex.Width,
        uint32 tex.Height,
        1u, 1u, 1u,
        PixelFormat.R8_G8_B8_A8_UNorm,
        TextureUsage.Sampled,
        TextureType.Texture2D
    ))

    gd.UpdateTexture(texObj, rgba32Array, 0u, 0u, 0u, uint32 tex.Width, uint32 tex.Height, 1u, 0u, 0u)

    factory.CreateTextureView(texObj)