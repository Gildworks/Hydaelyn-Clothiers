namespace Shared

open xivModdingFramework.Textures.Enums
open xivModdingFramework.Models.DataContainers
open xivModdingFramework.Models.ModelTextures
open xivModdingFramework.Items.Interfaces
open xivModdingFramework.Materials.DataContainers
open xivModdingFramework.Items.DataContainers
open xivModdingFramework.General.Enums
open xivModdingFramework.Exd.FileTypes
open System
open System.ComponentModel
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

[<Struct>]
type TransformsUBO =
    {
        World: Matrix4x4
        View: Matrix4x4
        Projection: Matrix4x4
        EyePosition: Vector4
    }

type ViewModelBase() =
    let propertyChanged = Event<PropertyChangedEventHandler, PropertyChangedEventArgs>()

    [<CLIEvent>]
    member this.FSharpPropertyChanged = propertyChanged.Publish

    interface INotifyPropertyChanged with
        [<CLIEvent>]
        member _.PropertyChanged = propertyChanged.Publish

    member this.RaisePropertyChanged([<CallerMemberName>]?propertyName: string) =
        match propertyName with
        | Some name -> propertyChanged.Trigger(this, PropertyChangedEventArgs(name))
        | None -> ()
    member this.SetValue<'T>(field: byref<'T>, value: 'T, [<CallerMemberName>]?propertyName: string) =
        match propertyName with
            | Some name ->
                if not (System.Object.Equals(field, value)) then
                    field <- value
                    this.RaisePropertyChanged(name)
            | None -> ()

type raceIds = 
    | Hyur_Midlander_Male = 0
    | Hyur_Midlander_Female = 1
    | Hyur_Highlander_Male = 2
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

type ComboOption = 
    {
        Display: string
        Value: string
    }
    override this.ToString () : string =
        this.Display
    

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

type swatchOption = {
    Color                       : Avalonia.Media.Color
    Index                       : int
}

type EquipRestriction =
    | None = 0
    | Unknown = 1
    | AllMale = 2
    | AllFemale = 3
    | HyurMale = 4
    | HyurFemale = 5
    | ElezenMale = 6
    | ElezenFemale = 7
    | LalafellMale = 8
    | LalafellFemale = 9
    | MiqoteMale = 10
    | MiqoteFemale = 11
    | RoegadynMale = 12
    | RoegadynFemale = 13
    | AuRaMale = 14
    | AuRaFemale = 15
    | HrothgarMale = 16
    | VieraFemale = 17
    | VieraMale = 18
    | HrothgarFemale = 19

type ClassJobEquip = {
    GLA: bool
    PGL: bool
    MRD: bool
    LNC: bool
    ARC: bool
    CNJ: bool
    THM: bool
    CRP: bool
    BSM: bool
    ARM: bool
    GSM: bool
    LTW: bool
    WVR: bool
    ALC: bool
    CUL: bool
    MIN: bool
    BTN: bool
    FSH: bool
    PLD: bool
    MNK: bool
    WAR: bool
    DRG: bool
    BRD: bool
    WHM: bool
    BLM: bool
    ACN: bool
    SMN: bool
    SCH: bool
    ROG: bool
    NIN: bool
    MCH: bool
    DRK: bool
    AST: bool
    SAM: bool
    RDM: bool
    BLU: bool
    GNB: bool
    DNC: bool
    RPR: bool
    SGE: bool
    VPR: bool
    PCT: bool
}
    with
        static member AllJobs = {
            GLA = true
            PGL = true
            MRD = true
            LNC = true
            ARC = true
            CNJ = true
            THM = true
            CRP = true
            BSM = true
            ARM = true
            GSM = true
            LTW = true
            WVR = true
            ALC = true
            CUL = true
            MIN = true
            BTN = true
            FSH = true
            PLD = true
            MNK = true
            WAR = true
            DRG = true
            BRD = true
            WHM = true
            BLM = true
            ACN = true
            SMN = true
            SCH = true
            ROG = true
            NIN = true
            MCH = true
            DRK = true
            AST = true
            SAM = true
            RDM = true
            BLU = true
            GNB = true
            DNC = true
            RPR = true
            SGE = true
            VPR = true
            PCT = true
        }
        static member NoJobs = {
            GLA = false
            PGL = false
            MRD = false
            LNC = false
            ARC = false
            CNJ = false
            THM = false
            CRP = false
            BSM = false
            ARM = false
            GSM = false
            LTW = false
            WVR = false
            ALC = false
            CUL = false
            MIN = false
            BTN = false
            FSH = false
            PLD = false
            MNK = false
            WAR = false
            DRG = false
            BRD = false
            WHM = false
            BLM = false
            ACN = false
            SMN = false
            SCH = false
            ROG = false
            NIN = false
            MCH = false
            DRK = false
            AST = false
            SAM = false
            RDM = false
            BLU = false
            GNB = false
            DNC = false
            RPR = false
            SGE = false
            VPR = false
            PCT = false
        }

