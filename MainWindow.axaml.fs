namespace fs_mdl_viewer

open System
open Avalonia
open Avalonia.Controls
open Avalonia.Markup.Xaml
open AvaloniaRender.Veldrid


type MainWindow () as this = 
    inherit Window ()

    let viewModel = new VeldridWindowViewModel()            

    do 
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
        
        match viewModel :> IVeldridWindowModel with
        | vm ->
            match vm.Render with
            | :? VeldridView as render ->
                render.AttachInputHandlers(overlay)
            | _ -> ()
        | _ -> ()
        

    member private this.InitializeComponent() =
#if DEBUG
        this.AttachDevTools()
#endif
        AvaloniaXamlLoader.Load(this)

    
