module ApplyFlags

open System.Threading.Tasks
open xivModdingFramework.Models.DataContainers

open Shared

//================================================ TO DO ================================================\\
//    - Load all flags for every mesh                                                                    \\
//    - Assess flags for overrides/priorities                                                            \\
//    - Create methods for each task defining behavior (which part(s) to hide, if present)               \\
//    - Create a helper task that takes a list of flags and applies the logic to them                    \\
//    - Output the modified models to either a new map or the current one, if possible                   \\
//    - Start testing!                                                                                   \\
//=======================================================================================================\\


let applyFlags (models: Map<EquipmentSlot, TTModel option>) : Task<Map<EquipmentSlot, TTModel option>> =
    task {
        printfn "Applying render flags to models..."
        return models
    }