namespace Shared

open xivModdingFramework.Textures.Enums
open xivModdingFramework.Models.DataContainers
open xivModdingFramework.Materials.DataContainers
open System.Numerics
open System.Runtime.InteropServices
open Veldrid


[<Struct; StructLayout(LayoutKind.Sequential, Pack = 16)>]
type VertexPositionColorUv =
    val Position        : Vector3
    val Color           : Vector4
    val Color2          : Vector4
    val UV              : Vector2
    val Normal          : Vector3
    val BiTangent       : Vector3
    val Unknown1        : Vector3
    new(pos, col, col2, uv, nor, bitan, un1) = { Position = pos; Color = col; Color2 = col2; UV = uv; Normal = nor; BiTangent = bitan; Unknown1 = un1 }



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
        RawMtrl                 : XivMtrl
    }

type LoadedModel =
    {
        Vertices        : VertexPositionColorUv[]
        Indices         : uint16[]
        Materials       : InterpretedMaterial list
        RawModel        : XivMdl
    }

type PreparedMaterial =
    {
        MaterialName            : string
        ResourceLayout          : ResourceLayout
        ResourceSet             : ResourceSet
        ColorSetBuffer          : DeviceBuffer
    }

type RenderModel =
    {
        Vertices                : DeviceBuffer
        Indices                 : DeviceBuffer
        IndexCount              : uint32
        MVPBuffer               : DeviceBuffer
        MVPSet                  : ResourceSet
        MaterialSet             : ResourceSet
        MaterialLayout          : ResourceLayout
        Pipeline                : Pipeline option
        RawModel                : LoadedModel
    }

type MdlEntry = {
    Name: string
    MdlPath: string
}

type GearEntry = {
    Name: string
    MdlPath: string
    Slot: int
}