namespace fs_mdl_viewer

open Avalonia
open Avalonia.Controls.ApplicationLifetimes
open Avalonia.Markup.Xaml

open System

open Velopack
open Velopack.Sources

type App() =
    inherit Application()

    override this.Initialize() =
        AvaloniaXamlLoader.Load(this)

    override this.OnFrameworkInitializationCompleted() =
        let asyncUpdateApp = async {
            // === Velopack Automatic Updates Section - Comment while in development, uncomment for release ===

            let mgr = UpdateManager(new GithubSource("https://github.com/Gildworks/Hydaelyn-Clothiers", String.Empty, true))
            let! newVer = mgr.CheckForUpdatesAsync() |> Async.AwaitTask
            if not (isNull newVer) then
                do! mgr.DownloadUpdatesAsync(newVer) |> Async.AwaitTask
                mgr.ApplyUpdatesAndRestart(newVer)

            // === End Velopack Automatic Updates Section ===

            match this.ApplicationLifetime with
            | :? IClassicDesktopStyleApplicationLifetime as desktop ->
                desktop.MainWindow <- MainWindow()
                desktop.MainWindow.Show()
            | _ -> ()
        }
        Async.StartImmediate asyncUpdateApp

        base.OnFrameworkInitializationCompleted()