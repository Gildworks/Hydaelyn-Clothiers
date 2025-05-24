namespace fs_mdl_viewer

open Avalonia
open Avalonia.Controls.ApplicationLifetimes
open Avalonia.Controls

open Avalonia.Dialogs

open System
open System.IO
open System.Text.Json

open Velopack
open Velopack.Sources

open Shared
open System
open xivModdingFramework.Cache
open xivModdingFramework.General.Enums

open Avalonia.Markup.Xaml
open Avalonia.Platform
open Avalonia.Threading
open Avalonia.Layout

type App() =
    inherit Application()

    let showSimpleDialog (owner: Window) (title: string) (message: string) =
        let dlg = Window()
        dlg.Title <- title
        dlg.Width <- 300.0
        dlg.Height <- 150.0
        dlg.WindowStartupLocation <- WindowStartupLocation.CenterOwner
        dlg.CanResize <- false

        let panel = StackPanel(Orientation = Orientation.Vertical, Margin = Thickness(10.0))
        let txt = TextBlock(Text = message, Margin = Thickness(0.0, 0.0, 0.0, 10.0))
        let btn = Button(Content = "OK", HorizontalAlignment = HorizontalAlignment.Center)
        btn.Click.Add(fun _ -> dlg.Close())

        panel.Children.Add(txt) |> ignore
        panel.Children.Add(btn) |> ignore
        dlg.Content <- panel

        dlg.ShowDialog(owner) |> ignore

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

                // === This part is responsible for controlling automatic updates, will throw an error when running in debug, uncomment when building a release ===
                
                //let mgr = UpdateManager(new GithubSource("https://github.com/shandonb/hc-modelviewer", String.Empty, true))

                //let! newVer = mgr.CheckForUpdatesAsync() |> Async.AwaitTask
                //if not (isNull newVer) then
                //    do! mgr.DownloadUpdatesAsync(newVer) |> Async.AwaitTask
                //    mgr.ApplyUpdatesAndRestart(newVer)

                match this.ApplicationLifetime with
                | :? IClassicDesktopStyleApplicationLifetime as desktop ->
                        desktop.MainWindow <- MainWindow()
                        desktop.MainWindow.Show()
                | _ -> ()
            }
            
        Async.StartImmediate runAsyncSetup

        base.OnFrameworkInitializationCompleted()