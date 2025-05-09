module MaterialHelper

open Veldrid
open Shared

let prepareMaterial (gd: GraphicsDevice) (factory: ResourceFactory) (mat: InterpretedMaterial) : PreparedMaterial =
    let fallbackWhite = TextureUtils.oneByWhite gd
    let fallbackNorm = TextureUtils.oneByNormal gd
    let fallbackBlack = TextureUtils.oneByBlack gd
    let sampler = factory.CreateSampler(SamplerDescription.Linear)

    let fallbackDiffuse =
        match mat.ColorSetData with
        | Some _ ->
            try
                let bytes, width, height = MaterialInterpreter.Interpreter.getColorizeDiffuseFromModelMaps mat.RawMtrl
                TextureUtils.texViewFromBytes gd {
                    Usage= xivModdingFramework.Textures.Enums.XivTexType.Diffuse
                    Path = "BakedDiffuse"
                    Data = bytes
                    Width = width
                    Height = height
                }
            with _ ->
                fallbackWhite
        | None -> fallbackWhite

    let getTex texOpt fallback =
        texOpt
        |> Option.map (TextureUtils.texViewFromBytes gd)
        |> Option.defaultValue fallback

    let diffuse = getTex mat.DiffuseTexture fallbackDiffuse
    let normal = getTex mat.NormalTexture fallbackNorm
    let mask = getTex mat.MaskTexture fallbackWhite
    let index = getTex mat.IndexTexture fallbackBlack

    let colorSetBuffer = 
        match mat.ColorSetData with
        | Some cs ->
            let flattened =
                cs.Rows
                |> Array.collect (fun row ->
                    [|
                        row.DiffuseColor.X; row.DiffuseColor.Y; row.DiffuseColor.Z; 0.0f
                        row.SpecularColor.X; row.SpecularColor.Y; row.SpecularColor.Z; 0.0f
                        row.SpecularPower; row.Gloss; row.EmissiveColor.X; row.EmissiveColor.Y
                        row.EmissiveColor.Z; row.SpecularStrength; 0.0f; 0.0f
                    |]
                )

            let bufferSizeBytes = uint32(flattened.Length * sizeof<float32>)

            let buf = factory.CreateBuffer(BufferDescription(bufferSizeBytes, BufferUsage.UniformBuffer))
            gd.UpdateBuffer(buf, 0u, flattened)
            Some buf
        | None -> None


    let layout = factory.CreateResourceLayout(
        ResourceLayoutDescription(
            ResourceLayoutElementDescription("tex_Diffuse", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
            ResourceLayoutElementDescription("tex_Normal", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
            ResourceLayoutElementDescription("tex_Mask", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
            ResourceLayoutElementDescription("tex_Index", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
            ResourceLayoutElementDescription("SharedSampler", ResourceKind.Sampler, ShaderStages.Fragment),
            ResourceLayoutElementDescription("ColorSetBuffer", ResourceKind.UniformBuffer, ShaderStages.Fragment)
    ))

    let colorSetFallback =
        colorSetBuffer |> Option.defaultWith (fun () ->
            let empty = Array.init 4 (fun _ -> 0.0f)
            let buf = factory.CreateBuffer(BufferDescription(4u, BufferUsage.UniformBuffer))
            gd.UpdateBuffer(buf, 0u, empty)
            buf
        )


    let set = factory.CreateResourceSet(ResourceSetDescription(
        layout, diffuse, normal, mask, index, sampler, colorSetFallback
    ))


    {
        MaterialName = mat.Name
        ResourceLayout = layout
        ResourceSet = set
        ColorSetBuffer = colorSetFallback
    }