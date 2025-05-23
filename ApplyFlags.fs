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
//    - Assess flags for overrides/priorities, discarding unused/unneeded                                \\
//    - Create methods for each task defining behavior (which part(s) to hide, if present)               \\
//    ✓ Create a helper task that takes a list of flags and applies the logic to them                    \\
//    ✓ Output the modified models to either a new map or the current one, if possible                   \\
//    - Start testing!                                                                                   \\
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
                | "Shoe" -> do ModelModifiers.ApplyShapes(model, List<string>(["shp_sne"]), false)
                | "MidBoot" -> do ModelModifiers.ApplyShapes(model, List<string>(["shp_hiz"]), false)
                | "LongBoot" -> do ModelModifiers.ApplyShapes(model, List<string>(["shp_mom"]), false)
                | _ -> ()
                removeParts model "atr_hiz"
            | EquipmentParameterFlag.FootHideCalf, true ->
                match shoeType() with
                | "Shoe" -> do ModelModifiers.ApplyShapes(model, List<string>(["shp_sne"]), false)
                | "MidBoot" -> do ModelModifiers.ApplyShapes(model, List<string>(["shp_hiz"]), false)
                | "LongBoot" -> do ModelModifiers.ApplyShapes(model, List<string>(["shp_mom"]), false)
                | _ -> ()
                removeParts model "atr_sne"
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

let removeSlot (flags: Map<EquipmentParameterFlag, bool>) (models: Map<EquipmentSlot, TTModel * IItemModel>) =
    task {
        let mutable visibleModels = models
        for flag in flags do
            match (flag.Key, flag.Value) with
            | EquipmentParameterFlag.BodyShowLeg, false ->
                visibleModels <- models.Remove(Legs)
            | EquipmentParameterFlag.BodyShowHand, false ->
                visibleModels <- models.Remove(Hands)
            | EquipmentParameterFlag.BodyShowHead, false ->
                visibleModels <- models.Remove(Head)
            | EquipmentParameterFlag.BodyShowNecklace, false ->
                visibleModels <- models.Remove(Necklace)
            | EquipmentParameterFlag.HandShowBracelet, false
            | EquipmentParameterFlag.BodyShowBracelet, false ->
                visibleModels <- models.Remove(Bracelet)
            | EquipmentParameterFlag.LegShowTail, false
            | EquipmentParameterFlag.BodyShowTail, false ->
                visibleModels <- models.Remove(Tail)
            | EquipmentParameterFlag.LegShowFoot, false ->
                visibleModels <- models.Remove(Feet)
            | EquipmentParameterFlag.HandShowRingL, false ->
                visibleModels <- models.Remove(RingL)
            | EquipmentParameterFlag.HandShowRingR, false ->
                visibleModels <- models.Remove(RingR)
            | EquipmentParameterFlag.HeadShowHairOverride, true ->
                usedFlags <- usedFlags.Remove(EquipmentParameterFlag.HeadHideHair)
                usedFlags <- usedFlags.Remove(EquipmentParameterFlag.HeadHideScalp)
            | EquipmentParameterFlag.HeadHideHair, true ->
                visibleModels <- models.Remove(Hair)
            | EquipmentParameterFlag.HeadShowNecklace, false ->
                visibleModels <- models.Remove(Necklace)
            | EquipmentParameterFlag.HeadShowEarrings, false ->
                visibleModels <- models.Remove(Earrings)
            | EquipmentParameterFlag.HeadShowEarViera, false ->
                visibleModels <- models.Remove(Ear)
            | _ -> ()
        return visibleModels
    }

let applyFlags (models: Map<EquipmentSlot, TTModel * IItemModel>) : Task<Map<EquipmentSlot, TTModel * IItemModel>> =
    task {
        for (model, item) in models.Values do
            let attributes = model.Attributes
            let flags = model.Flags
            printfn $"\n\n\n{model.Source} attributes:"
            for attr in attributes do
                printfn $"{attr}"
            printfn $" Model Flags: {flags}"
            for group in model.MeshGroups do
                printfn $"\n Parts for {group.Name}:"
                for part in group.Parts do
                    let partAttr = part.Attributes
                    let partName = part.Name
                    printfn $"{partName}:"
                    for attr in partAttr do
                        printfn $"Attribute: {attr}"

            printfn $"Primary Category: {item.PrimaryCategory} | Secondary Category: {item.SecondaryCategory} | Tertiary Category: {item.TertiaryCategory} | ID: {item.ModelInfo.PrimaryID}"

            if item.PrimaryCategory = "Gear" && item.ModelInfo.PrimaryID > 0 then

                let eqp = new Eqp()
            
                let! itemAttr = eqp.GetEqpEntry(item)
                let itemFlags = itemAttr.AvailableFlags
                let itemFlagValues = itemAttr.GetFlags()
                printfn "\n\n==================================================="
                printfn "Flags:"
                printfn "==================================================="
                for flag in itemFlagValues do
                    printfn $"{flag.Key}: {flag.Value}"
                    allFlags <- allFlags |> Map.add flag.Key flag.Value
            
                printfn "\n\n==================================================="
                printfn $"Relevant Flags:"
                printfn "==================================================="
                for flag in itemFlagValues do                
                    if flag.Key.ToString().Contains("Hide") && flag.Value then
                        printfn $"{flag.Key}"
                        usedFlags <- usedFlags |> Map.add flag.Key flag.Value
                    else if flag.Key.ToString().Contains("Show") && not flag.Value then
                        printfn $"{flag.Key}"
                        usedFlags <- usedFlags |> Map.add flag.Key flag.Value
        

        let! filteredFlags = filterFlagConflicts(usedFlags)
        usedFlags <- filteredFlags

        let! removedSlots = removeSlot filteredFlags models
       
        let! modifiedModels =
            if usedFlags.Count > 0 then
                removedSlots
                |> Map.toSeq
                |> Seq.map (fun (slot, (ttModel, item)) -> task {
                    let! edited = removeMeshParts usedFlags ttModel
                    return slot, (edited, item)
                })
                |> Task.WhenAll
            else
                removedSlots
                |> Map.toSeq
                |> Seq.map (fun (slot, (ttModel, item)) -> task {
                    return slot, (ttModel, item)
                })
                |> Task.WhenAll

        allFlags <- Map.empty
        usedFlags <- Map.empty
        return modifiedModels |> Map.ofArray
    }