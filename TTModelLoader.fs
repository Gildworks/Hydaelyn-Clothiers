module TTModelLoader

open Shared

open System.Collections.Generic
open System.Threading.Tasks

open Serilog

open xivModdingFramework.Items.Interfaces
open xivModdingFramework.General.Enums

open xivModdingFramework.Materials.DataContainers
open xivModdingFramework.Materials.FileTypes

open xivModdingFramework.Models.DataContainers
open xivModdingFramework.Models.FileTypes

open xivModdingFramework.Mods

let loadTTModel (item: IItemModel) (race: XivRace) (slot: EquipmentSlot) : Task<TTModel> =
    let resolvedModel =
        task {
            let eqp = new Eqp()
            let mutable modelRace : XivRace option = None
            let loadModel (item: IItemModel) (race: XivRace) =
                task {
                    let! model = 
                        try
                            Log.Information("Attempting to load model for {Item}", item.Name)
                            let slotAbbr =
                                match slot with
                                | EquipmentSlot.Body -> "top"
                                | EquipmentSlot.Head -> "met"
                                | EquipmentSlot.Hands -> "glv"
                                | EquipmentSlot.Legs -> "dwn"
                                | EquipmentSlot.Feet -> "sho"
                                | _ -> ""
                            let raceCode = race.GetRaceCode()
                            let mdlPath = $"chara/equipment/e{item.ModelInfo.PrimaryID:D4}/model/c{raceCode}e{item.ModelInfo.PrimaryID:D4}_{slotAbbr}.mdl"
                            Log.Information("Model path: {Path}", mdlPath)
                            Mdl.GetTTModel(mdlPath, true)
                        with ex ->
                            Log.Error("Failed to complete GetTTModel for {Item}: {Message}", item.Name, ex.Message)
                            //raise(ex)
                            try
                                try
                                    Log.Information("Attempting backup attempt at model loading")
                                    Mdl.GetTTModel(item, race)
                                with ex ->
                                    Log.Information("Attempt at backup model loading failed")
                                    raise ex
                            finally
                                Log.Information("Successfully loaded {Item}", item.Name)

                    let _ =
                        try
                            model.Source
                        with ex ->
                            Log.Error("Could not read model source for {Item}: {Message}", item.Name, ex.Message)
                            raise ex
                    return model
                }

            let! model =
                async {
                    let rec resolveModelRace (item: IItemModel, race: XivRace, slot: EquipmentSlot, races: XivRace list) : Async<XivRace> =
                        let rec tryResolveRace (slot: string) (races: XivRace list) (originalRace: XivRace) (eqdp: Dictionary<XivRace, EquipmentDeformationParameter>) =
                            async {
                                match races with
                                | [] ->
                                    return originalRace
                                | race::rest ->
                                    match eqdp.TryGetValue(race) with
                                    | true, param when param.HasModel ->
                                        return race
                                    | _ ->
                                        return! tryResolveRace slot rest originalRace eqdp
                            }
                        let searchSlot =
                            match slot with
                            | EquipmentSlot.Head -> "met"
                            | EquipmentSlot.Body -> "top"
                            | EquipmentSlot.Hands -> "glv"
                            | EquipmentSlot.Legs -> "dwn"
                            | EquipmentSlot.Feet -> "sho"
                            | _ -> ""

                        async {
                            let! eqdp = eqp.GetEquipmentDeformationParameters(item.ModelInfo.SecondaryID, searchSlot, false) |> Async.AwaitTask
                            return! tryResolveRace searchSlot races race eqdp
                        }
                    let priorityList = XivRaces.GetModelPriorityList(race) |> Seq.toList
                    let! resolvedRace = resolveModelRace(item, race, slot, priorityList)
                    modelRace <- Some resolvedRace

                    let rec racialFallbacks (item: IItemModel) (races: XivRace list) : Async<TTModel> =
                        async {
                            match races with
                            | [] ->
                                return raise (exn "Failed to load any model, rage quitting.")
                            | race::rest ->
                                try
                                    let! model = loadModel item race |> Async.AwaitTask
                                    if not (obj.ReferenceEquals(model, null)) then
                                        return model
                                    else
                                        return! racialFallbacks item rest
                                with ex ->
                                    return! racialFallbacks item rest
                        }
                    try
                        let! result = loadModel item race |> Async.AwaitTask
                        if obj.ReferenceEquals(result, null) then
                            try
                                let! fallback = racialFallbacks item priorityList
                                if obj.ReferenceEquals(fallback, null) then
                                    return! loadModel item XivRace.Hyur_Midlander_Male |> Async.AwaitTask
                                else return fallback
                            with ex ->
                                return raise ex
                        else
                            return result
                    with ex ->
                        return! racialFallbacks item priorityList
                }
            return model
        }
    resolvedModel

let resolveMtrl (model: TTModel) (material: string) (item: IItemModel) (tx: ModTransaction) : Async<XivMtrl> =
    let finalMat =
        task {
            let! materialPath =
                task {
                    try
                        let! loaded = Mtrl.GetXivMtrl(material, item, false, tx)
                        return loaded.MTRLPath
                    with
                    | _ ->
                        return Mtrl.GetMtrlPath(model.Source, material)
                } |> Async.AwaitTask
            let! mat =
                try
                    Mtrl.GetXivMtrl(materialPath, true, tx) |> Async.AwaitTask
                with ex ->
                    raise ex
            return mat
        } |> Async.AwaitTask
    finalMat