module ApplyFlags

open System.Threading.Tasks
open System.Collections.Generic

open xivModdingFramework.Models.DataContainers
open xivModdingFramework.Models.FileTypes
open xivModdingFramework.Models.Helpers
open xivModdingFramework.Items.Interfaces

open Shared

//================================================ TO DO ================================================\\
//    ✓ Load all flags for every mesh into list (custom flag type for list probably needed)              \\
//    ✓ Assess flags for overrides/priorities, discarding unused/unneeded                                \\
//    ✓ Create methods for each task defining behavior (which part(s) to hide, if present)               \\
//    ✓ Create a helper task that takes a list of flags and applies the logic to them                    \\
//    ✓ Output the modified models to either a new map or the current one, if possible                   \\
//    ✓ Start testing!                                                                                   \\
//=======================================================================================================\\


let mutable allFlags    : Map<EquipmentParameterFlag, bool> = Map.empty
let mutable usedFlags   : Map<EquipmentParameterFlag, bool> = Map.empty


let gloveType () : string =
    let has flag = allFlags |> Map.tryFind flag |> Option.defaultValue false

    match has EquipmentParameterFlag.HandHideElbow, has EquipmentParameterFlag.HandHideForearm with
    | true, false -> "ShortGlove"
    | false, true -> "MidGlove"
    | true, true -> "LongGlove"
    | false, false -> ""

let shoeType () : string =
    let has flag = allFlags |> Map.tryFind flag |> Option.defaultValue false

    match has EquipmentParameterFlag.FootHideKnee, has EquipmentParameterFlag.FootHideCalf with
    | true, false -> "Shoe"
    | false, true -> "MidBoot"
    | true,  true -> "LongBoot"
    | false, false -> ""

let filterFlagConflicts (flags: Map<EquipmentParameterFlag, bool>): Task<Map<EquipmentParameterFlag, bool>> =
    task {
        let mutable finalFlags : Map<EquipmentParameterFlag, bool> = flags
        for flag in flags do
            match (flag.Key, flag.Value) with
            | EquipmentParameterFlag.BodyHideLongGloves, true ->
                if gloveType() = "LongGlove" then
                    finalFlags <- finalFlags.Remove EquipmentParameterFlag.HandHideElbow
                    finalFlags <- finalFlags.Remove EquipmentParameterFlag.HandHideForearm
            | EquipmentParameterFlag.BodyHideMidGloves, true ->
                if gloveType() = "MidGlove" then
                    finalFlags <- finalFlags.Remove EquipmentParameterFlag.HandHideForearm
            | EquipmentParameterFlag.BodyHideShortGloves, true ->
                if gloveType() = "ShortGlove" then
                    finalFlags <- finalFlags.Remove EquipmentParameterFlag.HandHideElbow
            | EquipmentParameterFlag.BodyShowHand, false ->
                finalFlags <- finalFlags.Remove EquipmentParameterFlag.HandHideElbow
                finalFlags <- finalFlags.Remove EquipmentParameterFlag.HandHideForearm
                
            | _ -> ()

        return finalFlags
    }

let removeParts (model: TTModel) (attr: string) =
    for group in model.MeshGroups do
        group.Parts <-
            group.Parts
            |> List.ofSeq
            |> List.filter (fun part -> not (part.Attributes.Contains(attr)))
            |> fun seq -> new List<TTMeshPart>(seq)

