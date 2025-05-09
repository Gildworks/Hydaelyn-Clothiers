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


type MainWindow () as this = 
    inherit Window ()

    let viewModel = new VeldridWindowViewModel(Some "chara/monster/m8299/obj/body/b0001/model/m8299b0001.mdl")    

    do 
        if not XivCache.Initialized then
            let gdp = @"F:\Games\SquareEnix\FINAL FANTASY XIV - A Realm Reborn\game\sqpack\ffxiv"
            let info = xivModdingFramework.GameInfo(DirectoryInfo(gdp), XivLanguage.English)
            XivCache.SetGameInfo(info) |> ignore

        this.InitializeComponent()
        let viewer = this.FindControl<EmbeddedWindowVeldrid>("ViewerControl")
        let overlay = this.FindControl<Border>("InputOverlay")        

        viewer.DataContext <- viewModel

        let defaultModel = "brave new Y'shtola"

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
                    let! minions = render.GetMinions()
                    let! gear = render.GetEquipment()

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
                    


                    let names = minions |> List.map (fun m -> m.Name)

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


                    headSlot.SelectionChanged.Add(fun _ ->
                        let idx = headSlot.SelectedIndex
                        if idx >= 0 && idx < headGear.Length then
                            let entry = headGear[idx]
                            let slot =
                                match entry.SecondaryCategory with
                                | "Head" -> "met"
                                | "Body" -> "top"
                                | "Hands" -> "glv"
                                | "Legs" -> "dwn"
                                | "Feet" -> "sho"
                                | _ -> ""

                            let path = $"chara/equipment/e{entry.ModelInfo.PrimaryID:D4}/model/c0101e{entry.ModelInfo.PrimaryID:D4}_{slot}.mdl"
                            if render.ModelCount > 0 then
                                render.ReplaceTrigger(0, path)
                                
                            else
                                render.AppendTrigger(path)
                                
                    )

                    bodySlot.SelectionChanged.Add(fun _ ->
                        let idx = bodySlot.SelectedIndex
                        if idx >= 0 && idx < bodyGear.Length then
                            let entry = bodyGear[idx]
                            let slot =
                                match entry.SecondaryCategory with
                                | "Head" -> "met"
                                | "Body" -> "top"
                                | "Hands" -> "glv"
                                | "Legs" -> "dwn"
                                | "Feet" -> "sho"
                                | _ -> ""

                            let path = $"chara/equipment/e{entry.ModelInfo.PrimaryID:D4}/model/c0101e{entry.ModelInfo.PrimaryID:D4}_{slot}.mdl"
                            if render.ModelCount > 1 then
                                render.ReplaceTrigger(1, path)
                                
                            else
                                render.AppendTrigger(path)
                                
                    )

                    handSlot.SelectionChanged.Add(fun _ ->
                        let idx = handSlot.SelectedIndex
                        if idx >= 0 && idx < handGear.Length then
                            let entry = handGear[idx]
                            let slot =
                                match entry.SecondaryCategory with
                                | "Head" -> "met"
                                | "Body" -> "top"
                                | "Hands" -> "glv"
                                | "Legs" -> "dwn"
                                | "Feet" -> "sho"
                                | _ -> ""

                            let path = $"chara/equipment/e{entry.ModelInfo.PrimaryID:D4}/model/c0101e{entry.ModelInfo.PrimaryID:D4}_{slot}.mdl"
                            if render.ModelCount > 2 then
                                render.ReplaceTrigger(2, path)
                                
                            else
                                render.AppendTrigger(path)
                                
                    )

                    legsSlot.SelectionChanged.Add(fun _ ->
                        let idx = legsSlot.SelectedIndex
                        if idx >= 0 && idx < legsGear.Length then
                            let entry = legsGear[idx]
                            let slot =
                                match entry.SecondaryCategory with
                                | "Head" -> "met"
                                | "Body" -> "top"
                                | "Hands" -> "glv"
                                | "Legs" -> "dwn"
                                | "Feet" -> "sho"
                                | _ -> ""

                            let path = $"chara/equipment/e{entry.ModelInfo.PrimaryID:D4}/model/c0101e{entry.ModelInfo.PrimaryID:D4}_{slot}.mdl"
                            if render.ModelCount > 3 then
                                render.ReplaceTrigger(3, path)
                                
                            else
                                render.AppendTrigger(path)
                                
                    )

                    feetSlot.SelectionChanged.Add(fun _ ->
                        let idx = feetSlot.SelectedIndex
                        if idx >= 0 && idx < feetGear.Length then
                            let entry = feetGear[idx]
                            let slot =
                                match entry.SecondaryCategory with
                                | "Head" -> "met"
                                | "Body" -> "top"
                                | "Hands" -> "glv"
                                | "Legs" -> "dwn"
                                | "Feet" -> "sho"
                                | _ -> ""

                            let path = $"chara/equipment/e{entry.ModelInfo.PrimaryID:D4}/model/c0101e{entry.ModelInfo.PrimaryID:D4}_{slot}.mdl"
                            if render.ModelCount > 4 then
                                render.ReplaceTrigger(4, path)
                                
                            else
                                render.AppendTrigger(path)
                                
                    )


                | _ -> ()
            | _ -> ()
        } |> Async.StartImmediate
        

    member private this.InitializeComponent() =
#if DEBUG
        this.AttachDevTools()
#endif
        AvaloniaXamlLoader.Load(this)

    
