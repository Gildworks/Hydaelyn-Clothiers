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

        let getCustomizableParts (targetRace: XivRace) (partCategory: string) (characterItems: XivCharacter list) : XivCharacter list =
            characterItems
            |> List.filter (fun item ->
                item.TertiaryCategory = targetRace.GetDisplayName() &&
                item.SecondaryCategory = partCategory
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

                    let mutable race        : XivBaseRace option     = None
                    let mutable clan        : XivSubRace option     = None
                    let mutable gender      : XivGender option     = None
                    let mutable charRace    : string option     = None
                    let mutable finalRace   : XivRace option    = None

                    let! gear = render.GetEquipment()
                    let! chara = render.GetChara()
                    let eqpCategory = Eqp()
                    let tx = ModTransaction.BeginReadonlyTransaction()

                    
                    let baseRaces =
                        Enum.GetValues(typeof<XivBaseRace>)
                        |> Seq.cast<XivBaseRace>
                        |> Seq.toList

                    let clanMap =
                        Enum.GetValues(typeof<XivSubRace>)
                        |> Seq.cast<XivSubRace>
                        |> Seq.groupBy (fun sub ->
                            let name = sub.ToString()
                            Enum.Parse<XivBaseRace>(name.Split('_')[0])
                        )
                        |> Map.ofSeq
                    
                    let genderOptions =
                        Enum.GetValues(typeof<XivGender>)
                        |> Seq.cast<XivGender>
                        |> Seq.toList

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
                    raceSelector.ItemsSource <- baseRaces
                    
                    let clanSelector = this.FindControl<ComboBox>("ClanSelector")
                    clanSelector.IsEnabled <- false

                    let genderSelector = this.FindControl<ComboBox>("GenderSelector")
                    genderSelector.ItemsSource <- genderOptions

                    let submitCharacter = this.FindControl<Button>("SubmitCharacter")
                    submitCharacter.IsEnabled <- false

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
                        let baseRaceSelection = raceSelector.SelectedValue :?> XivBaseRace
                        race <- Some baseRaceSelection
                        match raceSelector.SelectedValue with
                        | :? XivBaseRace as selectedBase ->
                            match clanMap.TryFind(selectedBase) with
                            | Some subraces ->
                                clanSelector.ItemsSource <- subraces
                                clanSelector.IsEnabled <- true
                            | None ->
                                clanSelector.ItemsSource <- []
                                clanSelector.IsEnabled <- false
                        | _ -> ()

                    )

                    clanSelector.SelectionChanged.Add(fun _ ->
                        let clanSelection = clanSelector.SelectedValue :?> XivSubRace
                        clan <- Some clanSelection
                        if race.IsSome && clan.IsSome && gender.IsSome then
                            submitCharacter.IsEnabled <- true
                        else
                            submitCharacter.IsEnabled <- false
                    )

                    genderSelector.SelectionChanged.Add(fun _ ->
                        let genderSelection = genderSelector.SelectedValue :?> XivGender
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

                    submitCharacter.Click.Add(fun _ ->
                        render.clearCharacter()
                        let raceItem = raceSelector.SelectedItem :?> XivBaseRace
                        let subRaceItem = clanSelector.SelectedItem :?> XivSubRace
                        let genderItem = genderSelector.SelectedItem :?> XivGender

                        let finalRaceStr =
                            match raceItem with
                            | XivBaseRace.Hyur -> $"{subRaceItem.ToString()}_{genderItem.ToString()}"
                            | _ -> $"{raceItem.ToString()}_{genderItem.ToString()}"
                           

                        match Enum.TryParse<XivRace>(finalRaceStr) with
                        | true, parsedRace ->
                            Async.StartImmediate <|
                            async {
                                characterRace <- parsedRace
                                let availableBodyItems = getCustomizableParts parsedRace "Body" chara
                                let availableFaceItem = getCustomizableParts parsedRace "Face" chara

                                let availableHairs = getCustomizableParts parsedRace "Hair" chara
                                let availableEars = getCustomizableParts parsedRace "Ear" chara
                                let availableTails = getCustomizableParts parsedRace "Tail" chara

                                let effectiveBodyItem = availableBodyItems |> List.tryHead
                                let effectiveFaceItem = availableFaceItem |> List.tryHead
                                let defaultHair = availableHairs |> List.tryHead
                                let defaultEar = availableEars |> List.tryHead
                                let defaultTail = availableTails |> List.tryHead

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

                                do! render.AssignTrigger(Shared.EquipmentSlot.Face, effectiveFaceItem.Value, parsedRace)
                                do! render.AssignTrigger(Shared.EquipmentSlot.Hair, defaultHair.Value, parsedRace)
                                match defaultEar with
                                | Some ear -> do! render.AssignTrigger(Shared.EquipmentSlot.Ear, ear, parsedRace)
                                | None -> ()
                                match defaultTail with
                                |Some tail -> do! render.AssignTrigger(Shared.EquipmentSlot.Tail, tail, parsedRace)
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
