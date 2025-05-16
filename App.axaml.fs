namespace fs_mdl_viewer

open Avalonia
open Avalonia.Controls.ApplicationLifetimes
open Avalonia.Controls
open System.IO
open System.Text.Json
open Shared
open System
open xivModdingFramework.Cache
open xivModdingFramework.General.Enums
open Avalonia.Markup.Xaml
open Avalonia.Platform

type App() =
    inherit Application()

    override this.Initialize() =
            AvaloniaXamlLoader.Load(this)

    override this.OnFrameworkInitializationCompleted() =
        let runAsyncSetup =
            async {
                let configPath =
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "fs-mdl-viewer", "config.json")

                let loadConfig () : Config option =
                    if File.Exists(configPath) then
                        let json = File.ReadAllText(configPath)
                        JsonSerializer.Deserialize<Config>(json) |> Some
                    else
                        None

                let saveConfig (cfg: Config) =
                    let dir = Path.GetDirectoryName(configPath)
                    if not (Directory.Exists(dir)) then Directory.CreateDirectory(dir) |> ignore
                    let json = JsonSerializer.Serialize(cfg)
                    File.WriteAllText(configPath, json)

                let showGamePathPrompt () : Async<string option> =
                    async {
                        let dummy = new Window()
                    
                        let dialog = OpenFolderDialog()
                        dialog.Title <- "Select FFXIV game folder"
                        let! result = dialog.ShowAsync(dummy) |> Async.AwaitTask
                        return if String.IsNullOrWhiteSpace(result) then None else Some result
                    }

                let showError (window: Window) (title: string) (message: string) =
                    let dialog = Window()
                    dialog.Title <- title
                    dialog.Width <- 400.0
                    dialog.Height <- 200.0

                    let text = TextBlock()
                    text.Text <- message
                    dialog.Content <- text
                    dialog.WindowStartupLocation <- WindowStartupLocation.CenterOwner
                    dialog.ShowDialog(window) |> Async.AwaitTask

                let! gamePath =
                    match loadConfig () with
                    | Some config -> async { return config.GamePath }
                    | None ->
                        async {
                            let! selected = showGamePathPrompt ()
                            match selected with
                            | Some path ->
                                saveConfig { GamePath = path }
                                return path
                            | None ->
                                Environment.Exit(1)
                                return ""
                        }

                let info = xivModdingFramework.GameInfo(DirectoryInfo(gamePath), XivLanguage.English)
                XivCache.SetGameInfo(info) |> ignore

                match this.ApplicationLifetime with
                | :? IClassicDesktopStyleApplicationLifetime as desktop ->
                        desktop.MainWindow <- MainWindow()
                        desktop.MainWindow.Show()
                | _ -> ()
            }
            
        Async.StartImmediate runAsyncSetup

        base.OnFrameworkInitializationCompleted()

        
