namespace fs_mdl_viewer

open System
open Avalonia
open Avalonia.OpenGL
open Avalonia.OpenGL.Controls
open Avalonia.Controls.ApplicationLifetimes
open Avalonia.Reactive
open Lumina

module Program =

    [<EntryPoint>]
    let main argv =

        AppBuilder
            .Configure<App>()
            .UsePlatformDetect()
            .LogToTrace()
            .StartWithClassicDesktopLifetime(argv)