namespace fs_mdl_viewer

open Avalonia
open Avalonia.Controls.ApplicationLifetimes
open Avalonia.Svg.Skia

open Velopack

module Program =
    [<CompiledName "BuildAvaloniaApp">]
    let buildAvaloniaApp () =
        AppBuilder
            .Configure<App>()
            .UsePlatformDetect()
            .LogToTrace()
            .UseSkia()
    [<EntryPoint>]
    let main argv =

        // -- Velopack: handle --install
        VelopackApp.Build().Run()

        buildAvaloniaApp().StartWithClassicDesktopLifetime(argv)
        