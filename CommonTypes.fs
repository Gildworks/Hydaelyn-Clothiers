namespace Shared

open xivModdingFramework.Textures.Enums
open System.Numerics
open Veldrid

type LoadedTexture =
    {
        Usage                   : XivTexType
        Path                    : string
        Data                    : byte[]
        Width                   : int
        Height                  : int
    }

type ColorSetRow =
    {
        DiffuseColor            : Vector3
        SpecularColor           : Vector3
        SpecularPower           : float32
        Gloss                   : float32
        EmissiveColor           : Vector3
        SpecularStrength        : float32
    }

type ColorSet =
    {
        Rows                    : ColorSetRow[]
        DyeData                 : byte[] option
    }

type InterpretedMaterial =
    {
        Name                    : string
        DiffuseTexture          : LoadedTexture option
        NormalTexture           : LoadedTexture option
        SpecularTexture         : LoadedTexture option
        MaskTexture             : LoadedTexture option
        IndexTexture            : LoadedTexture option
        ReflectionTexture       : LoadedTexture option
        EnableTranslucency      : bool
        TwoSided                : bool
        AlphaThreshold          : float32 option
        ShaderConstants         : Map<string, System.Collections.Generic.List<float32>>
        ShaderKeys              : Map<string, uint32>
        ColorSetData            : ColorSet option
    }

type PreparedMaterial =
    {
        MaterialName            : string
        ResourceLayout          : ResourceLayout
        ResourceSet             : ResourceSet
        ColorSetBuffer          : DeviceBuffer
    }