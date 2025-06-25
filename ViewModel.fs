namespace fs_mdl_viewer

open System
open System.Collections.ObjectModel
open System.Linq
open System.ComponentModel
open System.Runtime.CompilerServices
open System.Runtime.InteropServices
open System.Threading
open System.Windows.Input
open ReactiveUI
open Avalonia
open Avalonia.Controls.ApplicationLifetimes
open Avalonia.Controls
open Avalonia.Media
open Avalonia.Platform
open Avalonia.Collections
open Avalonia.Svg.Skia
open AvaloniaRender.Veldrid
open AvaloniaRender
open Microsoft.FSharp.Reflection

open xivModdingFramework.Exd.Enums
open xivModdingFramework.Exd.FileTypes

open Shared

type JobViewModel(job: Job) =
    inherit ViewModelBase()
    
    let loadImageFromResources (path: string) : IImage =
        let uri = Uri(path, UriKind.Absolute)
        if AssetLoader.Exists(Uri(path, UriKind.Absolute)) then
            use stream = AssetLoader.Open(uri)
            let svgSource = SvgSource.LoadFromStream(stream)
            let svgImage = SvgImage(Source = svgSource)
            svgImage :> IImage
        else
            null

    let mutable _isSelected = false
    let mutable _classlevel = 1
    let colorPath = $"avares://Hydaelyn Clothiers/Assets/Icons/Jobs-Active/{job.ToString().ToUpperInvariant()}.svg"
    let greyscalePath = $"avares://Hydaelyn Clothiers/Assets/Icons/Jobs-Inactive/{job.ToString().ToUpperInvariant()}.svg"
    let colorIcon = loadImageFromResources colorPath
    let greyscaleIcon = loadImageFromResources greyscalePath

    member _.JobType = job
    member _.JobName = Job.ToDisplayName job
    member _.ColorIcon = colorIcon
    member _.GreyscaleIcon = greyscaleIcon
    member this.IsSelected
        with get() = _isSelected
        and set(value) = 
            this.SetValue(&_isSelected, value)
    member this.ClassLevel
        with get() = _classlevel
        and set(value) =
            this.SetValue(&_classlevel, value)

type BookViewModel(book: MasterBookItem) =
    inherit ViewModelBase()

    let mutable _isSelected = false

    member _.BookTitle = book.DisplayName
    member this.IsSelected
        with get() = _isSelected
        and set(value) =
            this.SetValue(&_isSelected, value)


type SettingsViewModel ()  =
    inherit ViewModelBase()

    let mutable _isSelected = false

    let getExdData (exd: XivEx) =
        async {
            let ex = new Ex()
            return! ex.ReadExData(exd) |> Async.AwaitTask
        }
    let exdToMap (exDictionary: Collections.Generic.Dictionary<int, Ex.ExdRow>) : Map<int, Ex.ExdRow> =
        exDictionary
        |> Seq.map (fun kvp -> kvp.Key, kvp.Value)
        |> Map.ofSeq

    let createJobVm (job: Job) =
        let vm = JobViewModel(job)
        vm

    let createBookVm (book: MasterBookItem) =
        let vm = BookViewModel(book)
        vm

    let recipeBooks =
        async {
            let! masterRecipeBooksAsync = getExdData(XivEx.secretrecipebook) |> Async.StartChild
            let! masterRecipeBooks = masterRecipeBooksAsync
            return exdToMap masterRecipeBooks
        } |> Async.RunSynchronously


    let bookList =
        let allBooks = Enum.GetValues(typeof<MasterBook>) :?> int array
        let mappedBooks =
            allBooks
            |> Array.map (fun book ->
                let bookTitle =
                    match Map.tryFind book recipeBooks with
                    | Some exdRow -> exdRow.GetColumn(1) :?> string
                    | None -> ""
                { Book = enum<MasterBook>(book); DisplayName = bookTitle }
            )
            |> Array.filter (fun book ->
                not (String.IsNullOrEmpty(book.DisplayName))
            )
        mappedBooks

    member val CRPBooks =
        bookList
        |> Array.filter (fun book ->
            book.DisplayName.Contains("Carpenter")
        )
        |> Array.map createBookVm
        |> fun vms -> AvaloniaList<BookViewModel>(vms)

    member val BSMBooks =
        bookList
        |> Array.filter (fun book ->
            book.DisplayName.Contains("Blacksmith")
        )
        |> Array.map createBookVm
        |> fun vms -> AvaloniaList<BookViewModel>(vms)

    member val ARMBooks =
        bookList
        |> Array.filter (fun book ->
            book.DisplayName.Contains("Armorer")
        )
        |> Array.map createBookVm
        |> fun vms -> AvaloniaList<BookViewModel>(vms)

    member val GSMBooks =
        bookList
        |> Array.filter (fun book ->
            book.DisplayName.Contains("Goldsmith")
        )
        |> Array.map createBookVm
        |> fun vms -> AvaloniaList<BookViewModel>(vms)

    member val LTWBooks =
        bookList
        |> Array.filter (fun book ->
            book.DisplayName.Contains("Leatherworker")
        )
        |> Array.map createBookVm
        |> fun vms -> AvaloniaList<BookViewModel>(vms)

    member val WVRBooks =
        bookList
        |> Array.filter (fun book ->
            book.DisplayName.Contains("Weaver")
        )
        |> Array.map createBookVm
        |> fun vms -> AvaloniaList<BookViewModel>(vms)

    member val ALCBooks =
        bookList
        |> Array.filter (fun book ->
            book.DisplayName.Contains("Alchemist")
        )
        |> Array.map createBookVm
        |> fun vms -> AvaloniaList<BookViewModel>(vms)

    member val CULBooks =
        bookList
        |> Array.filter (fun book ->
            book.DisplayName.Contains("Culinarian")
        )
        |> Array.map createBookVm
        |> fun vms -> AvaloniaList<BookViewModel>(vms)

    member val Crafters =
        [Job.CRP; Job.BSM; Job.ARM; Job.GSM; Job.LTW; Job.WVR; Job.ALC; Job.CUL]
        |> List.map createJobVm
        |> fun vms -> AvaloniaList<JobViewModel>(vms)
    
    

