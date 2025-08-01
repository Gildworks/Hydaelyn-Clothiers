namespace fs_mdl_viewer

open Avalonia
open Avalonia.Controls.ApplicationLifetimes
open Avalonia.Markup.Xaml
open Avalonia.Svg
open Avalonia.Svg.Skia

open Serilog

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

    let mutable releaseChannelURL: string = ""
    let mutable userAccessToken: string = ""

    let logger =
        LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console()
            .WriteTo.File(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                                "Hydaelyn Clothiers", "logs", "hc-.log"),
                rollingInterval = RollingInterval.Day,
                retainedFileCountLimit = 7,
                shared = true)
            .Enrich.WithProperty("Application", "Hydaelyn Clothiers")
            .CreateLogger()

    override this.Initialize() =
        AvaloniaXamlLoader.Load(this)

    member this.Shutdown() =
        this.Shutdown()

    member this.loadConfig() : Config option =
        if File.Exists(configPath) then
            try JsonSerializer.Deserialize<Config>(File.ReadAllText(configPath)) |> Some
            with ex -> None
        else None

    member this.GetLicenseInfo() : Async<string * string> =
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
                    let! response = client.GetAsync($"https://www.hydaelynclothiers.com/api/license-check?patreonId={memberId.Value}") |> Async.AwaitTask

                    if response.IsSuccessStatusCode then
                        let! json = response.Content.ReadAsStringAsync() |> Async.AwaitTask
                        let result = JsonSerializer.Deserialize<{| releaseURL: string; accessToken: string |}>(json)

                        if String.IsNullOrWhiteSpace(result.accessToken) then
                            return "", ""
                        else return result.releaseURL, result.accessToken
                    else return "", ""
                else return "", ""
            with
            | ex ->
                return "", ""
        }

    member this.GetReleaseChannelURL() =
        async {
            let! releaseUrl, accessToken = this.GetLicenseInfo()
            releaseChannelURL <- releaseUrl
            userAccessToken <- accessToken
        }
        
            

    override this.OnFrameworkInitializationCompleted() =
        Log.Logger <- logger
        let asyncUpdateApp = async {
            try
                // === Velopack Automatic Updates Section ===
                do! this.GetReleaseChannelURL()
                let finalURL =
                    if String.IsNullOrWhiteSpace(releaseChannelURL) then
                        "https://github.com/Gildworks/Hydaelyn-Clothiers"
                    else
                        releaseChannelURL
                let accToken =
                    if String.IsNullOrWhiteSpace(userAccessToken) then
                        String.Empty
                    else
                        userAccessToken

                let mgr = UpdateManager(new GithubSource(finalURL, accToken, true))
                let! newVer = mgr.CheckForUpdatesAsync() |> Async.AwaitTask
                if not (isNull newVer) then
                    do! mgr.DownloadUpdatesAsync(newVer) |> Async.AwaitTask
                    mgr.ApplyUpdatesAndRestart(newVer)
                // === End Velopack Automatic Updates Section ===
            with ex ->
                printfn $"Failed to check for updates: {ex.Message}"

            match this.ApplicationLifetime with
            | :? IClassicDesktopStyleApplicationLifetime as desktop ->
                desktop.MainWindow <- MainWindow()
                desktop.MainWindow.Show()
            | _ -> ()
        }
        Async.StartImmediate asyncUpdateApp

        base.OnFrameworkInitializationCompleted()
