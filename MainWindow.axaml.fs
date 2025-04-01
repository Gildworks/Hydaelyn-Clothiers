namespace fs_mdl_viewer

open Avalonia
open Avalonia.Controls
open Avalonia.Markup.Xaml

type MainWindow () as this = 
    inherit Window ()

    do 
        this.InitializeComponent()
        let customControl = CustomVeldridControl.CustomVeldridControl()
        let host = this.FindControl<ContentControl>("HostHere")
        host.Content <- customControl

    member private this.InitializeComponent() =
#if DEBUG
        this.AttachDevTools()
#endif
        AvaloniaXamlLoader.Load(this)
