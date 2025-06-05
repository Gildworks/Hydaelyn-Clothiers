namespace fs_mdl_viewer

open System
open System.Numerics
open System.Threading.Tasks
open System.IO

open SixLabors.ImageSharp

open Avalonia
open Avalonia.Controls
open Avalonia.Markup.Xaml
open Avalonia.Input
open Avalonia.Interactivity
open Avalonia.Layout
open Avalonia.Media

open AvaloniaRender.Veldrid
open xivModdingFramework.General.Enums
open xivModdingFramework.General.DataContainers
open xivModdingFramework.SqPack.FileTypes
open xivModdingFramework.Items.Interfaces
open xivModdingFramework.Items.DataContainers
open xivModdingFramework.Models.FileTypes
open xivModdingFramework.Models.ModelTextures
open xivModdingFramework.Mods
open xivModdingFramework.Materials.FileTypes

open Shared
open TTModelLoader

module DataHelpers =

    let rec findCharacterPart (currentRaceInTree: XivRace) (partId: int) (partCategory: string) (characterItems: XivCharacter list): XivCharacter option =
        let tryFindWithId id =
            characterItems
            |> List.tryFind (fun item ->
                item.TertiaryCategory = currentRaceInTree.GetDisplayName() &&
                item.SecondaryCategory = partCategory &&
                item.ModelInfo.SecondaryID = id
            )

        match tryFindWithId partId with
        | Some part -> Some part
        | None ->
            match tryFindWithId 5 with
            | Some alt -> Some alt
            | None ->
                let parentNode = XivRaceTree.GetNode(currentRaceInTree).Parent
                if parentNode <> null && parentNode.Race <> XivRace.All_Races then
                    findCharacterPart parentNode.Race partId partCategory characterItems
                else
                    None

    let hasModel (item: IItemModel) (characterRace: XivRace) =
        try
            let modelTask = Mdl.GetTTModel(item, characterRace)
            not modelTask.IsFaulted
        with _ ->
            false

    let getColorPalette (race: raceIds) (palette: paletteOptions) =
        task {
            let cmpPath = "chara/xls/charamake/human.cmp"
            let sharedOffset = 320 * 8
            let blockSize = 160 * 8
            let raceBlockStart = sharedOffset + (blockSize * (int race))
            let uiPalettesOffset = 160 * 8

            let offsetsMap = dict [
                paletteOptions.RenderHighlights, (0, 192)
                paletteOptions.RenderEyeColor, (256, 192)
                paletteOptions.RenderLipDark, (512, 96)
                paletteOptions.RenderLipLight, (640, 96)
                paletteOptions.RenderTattoo, (768, 192)
                paletteOptions.RenderFaceDark, (1024, 96)
                paletteOptions.RenderFaceLight, (1152, 96)
                paletteOptions.UIHighlights, (0 + uiPalettesOffset, 192)
                paletteOptions.UIEyeColor, (256 + uiPalettesOffset, 192)
                paletteOptions.UILipDark, (512 + uiPalettesOffset, 96)
                paletteOptions.UILipLight, (640 + uiPalettesOffset, 96)
                paletteOptions.UITattoo, (768 + uiPalettesOffset, 192)
                paletteOptions.UIFaceDark, (1024 + uiPalettesOffset, 96)
                paletteOptions.UIFaceLight, (1152 + uiPalettesOffset, 96)
                paletteOptions.RenderSkin, (raceBlockStart + 0, 192)
                paletteOptions.RenderHair, (raceBlockStart + 256, 192)
                paletteOptions.UISkin, (raceBlockStart + 768, 192)
                paletteOptions.UIHair, (raceBlockStart + 1024, 192)
            ]

            let paletteStartIndex, paletteLength = offsetsMap.[palette]

            let! cmpData =
                try Dat.ReadFile(cmpPath, true) |> Async.AwaitTask
                with ex -> printfn $"Failed to load cmp: {ex.Message}"; reraise()

            let cmpSet = new CharaMakeParameterSet(cmpData)
            let allBytes = cmpSet.GetBytes()
            let metadataStartOffset = CharaMakeParameterSet.MetadataStart

            let colorDataBytes = allBytes |> Seq.take metadataStartOffset

            let colors =
                if (colorDataBytes |> Seq.length) >= (paletteStartIndex + paletteLength) * 4 && paletteLength > 0 then
                    colorDataBytes
                    |> Seq.chunkBySize 4
                    |> Seq.filter (fun chunk -> chunk.Length = 4)
                    |> Seq.skip paletteStartIndex
                    |> (fun seq ->
                        if palette = paletteOptions.RenderHair then
                            seq |> Seq.mapi (fun i el -> i, el) |> Seq.filter (fun (i,_) -> i % 2 = 0) |> Seq.map snd
                        else seq)
                    |> Seq.take paletteLength
                    |> Seq.map (fun chunk ->
                        Vector4(float32 chunk.[0], float32 chunk.[1], float32 chunk.[2], float32 chunk.[3])
                    )
                    |> List.ofSeq
                else
                    List.Empty
            return colors
        }

    let getCustomizableParts (targetRace: XivRace) (partCategory: string) (characterItems: XivCharacter list) (currentCharacterRaceForHasModel: XivRace) : XivCharacter list =
        characterItems
        |> List.filter (fun item ->
            item.TertiaryCategory = targetRace.GetDisplayName() &&
            item.SecondaryCategory = partCategory &&
            hasModel item currentCharacterRaceForHasModel
        )
        |> List.sortBy (fun item -> item.ModelInfo.SecondaryID)

    let getDyeSwatches () =
        task {
            let! dyeDict = STM.GetDyeNames()
            return dyeDict |> Seq.map (fun kvp -> kvp.Value) |> Seq.toList
        }

    let applyDye (item: IItemModel) (race: XivRace) (slot: EquipmentSlot) (dye1: int option) (dye2: int option) (tx: ModTransaction) =
        task { // Assuming ModelTexture.GetCustomColors() returns Task
            let! _stainTemplate = STM.GetStainingTemplateFile(STM.EStainingTemplate.Dawntrail)
            let actualDyeColors = ModelTexture.GetCustomColors() // Changed to let!
            return actualDyeColors
        }