type SimpleCommand(execute: unit -> unit) =
    interface ICommand with
        member _.CanExecute(_) = true
        member _.Execute(_) = execute()
        [<CLIEvent>]
        member _.CanExecuteChanged = Event<EventHandler, EventArgs>().Publish

type VeldridWindowViewModel() as this =
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
    let mutable _windowHeight = 0.0
    let mutable _listBoxHeight = 125.0

    let mutable allGearCache: FilterGear list = List.empty

    let mutable _globallyFilteredGear: FilterGear list = List.empty


    let mutable selectedRace = { Display = "Hyur"; Value="Hyur" }
    let mutable selectedTribe = { Display = "Midlander"; Value= "Midlander" }
    let mutable selectedGender = { Display = "Male"; Value = "Male" }
    let mutable _availableTribes = ObservableCollection<ComboOption>(hyurTribes)

    let mutable _restrictEquip = false
    let mutable _craftedOnly = false
    let mutable _useProfile = false

    let mutable _characterLevel = 100
    let mutable _itemLevel = 1000

    let canEquip (itemJobs: Set<Job>) (selectedJobs: Set<Job>) : bool =
        if Set.isEmpty selectedJobs then true
        else not (Set.isEmpty (Set.intersect itemJobs selectedJobs))

    let createJobVm (job: Job) =
        let vm = JobViewModel(job)
        vm.FSharpPropertyChanged.Add(fun args ->
            if args.PropertyName = "IsSelected" then
                this.ApplyGlobalFilters()
        )
        vm

    do
        this.FSharpPropertyChanged.Add(fun args ->
            match args.PropertyName with
            | "EquipRestrict" | "CharacterLevel" | "ItemLevel" 
            | "CraftedOnly" | "UseProfile" -> 
                this.ApplyGlobalFilters()
            | "SelectedRace" | "SelectedGender" ->
                let raceOk = not (String.IsNullOrWhiteSpace(selectedRace.Value))
                let genderOk = not (String.IsNullOrWhiteSpace(selectedGender.Value))
                if raceOk && genderOk then
                    this.ApplyGlobalFilters()
            | _ -> ()
        )

    member this.WindowHeight
        with get() = _windowHeight
        and set(value) =
            if _windowHeight <> value then
                this.SetValue(&_windowHeight, value)
                this.SetValue(&_listBoxHeight, (value * 0.1), "ListBoxHeight")

    member _.ListBoxHeight = int _listBoxHeight

    member this.EquipRestrict
        with get() = _restrictEquip
        and set(value) =
            this.SetValue(&_restrictEquip, value)

    member this.CraftedOnly
        with get() = _craftedOnly
        and set(value) =
            this.SetValue(&_craftedOnly, value)

    member this.UseProfile
        with get() = _useProfile
        and set(value) =
            this.SetValue(&_useProfile, value)

    member this.CharacterLevel
        with get() = _characterLevel
        and set(value) =
            this.SetValue(&_characterLevel, value)

    member this.ItemLevel
        with get() = _itemLevel
        and set(value) =
            this.SetValue(&_itemLevel, value)

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

    member val Tanks =
        [Job.GLA; Job.PLD; Job.MRD; Job.WAR; Job.DRK; Job.GNB]
        |> List.map createJobVm
        |> fun vms -> new AvaloniaList<JobViewModel>(vms)
    member val Healers =
        [Job.CNJ; Job.WHM; Job.SCH; Job.AST; Job.SGE]
        |> List.map createJobVm
        |> fun vms -> new AvaloniaList<JobViewModel>(vms)
    member val RangedDPS =
        [Job.ARC; Job.BRD; Job.MCH; Job.DNC]
        |> List.map createJobVm
        |> fun vms -> new AvaloniaList<JobViewModel>(vms)
    member val MeleeDPS =
        [Job.PGL; Job.MNK; Job.LNC; Job.DRG; Job.ROG; Job.NIN; Job.SAM; Job.RPR; Job.VPR ]
        |> List.map createJobVm
        |> fun vms -> AvaloniaList<JobViewModel>(vms)
    member val MagicDPS =
        [Job.THM; Job.BLM; Job.ACN; Job.SMN; Job.RDM; Job.BLU; Job.PCT]
        |> List.map createJobVm
        |> fun vms -> AvaloniaList<JobViewModel>(vms)
    member val Crafters =
        [Job.CRP; Job.BSM; Job.ARM; Job.GSM; Job.LTW; Job.WVR; Job.ALC; Job.CUL]
        |> List.map createJobVm
        |> fun vms -> AvaloniaList<JobViewModel>(vms)
    member val Gatherers =
        [Job.MIN; Job.BTN; Job.FSH]
        |> List.map createJobVm
        |> fun vms -> AvaloniaList<JobViewModel>(vms)

    member this.GloballyFilteredGear
        with get() = _globallyFilteredGear
        and private set(v) = this.SetValue(&_globallyFilteredGear, v)

    member private this.ApplyGlobalFilters() =
        let selectedJobs =
            this.Tanks.Concat(this.Healers).Concat(this.RangedDPS).Concat(this.MeleeDPS).Concat(this.MagicDPS).Concat(this.Crafters).Concat(this.Gatherers)
            |> Seq.filter (fun vm -> vm.IsSelected)
            |> Seq.map (fun vm -> vm.JobType)
            |> Set.ofSeq
        let filteredList =
            allGearCache
            |> List.filter (fun item ->
                if not _restrictEquip then true else
                    match selectedGender.Value with
                    | "Male" ->
                        match int item.EquipRestriction with
                        | 0 | 1 | 2 | 4
                        | 6 | 8 | 10 | 12
                        | 14 | 16 | 18 -> true
                        | _ -> false
                    | "Female" ->
                        match int item.EquipRestriction with
                        | 0 | 1 | 3 | 5
                        | 7 | 9 | 11 | 13
                        | 15 | 17 | 19 -> true
                        | _ -> false
                    | _ -> true
            )
            |> List.filter (fun item ->
                if not _restrictEquip then true else
                    match selectedRace.Value with
                    | "Hyur" ->
                        match int item.EquipRestriction with
                        | 0 | 1 | 2 | 3
                        | 4 | 5 -> true
                        | _ -> false
                    | "Elezen" ->
                        match int item.EquipRestriction with
                        | 0 | 1 | 2 | 3
                        | 6 | 7 -> true
                        | _ -> false
                    | "Lalafell" ->
                        match int item.EquipRestriction with
                        | 0 | 1 | 2 | 3
                        | 8 | 9 -> true
                        | _ -> false
                    | "Miqote" | "Miqo'te" ->
                        match int item.EquipRestriction with
                        | 0 | 1 | 2 | 3
                        | 10 | 11 -> true
                        | _ -> false
                    | "Roegadyn" ->
                        match int item.EquipRestriction with
                        | 0 | 1 | 2 | 3
                        | 12 | 13 -> true
                        | _ -> false
                    | "AuRa" | "Au Ra" ->
                        match int item.EquipRestriction with
                        | 0 | 1 | 2 | 3
                        | 14 | 15 -> true
                        | _ -> false
                    | "Hrothgar" ->
                        match int item.EquipRestriction with
                        | 0 | 1 | 2 | 3
                        | 16 | 19 -> true
                        | _ -> false
                    | "Viera" ->
                        match int item.EquipRestriction with
                        | 0 | 1 | 2 | 3
                        | 17 | 18 -> true
                        | _ -> false
                    | _ -> true
            )
            |> List.filter (fun item ->
                if not _craftedOnly then true else
                    if item.CraftingDetails.Length > 0 then true else false
            )
            |> List.filter (fun item ->
                canEquip item.EquippableBy selectedJobs
            )
            |> List.filter (fun item ->
                let levelOk = item.EquipLevel <= _characterLevel
                let iLvlOk = item.ItemLevel <= _itemLevel
                levelOk && iLvlOk
            )
        this.GloballyFilteredGear <- filteredList

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


    member this.InitializeDataAsync(render: VeldridView) =
        async {
            let! loadedGear = render.GetEquipment()
            allGearCache <- loadedGear
            this.ApplyGlobalFilters()
        }

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