type MasterBook =
    | noBook = 0
    | crpI = 1 | bsmI = 2 | armI = 3 | gsmI = 4
    | ltwI = 5 | wvrI = 6 | alcI = 7 | culI = 8
    | crpGlam = 9 | bsmGlam = 10 | armGlam = 11 | gsmGlam = 12
    | ltwGlam = 13 | wvrGlam = 14 | alcGlam = 15 | culGlam = 16
    | crpDemi = 17 | bsmDemi = 18 | armDemi = 19 | gsmDemi = 20
    | ltwDemi = 21 | wvrDemi = 22 | alcDemi = 23 | crpII = 24
    | bsmII = 25 | armII = 26 | gsmII = 27 | ltwII = 28
    | wvrII = 29 | alcII = 30 | culII = 31 | crpIII = 32
    | bsmIII = 33 | armIII = 34 | gsmIII = 35 | ltwIII = 36
    | wvrIII = 37 | alcIII = 38 | culIII = 39 | crpIV = 40
    | bsmIV = 41 | armIV = 42 | gsmIV = 43 | ltwIV = 44
    | wvrIV = 45 | alcIV = 46 | culIV = 47 | crpV = 48
    | bsmV = 49 | armV = 50 | gsmV = 51 | ltwV = 52
    | wvrV = 53 | alcV = 54 | culV = 55 | crpVI = 56
    | bsmVI = 57 | armVI = 58 | gsmVI = 59 | ltwVI = 60
    | wvrVI = 61 | alcVI = 62 | culVI = 63 | crpVII = 64
    | bsmVII = 65 | armVII = 66 | gsmVII = 67 | ltwVII = 68
    | wvrVII = 69 | alcVII = 70 | culVII = 71 | crpVIII = 72
    | bsmVIII = 73 | armVIII = 74 | gsmVIII = 75 | ltwVIII = 76
    | wvrVIII = 77 | alcVIII = 78 | culVIII = 79 | crpIX = 80
    | bsmIX = 81 | armIX = 82 | gsmIX = 83 | ltwIX = 84
    | wvrIX = 85 | alcIX = 86 | culIX = 87 | crpX = 88
    | bsmX = 89 | armX = 90 | gsmX = 91 | ltwX = 92
    | wvrX = 93 | alcX = 94 | culX = 95 | crpXI = 96
    | bsmXI = 97 | armXI = 98 | gsmXI = 99 | ltwXI = 100
    | wvrXI = 101 | alcXI = 102 | culXI = 103 | crpXII = 104
    | bsmXII = 105 | armXII = 106 | gsmXII = 107 | ltwXII = 108
    | wvrXII = 109 | alcXII = 110 | culXII = 111

type MasterBookItem = 
    {
        Book                            : MasterBook
        DisplayName                     : string
    }
    with override this.ToString (): string = 
             this.DisplayName


type Job =
    | GLA | PGL | MRD | LNC | ARC | CNJ | THM
    | CRP | BSM | ARM | GSM | LTW | WVR | ALC | CUL
    | MIN | BTN | FSH
    | PLD | MNK | WAR | DRG | BRD | WHM | BLM
    | ACN | SMN | SCH | ROG | NIN | MCH | DRK | AST
    | SAM | RDM | BLU | GNB | DNC | RPR | SGE
    | VPR | PCT
    static member ToDisplayName = function
        | GLA -> "Gladiator" | PGL -> "Pugilist" | MRD -> "Marauder" | LNC -> "Lancer" | ARC -> "Archer" | CNJ -> "Conjurer" | THM -> "Thaumaturge"
        | CRP -> "Carpenter" | BSM -> "Blacksmith" | ARM -> "Armorer" | GSM -> "Goldsmith" | LTW -> "Leatherworker" | WVR -> "Weaver" | ALC -> "Alchemist" | CUL -> "Culinarian"
        | MIN -> "Miner" | BTN -> "Botanist" | FSH -> "Fisher"
        | PLD -> "Paladin" | MNK -> "Monk" | WAR -> "Warrior" | DRG -> "Dragoon" | BRD -> "Bard" | WHM -> "White Mage" | BLM -> "Black Mage"
        | ACN -> "Arcanist" | SMN -> "Summoner" | SCH -> "Scholar" | ROG -> "Rogue" | NIN -> "Ninja" | MCH -> "Machinist" | DRK -> "Dark Knight" | AST -> "Astrologian"
        | SAM -> "Samurai" | RDM -> "Red Mage" | BLU -> "Blue Mage" | GNB -> "Gunbreaker" | DNC -> "Dancer" | RPR -> "Reaper" | SGE -> "Sage"
        | VPR -> "Viper" | PCT -> "Pictomancer"

type CraftingInfo =
    {
        Job                         : string
        RecipeLevel                 : int
        RecipeStars                 : int
        MasterBook                  : MasterBookItem
    }

type CrafterInfo =
    {
        Levels                      : System.Collections.Generic.Dictionary<int, string>
        RecipeBooks                 : int list
    }

type FilterGear = 
    {
        Item                        : XivGear
        ExdRow                      : Ex.ExdRow
        ItemLevel                   : int
        EquipLevel                  : int
        EquipRestriction            : EquipRestriction
        EquippableBy                : Set<Job>
        CraftingDetails             : CraftingInfo list
    }
    override this.ToString (): string = 
        this.Item.Name