let removeMeshParts (flags: Map<EquipmentParameterFlag, bool>) (model: TTModel) : Task<TTModel> =
    task {
        for flag in flags do
            match (flag.Key, flag.Value) with
            | EquipmentParameterFlag.BodyHideWaist, true ->
                do ModelModifiers.ApplyShapes(model, List<string>(["shp_kod"]), false)
                removeParts model "atr_kod" 
            | EquipmentParameterFlag.BodyHideLongGloves, true ->
                if gloveType() = "LongGlove" then
                    do ModelModifiers.ApplyShapes(model, List<string>(["shp_arm"]), false)
                    removeParts model "atr_arm"
            | EquipmentParameterFlag.BodyHideMidGloves, true ->
                if gloveType() = "MidGlove" then
                    do ModelModifiers.ApplyShapes(model, List<string>(["shp_arm"]), false)
                    removeParts model "atr_arm"
            | EquipmentParameterFlag.BodyHideShortGloves, true ->
                if gloveType() = "ShortGlove" then
                    removeParts model "atr_arm"
            | EquipmentParameterFlag.BodyHideGorget, true ->
                do ModelModifiers.ApplyShapes(model, List<string>(["shp_inr"]), false)
                removeParts model "atr_inr"
            | EquipmentParameterFlag.LegHideKneePads, true ->
                do ModelModifiers.ApplyShapes(model, List<string>(["shp_lpd"]), false)
                removeParts model "atr_lpd"
            | EquipmentParameterFlag.LegHideShortBoot, true ->
                if shoeType() = "Shoe" then
                    do ModelModifiers.ApplyShapes(model, List<string>(["shp_leg"]), false)
                    removeParts model "atr_leg"
            | EquipmentParameterFlag.LegHideHalfBoot, true ->
                if shoeType() = "MidBoot" then
                    do ModelModifiers.ApplyShapes(model, List<string>(["shp_leg"]), false)
                    removeParts model "atr_leg"
            | EquipmentParameterFlag.HandHideElbow, true ->
                match gloveType() with
                | "ShortGlove" -> do ModelModifiers.ApplyShapes(model, List<string>(["shp_hij"]), false)
                | "MidGlove" -> do ModelModifiers.ApplyShapes(model, List<string>(["shp_ude"]), false)
                | "LongGlove" -> 
                    do ModelModifiers.ApplyShapes(model, List<string>(["shp_kat"]), false)
                    removeParts model "atr_ude"
                | _ -> ()
            | EquipmentParameterFlag.HandHideForearm, true ->
                match gloveType() with
                | "ShortGlove" -> do ModelModifiers.ApplyShapes(model, List<string>(["shp_hij"]), false)
                | "MidGlove" -> 
                    do ModelModifiers.ApplyShapes(model, List<string>(["shp_ude"]), false)
                    removeParts model "atr_hij"
                | "LongGlove" -> do ModelModifiers.ApplyShapes(model, List<string>(["shp_kat"]), false)
                | _ -> ()
            | EquipmentParameterFlag.FootHideKnee, true ->
                match shoeType() with
                | "Shoe" -> 
                    do ModelModifiers.ApplyShapes(model, List<string>(["shp_sne"]), false)
                | "MidBoot" -> 
                    do ModelModifiers.ApplyShapes(model, List<string>(["shp_hiz"]), false)
                    removeParts model "atr_hiz"
                | "LongBoot" -> 
                    do ModelModifiers.ApplyShapes(model, List<string>(["shp_mom"]), false)
                    removeParts model "atr_hiz"
                | _ -> ()
            | EquipmentParameterFlag.FootHideCalf, true ->
                match shoeType() with
                | "Shoe" -> 
                    do ModelModifiers.ApplyShapes(model, List<string>(["shp_sne"]), false)
                | "MidBoot" -> 
                    do ModelModifiers.ApplyShapes(model, List<string>(["shp_hiz"]), false)
                    removeParts model "atr_sne"
                | "LongBoot" -> 
                    do ModelModifiers.ApplyShapes(model, List<string>(["shp_mom"]), false)
                    removeParts model "atr_sne"
                | _ -> ()
            | EquipmentParameterFlag.HeadHideScalp, true ->
                do ModelModifiers.ApplyShapes(model, List<string>(["shp_hib"]), false)
                removeParts model "atr_kam"
            | EquipmentParameterFlag.HeadHideNeck, true ->
                do ModelModifiers.ApplyShapes(model, List<string>(["shp_nek"]), false)
                removeParts model "atr_nek"
            | EquipmentParameterFlag.HeadShowEarAura, false ->
                do ModelModifiers.ApplyShapes(model, List<string>(["shp_hrn"]), false)
                removeParts model "atr_hrn"
            | EquipmentParameterFlag.HeadShowEarHuman, false ->
                removeParts model "atr_mim"
            | EquipmentParameterFlag.HeadShowEarMiqo, false ->
                do ModelModifiers.ApplyShapes(model, List<string>(["shp_top"]), false)
                removeParts model "atr_top"
            | _ ->
                ()

        model.MeshGroups <-
            model.MeshGroups
            |> Seq.filter (fun group -> group.Parts.Count > 0)
            |> Seq.toList
            |> fun lst -> new List<TTMeshGroup>(lst)
        return model
        }

