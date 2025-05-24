namespace fs_mdl_viewer

open System
open System.Collections.ObjectModel
open System.IO
open System.Text.Json
open Avalonia
open Avalonia.Controls
open Avalonia.Markup.Xaml
open AvaloniaRender.Veldrid
open xivModdingFramework.Cache
open xivModdingFramework.General.Enums
open xivModdingFramework.Items.Interfaces
open xivModdingFramework.Items.Categories
open xivModdingFramework.Items.DataContainers
open xivModdingFramework.Items.Enums
open xivModdingFramework.Models.FileTypes
open xivModdingFramework.Mods

open Shared

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

                    let bodySlot = this.FindControl<ComboBox>("BodySlot")
                    let bodyClear = this.FindControl<Button>("BodyClear")
                    bodyClear.Click.Add(fun _ -> clearSelection bodySlot bodyGear)
                    bodySlot.ItemsSource <- bodyNames

                    let handSlot = this.FindControl<ComboBox>("HandSlot")
                    let handClear = this.FindControl<Button>("HandClear")
                    handClear.Click.Add(fun _ -> clearSelection handSlot handGear)
                    handSlot.ItemsSource <- handNames

                    let legsSlot = this.FindControl<ComboBox>("LegsSlot")
                    let legClear = this.FindControl<Button>("LegClear")
                    legClear.Click.Add(fun _ -> clearSelection legsSlot legsGear)
                    legsSlot.ItemsSource <- legsNames

                    let feetSlot = this.FindControl<ComboBox>("FeetSlot")
                    let feetClear = this.FindControl<Button>("FeetClear")
                    feetClear.Click.Add(fun _ -> clearSelection feetSlot feetGear)
                    feetSlot.ItemsSource <- feetNames


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
                            do render.AssignTrigger(Shared.EquipmentSlot.Hair, entry, characterRace) |> ignore
                    )

                    faceSelector.SelectionChanged.Add(fun _ ->
                        let idx = faceSelector.SelectedIndex
                        if idx >= 0 && idx < faces.Length then
                            let entry = faces[idx]
                            do render.AssignTrigger(Shared.EquipmentSlot.Face, entry, characterRace) |> ignore
                    )

                    earSelector.SelectionChanged.Add(fun _ ->
                        let idx = earSelector.SelectedIndex
                        if idx >= 0 && idx < ears.Length then
                            let entry = ears[idx]
                            
                            do render.AssignTrigger(Shared.EquipmentSlot.Ear, entry, characterRace) |> ignore
                    )

                    tailSelector.SelectionChanged.Add(fun _ ->
                        let idx = tailSelector.SelectedIndex
                        if idx >= 0 && idx < tails.Length then
                            let entry = tails[idx]
                            do render.AssignTrigger(Shared.EquipmentSlot.Tail, entry, characterRace) |> ignore
                    )

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
                                    do! render.AssignTrigger(Shared.EquipmentSlot.Face, faces[faceSelector.SelectedIndex], parsedRace)
                                else
                                    do! render.AssignTrigger(Shared.EquipmentSlot.Face, effectiveFaceItem.Value, parsedRace)

                                if hairSelector.SelectedIndex >= 0 then
                                    do! render.AssignTrigger(Shared.EquipmentSlot.Hair, hairs[hairSelector.SelectedIndex], parsedRace)
                                else
                                    do! render.AssignTrigger(Shared.EquipmentSlot.Hair, defaultHair.Value, parsedRace)

                                match defaultEar with
                                | Some ear ->
                                    if earSelector.SelectedIndex >= 0 then
                                        do! render.AssignTrigger(Shared.EquipmentSlot.Ear, ears[earSelector.SelectedIndex], parsedRace)
                                    else
                                        do! render.AssignTrigger(Shared.EquipmentSlot.Ear, ear, parsedRace)
                                | None -> ()
                                match defaultTail with
                                |Some tail -> 
                                    if tailSelector.SelectedIndex >= 0 then
                                        do! render.AssignTrigger(Shared.EquipmentSlot.Tail, tails[tailSelector.SelectedIndex], parsedRace)
                                    else
                                        do! render.AssignTrigger(Shared.EquipmentSlot.Tail, tail, parsedRace)
                                | None -> ()

                                let baseCharacter (slot: ComboBox) (gearList: XivGear list) (nameFilter: string) =
                                    if slot.SelectedIndex = -1 then
                                        gearList
                                        |> List.tryFind( fun g -> g.Name.Contains(nameFilter))
                                        |> Option.iter (fun sc ->
                                            let idx = gearList |> List.findIndex (fun g -> g = sc)
                                            slot.SelectedIndex <- idx
                                            )

                                let reloadGear (slot: ComboBox) =
                                    let current = slot.SelectedIndex
                                    if current >= 0 then
                                        slot.SelectedIndex <- -1
                                        slot.SelectedIndex <- current
                                reloadGear headSlot
                                reloadGear bodySlot
                                reloadGear handSlot
                                reloadGear legsSlot
                                reloadGear feetSlot

                                baseCharacter bodySlot bodyGear "SmallClothes"
                                baseCharacter handSlot handGear "SmallClothes"
                                baseCharacter legsSlot legsGear "SmallClothes"
                                baseCharacter feetSlot feetGear "SmallClothes"


                            }

                        | false, _ ->
                            printfn "Invalid race string. Could not parse into XivRace"

                    )


                    headSlot.SelectionChanged.Add(fun _ ->
                        printfn "SelectionChanged event firing"
                        let idx = headSlot.SelectedIndex
                        if idx >= 0 && idx < headGear.Length then
                            let entry = headGear[idx]
                            let path = $"chara/equipment/e{entry.ModelInfo.PrimaryID:D4}/model/c0101e{entry.ModelInfo.PrimaryID:D4}_met.mdl"
                            do render.AssignTrigger(Shared.EquipmentSlot.Head, entry, characterRace) |> ignore
                    )

                    bodySlot.SelectionChanged.Add(fun _ ->
                        printfn "SelectionChanged event firing"
                        let idx = bodySlot.SelectedIndex
                        if idx >= 0 && idx < bodyGear.Length then
                            let entry = bodyGear[idx]
                            let path = $"chara/equipment/e{entry.ModelInfo.PrimaryID:D4}/model/c0101e{entry.ModelInfo.PrimaryID:D4}_top.mdl"
                            printfn $"Path: {path}"
                            do render.AssignTrigger(Shared.EquipmentSlot.Body, entry, characterRace) |> ignore
                    )

                    handSlot.SelectionChanged.Add(fun _ ->
                        printfn "SelectionChanged event firing"
                        let idx = handSlot.SelectedIndex
                        if idx >= 0 && idx < handGear.Length then
                            let entry = handGear[idx]
                            let path = $"chara/equipment/e{entry.ModelInfo.PrimaryID:D4}/model/c0101e{entry.ModelInfo.PrimaryID:D4}_glv.mdl"
                            do render.AssignTrigger(Shared.EquipmentSlot.Hands, entry, characterRace) |> ignore
                    )

                    legsSlot.SelectionChanged.Add(fun _ ->
                        printfn "SelectionChanged event firing"
                        let idx = legsSlot.SelectedIndex
                        if idx >= 0 && idx < legsGear.Length then
                            let entry = legsGear[idx]
                            let path = $"chara/equipment/e{entry.ModelInfo.PrimaryID:D4}/model/c0101e{entry.ModelInfo.PrimaryID:D4}_dwn.mdl"
                            do render.AssignTrigger(Shared.EquipmentSlot.Legs, entry, characterRace) |> ignore
                    )

                    feetSlot.SelectionChanged.Add(fun _ ->
                        printfn "SelectionChanged event firing"
                        let idx = feetSlot.SelectedIndex
                        if idx >= 0 && idx < feetGear.Length then
                            let entry = feetGear[idx]
                            let path = $"chara/equipment/e{entry.ModelInfo.PrimaryID:D4}/model/c0101e{entry.ModelInfo.PrimaryID:D4}_sho.mdl"
                            do render.AssignTrigger(Shared.EquipmentSlot.Feet, entry, characterRace) |> ignore
                    )

                    


                | _ -> ()
            | _ -> ()
        } |> Async.StartImmediate
        

    member private this.InitializeComponent() =
#if DEBUG
        this.AttachDevTools()
#endif
        AvaloniaXamlLoader.Load(this)
