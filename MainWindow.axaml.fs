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
        let mutable characterRace   : XivRace option    = None

        let rec findCharacterPart (currentRaceInTree: XivRace) (partId: int) (partCategory: string) (characterItems: XivCharacter list): XivCharacter option =
                let foundPart =
                    characterItems
                    |> List.tryFind (fun item ->
                        item.TertiaryCategory = currentRaceInTree.GetDisplayName() &&
                        item.SecondaryCategory = partCategory &&
                        item.ModelInfo.SecondaryID = partId
                    )

                match foundPart with
                | Some part -> Some part
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

                    let mutable race        : string option     = None
                    let mutable clan        : string option     = None
                    let mutable gender      : string option     = None
                    let mutable charRace    : string option     = None
                    let mutable finalRace   : XivRace option    = None

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


                    let raceSelector = this.FindControl<ComboBox>("RaceSelector")
                    raceSelector.ItemsSource <- raceOptions
                    
                    let clanSelector = this.FindControl<ComboBox>("ClanSelector")
                    clanSelector.ItemsSource <- clanOptions
                    clanSelector.IsEnabled <- false

                    let genderSelector = this.FindControl<ComboBox>("GenderSelector")
                    genderSelector.ItemsSource <- genderOptions

                    let submitCharacter = this.FindControl<Button>("SubmitCharacter")
                    submitCharacter.IsEnabled <- false

                    let headSlot = this.FindControl<ComboBox>("HeadSlot")
                    headSlot.ItemsSource <- headNames

                    let bodySlot = this.FindControl<ComboBox>("BodySlot")
                    bodySlot.ItemsSource <- bodyNames

                    let handSlot = this.FindControl<ComboBox>("HandSlot")
                    handSlot.ItemsSource <- handNames

                    let legsSlot = this.FindControl<ComboBox>("LegsSlot")
                    legsSlot.ItemsSource <- legsNames

                    let feetSlot = this.FindControl<ComboBox>("FeetSlot")
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
                                characterRace <- Some parsedRace
                                let effectiveBodyItem = findCharacterPart parsedRace 1 "Body" chara
                                let effectiveFaceItem = findCharacterPart parsedRace 1 "Face" chara

                                let availableHairs = getCustomizableParts parsedRace "Hair" chara
                                let availableEars = getCustomizableParts parsedRace "Ear" chara
                                let availableTails = getCustomizableParts parsedRace "Tail" chara

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

                                baseCharacter bodySlot bodyGear "SmallClothes"
                                baseCharacter handSlot handGear "SmallClothes"
                                baseCharacter legsSlot legsGear "SmallClothes"
                                baseCharacter feetSlot feetGear "SmallClothes"

                                reloadGear headSlot
                                reloadGear bodySlot
                                reloadGear handSlot
                                reloadGear legsSlot
                                reloadGear feetSlot
                            }

                        | false, _ ->
                            printfn "Invalid race string. Could not parse into XivRace"

                    )


                    headSlot.SelectionChanged.Add(fun _ ->
                        let idx = headSlot.SelectedIndex
                        if idx >= 0 && idx < headGear.Length then
                            let entry = headGear[idx]
                            let path = $"chara/equipment/e{entry.ModelInfo.PrimaryID:D4}/model/c0101e{entry.ModelInfo.PrimaryID:D4}_met.mdl"
                            do render.AssignTrigger(Shared.EquipmentSlot.Head, entry, characterRace.Value) |> ignore
                    )

                    bodySlot.SelectionChanged.Add(fun _ ->
                        let idx = bodySlot.SelectedIndex
                        if idx >= 0 && idx < bodyGear.Length then
                            let entry = bodyGear[idx]
                            let path = $"chara/equipment/e{entry.ModelInfo.PrimaryID:D4}/model/c0101e{entry.ModelInfo.PrimaryID:D4}_top.mdl"
                            printfn $"Path: {path}"
                            do render.AssignTrigger(Shared.EquipmentSlot.Body, entry, characterRace.Value) |> ignore
                    )

                    handSlot.SelectionChanged.Add(fun _ ->
                        let idx = handSlot.SelectedIndex
                        if idx >= 0 && idx < handGear.Length then
                            let entry = handGear[idx]
                            let path = $"chara/equipment/e{entry.ModelInfo.PrimaryID:D4}/model/c0101e{entry.ModelInfo.PrimaryID:D4}_glv.mdl"
                            do render.AssignTrigger(Shared.EquipmentSlot.Hands, entry, characterRace.Value) |> ignore
                    )

                    legsSlot.SelectionChanged.Add(fun _ ->
                        let idx = legsSlot.SelectedIndex
                        if idx >= 0 && idx < legsGear.Length then
                            let entry = legsGear[idx]
                            let path = $"chara/equipment/e{entry.ModelInfo.PrimaryID:D4}/model/c0101e{entry.ModelInfo.PrimaryID:D4}_dwn.mdl"
                            do render.AssignTrigger(Shared.EquipmentSlot.Legs, entry, characterRace.Value) |> ignore
                    )

                    feetSlot.SelectionChanged.Add(fun _ ->
                        let idx = feetSlot.SelectedIndex
                        if idx >= 0 && idx < feetGear.Length then
                            let entry = feetGear[idx]
                            let path = $"chara/equipment/e{entry.ModelInfo.PrimaryID:D4}/model/c0101e{entry.ModelInfo.PrimaryID:D4}_sho.mdl"
                            do render.AssignTrigger(Shared.EquipmentSlot.Feet, entry, characterRace.Value) |> ignore
                    )

                    


                | _ -> ()
            | _ -> ()
        } |> Async.StartImmediate
        

    member private this.InitializeComponent() =
#if DEBUG
        this.AttachDevTools()
#endif
        AvaloniaXamlLoader.Load(this)
