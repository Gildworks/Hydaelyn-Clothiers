module MaterialLoader

open System
open System.IO
open xivModdingFramework.Materials.FileTypes
open xivModdingFramework.Materials.DataContainers
open xivModdingFramework.Mods
open xivModdingFramework.Textures.Enums
open xivModdingFramework.Textures.FileTypes

type LoadedTexture =
    {
        Usage: XivTexType
        Path: string
        Data: byte[]
        Width: int
        Height: int
    }

type LoadedMaterial =
    {
        MaterialPath: string
        Textures: LoadedTexture list
        ColorSet: byte[] option
        Flags: Map<string, obj>
    }