type MainWindow () as this =
    inherit Window ()

    let viewModel = new VeldridWindowViewModel()

    let mutable currentCharacterRace : XivRace = XivRace.Hyur_Midlander_Male
    let mutable selectedRaceNameOpt: string option = None
    let mutable selectedClanNameOpt: string option = None
    let mutable selectedGenderNameOpt: string option = None

    let mutable modelColors: CustomModelColors = ModelTexture.GetCustomColors()

    let mutable allGearCache: XivGear list = List.empty
    let mutable allCharaCache: XivCharacter list = List.empty
    let mutable dyeListCache: string list = List.empty
    let mutable currentTransaction: ModTransaction = null

    let mutable currentHairList: XivCharacter list = List.empty
    let mutable currentFaceList: XivCharacter list = List.empty
    let mutable currentEarList: XivCharacter list = List.empty
    let mutable currentTailList: XivCharacter list = List.empty

    let mutable viewerControl: EmbeddedWindowVeldrid = null
    let mutable inputOverlay: Border = null
    let mutable raceSelector: ComboBox = null
    let mutable clanSelector: ComboBox = null
    let mutable genderSelector: ComboBox = null
    let mutable submitCharacterButton: Button = null
    let mutable hairSelector: ComboBox = null
    let mutable faceSelector: ComboBox = null
    let mutable earSelector: ComboBox = null
    let mutable tailSelector: ComboBox = null
    let mutable headSlotCombo: ComboBox = null
    let mutable headClearButton: Button = null
    let mutable headDye1Combo: ComboBox = null
    let mutable headDye1ClearButton: Button = null
    let mutable headDye2Combo: ComboBox = null
    let mutable headDye2ClearButton: Button = null
    let mutable bodySlotCombo: ComboBox = null
    let mutable bodyClearButton: Button = null
    let mutable bodyDye1Combo: ComboBox = null
    let mutable bodyDye1ClearButton: Button = null
    let mutable bodyDye2Combo: ComboBox = null
    let mutable bodyDye2ClearButton: Button = null
    let mutable handSlotCombo: ComboBox = null
    let mutable handClearButton: Button = null
    let mutable handDye1Combo: ComboBox = null
    let mutable handDye1ClearButton: Button = null
    let mutable handDye2Combo: ComboBox = null
    let mutable handDye2ClearButton: Button = null
    let mutable legsSlotCombo: ComboBox = null
    let mutable legsClearButton: Button = null
    let mutable legsDye1Combo: ComboBox = null
    let mutable legsDye1ClearButton: Button = null
    let mutable legsDye2Combo: ComboBox = null
    let mutable legsDye2ClearButton: Button = null
    let mutable feetSlotCombo: ComboBox = null
    let mutable feetClearButton: Button = null
    let mutable feetDye1Combo: ComboBox = null
    let mutable feetDye1ClearButton: Button = null
    let mutable feetDye2Combo: ComboBox = null
    let mutable feetDye2ClearButton: Button = null

    let mutable skinColorSwatchesControl: ItemsControl = null

    let mutable veldridRenderView: VeldridView option = None

    do
        this.InitializeComponent()
        this.FindGuiControls()

        viewerControl.DataContext <- viewModel

        viewerControl.GetObservable(Control.BoundsProperty)
            .Subscribe(fun bounds ->
                if bounds.Width > 0.0 && bounds.Height > 0.0 then
                    let w = uint32 bounds.Width
                    let h = uint32 bounds.Height
                    match veldridRenderView with
                    | Some render -> render.RequestResize(w, h)
                    | None ->
                        match viewModel :> IVeldridWindowModel with
                        | vm -> match vm.Render with
                                | :? VeldridView as r -> r.RequestResize(w,h)
                                | _ -> ()
                        | _ -> ()
            ) |> ignore

        this.Loaded.Add(fun _ ->
            match viewModel :> IVeldridWindowModel with
            | vm ->
                match vm.Render with
                | :? VeldridView as render ->
                    veldridRenderView <- Some render
                    render.AttachInputHandlers(inputOverlay)
                    this.InitializeApplicationAsync(render) |> Async.StartImmediate
                | _ -> printfn "CRITICAL ERROR: VeldridView instance not found in ViewModel after Loaded event."
            | _ -> printfn "CRITICAL ERROR: ViewModel does not implement IVeldridWindowModel after Loaded event."
        )

    member private this.InitializeComponent() =
