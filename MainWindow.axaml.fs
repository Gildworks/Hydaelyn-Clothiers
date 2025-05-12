namespace fs_mdl_viewer

open System
open System.Collections.ObjectModel
open System.IO
open Avalonia
open Avalonia.Controls
open Avalonia.Markup.Xaml
open AvaloniaRender.Veldrid
open xivModdingFramework.Cache
open xivModdingFramework.General.Enums
open xivModdingFramework.Items.Categories
open xivModdingFramework.Items.DataContainers
open xivModdingFramework.Items.Enums

open Shared

type MainWindow () as this = 
    inherit Window ()

    let viewModel = new VeldridWindowViewModel()    

    do 
        if not XivCache.Initialized then
            let gdp = @"F:\Games\SquareEnix\FINAL FANTASY XIV - A Realm Reborn\game\sqpack\ffxiv"
            let info = xivModdingFramework.GameInfo(DirectoryInfo(gdp), XivLanguage.English)
            XivCache.SetGameInfo(info) |> ignore

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

                    //submitCharacter.Click.Add(fun _ ->
                    //    if race.Value = "Hyur" then
                    //        let raceValue = $"{race.Value}_{clan.Value}_{gender.Value}"
                    //        charRace <- Some raceValue
                    //    else
                    //        let raceValue = $"{race.Value}_{gender.Value}"
                    //        charRace <- Some raceValue

                    //    printfn $"Constructed XivRace: {charRace.Value}"
                    //    let parsedRace =
                    //        match Enum.TryParse<XivRace>(charRace.Value) with
                    //        | true, value -> Some value
                    //        | false, _ -> None

                    //    finalRace <- parsedRace
                    //    if finalRace.IsSome then
                    //        printfn "Successfully generated XivRace!"
                    //    else
                    //        printfn "No matching race found..."

                    //    async {
                    //        let chara = new Character()
                    //        let! charaList = chara.GetCharacterList() |> Async.AwaitTask

                    //        let getDefaultCharaPart (race: XivRace) (part: XivItemType) =
                    //            charaList
                    //            |> List.ofSeq
                    //            |> List.tryFind (fun x ->
                    //                x.Name = race.GetDisplayName() &&
                    //                x.SecondaryCategory = part.ToString() &&
                    //                x.ModelInfo.PrimaryID = 1
                    //            )

                    //        let defaultBody = getDefaultCharaPart finalRace.Value XivItemType.body
                    //        printfn $"Default Body Part: {defaultBody.Value.Name}"
                    //    } |> Async.StartImmediate
                    //)
                    submitCharacter.Click.Add(fun _ ->
                        let raceValue =
                            if race.Value = "Hyur" then
                                $"{race.Value}_{clan.Value}_{gender.Value}"
                            else
                                $"{race.Value}_{gender.Value}"

                        match Enum.TryParse<XivRace>(raceValue) with
                        | true, parsedRace ->
                            async {
                                let chara = Character()
                                let! charaList = chara.GetCharacterList() |> Async.AwaitTask

                                let getDefaultCharaPart (race: XivRace) (part: string) =
                                    let displayName = race.GetDisplayName()
                                    charaList
                                    |> Seq.tryFind (fun x ->
                                        x.Name = $"{displayName} {part} 1"
                                    )

                                match getDefaultCharaPart parsedRace "Hair" with
                                | Some defaultBody ->
                                    printfn $"Default Body Part: {defaultBody.Name}"
                                | None ->
                                    let distinctNames =
                                        charaList
                                        |> Seq.map (fun x -> x.Name)
                                        |> Seq.distinct
                                        |> String.concat", "

                                    let distinctCategories =
                                        charaList
                                        |> Seq.map (fun x -> x.SecondaryCategory)
                                        |> Seq.distinct
                                        |> String.concat", "

                                    printfn "Body logic failed."
                                    printfn "Diagnostics:"
                                    printfn $"Race Display Name: {parsedRace.GetDisplayName()}"
                                    printfn $"Passed name: {parsedRace.GetDisplayName()} Hair 1"
                                    //printfn $"Distinct Character Names: {distinctNames}"
                                    //printfn $"Distinct Parts: {distinctCategories}"
                            } |> Async.StartImmediate

                        | false, _ ->
                            printfn "Invalid race string. Could not parse into XivRace"
                    )


                    headSlot.SelectionChanged.Add(fun _ ->
                        let idx = headSlot.SelectedIndex
                        if idx >= 0 && idx < headGear.Length then
                            let entry = headGear[idx]
                            let path = $"chara/equipment/e{entry.ModelInfo.PrimaryID:D4}/model/c0101e{entry.ModelInfo.PrimaryID:D4}_met.mdl"
                            render.AssignTrigger(Shared.EquipmentSlot.Head, entry)
                    )

                    bodySlot.SelectionChanged.Add(fun _ ->
                        let idx = bodySlot.SelectedIndex
                        if idx >= 0 && idx < bodyGear.Length then
                            let entry = bodyGear[idx]
                            let path = $"chara/equipment/e{entry.ModelInfo.PrimaryID:D4}/model/c0101e{entry.ModelInfo.PrimaryID:D4}_top.mdl"
                            printfn $"Path: {path}"
                            render.AssignTrigger(Shared.EquipmentSlot.Body, entry)
                    )

                    handSlot.SelectionChanged.Add(fun _ ->
                        let idx = handSlot.SelectedIndex
                        if idx >= 0 && idx < handGear.Length then
                            let entry = handGear[idx]
                            let path = $"chara/equipment/e{entry.ModelInfo.PrimaryID:D4}/model/c0101e{entry.ModelInfo.PrimaryID:D4}_glv.mdl"
                            render.AssignTrigger(Shared.EquipmentSlot.Hands, entry)
                    )

                    legsSlot.SelectionChanged.Add(fun _ ->
                        let idx = legsSlot.SelectedIndex
                        if idx >= 0 && idx < legsGear.Length then
                            let entry = legsGear[idx]
                            let path = $"chara/equipment/e{entry.ModelInfo.PrimaryID:D4}/model/c0101e{entry.ModelInfo.PrimaryID:D4}_dwn.mdl"
                            render.AssignTrigger(Shared.EquipmentSlot.Legs, entry)
                    )

                    feetSlot.SelectionChanged.Add(fun _ ->
                        let idx = feetSlot.SelectedIndex
                        if idx >= 0 && idx < feetGear.Length then
                            let entry = feetGear[idx]
                            let path = $"chara/equipment/e{entry.ModelInfo.PrimaryID:D4}/model/c0101e{entry.ModelInfo.PrimaryID:D4}_sho.mdl"
                            render.AssignTrigger(Shared.EquipmentSlot.Feet, entry)
                    )


                | _ -> ()
            | _ -> ()
        } |> Async.StartImmediate
        

    member private this.InitializeComponent() =
#if DEBUG
        this.AttachDevTools()
#endif
        AvaloniaXamlLoader.Load(this)
