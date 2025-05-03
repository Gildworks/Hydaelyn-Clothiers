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
                    let names = minions |> List.map (fun m -> m.Name)

                    let combo = this.FindControl<ComboBox>("Minions")
                    combo.ItemsSource <- names
                    printfn "Names added! %d" names.Length

                    match minions |> List.tryFind (fun m -> m.Name = defaultModel) with
                    | Some defaultEntry ->
                        render.InitialModelPath <- Some defaultEntry.MdlPath
                        combo.SelectedIndex <- names |> List.findIndex (fun n -> n = defaultModel)
                    | None ->
                        failwith "Default model not found!"

                    



                    combo.SelectionChanged.Add(fun _ ->
                        let idx = combo.SelectedIndex
                        if idx >= 0 && idx < minions.Length then
                            let entry = minions[idx]
                            render.ChangeModel(entry.MdlPath)
                    )
                | _ -> ()
            | _ -> ()
        } |> Async.StartImmediate
        

    member private this.InitializeComponent() =
#if DEBUG
        this.AttachDevTools()
#endif
        AvaloniaXamlLoader.Load(this)

    