#if DEBUG
        this.AttachDevTools()
#endif
        AvaloniaXamlLoader.Load(this)

    member private this.FindGuiControls() =
        viewerControl <- this.FindControl<EmbeddedWindowVeldrid>("ViewerControl")
        inputOverlay <- this.FindControl<Border>("InputOverlay")
        raceSelector <- this.FindControl<ComboBox>("RaceSelector")
        clanSelector <- this.FindControl<ComboBox>("ClanSelector")
        genderSelector <- this.FindControl<ComboBox>("GenderSelector")
        submitCharacterButton <- this.FindControl<Button>("SubmitCharacter")
        hairSelector <- this.FindControl<ComboBox>("HairSelector")
        faceSelector <- this.FindControl<ComboBox>("FaceSelector")
        earSelector <- this.FindControl<ComboBox>("EarSelector")
        tailSelector <- this.FindControl<ComboBox>("TailSelector")
        headSlotCombo <- this.FindControl<ComboBox>("HeadSlot"); headClearButton <- this.FindControl<Button>("HeadClear")
        headDye1Combo <- this.FindControl<ComboBox>("HeadDye1"); headDye1ClearButton <- this.FindControl<Button>("HeadDye1Clear")
        headDye2Combo <- this.FindControl<ComboBox>("HeadDye2"); headDye2ClearButton <- this.FindControl<Button>("HeadDye2Clear")
        bodySlotCombo <- this.FindControl<ComboBox>("BodySlot"); bodyClearButton <- this.FindControl<Button>("BodyClear")
        bodyDye1Combo <- this.FindControl<ComboBox>("BodyDye1"); bodyDye1ClearButton <- this.FindControl<Button>("BodyDye1Clear")
        bodyDye2Combo <- this.FindControl<ComboBox>("BodyDye2"); bodyDye2ClearButton <- this.FindControl<Button>("BodyDye2Clear")
        handSlotCombo <- this.FindControl<ComboBox>("HandSlot"); handClearButton <- this.FindControl<Button>("HandClear")
        handDye1Combo <- this.FindControl<ComboBox>("HandDye1"); handDye1ClearButton <- this.FindControl<Button>("HandDye1Clear")
        handDye2Combo <- this.FindControl<ComboBox>("HandDye2"); handDye2ClearButton <- this.FindControl<Button>("HandDye2Clear")
        legsSlotCombo <- this.FindControl<ComboBox>("LegsSlot"); legsClearButton <- this.FindControl<Button>("LegClear")
        legsDye1Combo <- this.FindControl<ComboBox>("LegDye1"); legsDye1ClearButton <- this.FindControl<Button>("LegDye1Clear")
        legsDye2Combo <- this.FindControl<ComboBox>("LegDye2"); legsDye2ClearButton <- this.FindControl<Button>("LegDye2Clear")
        feetSlotCombo <- this.FindControl<ComboBox>("FeetSlot"); feetClearButton <- this.FindControl<Button>("FeetClear")
        feetDye1Combo <- this.FindControl<ComboBox>("FeetDye1"); feetDye1ClearButton <- this.FindControl<Button>("FeetDye1Clear")
        feetDye2Combo <- this.FindControl<ComboBox>("FeetDye2"); feetDye2ClearButton <- this.FindControl<Button>("FeetDye2Clear")

        skinColorSwatchesControl <- this.FindControl<ItemsControl>("SkinColorSwatches")

    member private this.UpdateSubmitButtonState() =
        let raceOk = selectedRaceNameOpt.IsSome
        let genderOk = selectedGenderNameOpt.IsSome
        let clanOk =
            match selectedRaceNameOpt with
            | Some "Hyur" -> selectedClanNameOpt.IsSome
            | Some _ -> true
            | None -> false
        submitCharacterButton.IsEnabled <- raceOk && genderOk && clanOk

    member private this.UpdateDyeChannelsForItem(item: IItemModel, itemSlot: EquipmentSlot, tx: ModTransaction) =
        task {
            let mutable dyeChannel1 = false
            let mutable dyeChannel2 = false

            printfn "[MAINWINDOW] ATTEMPTING TO LOAD DYEMODEL, IF YOU SEE THIS, THIS PORTION HAS BEEN REACHED."

            let! dyeModel = TTModelLoader.loadTTModel item currentCharacterRace itemSlot
                            |> Async.AwaitTask
            printfn $"DyeModel: {dyeModel.Source} | Material Count: {dyeModel.Materials.Count}"

            for matName in dyeModel.Materials do
                // *** FIXED HERE ***
                let! material = (TTModelLoader.resolveMtrl dyeModel matName item tx |> Async.StartAsTask)
                match material.ColorSetDyeData.Length with
                | len when len >= 128 ->
                    for i in 0 .. 15 do
                        let offset = i * 4
                        let stainId = material.ColorSetDyeData[offset]
                        let repeat = material.ColorSetDyeData[offset + 3]
                        if stainId > 0uy then
                            if repeat < 8uy then dyeChannel1 <- true
                            else dyeChannel2 <- true
                | 32 ->
                    for i in 0 .. 15 do
                        let flagsOffset = i * 2
                        let flags = material.ColorSetDyeData[flagsOffset]
                        if (flags &&& 0x01uy) <> 0uy then dyeChannel1 <- true
                        if (flags &&& 0x02uy) <> 0uy then dyeChannel2 <- true
                | _ -> ()
            return dyeChannel1, dyeChannel2
        }

    member private this.HandleGearSelectionChanged(
        item: IItemModel, eqSlot: EquipmentSlot,
        dye1Combo: ComboBox, dye1ClearButton: Button,
        dye2Combo: ComboBox, dye2ClearButton: Button,
        render: VeldridView) =
        Async.StartImmediate(
            async {
                let! (ch1Enabled, ch2Enabled) = this.UpdateDyeChannelsForItem(item, eqSlot, currentTransaction) |> Async.AwaitTask
                dye1Combo.IsEnabled <- ch1Enabled
                dye1ClearButton.IsEnabled <- ch1Enabled
                dye1Combo.ItemsSource <- if ch1Enabled then dyeListCache else []

                dye2Combo.IsEnabled <- ch2Enabled
                dye2ClearButton.IsEnabled <- ch2Enabled
                dye2Combo.ItemsSource <- if ch2Enabled then dyeListCache else []

                dye1Combo.SelectedIndex <- -1
                dye2Combo.SelectedIndex <- -1
                render.AssignTrigger(eqSlot, item, currentCharacterRace, -1, -1, modelColors) |> ignore
            }
        )

    member private this.HandleDyeSelectionChanged(
        item: IItemModel, eqSlot: EquipmentSlot,
        dye1Combo: ComboBox, dye2Combo: ComboBox, render: VeldridView) =
        let dye1Idx = if dye1Combo.IsEnabled && dye1Combo.SelectedIndex >=0 then dye1Combo.SelectedIndex + 1 else -1
        let dye2Idx = if dye2Combo.IsEnabled && dye2Combo.SelectedIndex >=0 then dye2Combo.SelectedIndex + 1 else -1
        if dye1Idx > 0 || dye2Idx > 0 then
             render.AssignTrigger(eqSlot, item, currentCharacterRace, dye1Idx, dye2Idx, modelColors) |> ignore

    member private this.ClearGearSlot(
        slotCombo: ComboBox, eqSlot: EquipmentSlot, gearList: XivGear list,
        dye1Combo: ComboBox, dye1ClearButton: Button,
        dye2Combo: ComboBox, dye2ClearButton: Button, render: VeldridView) =

        let smallClothesNamePart =
            match eqSlot with
            | EquipmentSlot.Body -> "Body" | EquipmentSlot.Hands -> "Hands"
            | EquipmentSlot.Legs -> "Legs" | EquipmentSlot.Feet -> "Feet"
            | _ -> null

        // *** FIXED HERE ***
        let defaultIndex =
            if isNull smallClothesNamePart then -1
            else
                match gearList |> List.tryFindIndex (fun g -> g.Name.Contains("SmallClothes") && g.Name.Contains(smallClothesNamePart)) with
                | Some idx -> idx
                | None -> -1

        match defaultIndex with
        | i when i >= 0 -> slotCombo.SelectedIndex <- i
        | _ ->
            render.ClearGearSlot eqSlot
            slotCombo.SelectedIndex <- -1
            dye1Combo.IsEnabled <- false; dye1ClearButton.IsEnabled <- false; dye1Combo.ItemsSource <- System.Linq.Enumerable.Empty<string>()
            dye2Combo.IsEnabled <- false; dye2ClearButton.IsEnabled <- false; dye2Combo.ItemsSource <- System.Linq.Enumerable.Empty<string>()

    member private this.ClearDyeChannel(
        item: IItemModel, eqSlot: EquipmentSlot, channelToClear: int,
        dye1Combo: ComboBox, dye2Combo: ComboBox, render: VeldridView) =
        if channelToClear = 1 then dye1Combo.SelectedIndex <- -1
        elif channelToClear = 2 then dye2Combo.SelectedIndex <- -1
        this.HandleDyeSelectionChanged(item, eqSlot, dye1Combo, dye2Combo, render)

    member private this.AttachEventHandlers(render: VeldridView) =
        raceSelector.SelectionChanged.Add(fun _ ->
            match raceSelector.SelectedValue with
            | :? ComboOption as selected ->
                selectedRaceNameOpt <- Some selected.Value
                if selected.Value = "Hyur" then
                    clanSelector.ItemsSource <- ["Midlander"; "Highlander"]
                    clanSelector.IsEnabled <- true
                else
                    clanSelector.ItemsSource <- System.Linq.Enumerable.Empty<string>()
                    clanSelector.SelectedIndex <- -1
                    selectedClanNameOpt <- None
                this.UpdateSubmitButtonState()
            | _ -> selectedRaceNameOpt <- None; this.UpdateSubmitButtonState()
        )
        clanSelector.SelectionChanged.Add(fun _ ->
            match clanSelector.SelectedValue with
            | :? string as clanStr -> selectedClanNameOpt <- Some clanStr
            | _ -> selectedClanNameOpt <- None
            this.UpdateSubmitButtonState()
        )
        genderSelector.SelectionChanged.Add(fun _ ->
            match genderSelector.SelectedValue with
            | :? string as genderStr -> selectedGenderNameOpt <- Some genderStr
            | _ -> selectedGenderNameOpt <- None
            this.UpdateSubmitButtonState()
        )

        let setupCharPartSelector (selector: ComboBox) (partSlot: EquipmentSlot) (getPartList: unit -> XivCharacter list) =
            selector.SelectionChanged.Add(fun _ ->
                let idx = selector.SelectedIndex
                let parts = getPartList()
                if idx >= 0 && idx < parts.Length then
                    render.AssignTrigger(partSlot, parts[idx], currentCharacterRace, -1, -1, modelColors) |> ignore
            )
        setupCharPartSelector hairSelector EquipmentSlot.Hair (fun () -> currentHairList)
        setupCharPartSelector faceSelector EquipmentSlot.Face (fun () -> currentFaceList)
        setupCharPartSelector earSelector EquipmentSlot.Ear (fun () -> currentEarList)
        setupCharPartSelector tailSelector EquipmentSlot.Tail (fun () -> currentTailList)

        let setupGearSlot (
            slotCombo: ComboBox, clearButton: Button,
            dye1Combo: ComboBox, dye1ClearButton: Button,
            dye2Combo: ComboBox, dye2ClearButton: Button,
            eqSlot: EquipmentSlot, gearCategory: string) =

            let getGearList() = allGearCache |> List.filter (fun m -> m.SecondaryCategory = gearCategory)
            slotCombo.ItemsSource <- getGearList() |> List.map (fun g -> g.Name)

            slotCombo.SelectionChanged.Add(fun _ ->
                let idx = slotCombo.SelectedIndex
                let gearList = getGearList()
                if idx >= 0 && idx < gearList.Length then
                    this.HandleGearSelectionChanged(gearList[idx], eqSlot, dye1Combo, dye1ClearButton, dye2Combo, dye2ClearButton, render)
            )
            clearButton.Click.Add(fun _ ->
                this.ClearGearSlot(slotCombo, eqSlot, getGearList(), dye1Combo, dye1ClearButton, dye2Combo, dye2ClearButton, render)
            )
            dye1ClearButton.Click.Add(fun _ ->
                let idx = slotCombo.SelectedIndex
                let gearList = getGearList()
                if idx >=0 && idx < gearList.Length then
                    this.ClearDyeChannel(gearList[idx], eqSlot, 1, dye1Combo, dye2Combo, render)
            )
            dye2ClearButton.Click.Add(fun _ ->
                let idx = slotCombo.SelectedIndex
                let gearList = getGearList()
                if idx >=0 && idx < gearList.Length then
                    this.ClearDyeChannel(gearList[idx], eqSlot, 2, dye1Combo, dye2Combo, render)
            )
            dye1Combo.SelectionChanged.Add(fun _ ->
                let idx = slotCombo.SelectedIndex
                let gearList = getGearList()
                if idx >=0 && idx < gearList.Length then
                    if dye1Combo.SelectedIndex >= 0 then
                       this.HandleDyeSelectionChanged(gearList[idx], eqSlot, dye1Combo, dye2Combo, render)
            )
            dye2Combo.SelectionChanged.Add(fun _ ->
                let idx = slotCombo.SelectedIndex
                let gearList = getGearList()
                if idx >=0 && idx < gearList.Length then
                    if dye2Combo.SelectedIndex >= 0 then
                        this.HandleDyeSelectionChanged(gearList[idx], eqSlot, dye1Combo, dye2Combo, render)
            )

        setupGearSlot (headSlotCombo, headClearButton, headDye1Combo, headDye1ClearButton, headDye2Combo, headDye2ClearButton, EquipmentSlot.Head, "Head")
        setupGearSlot (bodySlotCombo, bodyClearButton, bodyDye1Combo, bodyDye1ClearButton, bodyDye2Combo, bodyDye2ClearButton, EquipmentSlot.Body, "Body")
        setupGearSlot (handSlotCombo, handClearButton, handDye1Combo, handDye1ClearButton, handDye2Combo, handDye2ClearButton, EquipmentSlot.Hands, "Hands")
        setupGearSlot (legsSlotCombo, legsClearButton, legsDye1Combo, legsDye1ClearButton, legsDye2Combo, legsDye2ClearButton, EquipmentSlot.Legs, "Legs")
        setupGearSlot (feetSlotCombo, feetClearButton, feetDye1Combo, feetDye1ClearButton, feetDye2Combo, feetDye2ClearButton, EquipmentSlot.Feet, "Feet")

        submitCharacterButton.Click.Add(fun _ -> this.OnSubmitCharacter(render) |> Async.StartImmediate)

    member private this.OnSubmitCharacter(render: VeldridView) =
        async {
            render.clearCharacter()

            modelColors.SkinColor <- SharpDX.Color(73uy, 92uy, 120uy, 255uy)
            modelColors.HairColor <- SharpDX.Color(254uy, 254uy, 254uy, 255uy)
            modelColors.EyeColor <- SharpDX.Color(130uy, 142uy, 184uy, 255uy)
            modelColors.LipColor <- SharpDX.Color(74uy, 26uy, 66uy, 255uy)
            modelColors.TattooColor <- SharpDX.Color(255uy)
            modelColors.HairHighlightColor <- SharpDX.Color(49uy, 57uy, 69uy)

            let raceStr =
                match selectedRaceNameOpt, selectedClanNameOpt, selectedGenderNameOpt with
                | Some r, Some c, Some g when r = "Hyur" -> Some $"{r}_{c}_{g}"
                | Some r, _, Some g when r <> "Hyur" -> Some $"{r}_{g}"
                | _ -> None

            match raceStr with
            | Some validRaceStr ->
                match Enum.TryParse<XivRace>(validRaceStr) with
                | true, parsedXivRace ->
                    currentCharacterRace <- parsedXivRace

                    let getParts cat = DataHelpers.getCustomizableParts parsedXivRace cat allCharaCache currentCharacterRace
                    currentFaceList <- getParts "Face"
                    currentHairList <- getParts "Hair"
                    currentEarList <- getParts "Ear"
                    currentTailList <- getParts "Tail"

                    let populateSelector (selector: ComboBox) (parts: XivCharacter list) defaultToFirst =
                        selector.ItemsSource <- parts |> List.map (fun p -> p.Name)
                        selector.IsEnabled <- not (List.isEmpty parts)
                        if defaultToFirst && not (List.isEmpty parts) && selector.SelectedIndex < 0 then
                            selector.SelectedIndex <- 0

                    populateSelector faceSelector currentFaceList true
                    populateSelector hairSelector currentHairList true
                    populateSelector earSelector currentEarList true
                    populateSelector tailSelector currentTailList true

                    let tryAssignDefault (parts: XivCharacter list) (slot: EquipmentSlot) (selector: ComboBox) =
                        if selector.IsEnabled && selector.SelectedIndex < 0 then
                           match List.tryHead parts with
                           | Some defaultPart -> render.AssignTrigger(slot, defaultPart, parsedXivRace, -1, -1, modelColors) |> ignore
                           | None -> ()
                        else ()

                    do tryAssignDefault currentFaceList EquipmentSlot.Face faceSelector
                    do tryAssignDefault currentHairList EquipmentSlot.Hair hairSelector
                    if currentEarList |> List.isEmpty |> not then do tryAssignDefault currentEarList EquipmentSlot.Ear earSelector
                    if currentTailList |> List.isEmpty |> not then do tryAssignDefault currentTailList EquipmentSlot.Tail tailSelector

                    let reselectIfPopulated (combo: ComboBox) = if combo.SelectedIndex >=0 then let s = combo.SelectedIndex in combo.SelectedIndex <- -1; combo.SelectedIndex <- s
                    reselectIfPopulated headSlotCombo
                    reselectIfPopulated bodySlotCombo
                    reselectIfPopulated handSlotCombo
                    reselectIfPopulated legsSlotCombo
                    reselectIfPopulated feetSlotCombo

                    let setDefaultGear (combo: ComboBox, category: string, nameFilter: string) =
                        if combo.SelectedIndex = -1 then
                            let gear = allGearCache |> List.filter(fun g -> g.SecondaryCategory = category)
                            match gear |> List.tryFindIndex(fun g -> g.Name.Contains(nameFilter)) with
                            | Some idx -> combo.SelectedIndex <- idx
                            | None -> ()
                    setDefaultGear (bodySlotCombo, "Body", "SmallClothes")
                    setDefaultGear (handSlotCombo, "Hands", "SmallClothes")
                    setDefaultGear (legsSlotCombo, "Legs", "SmallClothes")
                    setDefaultGear (feetSlotCombo, "Feet", "SmallClothes")

                | false, _ -> printfn $"Invalid XivRace string for Enum.TryParse: '{validRaceStr}'"
            | None -> printfn "Cannot submit character: Incomplete race/clan/gender selection."
        }

    member private this.InitializeApplicationAsync(render: VeldridView) =
        async {
            try
                currentTransaction <- ModTransaction.BeginReadonlyTransaction()

                let! gear = render.GetEquipment()
                allGearCache <- gear
                let! chara = render.GetChara()
                allCharaCache <- chara
                let! dyes = DataHelpers.getDyeSwatches() |> Async.AwaitTask
                dyeListCache <- dyes

                raceSelector.ItemsSource <-[
                    { Display = "Hyur"; Value = "Hyur"}; { Display = "Elezen"; Value = "Elezen"}
                    { Display = "Lalafell"; Value = "Lalafell"}; { Display = "Miqo'te"; Value = "Miqote"}
                    { Display = "Roegadyn"; Value = "Roegadyn"}; { Display = "Au Ra"; Value = "AuRa"}
                    { Display = "Hrothgar"; Value = "Hrothgar"}; { Display = "Viera"; Value = "Viera"}
                ]
                genderSelector.ItemsSource <- ["Male"; "Female"]
                submitCharacterButton.IsEnabled <- false
                hairSelector.IsEnabled <- false; faceSelector.IsEnabled <- false
                earSelector.IsEnabled <- false; tailSelector.IsEnabled <- false
                
                this.AttachEventHandlers(render)

                let! skinColorVec4List = DataHelpers.getColorPalette raceIds.AuRa_Xaela_Male paletteOptions.UISkin |> Async.AwaitTask
                let avaloniaSkinColors =
                    skinColorVec4List
                    |> List.map (fun v4 ->
                        let r = byte (Math.Clamp(v4.X, 0.0f, 255.0f))
                        let g = byte (Math.Clamp(v4.Y, 0.0f, 255.0f))
                        let b = byte (Math.Clamp(v4.Z, 0.0f, 255.0f))
                        let a = byte (Math.Clamp(v4.W, 0.0f, 255.0f))
                        Color.FromArgb(a, r, g, b)
                    )
                if skinColorSwatchesControl <> null then
                    skinColorSwatchesControl.ItemsSource <- avaloniaSkinColors
                else
                    printfn "Error: SkinColorSwatches not found."

                match currentTransaction with
                | null -> ()
                | tx ->
                    tx.Dispose()
                    currentTransaction <- null

            with ex ->
                printfn $"Error during application initialization: {ex.ToString()}"
        }