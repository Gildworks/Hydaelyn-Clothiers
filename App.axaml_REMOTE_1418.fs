namespace fs_mdl_viewer

open Avalonia
open Avalonia.Controls.ApplicationLifetimes
open Avalonia.Markup.Xaml

open System
open System.IO
open System.Net.Http
open System.Text.Json

open Velopack
open Velopack.Sources

open Shared

type App() =
    inherit Application()

    let configPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Hydaelyn Clothiers", "config.json")

    let mutable releaseChannelURL: string option = None
    let mutable userAccessToken: string option = None

    override this.Initialize() =
        AvaloniaXamlLoader.Load(this)

    member this.loadConfig() : Config option =
        if File.Exists(configPath) then
            try JsonSerializer.Deserialize<Config>(File.ReadAllText(configPath)) |> Some
            with ex -> None
        else None

    member this.GetLicenseInfo() : Async<string option * string option> =
        async {
            try
                use client = new HttpClient()
                let memberId = 
                    match this.loadConfig() with
                    | Some config ->
                        match config.PatreonID with
                        | Some id -> Some id
                        | None -> None
                    | None -> None
                if memberId.IsSome then
                    let! response = client.GetAsync($"https://www.hydaelynclothiers.com/api/license-check?patreonId={memberId}") |> Async.AwaitTask

                    if response.IsSuccessStatusCode then
                        let! json = response.Content.ReadAsStringAsync() |> Async.AwaitTask
                        let result = JsonSerializer.Deserialize<{| releaseURL: string option; accessToken: string option |}>(json)

                        match result.accessToken with
                        | Some token ->
                            return result.releaseURL, result.accessToken
                        | None -> return None, None
                    else return None, None
                else return None, None
            with
            | ex ->
                return None, None
        }

    member this.GetReleaseChannelURL() =
        async {
            let! releaseUrl, accessToken = this.GetLicenseInfo()
            releaseChannelURL <- releaseUrl
            userAccessToken <- accessToken
        }
        
            

    override this.OnFrameworkInitializationCompleted() =
        let asyncUpdateApp = async {

            
            // === Velopack Automatic Updates Section - Comment while in development, uncomment for release ===
            do! this.GetReleaseChannelURL()
            let finalURL =
                match releaseChannelURL with
                | Some url -> url
                | None -> "https://github.com/Gildworks/Hydaelyn-Clothiers"
            let accToken =
                match userAccessToken with
                | Some token -> token
                | None -> String.Empty

            let mgr = UpdateManager(new GithubSource(finalURL, accToken, true))
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