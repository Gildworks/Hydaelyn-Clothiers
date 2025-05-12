namespace fs_mdl_viewer

open System
open System.ComponentModel
open System.Runtime.CompilerServices
open AvaloniaRender.Veldrid
open AvaloniaRender

open Shared

type VeldridWindowViewModel() =

    let mutable _render         : VeldridRender                    = new VeldridView()
    let mutable _windowHandle   : Core.IDisposableWindow    option = None

    let mutable selectedRace = Unchecked.defaultof<ComboOption>

    let propertyChanged = Event<PropertyChangedEventHandler, PropertyChangedEventArgs>()

    member this.SelectedRace
        with get() = selectedRace
        and set(value) =
            if selectedRace <> value then
                selectedRace <- value
                propertyChanged.Trigger(this, PropertyChangedEventArgs("SelectedRace"))

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

