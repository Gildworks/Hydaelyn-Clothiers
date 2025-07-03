namespace fs_mdl_viewer

open System
open System.ComponentModel
open System.Collections.ObjectModel
open System.Linq
open System.Windows.Input
open System.IO
open System.Diagnostics
open System.Net.Http
open System.Text.Json
open System.Web
open System.Runtime.CompilerServices

open Avalonia.Controls.ApplicationLifetimes
open Avalonia.Controls
open Avalonia.Platform
open Avalonia

open ReactiveUI

open Microsoft.FSharp.Reflection

open AvaloniaRender.Veldrid
open AvaloniaRender

open Shared

type SimpleCommand(execute: unit -> unit) =
    interface ICommand with
        member _.CanExecute(_) = true
        member _.Execute(_) = execute()
        [<CLIEvent>]
        member _.CanExecuteChanged = Event<EventHandler, EventArgs>().Publish

type SettingsViewModel() =
    inherit ViewModelBase()

    let configPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Hydaelyn Clothiers", "config.json")

    member this.AuthorizePatreon() =

        let mutable isAuthenticating = false
        
        async {
            try
                try
                    isAuthenticating <- true

                    let clientId = "KJUu49q1cFtcRG5TUNIuXsBufdbrwnRnahkGvW2l5vaSBwEKAuv1_Ctj9oaaTIfB"
                    let redirectUri = "https://www.hydaelynclothiers.com/confirm"
                    let scopes = "identity"
                    let sessionId = Guid.NewGuid().ToString()

                    let authUrl =
                        sprintf "https://www.patreon.com/oauth2/authorize?response_type=code&client_id=%s&redirect_uri=%s&scope=%s&state=%s"
                            clientId
                            (HttpUtility.UrlEncode(redirectUri))
                            scopes
                            sessionId

                    Process.Start(ProcessStartInfo(authUrl, UseShellExecute = true)) |> ignore
                    //let! authCode = this.WaitForCallback()
                    //let! accessToken = this.ExchangeCodeForToken(authCode, clientId)
                    let! patreonId = this.PollForAuthResult(sessionId)
                    printfn $"Fetched Patreon ID: {patreonId}"
                    this.SavePatreonId(patreonId)

                finally

                    isAuthenticating <- false

            with
            | ex ->
                printfn $"Error authorizing: {ex.Message}"

        } |> Async.Start

    member private this.PollForAuthResult(sessionId: string) =
        async {
            let rec poll attempts =
                async {
                    if attempts >= 30 then
                        return failwith "Authorization timeout"
                    else
                        try
                            use client = new HttpClient()
                            let! response = client.GetAsync($"https://www.hydaelynclothiers.com/api/patreon-auth-status?sessionId={sessionId}") |> Async.AwaitTask
                            printfn "Fetched reponse"

                            if response.IsSuccessStatusCode then
                                let! json = response.Content.ReadAsStringAsync() |> Async.AwaitTask
                                let result = JsonSerializer.Deserialize<{| patreonId: string option |}>(json)

                                match result.patreonId with
                                | Some id -> return id
                                | None ->
                                    do! Async.Sleep(3000)
                                    return! poll (attempts + 1)
                            else
                                do! Async.Sleep(3000)
                                return! poll (attempts + 1)

                        with
                        | ex ->
                            do! Async.Sleep(3000)
                            return! poll (attempts + 1)
                }
            return! poll 0
        }
    member private this.SavePatreonId(patreonId: string) =
        match this.loadConfig() with
        | Some config ->
            let newConfig =
                { GamePath = config.GamePath; PatreonID = Some patreonId }
            this.saveConfig(newConfig)
        | _ -> ()

    member this.loadConfig() : Config option =
        if File.Exists(configPath) then
            try JsonSerializer.Deserialize<Config>(File.ReadAllText(configPath)) |> Some
            with ex -> None
        else None

    member this.saveConfig (cfg: Config) =
        try let dir = Path.GetDirectoryName(configPath)
            if not (Directory.Exists(dir)) then Directory.CreateDirectory(dir) |> ignore
            File.WriteAllText(configPath, JsonSerializer.Serialize(cfg))
            printfn "File saved!"
        with ex -> printfn $"File failed to save: {ex.Message}"

