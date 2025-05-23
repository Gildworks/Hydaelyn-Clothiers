module CmpLoader

open xivModdingFramework.General
open xivModdingFramework.General.Enums

let getScalingParameters (race: XivSubRace) (gender: XivGender) =
    task{
        let! rgsp = CMP.GetScalingParameter (race, gender)
        printfn $"Height range: {rgsp.MinSize} to {rgsp.MaxSize}"
        printfn $"Tail size: {rgsp.MinTail} to {rgsp.MaxTail}"
    }
