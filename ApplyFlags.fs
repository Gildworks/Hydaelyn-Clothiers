module ApplyFlags

open System.Threading.Tasks
open System.Collections.Generic

open xivModdingFramework.Models.DataContainers
open xivModdingFramework.Models.FileTypes
open xivModdingFramework.Items.Interfaces

open Shared

//================================================ TO DO ================================================\\
//    - Load all flags for every mesh into list (custom flag type for list probably needed)              \\
//    - Assess flags for overrides/priorities, discarding unused/unneeded                                \\
//    - Create methods for each task defining behavior (which part(s) to hide, if present)               \\
//    - Create a helper task that takes a list of flags and applies the logic to them                    \\
//    - Output the modified models to either a new map or the current one, if possible                   \\
//    - Start testing!                                                                                   \\
//=======================================================================================================\\


let mutable allFlags    : Map<EquipmentParameterFlag, bool> = Map.empty
let mutable usedFlags   : Map<EquipmentParameterFlag, bool> = Map.empty

let filterFlagConflicts (flags: Map<EquipmentParameterFlag, bool>): Task<Map<EquipmentParameterFlag, bool>> =
    task {
        let filteredFlags = flags
        // Fill this section with all the rules for flag overrides
        return filteredFlags
    }

let removeMeshParts (flags: Map<EquipmentParameterFlag, bool>) (model: TTModel) : Task<TTModel> =
    task {
        for flag in flags do
            match (flag.Key, flag.Value) with
            | EquipmentParameterFlag.BodyHideWaist, true ->
                for group in model.MeshGroups do
                    group.Parts <-
                        group.Parts
                        |> List.ofSeq
                        |> List.filter (fun part -> not (part.Attributes.Contains("atr_kod")))
                        |> fun seq -> new List<TTMeshPart>(seq)
            // Continue for all other flags
            | EquipmentParameterFlag.BodyHideLongGloves, true ->
                for group in model.MeshGroups do
                    group.Parts <-
                        group.Parts
                        |> List.ofSeq
                        |> List.filter ( fun part -> not (part.Attributes.Contains("atr_arm")))
                        |> fun seq -> new List<TTMeshPart>(seq)
            | _ ->
                ()

        model.MeshGroups <-
            model.MeshGroups
            |> Seq.filter (fun group -> group.Parts.Count > 0)
            |> Seq.toList
            |> fun lst -> new List<TTMeshGroup>(lst)
        return model
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
        
        printfn "\n\n==================================================="
        printfn "All possible flags for all models:"
        printfn "==================================================="
        for flag in allFlags do
            printfn $"{flag.Key}: {flag.Value}"

        printfn "\n\n==================================================="
        printfn "All used flags for all models:"
        printfn "==================================================="
        for flag in usedFlags do
            printfn $"{flag.Key}: {flag.Value}"

        let! filteredFlags = filterFlagConflicts(usedFlags)
        usedFlags <- filteredFlags
        let! modifiedModels =
            models
            |> Map.toSeq
            |> Seq.map (fun (slot, (ttModel, item)) -> task {
                let! edited = removeMeshParts usedFlags ttModel
                return slot, (edited, item)
            })
            |> Task.WhenAll

        allFlags <- Map.empty
        usedFlags <- Map.empty
        return modifiedModels |> Map.ofArray
    }