type VeldridWindowViewModel() =
    inherit ViewModelBase()

    let hyurTribes = [{ Display = "Highlander"; Value="Highlander"}; { Display = "Midlander"; Value="Midlander"}]
    let elezenTribes = [{ Display = "Wildwood"; Value="Wildwood"}; { Display = "Duskwight"; Value="Duskwight"}]
    let lalafellTribes = [{ Display = "Plainsfolk"; Value="Plainsfolk"}; { Display = "Dunesfolk"; Value="Dunesfolk"}]
    let miqoteTribes = [{ Display = "Seekers of the Sun"; Value = "Seeker"}; { Display = "Keepers of the Moon"; Value = "Keeper" }]
    let roegadynTribes = [{ Display = "Sea Wolves"; Value = "SeaWolves" }; { Display = "Hellsguard"; Value = "Hellsguard" }]
    let auRaTribes = [{ Display = "Raen"; Value="Raen"}; { Display = "Xaela"; Value="Xaela"}]
    let hrothgarTribes = [{ Display = "Helions"; Value = "Helions" }; { Display = "The Lost"; Value = "Lost" }]
    let vieraTribes = [{ Display = "Rava"; Value="Rava"}; { Display = "Veena"; Value="Veena"}]

    let mutable _render         : VeldridRender                    = new VeldridView()
    let mutable _windowHandle   : Core.IDisposableWindow    option = None

    let mutable selectedRace = { Display = "Hyur"; Value="Hyur" }
    let mutable selectedTribe = { Display = "Midlander"; Value= "Midlander" }
    let mutable selectedGender = { Display = "Male"; Value = "Male" }
    let mutable _availableTribes = ObservableCollection<ComboOption>(hyurTribes)

    let propertyChanged = Event<PropertyChangedEventHandler, PropertyChangedEventArgs>()

    member val Race =
        [
            { Display = "Hyur"; Value = "Hyur"}; { Display = "Elezen"; Value = "Elezen"};
            { Display = "Lalafell"; Value = "Lalafell"}; { Display = "Miqo'te"; Value = "Miqote"};
            { Display = "Roegadyn"; Value = "Roegadyn"}; { Display = "Au Ra"; Value = "AuRa"};
            { Display = "Hrothgar"; Value = "Hrothgar"}; { Display = "Viera"; Value = "Viera"}
        ]

    member val Gender =
        [
            { Display = "Male"; Value = "Male" };
            { Display = "Female"; Value = "Female" }
        ]

    member this.SelectedRace
        with get() = selectedRace
        and set(value) =
            if selectedRace <> value then
                this.SetValue(&selectedRace, value)
                this.UpdateTribeList()

    member this.AvailableTribes
        with get() = _availableTribes
        and set(value) =
            this.SetValue(&_availableTribes, value)

    member this.SelectedTribe
        with get() = selectedTribe
        and set(value) =
            this.SetValue(&selectedTribe, value)

    member this.SelectedGender
        with get() = selectedGender
        and set(value) =
            this.SetValue(&selectedGender, value)

    member private this.UpdateTribeList() =
        let newTribes =
            match selectedRace.Value with
            | "Hyur" -> hyurTribes
            | "Elezen" -> elezenTribes
            | "Lalafell" -> lalafellTribes
            | "Miqote" -> miqoteTribes
            | "Roegadyn" -> roegadynTribes
            | "AuRa" -> auRaTribes
            | "Hrothgar" -> hrothgarTribes
            | "Viera" -> vieraTribes
            | _ -> List<ComboOption>.Empty

        _availableTribes.Clear()
        for tribe in newTribes do
            _availableTribes.Add(tribe)

        if not newTribes.IsEmpty then
            this.SelectedTribe <- newTribes.Head

    member this.OpenSettingsCommand =
        SimpleCommand(fun () ->
            Avalonia.Threading.Dispatcher.UIThread.Post(fun () ->
                let settingsWindow = new SettingsWindow()
                settingsWindow.DataContext <- SettingsViewModel()
                match Application.Current.ApplicationLifetime with
                | :? IClassicDesktopStyleApplicationLifetime as desktop ->
                    settingsWindow.Show(desktop.MainWindow)
                | _ -> 
                    settingsWindow.Show()
            )
        )

    member val ExitCommand =
        ReactiveCommand.Create(fun () ->
            match Application.Current.ApplicationLifetime with
            | :? IClassicDesktopStyleApplicationLifetime as desktop ->
                desktop.Shutdown()
            | :? ISingleViewApplicationLifetime as singleView ->
                singleView.MainView <- null
            | _ -> ()
        )

    interface INotifyPropertyChanged with
        [<CLIEvent>]
        member _.PropertyChanged = propertyChanged.Publish
    

    interface IVeldridWindowModel with

        member this.Render
            with get (): VeldridRender = _render
        
        member this.WindowHandle
            with get (): Core.IDisposableWindow = 
                match _windowHandle with
                | Some wh -> wh
                | None -> null
            and set (value: Core.IDisposableWindow) = 
                _windowHandle <- if value <> null then Some value else None


    member this.DisposeGraphicsContext() = 
        _windowHandle |> Option.iter (fun wh -> wh.DisposeGraphics())

    

    interface System.IDisposable with
        member this.Dispose() =
            this.DisposeGraphicsContext()

