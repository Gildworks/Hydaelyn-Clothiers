namespace fs_mdl_viewer

open System
open System.IO

open Avalonia.Controls
open Avalonia.Interactivity
open Avalonia.Markup.Xaml
open Avalonia.Platform.Storage
open Avalonia.Threading

type ConfigData = {
        GamePath: string
        Language: string
    }

type GamePathPromptWindow() as this =
    inherit Window()

    let mutable pathTextBox                 : TextBox               = null
    let mutable errorTextBlock              : TextBlock             = null
    let mutable confirmButton               : Button                = null
    let mutable languageDropdown            : ComboBox              = null
    
    let languages =
        [|
            "English";
            "German";
            "Japanese";
            "Chinese";
            "Korean";
            "French";
        |]

    do
        AvaloniaXamlLoader.Load(this)
        pathTextBox <- this.FindControl<TextBox>("PathTextBox")
        errorTextBlock <- this.FindControl<TextBlock>("ErrorTextBlock")
        confirmButton <- this.FindControl<Button>("ConfirmButton")
        languageDropdown <- this.FindControl<ComboBox>("LanguageBox")
        let browseButton = this.FindControl<Button>("BrowseButton")
        let cancelButton = this.FindControl<Button>("CancelButton")
        languageDropdown.ItemsSource <- languages

        confirmButton.IsEnabled <- false

        browseButton.Click.Add(fun _ -> this.OnBrowseClicked() |> Async.StartImmediate)
        confirmButton.Click.Add(fun _ -> this.OnConfirmClicked())
        cancelButton.Click.Add(fun _ ->
            this.selectedPathOpt <- None
            this.Close()
        )

    member val selectedPathOpt: string option = None with get, set
    member val selectedLanguage: string option = None with get, set

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
                else
                    ()
            else
                this.ShowError("Could not open folder dialog")
        }

    member private this.ValidatePath(path: string) : bool =
        if not (String.IsNullOrWhiteSpace(path)) && Directory.Exists(path) && Directory.Exists(Path.Combine(path, "game", "sqpack")) then
            if path.Contains("Program Files") || path.Contains("(x86)") then
                this.ShowError("Your game may be installed to a system directory. If you cannot create a character, try running Hydaelyn Clothiers as an Administrator.")
                confirmButton.IsEnabled <- true
                true
            else
                this.ShowError("")
                confirmButton.IsEnabled <- true
                true
        else
            let errorMsg =
                if String.IsNullOrWhiteSpace(path) then "Path cannot be empty."
                else "Invalid game folder. Please ensure the selected directoy contains the 'game' and 'boot' subdirectories."
            this.ShowError(errorMsg)
            confirmButton.IsEnabled <- false
            false

    member private this.OnConfirmClicked() =
        let currentPath = pathTextBox.Text
        let currentLanguage =
            match languageDropdown.SelectedItem with
            | :? string as language -> language
            | _ -> String.Empty
        let exportData = { GamePath = currentPath; Language = currentLanguage }
        if this.ValidatePath(currentPath) then
            this.selectedPathOpt <- Some currentPath
            this.selectedLanguage <- Some currentLanguage
            this.Close(Some exportData)

    member private this.ShowError(message: string) =
        errorTextBlock.Text <- message
        errorTextBlock.IsVisible <- not (String.IsNullOrWhiteSpace(message))