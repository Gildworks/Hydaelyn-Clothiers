namespace Shared

open xivModdingFramework.Textures.Enums
open xivModdingFramework.Models.DataContainers
open xivModdingFramework.Models.ModelTextures
open xivModdingFramework.Items.Interfaces
open xivModdingFramework.Materials.DataContainers
open xivModdingFramework.Items.DataContainers
open xivModdingFramework.General.Enums
open System
open System.Numerics
open System.Runtime.InteropServices
open System.Runtime.CompilerServices
open Veldrid


[<Struct; StructLayout(LayoutKind.Sequential, Pack = 16)>]
type VertexPositionColorUvUnused =
    val Position        : Vector3
    val Color           : Vector4
    val Color2          : Vector4
    val UV              : Vector2
    val Normal          : Vector3
    val BiTangent       : Vector3
    val Unknown1        : Vector3
    new(pos, col, col2, uv, nor, bitan, un1) = { Position = pos; Color = col; Color2 = col2; UV = uv; Normal = nor; BiTangent = bitan; Unknown1 = un1 }

[<Struct>]
type VertexPositionColorUv =
    val Position        : Vector3
    val Color           : Vector4
    val UV              : Vector2
    val Normal          : Vector3
    val Tangent         : Vector3
    val BiTangent       : Vector3
    new (pos, col, uv, normal, tangent, bitangent) = {
        Position = pos
        Color = col
        UV = uv
        Normal = normal
        Tangent = tangent
        BiTangent = bitangent
    }

type raceIds = 
    | Hyur_Midlander_Male = 0
    | Hyur_Midlander_Female = 1
    | Hyur_HighLander_Male = 2
    | Hyur_Highlander_Female = 3
    | Elezen_Wildwood_Male = 4
    | Elezen_Wildwood_Female = 5
    | Elezen_Duskwight_Male = 6
    | Elezen_Duskwight_Female = 7
    | Lalafell_Plainsfolk_Male = 8
    | Lalafell_Plainsfolk_Female = 9
    | Lalafell_DunesFolk_Male = 10
    | Lalafell_DunesFolk_Female = 11
    | Miqote_Seeker_Male = 12
    | Miqote_Seeker_Female = 13
    | Miqote_Keeper_Male = 14
    | Miqote_Keeper_Female = 15
    | Roegadyn_SeaWolves_Male = 16
    | Roegadyn_SeaWolves_Female = 17
    | Roegadyn_Hellsguard_Male = 18
    | Roegadyn_Hellsguard_Female = 19
    | AuRa_Raen_Male = 20
    | AuRa_Raen_Female = 21
    | AuRa_Xaela_Male = 22
    | AuRa_Xaela_Female = 23
    | Hrothgar_Helions_Male = 24
    | Hrothgar_Helions_Female = 25
    | Hrothgar_Lost_Male = 26
    | Hrothgar_Lost_Female = 27
    | Viera_Rava_Male = 28
    | Viera_Rava_Female = 29
    | Viera_Veena_Male = 30
    | Viere_Veena_Female = 31

type paletteOptions =
    | RenderHighlights = 0
    | RenderEyeColor = 1
    | RenderLipDark = 2
    | RenderLipLight = 3
    | RenderTattoo = 4
    | RenderFaceDark = 5
    | RenderFaceLight = 6
    | UIHighlights = 7
    | UIEyeColor = 8
    | UILipDark = 9
    | UILipLight = 10
    | UITattoo = 11
    | UIFaceDark = 12
    | UIFaceLight = 13
    | RenderSkin = 14
    | RenderHair = 15
    | UISkin = 16
    | UIHair = 17



type LoadedTexture =
    {
        Usage                   : XivTexType
        Path                    : string
        Data                    : byte[]
        Width                   : int
        Height                  : int
    }

type EquipmentSlot =
    | Head
    | Body
    | Hands
    | Legs
    | Feet
    | Face
    | Hair
    | Tail
    | Ear
    | Bracelet
    | RingL
    | RingR
    | Necklace
    | Earrings

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
        DiffuseTexture          : Texture
        NormalTexture           : Texture 
        SpecularTexture         : Texture
        EmissiveTexture         : Texture
        AlphaTexture            : Texture
        RoughnessTexture        : Texture
        MetalnessTexture        : Texture
        OcclusionTexture        : Texture
        SubsurfaceTexture       : Texture
        ResourceSet             : ResourceSet
        Mtrl                    : XivMtrl
    }
    with
        member this.Dispose() =
            this.DiffuseTexture.Dispose()
            this.AlphaTexture.Dispose()
            this.EmissiveTexture.Dispose()
            this.MetalnessTexture.Dispose()
            this.NormalTexture.Dispose()
            this.OcclusionTexture.Dispose()
            this.RoughnessTexture.Dispose()
            this.SpecularTexture.Dispose()
            this.SubsurfaceTexture.Dispose()
            this.ResourceSet.Dispose()


type MdlEntry = {
    Name: string
    MdlPath: string
}

type GearEntry = {
    Name: string
    MdlPath: string
    Slot: int
}

type RenderMesh = {
    VertexBuffer                : DeviceBuffer
    IndexBuffer                 : DeviceBuffer
    IndexCount                  : int
    Material                    : PreparedMaterial
    RawModel                    : TTModel
}

type RenderModel = {
    Meshes                      : RenderMesh list
    Original                    : TTModel
}
    with
        member this.Dispose() =
            for mesh in this.Meshes do
                mesh.VertexBuffer.Dispose()
                mesh.IndexBuffer.Dispose()
                mesh.Material.Dispose()

type PipelineKey = {
    VertexLayout                : VertexLayoutDescription
    FragmentLayout              : ResourceLayout
    OutputDescription           : OutputDescription
}

type ComboOption = {
    Display: string
    Value: string
}

type CharacterCustomizationOptions = {
    Race                        : XivRace
    AvailableBodyParts          : XivCharacter list
    AvailableFaceParts          : XivCharacter list
    AvailableHairParts          : XivCharacter list
    AvailableTailParts          : XivCharacter list
    AvailableEarParts           : XivCharacter list
}

type Config = {
    GamePath: string
}

type InputModel = {
    Model                       : TTModel
    Item                        : IItemModel
    Dye1                        : int
    Dye2                        : int
    Colors                      : CustomModelColors
}