let removeSlot (flags: Map<EquipmentParameterFlag, bool>) (models: Map<EquipmentSlot, InputModel>) =
    task {
        let mutable visibleModels = models
        for flag in flags do
            match (flag.Key, flag.Value) with
            | EquipmentParameterFlag.BodyShowLeg, false ->
                visibleModels <- visibleModels.Remove(Legs)
            | EquipmentParameterFlag.BodyShowHand, false ->
                visibleModels <- visibleModels.Remove(Hands)
            | EquipmentParameterFlag.BodyShowHead, false ->
                visibleModels <- visibleModels.Remove(Head)
            | EquipmentParameterFlag.HeadShowNecklace, false
            | EquipmentParameterFlag.BodyShowNecklace, false ->
                visibleModels <- visibleModels.Remove(Necklace)
            | EquipmentParameterFlag.HandShowBracelet, false
            | EquipmentParameterFlag.BodyShowBracelet, false ->
                visibleModels <- visibleModels.Remove(Bracelet)
            | EquipmentParameterFlag.LegShowTail, false
            | EquipmentParameterFlag.BodyShowTail, false ->
                visibleModels <- visibleModels.Remove(Tail)
            | EquipmentParameterFlag.LegShowFoot, false ->
                visibleModels <- visibleModels.Remove(Feet)
            | EquipmentParameterFlag.HandShowRingL, false ->
                visibleModels <- visibleModels.Remove(RingL)
            | EquipmentParameterFlag.HandShowRingR, false ->
                visibleModels <- visibleModels.Remove(RingR)
            | EquipmentParameterFlag.HeadShowHairOverride, true ->
                usedFlags <- usedFlags.Remove(EquipmentParameterFlag.HeadHideHair)
                usedFlags <- usedFlags.Remove(EquipmentParameterFlag.HeadHideScalp)
            | EquipmentParameterFlag.HeadHideHair, true ->
                visibleModels <- visibleModels.Remove(Hair)
            | EquipmentParameterFlag.HeadShowEarrings, false ->
                visibleModels <- visibleModels.Remove(Earrings)
            | EquipmentParameterFlag.HeadShowEarViera, false ->
                visibleModels <- visibleModels.Remove(Ear)
            | _ -> ()
        return visibleModels
    }

let applyFlags (models: Map<EquipmentSlot, InputModel>) : Task<Map<EquipmentSlot, InputModel>> =
    task {
        for model in models.Values do
            let attributes = model.Model.Attributes
            let flags = model.Model.Flags
            if model.Item.PrimaryCategory = "Gear" && model.Item.ModelInfo.PrimaryID > 0 then

                let eqp = new Eqp()
            
                let! itemAttr = eqp.GetEqpEntry(model.Item)
                let itemFlags = itemAttr.AvailableFlags
                let itemFlagValues = itemAttr.GetFlags()
                for flag in itemFlagValues do
                    allFlags <- allFlags |> Map.add flag.Key flag.Value
            
                for flag in itemFlagValues do                
                    if flag.Key.ToString().Contains("Hide") && flag.Value then
                        usedFlags <- usedFlags |> Map.add flag.Key flag.Value
                    else if flag.Key.ToString().Contains("Show") && not flag.Value then
                        usedFlags <- usedFlags |> Map.add flag.Key flag.Value
        

        let! filteredFlags = filterFlagConflicts(usedFlags)
        usedFlags <- filteredFlags

        let! removedSlots = removeSlot filteredFlags models
       
        let! modifiedModels =
            if usedFlags.Count > 0 then
                removedSlots
                |> Map.toSeq
                |> Seq.map (fun (slot, removedSlots) -> task {
                    let! edited = removeMeshParts usedFlags removedSlots.Model
                    return slot, {removedSlots with Model = edited}
                })
                |> Task.WhenAll
            else
                removedSlots
                |> Map.toSeq
                |> Seq.map (fun (slot,removedSlots) -> task {
                    return slot, removedSlots
                })
                |> Task.WhenAll

        allFlags <- Map.empty
        usedFlags <- Map.empty
        return modifiedModels |> Map.ofArray
    }