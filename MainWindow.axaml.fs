namespace fs_mdl_viewer

open System
open System.Collections.ObjectModel
open System.IO
open System.Text.Json
open System.Numerics
open System.Threading.Tasks
open Avalonia
open Avalonia.Controls
open Avalonia.Markup.Xaml
open Avalonia.Media.Imaging
open AvaloniaRender.Veldrid
open xivModdingFramework.Cache
open xivModdingFramework.General.Enums
open xivModdingFramework.Items.Interfaces
open xivModdingFramework.Items.Categories
open xivModdingFramework.Items.DataContainers
open xivModdingFramework.Items.Enums
open xivModdingFramework.Models.FileTypes
open xivModdingFramework.Models.DataContainers
open xivModdingFramework.Models.ModelTextures
open xivModdingFramework.Mods
open xivModdingFramework.Materials.FileTypes
open xivModdingFramework.Materials.DataContainers
open xivModdingFramework.Exd.FileTypes
open xivModdingFramework.Exd.Enums
open xivModdingFramework.Textures.FileTypes
open xivModdingFramework.Textures.Enums
open xivModdingFramework.Textures.DataContainers

open Shared
open SharpToNumerics
open TTModelLoader

type MainWindow () as this = 
    inherit Window ()

    let viewModel = new VeldridWindowViewModel()
    
    do 
        let mutable characterRace   : XivRace = XivRace.Hyur_Midlander_Male

        let rec findCharacterPart (currentRaceInTree: XivRace) (partId: int) (partCategory: string) (characterItems: XivCharacter list): XivCharacter option =
                let tryFindWithId id =
                    characterItems
                    |> List.tryFind (fun item ->
                        item.TertiaryCategory = currentRaceInTree.GetDisplayName() &&
                        item.SecondaryCategory = partCategory &&
                        item.ModelInfo.SecondaryID = partId
                    )

                match tryFindWithId partId with
                | Some part -> Some part
                | None ->
                    match tryFindWithId 5 with
                    | Some alt -> Some alt
                    | None ->
                        let parentNode = XivRaceTree.GetNode(currentRaceInTree).Parent
                        if parentNode <> null && parentNode.Race <> XivRace.All_Races then
                            findCharacterPart parentNode.Race partId partCategory characterItems
                        else
                            None

        let hasModel (item: IItemModel) =
            try
                let model = Mdl.GetTTModel(item, characterRace)
                not model.IsFaulted
            with _ ->
                false

        let getCustomizableParts (targetRace: XivRace) (partCategory: string) (characterItems: XivCharacter list) : XivCharacter list =
            characterItems
            |> List.filter (fun item ->
                item.TertiaryCategory = targetRace.GetDisplayName() &&
                item.SecondaryCategory = partCategory &&
                hasModel item
            )
            |> List.sortBy (fun item -> item.ModelInfo.SecondaryID)

        let getDyeSwatches () =
            task {
                let! dyeDict = STM.GetDyeNames()
                let allDyes =
                    dyeDict
                    |> Seq.map (fun kvp ->kvp.Value)
                    |> Seq.toList
                return allDyes
            }
        // === Helper methods for applying dye (if that ever works) ===
        let halfToColor (h: SharpDX.Half[]) =
            if h.Length >= 3 then
                let color = Vector4(
                    SharpDX.Half.op_Implicit(h[0]),
                    SharpDX.Half.op_Implicit(h[1]),
                    SharpDX.Half.op_Implicit(h[2]),
                    255.0f
                )
                let sv = SharpDX.Vector4(color.X, color.Y, color.Z, color.W)
                SharpDX.Color(sv)
            else
                SharpDX.Color.Transparent

        let globalDefaultColors = ModelTexture.GetCustomColors()

        let applyDye (item: IItemModel) (race: XivRace) (slot: EquipmentSlot) (dye1: int option) (dye2: int option) (tx: ModTransaction) =
            task {
                let! stainTemplate = STM.GetStainingTemplateFile(STM.EStainingTemplate.Dawntrail)
                let dyeColors = ModelTexture.GetCustomColors()
                let! ttModel = loadTTModel item race slot
                for mat in ttModel.Materials do
                    let! material = resolveMtrl ttModel mat item tx
                    match material.ColorSetDyeData.Length with
                    | l when l >= 128 ->
                        for i in 0 .. 15 do
                            let offset = i * 4
                            let b0 = material.ColorSetDyeData[offset]
                            let b2 = material.ColorSetDyeData[offset + 2]
                            let b3 = material.ColorSetDyeData[offset + 3]

                            if b0 > 0uy then
                                let channel = if b3 < 8uy then 1 elif b3 >= 8uy then 2 else 0
                                match channel with
                                | 1 when dye1.IsSome ->
                                    let dyeId = dye1.Value
                                    let templateNumber = uint16 b2 ||| (uint16 b3 <<< 8)
                                    let dyeTemplate = templateNumber + 1000us
                                    let color = stainTemplate.GetTemplate(dyeTemplate)
                                    let diffuse = color.GetDiffuseData(dyeId)
                                    let finalColor = halfToColor diffuse
                                    dyeColors.DyeColors[i] <- finalColor
                                | 2 when dye2.IsSome ->
                                    let dyeId = dye2.Value
                                    let templateNumber = uint16 b2 ||| (uint16 b3 <<< 8)
                                    let dyeTemplate = templateNumber + 1000us
                                    let color = stainTemplate.GetTemplate(dyeTemplate)
                                    let diffuse = color.GetDiffuseData(dyeId)
                                    let finalColor = halfToColor diffuse
                                    dyeColors.DyeColors[i] <- finalColor
                                | _ -> ()
                    | l when l = 32 ->
                        if dye1.IsSome then
                            for i in 0 .. 15 do
                                let b0 = material.ColorSetDyeData[i * 4]
                                let b2 = material.ColorSetDyeData[i * 4 + 2]
                                let b3 = material.ColorSetDyeData[i * 4 + 3]
                                if b0 > 0uy then
                                    let dyeId = dye1.Value
                                    let templateNumber = uint16 b2 ||| (uint16 b3 <<< 8)
                                    let dyeTemplate = templateNumber + 1000us
                                    let color = stainTemplate.GetTemplate(dyeTemplate)
                                    let diffuse = color.GetDiffuseData(dyeId)
                                    let finalColor = halfToColor diffuse
                                    dyeColors.DyeColors[i] <- finalColor
                    | _ -> ()
                return dyeColors

            }

        this.InitializeComponent()
        let viewer = this.FindControl<EmbeddedWindowVeldrid>("ViewerControl")
        let overlay = this.FindControl<Border>("InputOverlay")
        

        viewer.DataContext <- viewModel

        viewer.GetObservable(Control.BoundsProperty)
            .Subscribe(fun bounds ->
                let w = uint32 bounds.Width
                let h = uint32 bounds.Height
                match viewModel :> IVeldridWindowModel with
                | vm ->
                    match vm.Render with
                    | :? VeldridView as render ->
                        render.RequestResize(w, h)
                    | _ -> ()
                | _ -> ()
            )
        |> ignore
        
        async {
            match viewModel :> IVeldridWindowModel with
            | vm ->
                match vm.Render with
                | :? VeldridView as render ->
                    render.AttachInputHandlers(overlay)

                    let mutable race        : string option     = None
                    let mutable clan        : string option     = None
                    let mutable gender      : string option     = None
                    let mutable charRace    : string option     = None
                    let mutable finalRace   : XivRace option    = None
                    let mutable hairs       : XivCharacter list = List.Empty
                    let mutable faces       : XivCharacter list = List.Empty
                    let mutable ears        : XivCharacter list = List.Empty
                    let mutable tails       : XivCharacter list = List.Empty

                    let! gear = render.GetEquipment()
                    let! chara = render.GetChara()
                    let eqpCategory = Eqp()
                    let tx = ModTransaction.BeginReadonlyTransaction()

                    let enableDyeSlots (item: IItemModel, slot: EquipmentSlot, box1: ComboBox, box2: ComboBox, dyes: string list, tx: ModTransaction)=
                        task {
                            let mutable dyeChannel1 = false
                            let mutable dyeChannel2 = false

                            // === Simplified race conversion stuff, in final app move to separate module ===
                            let! dyeModel =
                                let loadModel (item: IItemModel) (race: XivRace) =
                                    task {
                                        let! model = Mdl.GetTTModel(item, race)
                                        return model
                                    }
                                async {
                                    let rec resolveModelRace (item: IItemModel, race: XivRace, slot: EquipmentSlot, races: XivRace list) : Async<XivRace> =
                                        let rec tryResolveRace (slot: string) (races: XivRace list) (originalRace: XivRace) (eqdp: Collections.Generic.Dictionary<XivRace, xivModdingFramework.Models.DataContainers.EquipmentDeformationParameter>) : Async<XivRace> =
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
                                            | EquipmentSlot.Body -> "top"
                                            | EquipmentSlot.Head -> "met"
                                            | EquipmentSlot.Hands -> "glv"
                                            | EquipmentSlot.Legs -> "dwn"
                                            | EquipmentSlot.Feet -> "sho"
                                            | _ -> ""

                                        async {
                                            let! eqdp = eqpCategory.GetEquipmentDeformationParameters(item.ModelInfo.SecondaryID, searchSlot, false, false, false, tx) |> Async.AwaitTask
                                            return! tryResolveRace searchSlot races race eqdp
                                        }
                                    let priorityList = XivRaces.GetModelPriorityList(characterRace) |> Seq.toList
                                    let! resolvedRace = resolveModelRace(item, characterRace, slot, priorityList)

                                    let rec racialFallbacks (item: IItemModel) (races: XivRace list) (targetRace: XivRace): Async<TTModel> =
                                        async {
                                            match races with
                                            | [] ->
                                                printfn "All races failed, this sucks..."
                                                return raise (exn "Failed to load any model, rage quitting.")
                                            | race::rest ->
                                                try
                                                    return! loadModel item race |> Async.AwaitTask
                                                with ex ->
                                                    printfn $"Fallback failed for {race}: {ex.Message}"
                                                    return! racialFallbacks item rest race
                                        }
                                    try
                                        let! result = loadModel item resolvedRace |> Async.AwaitTask
                                        if obj.ReferenceEquals(result, null) then
                                            printfn $"[Dye Selectors] loadModel returned null for resolved race {resolvedRace}"
                                            //return! racialFallbacks item priorityList resolvedRace
                                            try
                                                let! fallback = racialFallbacks item priorityList resolvedRace
                                                if obj.ReferenceEquals(fallback, null) then
                                                    printfn $"[Dye Selectors] Attempts at fallbacks failed, using Hyur Midlander Male"
                                                    return! loadModel item XivRace.Hyur_Midlander_Male |> Async.AwaitTask
                                                else
                                                    return fallback
                                            with ex ->
                                                printfn $"[Dye Selectors] This is still erroring, trying with HMM"
                                                return! loadModel item XivRace.Hyur_Midlander_Male |> Async.AwaitTask
                                        else
                                            return result
                                    with ex ->
                                        printfn $"[Dye Selectors] Exception during model load: {ex.Message}"
                                        return! loadModel item XivRace.Hyur_Midlander_Male |> Async.AwaitTask
                                }

                            for mat in dyeModel.Materials do
                                let! materialPath =
                                    task{
                                        try                                        
                                            let! loaded = Mtrl.GetXivMtrl(mat, item, false, tx)
                                            printfn "[Dye Selectors] Material Path loaded"
                                            return loaded.MTRLPath
                                        with
                                        | _ ->
                                            printfn "[Dye Selectors] Material Path loaded, alternate logic used"
                                            return Mtrl.GetMtrlPath(dyeModel.Source, mat)
                                    }
                                let! material =
                                    try
                                        Mtrl.GetXivMtrl(materialPath, true, tx)
                                    with ex ->
                                        printfn $"[Dye Selectors] Failed to load material for dye selection: {ex.Message}"
                                        raise ex

                                match material.ColorSetDyeData.Length with
                                | l when l >= 128 ->
                                    printfn "Dawntrail gear found"
                                    for i in 0 .. 15 do
                                        let offset = i * 4
                                        let b0 = material.ColorSetDyeData[offset]
                                        let b3 = material.ColorSetDyeData[offset + 3]

                                        if b0 > 0uy then
                                            if b3 < 8uy then
                                                dyeChannel1 <- true
                                            elif b3 >= 8uy then
                                                dyeChannel2 <- true
                                | l when l = 32 ->
                                    printfn "Endwalker gear found"
                                    for i in 0 .. 15 do
                                        let offset = i * 4
                                        let b0 = material.ColorSetDyeData[offset]
                                        if b0 > 0uy then dyeChannel1 <- true
                                | _ -> ()

                            if not dyeChannel1 then box1.ItemsSource <- [] else box1.ItemsSource <- dyes
                            if not dyeChannel2 then box2.ItemsSource <- [] else box2.ItemsSource <- dyes
                            return (dyeChannel1, dyeChannel2)
                        }
                    
                    let raceOptions = [
                        { Display = "Hyur"; Value = "Hyur"}
                        { Display = "Elezen"; Value = "Elezen"}
                        { Display = "Lalafell"; Value = "Lalafell"}
                        { Display = "Miqo'te"; Value = "Miqote"}
                        { Display = "Roegadyn"; Value = "Roegadyn"}
                        { Display = "Au Ra"; Value = "AuRa"}
                        { Display = "Hrothgar"; Value = "Hrothgar"}
                        { Display = "Viera"; Value = "Viera"}
                    ]
                    let genderOptions = ["Male"; "Female"]
                    let clanOptions = ["Midlander"; "Highlander"]

                    let headGear = gear |> List.filter (fun m -> m.SecondaryCategory = "Head")
                    let bodyGear = gear |> List.filter (fun m -> m.SecondaryCategory = "Body")
                    let handGear = gear |> List.filter (fun m -> m.SecondaryCategory = "Hands")
                    let legsGear = gear |> List.filter (fun m -> m.SecondaryCategory = "Legs")
                    let feetGear = gear |> List.filter (fun m -> m.SecondaryCategory = "Feet")

                    let headNames = headGear |> List.map (fun m -> m.Name)
                    let bodyNames = bodyGear |> List.map (fun m -> m.Name)
                    let handNames = handGear |> List.map (fun m -> m.Name)
                    let legsNames = legsGear |> List.map (fun m -> m.Name)
                    let feetNames = feetGear |> List.map (fun m -> m.Name)

                    let! dyeList = getDyeSwatches() |> Async.AwaitTask
                    

                    let clearSelection (slot: ComboBox)  (gearList: XivGear list) =
                        let index =
                            let mutable index: int = -1
                            gearList
                            |> List.tryFind( fun g -> g.Name.Contains("SmallClothes"))
                            |> Option.iter (fun sc ->
                                let idx = gearList |> List.findIndex (fun g -> g = sc)
                                index <- idx
                                )
                            index
                               
                        if index >= 0 then
                            slot.SelectedIndex <- index
                        else
                            render.ClearGearSlot EquipmentSlot.Head
                            slot.SelectedIndex <- -1


                    // === Character Selection Boxes ===
                    let raceSelector = this.FindControl<ComboBox>("RaceSelector")
                    raceSelector.ItemsSource <- raceOptions
                    
                    let clanSelector = this.FindControl<ComboBox>("ClanSelector")
                    clanSelector.ItemsSource <- clanOptions
                    clanSelector.IsEnabled <- false

                    let genderSelector = this.FindControl<ComboBox>("GenderSelector")
                    genderSelector.ItemsSource <- genderOptions

                    let submitCharacter = this.FindControl<Button>("SubmitCharacter")
                    submitCharacter.IsEnabled <- false

                    // === Character Customization Boxes ===
                    let hairSelector = this.FindControl<ComboBox>("HairSelector")
                    hairSelector.IsEnabled <- false

                    let faceSelector = this.FindControl<ComboBox>("FaceSelector")
                    faceSelector.IsEnabled <- false

                    let earSelector = this.FindControl<ComboBox>("EarSelector")
                    earSelector.IsEnabled <- false

                    let tailSelector = this.FindControl<ComboBox>("TailSelector")
                    tailSelector.IsEnabled <- false


                    // === Gear Selection Boxes ===
                    let headSlot = this.FindControl<ComboBox>("HeadSlot")
                    let headClear = this.FindControl<Button>("HeadClear")
                    headClear.Click.Add(fun _ -> clearSelection headSlot headGear)
                    headSlot.ItemsSource <- headNames
                    let headDye1 = this.FindControl<ComboBox>("HeadDye1")
                    let headDye2 = this.FindControl<ComboBox>("HeadDye2")
                    headDye1.IsEnabled <- false
                    headDye2.IsEnabled <- false

                    let bodySlot = this.FindControl<ComboBox>("BodySlot")
                    let bodyClear = this.FindControl<Button>("BodyClear")
                    bodyClear.Click.Add(fun _ -> clearSelection bodySlot bodyGear)
                    bodySlot.ItemsSource <- bodyNames
                    let bodyDye1 = this.FindControl<ComboBox>("BodyDye1")
                    let bodyDye2 = this.FindControl<ComboBox>("BodyDye2")
                    bodyDye1.IsEnabled <- false
                    bodyDye2.IsEnabled <- false


                    let handSlot = this.FindControl<ComboBox>("HandSlot")
                    let handClear = this.FindControl<Button>("HandClear")
                    handClear.Click.Add(fun _ -> clearSelection handSlot handGear)
                    handSlot.ItemsSource <- handNames
                    let handDye1 = this.FindControl<ComboBox>("HandDye1")
                    let handDye2 = this.FindControl<ComboBox>("HandDye2")
                    handDye1.IsEnabled <- false
                    handDye2.IsEnabled <- false


                    let legsSlot = this.FindControl<ComboBox>("LegsSlot")
                    let legClear = this.FindControl<Button>("LegClear")
                    legClear.Click.Add(fun _ -> clearSelection legsSlot legsGear)
                    legsSlot.ItemsSource <- legsNames
                    let legDye1 = this.FindControl<ComboBox>("LegDye1")
                    let legDye2 = this.FindControl<ComboBox>("LegDye2")
                    legDye1.IsEnabled <- false
                    legDye2.IsEnabled <- false

                    let feetSlot = this.FindControl<ComboBox>("FeetSlot")
                    let feetClear = this.FindControl<Button>("FeetClear")
                    feetClear.Click.Add(fun _ -> clearSelection feetSlot feetGear)
                    feetSlot.ItemsSource <- feetNames
                    let feetDye1 = this.FindControl<ComboBox>("FeetDye1")
                    let feetDye2 = this.FindControl<ComboBox>("FeetDye2")
                    feetDye1.IsEnabled <- false
                    feetDye2.IsEnabled <- false

                    // === ComboBox Selection Methods ===
                        
                    // === Character Selectors

                    raceSelector.SelectionChanged.Add(fun _ ->
                        match raceSelector.SelectedValue with
                        | :? ComboOption as selected ->
                            race <- Some selected.Value
                            if selected.Value = "Hyur" then
                                clanSelector.IsEnabled <- true
                                if race.IsSome && clan.IsSome && gender.IsSome then
                                    submitCharacter.IsEnabled <- true
                                else
                                    submitCharacter.IsEnabled <- false
                            else
                                clanSelector.Clear()
                                clan <- None
                                clanSelector.IsEnabled <- false
                                if race.IsSome && gender.IsSome then
                                    submitCharacter.IsEnabled <- true
                                else
                                    submitCharacter.IsEnabled <- false
                        | _ -> ()
                    )

                    clanSelector.SelectionChanged.Add(fun _ ->
                        let clanSelection = clanSelector.SelectedValue :?> string
                        clan <- Some clanSelection
                        if race.IsSome && clan.IsSome && gender.IsSome then
                            submitCharacter.IsEnabled <- true
                        else
                            submitCharacter.IsEnabled <- false
                    )

                    genderSelector.SelectionChanged.Add(fun _ ->
                        let genderSelection = genderSelector.SelectedValue :?> string
                        gender <- Some genderSelection
                        if clanSelector.IsEnabled then
                            if race.IsSome && clan.IsSome && gender.IsSome then
                                submitCharacter.IsEnabled <- true
                            else
                                submitCharacter.IsEnabled <- false
                        else
                            if race.IsSome && gender.IsSome then
                                submitCharacter.IsEnabled <- true
                            else
                                submitCharacter.IsEnabled <- false
                    )

                    hairSelector.SelectionChanged.Add(fun _ ->
                        let idx = hairSelector.SelectedIndex
                        if idx >= 0 && idx < hairs.Length then
                            let entry = hairs[idx]
                            do render.AssignTrigger(Shared.EquipmentSlot.Hair, entry, characterRace, -1, -1) |> ignore
                    )

                    faceSelector.SelectionChanged.Add(fun _ ->
                        let idx = faceSelector.SelectedIndex
                        if idx >= 0 && idx < faces.Length then
                            let entry = faces[idx]
                            do render.AssignTrigger(Shared.EquipmentSlot.Face, entry, characterRace, -1, -1) |> ignore
                    )

                    earSelector.SelectionChanged.Add(fun _ ->
                        let idx = earSelector.SelectedIndex
                        if idx >= 0 && idx < ears.Length then
                            let entry = ears[idx]
                            
                            do render.AssignTrigger(Shared.EquipmentSlot.Ear, entry, characterRace, -1, -1) |> ignore
                    )

                    tailSelector.SelectionChanged.Add(fun _ ->
                        let idx = tailSelector.SelectedIndex
                        if idx >= 0 && idx < tails.Length then
                            let entry = tails[idx]
                            do render.AssignTrigger(Shared.EquipmentSlot.Tail, entry, characterRace, -1, -1) |> ignore
                    )

                    // === Gear Selectors ===

                    // === Head Slots ===
                    headSlot.SelectionChanged.Add(fun _ ->
                        headDye1.SelectedIndex <- -1
                        headDye2.SelectedIndex <- -1
                        let idx = headSlot.SelectedIndex
                        if idx >= 0 && idx < headGear.Length then
                            let entry = headGear[idx]
                            do
                                Async.StartImmediate(async {
                                   let! (slot1, slot2) = enableDyeSlots(entry,EquipmentSlot.Head , headDye1, headDye2, dyeList, tx) |> Async.AwaitTask
                                   headDye1.IsEnabled <- slot1
                                   headDye2.IsEnabled <- slot2
                                 })
                            do render.AssignTrigger(Shared.EquipmentSlot.Head, entry, characterRace, -1, -1) |> ignore
                    )

                    headDye1.SelectionChanged.Add(fun _ ->
                        let entryIdx = headSlot.SelectedIndex
                        if entryIdx >= 0 && entryIdx < headGear.Length then
                            let entry = headGear[entryIdx]
                            if headDye1.SelectedIndex > -1 then
                                let dye1Idx = headDye1.SelectedIndex + 1
                                let dye2Idx = 
                                    if headDye2.IsEnabled && headDye2.SelectedIndex >= 0 then
                                        headDye2.SelectedIndex + 1
                                    else
                                        -1
                                if dye1Idx >= 0 && dye1Idx < dyeList.Length then
                                    do render.AssignTrigger(EquipmentSlot.Head, entry, characterRace, dye1Idx, dye2Idx) |> ignore
                    )

                    headDye2.SelectionChanged.Add(fun _ ->
                        let entryIdx = headSlot.SelectedIndex
                        if entryIdx >= 0 && entryIdx < headGear.Length then
                            let entry = headGear[entryIdx]
                            if headDye2.SelectedIndex > -1 then
                                let dye2Idx = headDye2.SelectedIndex + 1
                                let dye1Idx =
                                    if headDye1.IsEnabled && headDye1.SelectedIndex >= 0 then
                                        headDye1.SelectedIndex + 1
                                    else
                                        -1
                                if dye2Idx >= 0 && dye2Idx < dyeList.Length then
                                    do render.AssignTrigger(EquipmentSlot.Head, entry, characterRace, dye1Idx, dye2Idx) |> ignore
                    )

                    // === Body Slots ===
                    bodySlot.SelectionChanged.Add(fun _ ->
                        bodyDye1.SelectedIndex <- -1
                        bodyDye2.SelectedIndex <- -1
                        let idx = bodySlot.SelectedIndex
                        if idx >= 0 && idx < bodyGear.Length then
                            let entry = bodyGear[idx]
                            do
                                Async.StartImmediate(async {
                                    let! (slot1, slot2) = enableDyeSlots(entry, EquipmentSlot.Body, bodyDye1, bodyDye2, dyeList, tx) |> Async.AwaitTask
                                    bodyDye1.IsEnabled <- slot1
                                    bodyDye2.IsEnabled <- slot2
                                })
                            
                            do render.AssignTrigger(Shared.EquipmentSlot.Body, entry, characterRace, -1, -1) |> ignore
                    )

                    bodyDye1.SelectionChanged.Add(fun _ ->
                        let entryIdx = bodySlot.SelectedIndex
                        if entryIdx >= 0 && entryIdx < bodyGear.Length then
                            let entry = bodyGear[entryIdx]
                            if bodyDye1.SelectedIndex > -1 then
                                let dye1Idx = bodyDye1.SelectedIndex + 1
                                let dye2Idx = 
                                    if bodyDye2.IsEnabled && bodyDye2.SelectedIndex >= 0 then
                                        bodyDye2.SelectedIndex + 1
                                    else
                                        -1
                                if dye1Idx >= 0 && dye1Idx < dyeList.Length then
                                    do render.AssignTrigger(EquipmentSlot.Body, entry, characterRace, dye1Idx, dye2Idx) |> ignore
                    )

                    bodyDye2.SelectionChanged.Add(fun _ ->
                        let entryIdx = bodySlot.SelectedIndex
                        if entryIdx >= 0 && entryIdx < bodyGear.Length then
                            let entry = bodyGear[entryIdx]
                            if bodyDye2.SelectedIndex > -1 then
                                let dye2Idx = bodyDye2.SelectedIndex + 1
                                let dye1Idx =
                                    if bodyDye1.IsEnabled && bodyDye1.SelectedIndex >= 0 then
                                        bodyDye1.SelectedIndex + 1
                                    else
                                        -1
                                if dye2Idx >= 0 && dye2Idx < dyeList.Length then
                                    do render.AssignTrigger(EquipmentSlot.Body, entry, characterRace, dye1Idx, dye2Idx) |> ignore
                    )

                    // === Hand Slots ===
                    handSlot.SelectionChanged.Add(fun _ ->
                        handDye1.SelectedIndex <- -1
                        handDye2.SelectedIndex <- -1
                        let idx = handSlot.SelectedIndex
                        if idx >= 0 && idx < handGear.Length then
                            let entry = handGear[idx]
                            do
                                Async.StartImmediate(async {
                                    let! (slot1, slot2) = enableDyeSlots(entry, EquipmentSlot.Hands, handDye1, handDye2, dyeList, tx) |> Async.AwaitTask
                                    handDye1.IsEnabled <- slot1
                                    handDye2.IsEnabled <- slot2
                                })
                            do render.AssignTrigger(Shared.EquipmentSlot.Hands, entry, characterRace, -1, -1) |> ignore
                    )

                    handDye1.SelectionChanged.Add(fun _ ->
                        let entryIdx = handSlot.SelectedIndex
                        if entryIdx >= 0 && entryIdx < handGear.Length then
                            let entry = handGear[entryIdx]
                            if handDye1.SelectedIndex > -1 then
                                let dye1Idx = handDye1.SelectedIndex + 1
                                let dye2Idx = 
                                    if handDye2.IsEnabled && handDye2.SelectedIndex >= 0 then
                                        handDye2.SelectedIndex + 1
                                    else
                                        -1
                                if dye1Idx >= 0 && dye1Idx < dyeList.Length then
                                    do render.AssignTrigger(EquipmentSlot.Hands, entry, characterRace, dye1Idx, dye2Idx) |> ignore
                    )

                    handDye2.SelectionChanged.Add(fun _ ->
                        let entryIdx = handSlot.SelectedIndex
                        if entryIdx >= 0 && entryIdx < handGear.Length then
                            let entry = handGear[entryIdx]
                            if handDye2.SelectedIndex > -1 then
                                let dye2Idx = handDye2.SelectedIndex + 1
                                let dye1Idx =
                                    if handDye1.IsEnabled && handDye1.SelectedIndex >= 0 then
                                        handDye1.SelectedIndex + 1
                                    else
                                        -1
                                if dye2Idx >= 0 && dye2Idx < dyeList.Length then
                                    do render.AssignTrigger(EquipmentSlot.Hands, entry, characterRace, dye1Idx, dye2Idx) |> ignore
                    )

                    // === Leg Slots ===
                    legsSlot.SelectionChanged.Add(fun _ ->
                        legDye1.SelectedIndex <- -1
                        legDye2.SelectedIndex <- -1
                        let idx = legsSlot.SelectedIndex
                        if idx >= 0 && idx < legsGear.Length then
                            let entry = legsGear[idx]
                            do
                                Async.StartImmediate(async {
                                    let! (slot1, slot2) = enableDyeSlots(entry, EquipmentSlot.Legs, legDye1, legDye2, dyeList, tx) |> Async.AwaitTask
                                    legDye1.IsEnabled <- slot1
                                    legDye2.IsEnabled <- slot2
                                })
                            do render.AssignTrigger(Shared.EquipmentSlot.Legs, entry, characterRace, -1, -1) |> ignore
                    )

                    legDye1.SelectionChanged.Add(fun _ ->
                        let entryIdx = legsSlot.SelectedIndex
                        if entryIdx >= 0 && entryIdx < legsGear.Length then
                            let entry = legsGear[entryIdx]
                            if legDye1.SelectedIndex > -1 then
                                let dye1Idx = legDye1.SelectedIndex + 1
                                let dye2Idx = 
                                    if legDye2.IsEnabled && legDye2.SelectedIndex >= 0 then
                                        legDye2.SelectedIndex + 1
                                    else
                                        -1
                                if dye1Idx >= 0 && dye1Idx < dyeList.Length then
                                    do render.AssignTrigger(EquipmentSlot.Legs, entry, characterRace, dye1Idx, dye2Idx) |> ignore
                    )

                    legDye2.SelectionChanged.Add(fun _ ->
                        let entryIdx = legsSlot.SelectedIndex
                        if entryIdx >= 0 && entryIdx < legsGear.Length then
                            let entry = legsGear[entryIdx]
                            if legDye2.SelectedIndex > -1 then
                                let dye2Idx = legDye2.SelectedIndex + 1
                                let dye1Idx =
                                    if legDye1.IsEnabled && legDye1.SelectedIndex >= 0 then
                                        legDye1.SelectedIndex + 1
                                    else
                                        -1
                                if dye2Idx >= 0 && dye2Idx < dyeList.Length then
                                    do render.AssignTrigger(EquipmentSlot.Legs, entry, characterRace, dye1Idx, dye2Idx) |> ignore
                    )

                    // === Feet Slots ===
                    feetSlot.SelectionChanged.Add(fun _ ->
                        feetDye1.SelectedIndex <- -1
                        feetDye2.SelectedIndex <- -1
                        let idx = feetSlot.SelectedIndex
                        if idx >= 0 && idx < feetGear.Length then
                            let entry = feetGear[idx]
                            do
                                Async.StartImmediate(async {
                                    let! (slot1, slot2) = enableDyeSlots(entry, EquipmentSlot.Feet, feetDye1, feetDye2, dyeList, tx) |> Async.AwaitTask
                                    feetDye1.IsEnabled <- slot1
                                    feetDye2.IsEnabled <- slot2
                                })
                            do render.AssignTrigger(Shared.EquipmentSlot.Feet, entry, characterRace, -1, -1) |> ignore
                    )

                    feetDye1.SelectionChanged.Add(fun _ ->
                        let entryIdx = feetSlot.SelectedIndex
                        if entryIdx >= 0 && entryIdx < feetGear.Length then
                            let entry = feetGear[entryIdx]
                            if feetDye1.SelectedIndex > -1 then
                                let dye1Idx = feetDye1.SelectedIndex + 1
                                let dye2Idx = 
                                    if feetDye2.IsEnabled && feetDye2.SelectedIndex >= 0 then
                                        feetDye2.SelectedIndex + 1
                                    else
                                        -1
                                if dye1Idx >= 0 && dye1Idx < dyeList.Length then
                                    do render.AssignTrigger(EquipmentSlot.Feet, entry, characterRace, dye1Idx, dye2Idx) |> ignore
                    )

                    feetDye2.SelectionChanged.Add(fun _ ->
                        let entryIdx = feetSlot.SelectedIndex
                        if entryIdx >= 0 && entryIdx < feetGear.Length then
                            let entry = feetGear[entryIdx]
                            if feetDye2.SelectedIndex > -1 then
                                let dye2Idx = feetDye2.SelectedIndex + 1
                                let dye1Idx =
                                    if feetDye1.IsEnabled && feetDye1.SelectedIndex >= 0 then
                                        feetDye1.SelectedIndex + 1
                                    else
                                        -1
                                if dye2Idx >= 0 && dye2Idx < dyeList.Length then
                                    do render.AssignTrigger(EquipmentSlot.Feet, entry, characterRace, dye1Idx, dye2Idx) |> ignore
                    )

                    // === Submit Character Button ===

                    submitCharacter.Click.Add(fun _ ->
                        render.clearCharacter()
                        let raceValue =
                            if race.Value = "Hyur" then
                                $"{race.Value}_{clan.Value}_{gender.Value}"
                            else
                                $"{race.Value}_{gender.Value}"

                        match Enum.TryParse<XivRace>(raceValue) with
                        | true, parsedRace ->
                            Async.StartImmediate <|
                            async {
                                characterRace <- parsedRace
                                let availableBodyItems = getCustomizableParts parsedRace "Body" chara

                                let availableFaceItem = getCustomizableParts parsedRace "Face" chara
                                faces <- availableFaceItem

                                let availableHairs = getCustomizableParts parsedRace "Hair" chara
                                hairs <- availableHairs

                                let availableEars = getCustomizableParts parsedRace "Ear" chara
                                ears <- availableEars

                                let availableTails = getCustomizableParts parsedRace "Tail" chara
                                tails <- availableTails

                                let effectiveBodyItem = availableBodyItems |> List.tryHead
                                let effectiveFaceItem = availableFaceItem |> List.tryHead
                                let defaultHair = availableHairs |> List.tryHead
                                let defaultEar = availableEars |> List.tryHead
                                let defaultTail = availableTails |> List.tryHead

                                let hairList = availableHairs |> List.map (fun m -> m.Name)
                                let faceList = availableFaceItem |> List.map (fun m -> m.Name)
                                let earList = availableEars |> List.map (fun m -> m.Name)
                                let tailList = availableTails |> List.map (fun m -> m.Name)

                                
                                hairSelector.ItemsSource <- hairList
                                hairSelector.IsEnabled <- true

                                
                                faceSelector.ItemsSource <- faceList
                                faceSelector.IsEnabled <- true

                                
                                if availableEars.Length > 0 then
                                    earSelector.IsEnabled <- true
                                    earSelector.ItemsSource <- earList
                                else
                                    earSelector.IsEnabled <- false
                                    earSelector.ItemsSource <- []

                                
                                if availableTails.Length > 0 then
                                    tailSelector.IsEnabled <- true
                                    tailSelector.ItemsSource <- tailList
                                else
                                    tailSelector.IsEnabled <- false
                                    tailSelector.ItemsSource <- []

                                match effectiveBodyItem, effectiveFaceItem with
                                | Some body, Some face ->
                                    printfn $"Effective Body: {body.Name} (Model from {body.TertiaryCategory})"
                                    printfn $"Effective Face: {face.Name} (Model from {face.TertiaryCategory})"
                                    printfn $"Effective Path: {body.ModelInfo.PrimaryID:D4} {body.ModelInfo.SecondaryID:D4} {body.ModelInfo.ImcSubsetID:D4}"
                                    defaultHair |> Option.iter (fun h -> printfn $"Default Hair: {h.Name}")
                                    defaultEar |> Option.iter (fun e -> printfn $"Default Ear: {e.Name}")
                                    defaultTail |> Option.iter (fun t -> printfn $"Default Tail: {t.Name}")
                                | _ ->
                                    printfn $"Could not determine effective body or face for {parsedRace.GetDisplayName()}"

                                if faceSelector.SelectedIndex >= 0 then
                                    do! render.AssignTrigger(Shared.EquipmentSlot.Face, faces[faceSelector.SelectedIndex], parsedRace, -1, -1)
                                else
                                    do! render.AssignTrigger(Shared.EquipmentSlot.Face, effectiveFaceItem.Value, parsedRace, -1, -1)

                                if hairSelector.SelectedIndex >= 0 then
                                    do! render.AssignTrigger(Shared.EquipmentSlot.Hair, hairs[hairSelector.SelectedIndex], parsedRace, -1, -1)
                                else
                                    do! render.AssignTrigger(Shared.EquipmentSlot.Hair, defaultHair.Value, parsedRace, -1, -1)

                                match defaultEar with
                                | Some ear ->
                                    if earSelector.SelectedIndex >= 0 then
                                        do! render.AssignTrigger(Shared.EquipmentSlot.Ear, ears[earSelector.SelectedIndex], parsedRace, -1, -1)
                                    else
                                        do! render.AssignTrigger(Shared.EquipmentSlot.Ear, ear, parsedRace, -1, -1)
                                | None -> ()
                                match defaultTail with
                                |Some tail -> 
                                    if tailSelector.SelectedIndex >= 0 then
                                        do! render.AssignTrigger(Shared.EquipmentSlot.Tail, tails[tailSelector.SelectedIndex], parsedRace, -1, -1)
                                    else
                                        do! render.AssignTrigger(Shared.EquipmentSlot.Tail, tail, parsedRace, -1, -1)
                                | None -> ()

                                let baseCharacter (slot: ComboBox) (gearList: XivGear list) (nameFilter: string) =
                                    if slot.SelectedIndex = -1 then
                                        gearList
                                        |> List.tryFind( fun g -> g.Name.Contains(nameFilter))
                                        |> Option.iter (fun sc ->
                                            let idx = gearList |> List.findIndex (fun g -> g = sc)
                                            slot.SelectedIndex <- idx
                                            )

                                let reloadGear (slot: ComboBox) (dye1: ComboBox) (dye2: ComboBox) =
                                    let current = slot.SelectedIndex
                                    let currentDye1 = dye1.SelectedIndex
                                    let currentDye2 = dye2.SelectedIndex
                                    if current >= 0 then
                                        slot.SelectedIndex <- -1
                                        slot.SelectedIndex <- current
                                    if currentDye1 >= 0 then
                                        dye1.SelectedIndex <- -1
                                        dye1.SelectedIndex <- currentDye1
                                    if currentDye2 >= 0 then
                                        dye2.SelectedIndex <- -1
                                        dye2.SelectedIndex <- currentDye2



                                reloadGear headSlot headDye1 headDye2
                                reloadGear bodySlot bodyDye1 bodyDye2
                                reloadGear handSlot handDye1 handDye2
                                reloadGear legsSlot legDye1 legDye2
                                reloadGear feetSlot feetDye1 feetDye2

                                baseCharacter bodySlot bodyGear "SmallClothes"
                                baseCharacter handSlot handGear "SmallClothes"
                                baseCharacter legsSlot legsGear "SmallClothes"
                                baseCharacter feetSlot feetGear "SmallClothes"


                            }

                        | false, _ ->
                            printfn "Invalid race string. Could not parse into XivRace"

                    )

                | _ -> ()
            | _ -> ()
        } |> Async.StartImmediate
        

    member private this.InitializeComponent() =
#if DEBUG
        this.AttachDevTools()
#endif
        AvaloniaXamlLoader.Load(this)
