namespace fs_mdl_viewer

open System
open System.IO

open Avalonia.Controls
open Avalonia.Markup.Xaml
open Avalonia.Platform.Storage

type SettingsWindow() as this =
    inherit Window()

    let mutable pathTextBox                 : TextBox           = null
    let mutable errorTextBlock              : TextBlock         = null
    let mutable confirmButton               : Button            = null

    do
        AvaloniaXamlLoader.Load(this)

        pathTextBox <- this.FindControl<TextBox>("PathTextBox")
        errorTextBlock <- this.FindControl<TextBlock>("ErrorTextBlock")
        confirmButton <- this.FindControl<Button>("ConfirmButton")
        let browseButton = this.FindControl<Button>("BrowseButton")
        let cancelButton = this.FindControl<Button>("CancelButton")

        confirmButton.IsEnabled <- false

        browseButton.Click.Add(fun _ -> this.OnBrowseClicked() |> Async.StartImmediate)
        confirmButton.Click.Add(fun _ -> this.OnConfirmClicked())
        cancelButton.Click.Add(fun _ ->
            this.selectedPathOpt <- None
            this.Close()
        )

    member val selectedPathOpt: string option = None with get, set

    member private this.OnBrowseClicked() =
        async {
            let topLevel = TopLevel.GetTopLevel(this)
            if topLevel <> null then
                let! folders =
                    topLevel.StorageProvider.OpenFolderPickerAsync(FolderPickerOpenOptions(
                        Title = "Select FFXIV Game Folder",
                        AllowMultiple = false
                        ))
                    |> Async.AwaitTask
                if folders.Count > 0 then
                    let folderPath = folders[0].TryGetLocalPath()
                    match folderPath with
                    | path ->
                        pathTextBox.Text <- path
                        this.ValidatePath(path) |> ignore
                else ()
            else
                this.ShowError("Could not open folder dialog")
        }

    member private this.ValidatePath(path: string) : bool =
        if not (String.IsNullOrWhiteSpace(path)) && Directory.Exists(path) && Directory.Exists(Path.Combine(path, "game", "sqpack")) then 
            this.ShowError("")
            confirmButton.IsEnabled <- true
            true
        else
            let errorMsg =
                match path with
                | emptyPath when String.IsNullOrWhiteSpace(path) ->
                    "Path cannot be empty"
                | nonexistentFolder when not (Directory.Exists(path)) ->
                    "Could not find the selected folder, please try again."
                | invalidFolder when not (Directory.Exists(Path.Combine(path, "game", "sqpack"))) ->
                    "Invalid game folder. Please ensure the selected dirctory contains the subdirectories/folders 'game' and 'boot'. If the selected folder is correct, try running in Administrator Mode"
                | _ -> "Unknown error, please try again."
            this.ShowError(errorMsg)
            confirmButton.IsEnabled <- false
            false

    member private this.OnConfirmClicked() =
        let currentPath = pathTextBox.Text
        if this.ValidatePath(currentPath) then
            this.selectedPathOpt <- Some currentPath
            this.Close(true)

    member private this.ShowError(message: string) =
        errorTextBlock.Text <- message
        errorTextBlock.IsVisible <- not (String.IsNullOrWhiteSpace(message))