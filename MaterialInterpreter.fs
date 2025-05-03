namespace MaterialInterpreter

open System
open System.Numerics
open xivModdingFramework.Materials.DataContainers
open xivModdingFramework.Textures.FileTypes
open xivModdingFramework.Textures.Enums
open Shared

module Interpreter =

    let private toLoadedTexture (tex: MtrlTexture) (textureData: LoadedTexture) =
        {
            Usage = tex.Usage
            Path = tex.TexturePath
            Data = textureData.Data
            Width = textureData.Width
            Height = textureData.Height
        }

    let private parseColorSet (xivMtrl: XivMtrl) : ColorSet option =
        if (xivMtrl.ColorSetData.Count <= 0) then
            None
        else
            let rows =
                xivMtrl.ColorSetData
                |> Seq.toList
                |> List.chunkBySize 16
                |> List.map (fun chunk ->
                    {
                        DiffuseColor = Vector3(float32 (List.item 0 chunk), float32 (List.item 1 chunk), float32 (List.item 2 chunk))
                        SpecularColor = Vector3(float32 (List.item 4 chunk), float32 (List.item 5 chunk), float32 (List.item 6 chunk))
                        SpecularPower = float32 (List.item 3 chunk)
                        Gloss = float32 (List.item 7 chunk)
                        EmissiveColor = Vector3(float32 (List.item 0 chunk), float32 (List.item 1 chunk), float32 (List.item 2 chunk))
                        SpecularStrength = float32 (List.item 11 chunk)
                    }
                )
            Some { Rows = rows |> List.toArray; DyeData = if xivMtrl.ColorSetDyeData.Length > 0 then Some xivMtrl.ColorSetDyeData else None}

    let fromXivMtrl (xivMtrl: XivMtrl) (loadedTextures: LoadedTexture list) : InterpretedMaterial =
        let findTex texType =
            loadedTextures |> List.tryFind (fun t -> t.Usage = texType)

        let enableTranslucency =
            (xivMtrl.MaterialFlags &&& EMaterialFlags1.EnableTranslucency) = EMaterialFlags1.EnableTranslucency

        let twoSided =
            (xivMtrl.MaterialFlags &&& EMaterialFlags1.HideBackfaces) <> EMaterialFlags1.HideBackfaces

        let alphaThreshold =
            match List.ofSeq xivMtrl.ShaderConstants |> List.tryFind (fun sc -> sc.ConstantId = 0x29AC0223u) with
            | Some threshold -> threshold.Values |> Seq.toList |> List.tryHead
            | None -> None

        let shaderConstants =
            List.ofSeq xivMtrl.ShaderConstants
            |> List.map (fun sc -> (sc.ConstantId.ToString("X8"), sc.Values))
            |> Map.ofList

        let shaderKeys =
            List.ofSeq xivMtrl.ShaderKeys
            |> List.map (fun sk -> (sk.KeyId.ToString("X8"), sk.Value))
            |> Map.ofList

        {
            Name = xivMtrl.MTRLPath
            DiffuseTexture = findTex XivTexType.Diffuse
            NormalTexture = findTex XivTexType.Normal
            SpecularTexture = findTex XivTexType.Specular
            MaskTexture = findTex XivTexType.Mask
            IndexTexture = findTex XivTexType.Index
            ReflectionTexture = findTex XivTexType.Reflection
            EnableTranslucency = enableTranslucency
            TwoSided = twoSided
            AlphaThreshold = alphaThreshold
            ShaderConstants = shaderConstants
            ShaderKeys = shaderKeys
            ColorSetData = parseColorSet xivMtrl
        }