namespace fs_mdl_viewer

open Avalonia

open Velopack

module Program =

    [<EntryPoint>]
    let main argv =

        // -- Velopack: handle --install
        VelopackApp.Build().Run()

        AppBuilder
            .Configure<App>()
            .UsePlatformDetect()
            .LogToTrace()
            .StartWithClassicDesktopLifetime(argv)