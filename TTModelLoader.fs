module TTModelLoader

open Shared

open System.Collections.Generic
open System.Threading.Tasks
open System.Text.RegularExpressions

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
                    let! model = Mdl.GetTTModel(item, race)
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

let resolveMtrl (model: TTModel) (race: XivRace) (tribe: XivSubRace) (material: string) (item: IItemModel) (tx: ModTransaction) : Async<XivMtrl> =
    let raceCode = race.GetRaceCodeInt()
    let target = $"c{raceCode:D4}"
    let finalMat =
        task {
            let! materialPath =
                task {
                    try
                        let! loaded = Mtrl.GetXivMtrl(material, item, false, tx)
                        let skinBase = Regex.Replace(loaded.MTRLPath, @"c\d{4}", target, RegexOptions.IgnoreCase)
                        let xaelaTail = Regex.Replace(skinBase, "t00", "t01", RegexOptions.IgnoreCase)
                        let xaelaBody = Regex.Replace(xaelaTail, "b00", "b01", RegexOptions.IgnoreCase)
                        let xaelaBase = Regex.Replace(xaelaBody, "f00", "f01", RegexOptions.IgnoreCase)
                        if loaded.ShaderPack = ShaderHelpers.EShaderPack.Skin then
                            try
                                let! skinReturn = Mtrl.GetXivMtrl(skinBase, true, tx) |> Async.AwaitTask
                                match tribe with
                                | XivSubRace.AuRa_Xaela ->
                                    try
                                        let! xaelaReturn = Mtrl.GetXivMtrl(xaelaBase, true, tx) |> Async.AwaitTask
                                        return xaelaReturn.MTRLPath
                                    with _ ->
                                        return skinReturn.MTRLPath
                                | _ ->
                                    return skinReturn.MTRLPath
                            with _ ->
                                return loaded.MTRLPath
                        else return loaded.MTRLPath
                    with
                    | _ ->
                        let basePath = Mtrl.GetMtrlPath(model.Source, material)
                        let skinBase = Regex.Replace(basePath, @"c\d{4}", target, RegexOptions.IgnoreCase)
                        let xaelaTail = Regex.Replace(skinBase, "t00", "t01", RegexOptions.IgnoreCase)
                        let xaelaBody = Regex.Replace(xaelaTail, "b00", "b01", RegexOptions.IgnoreCase)
                        let xaelaBase = Regex.Replace(xaelaBody, "f00", "f01", RegexOptions.IgnoreCase)
                        try
                            let! skinReturn = Mtrl.GetXivMtrl(skinBase, true, tx) |> Async.AwaitTask
                            match tribe with
                            | XivSubRace.AuRa_Xaela ->
                                try
                                    let! xaelaReturn = Mtrl.GetXivMtrl(xaelaBase, true, tx) |> Async.AwaitTask
                                    return xaelaReturn.MTRLPath
                                with _ ->
                                    return skinReturn.MTRLPath
                            | _ ->
                                return skinReturn.MTRLPath
                        with _ ->
                            return basePath
                } |> Async.AwaitTask
            let! mat =
                try
                    Mtrl.GetXivMtrl(materialPath, true, tx) |> Async.AwaitTask
                with ex ->
                    raise ex
            return mat
        } |> Async.AwaitTask
    finalMat