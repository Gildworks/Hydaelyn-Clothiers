namespace fs_mdl_viewer

open System
open System.Numerics
open System.Diagnostics
open System.Text
open System.Text.Json
open System.IO

open Avalonia
open Avalonia.Controls
open Avalonia.Controls.Primitives
open Avalonia.Markup.Xaml
open Avalonia.Input
open Avalonia.Interactivity
open Avalonia.Media
open Avalonia.Threading

open AvaloniaRender.Veldrid
open xivModdingFramework.General.Enums
open xivModdingFramework.Cache
open xivModdingFramework.General.DataContainers
open xivModdingFramework.SqPack.FileTypes
open xivModdingFramework.Items.Interfaces
open xivModdingFramework.Items.DataContainers
open xivModdingFramework.Models.FileTypes
open xivModdingFramework.Models.ModelTextures
open xivModdingFramework.Mods
open xivModdingFramework.Materials.FileTypes

open Shared

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
                with ex -> reraise()

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
                            seq |> Seq.mapi (fun i el -> i, el) |> Seq.filter (fun (i,_) -> (i % 2 = 0)) |> Seq.map snd
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

    let srgbToLinear (srgb: float32) =
        if srgb <= 0.04045f then
            float32 (srgb / 12.92f)
        else
            float32 (Math.Pow(float (srgb + 0.055f) / 1.055, 2.4))

    let vec4ToDXColor (input: Vector4) : SharpDX.Color =
        let normalizedSrgb = new Vector4(input.X / 255.0f, input.Y / 255.0f, input.Z / 255.0f, input.W / 255.0f)
        SharpDX.Color(
            srgbToLinear(normalizedSrgb.X),
            srgbToLinear(normalizedSrgb.Y),
            srgbToLinear(normalizedSrgb.Z),
            srgbToLinear(normalizedSrgb.W)
        )

    let getUIColorPalette (race: raceIds) (palette: paletteOptions) =
        task {
            let! vec4List = getColorPalette race palette |> Async.AwaitTask
            let uiColors =
                vec4List
                |> List.map (fun v ->
                    let r = byte (Math.Clamp(v.X, 0.0f, 255.0f))
                    let g = byte (Math.Clamp(v.Y, 0.0f, 255.0f))
                    let b = byte (Math.Clamp(v.Z, 0.0f, 255.0f))
                    let a = byte (Math.Clamp(v.W, 0.0f, 255.0f))
                    Color.FromArgb(a, r, g, b)
                )
            let avaloniaColors =
                uiColors
                |> List.mapi (fun i uiColor -> {Color = uiColor; Index = i})
            return avaloniaColors
        }


type MainWindow () as this =
    inherit Window ()
    let viewModel = new VeldridWindowViewModel()

    let mutable currentCharacterRace : XivRace = XivRace.Hyur_Midlander_Male
    let mutable selectedRaceNameOpt: string option = Some "Hyur"
    let mutable selectedClanNameOpt: string option = Some "Midlander"
    let mutable selectedGenderNameOpt: string option = Some "Male"

    let mutable modelColors: CustomModelColors = ModelTexture.GetCustomColors()
    let mutable selectedSwatchBorders: System.Collections.Generic.Dictionary<paletteOptions, Border option> = System.Collections.Generic.Dictionary()
    let mutable modelColorId: raceIds = raceIds.AuRa_Xaela_Female

    let mutable allGearCache: FilterGear list = List.empty
    let mutable allCharaCache: XivCharacter list = List.empty
    let mutable dyeListCache: string list = List.empty
    let mutable currentTransaction: ModTransaction = null
    let mutable lastSelectedGearItem: System.Collections.Generic.Dictionary<EquipmentSlot, IItemModel> = System.Collections.Generic.Dictionary()

    let mutable currentHairList: XivCharacter list = List.empty
    let mutable currentFaceList: XivCharacter list = List.empty
    let mutable currentEarList: XivCharacter list = List.empty
    let mutable currentTailList: XivCharacter list = List.empty

    let mutable busyOperationCount = 0
    let busyLock = obj()

    let mutable viewerControl: EmbeddedWindowVeldrid = null
    let mutable inputOverlay: Border = null
    let mutable raceSelector: ComboBox = null
    let mutable clanSelector: ComboBox = null
    let mutable genderSelector: ComboBox = null
    let mutable submitCharacterButton: Button = null
    let mutable clearAllButton: Button = null
    let mutable hairSelector: ComboBox = null
    let mutable faceSelector: ComboBox = null
    let mutable earSelector: ComboBox = null
    let mutable tailSelector: ComboBox = null
    let mutable headSlotCombo: ListBox = null
    let mutable headClearButton: Button = null
    let mutable headDye1Combo: ComboBox = null
    let mutable headDye1ClearButton: Button = null
    let mutable headDye2Combo: ComboBox = null
    let mutable headDye2ClearButton: Button = null
    let mutable bodySlotCombo: ListBox = null
    let mutable bodyClearButton: Button = null
    let mutable bodyDye1Combo: ComboBox = null
    let mutable bodyDye1ClearButton: Button = null
    let mutable bodyDye2Combo: ComboBox = null
    let mutable bodyDye2ClearButton: Button = null
    let mutable handSlotCombo: ListBox = null
    let mutable handClearButton: Button = null
    let mutable handDye1Combo: ComboBox = null
    let mutable handDye1ClearButton: Button = null
    let mutable handDye2Combo: ComboBox = null
    let mutable handDye2ClearButton: Button = null
    let mutable legsSlotCombo: ListBox = null
    let mutable legsClearButton: Button = null
    let mutable legsDye1Combo: ComboBox = null
    let mutable legsDye1ClearButton: Button = null
    let mutable legsDye2Combo: ComboBox = null
    let mutable legsDye2ClearButton: Button = null
    let mutable feetSlotCombo: ListBox = null
    let mutable feetClearButton: Button = null
    let mutable feetDye1Combo: ComboBox = null
    let mutable feetDye1ClearButton: Button = null
    let mutable feetDye2Combo: ComboBox = null
    let mutable feetDye2ClearButton: Button = null

    let mutable headSlotSearchBox: TextBox = null
    let mutable bodySlotSearchBox: TextBox = null
    let mutable handSlotSearchBox: TextBox = null
    let mutable legsSlotSearchBox: TextBox = null
    let mutable feetSlotSearchBox: TextBox = null

    let mutable headSearchClearButton: Button = null
    let mutable bodySearchClearButton: Button = null
    let mutable handSearchClearButton: Button = null
    let mutable legsSearchClearButton: Button = null
    let mutable feetSearchClearButton: Button = null

    let mutable skinColorSwatchesControl: ItemsControl = null
    let mutable hairColorSwatchesControl: ItemsControl = null
    let mutable highlightsColorSwatchesControl: ItemsControl = null
    let mutable eyeColorSwatchesControl: ItemsControl = null
    let mutable lipColorSwatchesControl: ItemsControl = null
    let mutable tattooColorSwatchesControl: ItemsControl = null

    let mutable uiEyePalette: swatchOption list = []
    let mutable uiLipDark: swatchOption list = []
    let mutable uiLipLight: swatchOption list = []
    let mutable uiTattoo: swatchOption list = []
    let mutable uiFaceDark: swatchOption list = []
    let mutable uiFaceLight: swatchOption list = []
    let mutable uiSkinPalette: swatchOption list = []
    let mutable uiHairPalette: swatchOption list = []
    let mutable uiHighlightPalette: swatchOption list = []

    let mutable highlightEnableControl: CheckBox = null
    let mutable lipRadioDarkControl: RadioButton = null
    let mutable lipRadioLightControl: RadioButton = null
    let mutable lipRadioNoneControl: RadioButton = null

    let mutable filterOpenButton: Button = null
    let mutable splitPane: SplitView = null
    let mutable filterScroller: ScrollViewer = null

    let mutable exportButton: MenuItem = null

    let mutable veldridRenderView: VeldridView option = None

    do
        this.InitializeComponent()
        this.FindGuiControls()

        viewerControl.DataContext <- viewModel
        this.DataContext <- viewModel

        //viewModel.FSharpPropertyChanged.Add(fun args ->
        //    if args.PropertyName = "GloballyFilteredGear" then
        //        allGearCache <- viewModel.GloballyFilteredGear
        //        this.UpdateAllSlotListsFromLocalCache()
        //)

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
            ) |> ignore

        this.Loaded.Add(fun _ ->
            match viewModel :> IVeldridWindowModel with
            | vm ->
                match vm.Render with
                | :? VeldridView as render ->
                    veldridRenderView <- Some render
                    render.AttachInputHandlers(inputOverlay)
                    this.InitializeApplicationAsync(render) |> Async.StartImmediate
                    viewModel.InitializeDataAsync(render) |> Async.StartImmediate
                | _ -> ()
        )
    member private this.InitializeComponent() =
#if DEBUG
        //this.AttachDevTools()
#endif
        AvaloniaXamlLoader.Load(this)

    member private this.FindGuiControls() =
        viewerControl <- this.FindControl<EmbeddedWindowVeldrid>("ViewerControl")
        inputOverlay <- this.FindControl<Border>("InputOverlay")
        raceSelector <- this.FindControl<ComboBox>("RaceSelector")
        clanSelector <- this.FindControl<ComboBox>("ClanSelector")
        genderSelector <- this.FindControl<ComboBox>("GenderSelector")
        submitCharacterButton <- this.FindControl<Button>("SubmitCharacter")
        clearAllButton <- this.FindControl<Button>("ClearAll")
        hairSelector <- this.FindControl<ComboBox>("HairSelector")
        faceSelector <- this.FindControl<ComboBox>("FaceSelector")
        earSelector <- this.FindControl<ComboBox>("EarSelector")
        tailSelector <- this.FindControl<ComboBox>("TailSelector")
        headSlotCombo <- this.FindControl<ListBox>("HeadSlot"); headClearButton <- this.FindControl<Button>("HeadClear")
        headDye1Combo <- this.FindControl<ComboBox>("HeadDye1"); headDye1ClearButton <- this.FindControl<Button>("HeadDye1Clear")
        headDye2Combo <- this.FindControl<ComboBox>("HeadDye2"); headDye2ClearButton <- this.FindControl<Button>("HeadDye2Clear")
        bodySlotCombo <- this.FindControl<ListBox>("BodySlot"); bodyClearButton <- this.FindControl<Button>("BodyClear")
        bodyDye1Combo <- this.FindControl<ComboBox>("BodyDye1"); bodyDye1ClearButton <- this.FindControl<Button>("BodyDye1Clear")
        bodyDye2Combo <- this.FindControl<ComboBox>("BodyDye2"); bodyDye2ClearButton <- this.FindControl<Button>("BodyDye2Clear")
        handSlotCombo <- this.FindControl<ListBox>("HandSlot"); handClearButton <- this.FindControl<Button>("HandClear")
        handDye1Combo <- this.FindControl<ComboBox>("HandDye1"); handDye1ClearButton <- this.FindControl<Button>("HandDye1Clear")
        handDye2Combo <- this.FindControl<ComboBox>("HandDye2"); handDye2ClearButton <- this.FindControl<Button>("HandDye2Clear")
        legsSlotCombo <- this.FindControl<ListBox>("LegsSlot"); legsClearButton <- this.FindControl<Button>("LegClear")
        legsDye1Combo <- this.FindControl<ComboBox>("LegDye1"); legsDye1ClearButton <- this.FindControl<Button>("LegDye1Clear")
        legsDye2Combo <- this.FindControl<ComboBox>("LegDye2"); legsDye2ClearButton <- this.FindControl<Button>("LegDye2Clear")
        feetSlotCombo <- this.FindControl<ListBox>("FeetSlot"); feetClearButton <- this.FindControl<Button>("FeetClear")
        feetDye1Combo <- this.FindControl<ComboBox>("FeetDye1"); feetDye1ClearButton <- this.FindControl<Button>("FeetDye1Clear")
        feetDye2Combo <- this.FindControl<ComboBox>("FeetDye2"); feetDye2ClearButton <- this.FindControl<Button>("FeetDye2Clear")

        headSlotSearchBox <- this.FindControl<TextBox>("HeadSlotSearch")
        bodySlotSearchBox <- this.FindControl<TextBox>("BodySlotSearch")
        handSlotSearchBox <- this.FindControl<TextBox>("HandSlotSearch")
        legsSlotSearchBox <- this.FindControl<TextBox>("LegsSlotSearch")
        feetSlotSearchBox <- this.FindControl<TextBox>("FeetSlotSearch")

        headSearchClearButton <- this.FindControl<Button>("HeadSearchClear")
        bodySearchClearButton <- this.FindControl<Button>("BodySearchClear")
        handSearchClearButton <- this.FindControl<Button>("HandSearchClear")
        legsSearchClearButton <- this.FindControl<Button>("LegsSearchClear")
        feetSearchClearButton <- this.FindControl<Button>("FeetSearchClear")

        skinColorSwatchesControl <- this.FindControl<ItemsControl>("SkinColorSwatches")
        hairColorSwatchesControl <- this.FindControl<ItemsControl>("HairColorSwatches")
        highlightsColorSwatchesControl <- this.FindControl<ItemsControl>("HighlightColorSwatches")
        eyeColorSwatchesControl <- this.FindControl<ItemsControl>("EyeColorSwatches")
        lipColorSwatchesControl <- this.FindControl<ItemsControl>("LipColorSwatches")
        tattooColorSwatchesControl <- this.FindControl<ItemsControl>("TattooColorSwatches")

        highlightEnableControl <- this.FindControl<CheckBox>("HighlightsCheckbox")
        lipRadioDarkControl <- this.FindControl<RadioButton>("DarkLip")
        lipRadioLightControl <- this.FindControl<RadioButton>("LightLip")
        lipRadioNoneControl <- this.FindControl<RadioButton>("NoneLip")

        exportButton <- this.FindControl<MenuItem>("ExportCommand")

        filterOpenButton <- this.FindControl<Button>("FilterOpenButton")
        splitPane <- this.FindControl<SplitView>("FilterPanel")
        filterScroller <- this.FindControl<ScrollViewer>("FilterScroller")



    member private this.UpdateSubmitButtonState() =
        let raceOk = selectedRaceNameOpt.IsSome
        let genderOk = selectedGenderNameOpt.IsSome
        let clanOk = selectedClanNameOpt.IsSome
        submitCharacterButton.IsEnabled <- raceOk && genderOk && clanOk
        clearAllButton.IsEnabled <- raceOk && genderOk && clanOk

    member private this.UpdateAllSlotListsFromLocalCache() =
        let updateSlot (slotList: ListBox) (gearCategory: string) (searchTerm: string) =
            let filteredList =
                allGearCache
                |> List.filter (fun m -> m.Item.SecondaryCategory = gearCategory)
                |> List.filter (fun m ->
                    if String.IsNullOrWhiteSpace(searchTerm) then true
                    else m.Item.Name.ToLower().Contains(searchTerm.ToLower())
                )
            slotList.ItemsSource <- filteredList

        updateSlot headSlotCombo "Head" headSlotSearchBox.Text
        updateSlot bodySlotCombo "Body" bodySlotSearchBox.Text
        updateSlot handSlotCombo "Hands" handSlotSearchBox.Text
        updateSlot legsSlotCombo "Legs" legsSlotSearchBox.Text
        updateSlot feetSlotCombo "Feet" feetSlotSearchBox.Text

    member private this.UpdateDyeChannelsForItem(item: IItemModel, itemSlot: EquipmentSlot, tx: ModTransaction) =
        task {
            let mutable dyeChannel1 = false
            let mutable dyeChannel2 = false

            let! dyeModel = TTModelLoader.loadTTModel item currentCharacterRace itemSlot
                            |> Async.AwaitTask

            for matName in dyeModel.Materials do
                // *** FIXED HERE ***
                let! material = (TTModelLoader.resolveMtrl dyeModel matName item tx |> Async.StartAsTask)
                match material.ColorSetDyeData.Length with
                | len when len >= 128 ->
                    for i in 0 .. 31 do
                        let offset = i * 4
                        let stainId = material.ColorSetDyeData[offset]
                        let repeat = material.ColorSetDyeData[offset + 3]
                        if stainId > 0uy then
                            if repeat < 8uy then dyeChannel1 <- true
                            else dyeChannel2 <- true
                | 32 ->
                    for i in 0 .. 31 do
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
        render: VeldridView) : Async<unit> =
        async {
            this.IncrementBusyCounter()
            let helperItem = item :?> XivGear
            try
                let mutable resetDye = true
                match lastSelectedGearItem.TryGetValue(eqSlot) with
                | true, previousItem when obj.ReferenceEquals(item, previousItem) ->
                    resetDye <- false
                | true, previousItem ->
                    ()
                | false, _ ->
                    ()

                lastSelectedGearItem[eqSlot] <- item

                let! (ch1Enabled, ch2Enabled) = this.UpdateDyeChannelsForItem(item, eqSlot, currentTransaction) |> Async.AwaitTask
                dye1Combo.IsEnabled <- ch1Enabled
                dye1ClearButton.IsEnabled <- ch1Enabled
                dye1Combo.ItemsSource <- if ch1Enabled then dyeListCache else []

                dye2Combo.IsEnabled <- ch2Enabled
                dye2ClearButton.IsEnabled <- ch2Enabled
                dye2Combo.ItemsSource <- if ch2Enabled then dyeListCache else []

                let currentDye1Index = dye1Combo.SelectedIndex
                let currentDye2Index = dye2Combo.SelectedIndex

                if resetDye then
                    dye1Combo.SelectedIndex <- -1
                    dye2Combo.SelectedIndex <- -1
                    do! this.ExecuteAssignTriggerAsync(render, eqSlot, item, currentCharacterRace, -1, -1, modelColors)
                else
                    let dye1ToApply = if currentDye1Index >= 0 then currentDye1Index else -1
                    let dye2ToApply = if currentDye2Index >= 0 then currentDye2Index else -1
                    do! this.ExecuteAssignTriggerAsync(render, eqSlot, item, currentCharacterRace, dye1ToApply, dye2ToApply, modelColors)
            finally
                this.DecrementBusyCounter()
        }

    member private this.updateModelWithSelectedColor(palette: paletteOptions, index: int, render: VeldridView) =
        Async.StartImmediate(
            async {
                try
                    
                    let! renderPalette = DataHelpers.getColorPalette modelColorId palette |> Async.AwaitTask
                    let selectedColor = DataHelpers.vec4ToDXColor renderPalette.[index]
                    printfn $"Selected Color Value: [{selectedColor.R}, {selectedColor.G}, {selectedColor.B}, {selectedColor.A}]"
                    match int palette with
                    | 15 ->
                        modelColors.HairColor <- selectedColor
                    | 0 ->
                        modelColors.HairHighlightColor <- Nullable selectedColor
                    | 1 ->
                        modelColors.EyeColor <- selectedColor
                    | 2 
                    | 3 ->
                        modelColors.LipColor <- selectedColor
                    | 4 ->
                        modelColors.TattooColor <- selectedColor
                    | 14 ->
                        modelColors.SkinColor <- selectedColor
                    | _ -> ()
                
                    do! this.OnSubmitCharacter(render)
                finally
                    ()
            }
        )
        

    member private this.PaletteSwatchPointerPressed (sender: obj) (args: PointerPressedEventArgs) (uiPaletteKey: paletteOptions) (render: VeldridView) =
        match args.Source with
        | :? Border as clickedBorder when (clickedBorder.DataContext :? swatchOption) ->
            let swatchData = clickedBorder.DataContext :?> swatchOption
            let selectedIndex = swatchData.Index

            match selectedSwatchBorders.TryGetValue(uiPaletteKey) with
            | true, Some previousSelectedBorder when not (obj.ReferenceEquals(previousSelectedBorder, clickedBorder)) ->
                previousSelectedBorder.BorderBrush <- SolidColorBrush(Colors.DarkGray)
                previousSelectedBorder.BorderThickness <- Thickness(0.0)
            | _ -> ()

            clickedBorder.BorderBrush <- SolidColorBrush(Colors.Gold)
            clickedBorder.BorderThickness <- Thickness(2.0)

            selectedSwatchBorders[uiPaletteKey] <- Some clickedBorder

            let renderPaletteOptionForModel =
                match uiPaletteKey with
                | paletteOptions.UISkin -> paletteOptions.RenderSkin
                | paletteOptions.UIHair -> paletteOptions.RenderHair
                | paletteOptions.UIHighlights -> paletteOptions.RenderHighlights
                | paletteOptions.UIEyeColor -> paletteOptions.RenderEyeColor
                | paletteOptions.UILipDark -> paletteOptions.RenderLipDark
                | paletteOptions.UILipLight -> paletteOptions.RenderLipLight
                | paletteOptions.UITattoo -> paletteOptions.RenderTattoo
                | paletteOptions.UIFaceDark -> paletteOptions.RenderFaceDark
                | paletteOptions.UIFaceLight -> paletteOptions.RenderFaceLight
                | _ -> uiPaletteKey

            this.updateModelWithSelectedColor(renderPaletteOptionForModel, selectedIndex, render)

            args.Handled <- true
        | _ -> ()

    member private this.HandleDyeSelectionChanged(
        item: IItemModel, eqSlot: EquipmentSlot,
        dye1Combo: ComboBox, dye2Combo: ComboBox, render: VeldridView) =
        let dye1Idx = if dye1Combo.IsEnabled && dye1Combo.SelectedIndex >=0 then dye1Combo.SelectedIndex else -1
        let dye2Idx = if dye2Combo.IsEnabled && dye2Combo.SelectedIndex >=0 then dye2Combo.SelectedIndex else -1
        if dye1Idx >= 0 || dye2Idx >= 0 then
             this.ExecuteAssignTriggerAsync(render, eqSlot, item, currentCharacterRace, dye1Idx, dye2Idx, modelColors) |> Async.StartImmediate

    member private this.ClearGearSlot(
        slotCombo: ListBox, eqSlot: EquipmentSlot, gearList: FilterGear list,
        dye1Combo: ComboBox, dye1ClearButton: Button,
        dye2Combo: ComboBox, dye2ClearButton: Button, render: VeldridView) =

        let smallClothesNamePart =
            match eqSlot with
            | EquipmentSlot.Body -> "Body" | EquipmentSlot.Hands -> "Hands"
            | EquipmentSlot.Legs -> "Legs" | EquipmentSlot.Feet -> "Feet"
            | _ -> null

        let emperorsNewNamePart =
            match eqSlot with
            | EquipmentSlot.Body -> "Robe" | EquipmentSlot.Hands -> "Gloves"
            | EquipmentSlot.Legs -> "Breeches" | EquipmentSlot.Feet -> "Boots"
            | _ -> null

        // *** FIXED HERE ***
        let defaultIndex =
            if isNull smallClothesNamePart then -1
            else
                match gearList |> List.tryFindIndex (fun g -> g.Item.Name.Contains("SmallClothes") && g.Item.Name.Contains(smallClothesNamePart)) with
                | Some idx -> 
                    idx
                | None ->
                    match gearList |> List.tryFindIndex (fun g -> g.Item.Name.Contains("Emperor's") && g.Item.Name.Contains(emperorsNewNamePart)) with
                    | Some idx -> idx
                    | None -> -1

        lastSelectedGearItem.Remove(eqSlot) |> ignore

        match defaultIndex with
        | i when i >= 0 -> slotCombo.SelectedIndex <- i
        | _ ->
            render.ClearGearSlot eqSlot
            slotCombo.SelectedIndex <- -1
            dye1Combo.SelectedIndex <- -1
            dye2Combo.SelectedIndex <- -1
            dye1Combo.IsEnabled <- false; dye1ClearButton.IsEnabled <- false; dye1Combo.ItemsSource <- System.Linq.Enumerable.Empty<string>()
            dye2Combo.IsEnabled <- false; dye2ClearButton.IsEnabled <- false; dye2Combo.ItemsSource <- System.Linq.Enumerable.Empty<string>()

    member private this.ClearDyeChannel(
        item: IItemModel, eqSlot: EquipmentSlot, channelToClear: int,
        dye1Combo: ComboBox, dye2Combo: ComboBox, render: VeldridView) =
        if channelToClear = 1 then dye1Combo.SelectedIndex <- -1
        elif channelToClear = 2 then dye2Combo.SelectedIndex <- -1
        this.HandleDyeSelectionChanged(item, eqSlot, dye1Combo, dye2Combo, render)        

    member private this.AttachEventHandlers(render: VeldridView) =

        this.SizeChanged.Add(fun _ ->
            viewModel.WindowHeight <- this.Bounds.Height
        )        

        raceSelector.SelectionChanged.Add(fun _ ->
            match raceSelector.SelectedValue with
            | :? ComboOption as selected ->
                selectedRaceNameOpt <- Some selected.Value
                this.UpdateSubmitButtonState()
            | _ -> selectedRaceNameOpt <- None; this.UpdateSubmitButtonState()
        )
        clanSelector.SelectionChanged.Add(fun _ ->
            match clanSelector.SelectedValue with
            | :? string as clanStr -> selectedClanNameOpt <- Some clanStr
            | :? ComboOption as clanCombo -> selectedClanNameOpt <- Some clanCombo.Value
            | _ -> selectedClanNameOpt <- None
            this.UpdateSubmitButtonState()
        )
        genderSelector.SelectionChanged.Add(fun _ ->
            match genderSelector.SelectedValue with
            | :? ComboOption as genderStr -> selectedGenderNameOpt <- Some genderStr.Value
            | _ -> selectedGenderNameOpt <- None
            this.UpdateSubmitButtonState()
        )

        skinColorSwatchesControl.AddHandler(
            InputElement.PointerPressedEvent,
            (fun sender args -> this.PaletteSwatchPointerPressed sender args paletteOptions.UISkin render),
            RoutingStrategies.Bubble
        )

        hairColorSwatchesControl.AddHandler(
            InputElement.PointerPressedEvent,
            (fun sender args -> this.PaletteSwatchPointerPressed sender args paletteOptions.UIHair render),
            RoutingStrategies.Bubble
        )

        highlightsColorSwatchesControl.AddHandler(
            InputElement.PointerPressedEvent,
            (fun sender args -> this.PaletteSwatchPointerPressed sender args paletteOptions.UIHighlights render),
            RoutingStrategies.Bubble
        )

        eyeColorSwatchesControl.AddHandler(
            InputElement.PointerPressedEvent,
            (fun sender args -> this.PaletteSwatchPointerPressed sender args paletteOptions.UIEyeColor render),
            RoutingStrategies.Bubble
        )

        lipColorSwatchesControl.AddHandler(
            InputElement.PointerPressedEvent,
            (fun sender args -> 
                if lipRadioDarkControl.IsChecked.GetValueOrDefault(false) then
                    this.PaletteSwatchPointerPressed sender args paletteOptions.UILipDark render
                elif lipRadioLightControl.IsChecked.GetValueOrDefault(false) then
                    this.PaletteSwatchPointerPressed sender args paletteOptions.UILipLight render
                else
                    modelColors.LipColor <- SharpDX.Color(0uy)
            ),
            RoutingStrategies.Bubble
        )

        tattooColorSwatchesControl.AddHandler(
            InputElement.PointerPressedEvent,
            (fun sender args -> this.PaletteSwatchPointerPressed sender args paletteOptions.UITattoo render),
            RoutingStrategies.Bubble
        )

        highlightEnableControl.IsCheckedChanged.Add( fun _ ->
            match highlightEnableControl.IsChecked.GetValueOrDefault(false) with
            | true -> 
                highlightsColorSwatchesControl.ItemsSource <- uiHighlightPalette
            | _ -> 
                highlightsColorSwatchesControl.ItemsSource <- []
        )

        lipRadioDarkControl.IsCheckedChanged.Add(fun _ ->
            match lipRadioDarkControl.IsChecked.GetValueOrDefault(false) with
            | true ->
                lipColorSwatchesControl.ItemsSource <- uiLipDark
            | _ -> ()
        )

        lipRadioLightControl.IsCheckedChanged.Add(fun _ ->
            match lipRadioLightControl.IsChecked.GetValueOrDefault(false) with
            | true ->
                lipColorSwatchesControl.ItemsSource <- uiLipLight
            | _ -> ()
        )

        lipRadioNoneControl.IsCheckedChanged.Add(fun _ ->
            match lipRadioNoneControl.IsChecked.GetValueOrDefault(false) with
            | true ->
                lipColorSwatchesControl.IsEnabled <- false
                modelColors.LipColor <- SharpDX.Color(0uy)
                do this.OnSubmitCharacter(render) |> ignore
            | _ ->
                lipColorSwatchesControl.IsEnabled <- true                
        )
        clearAllButton.Click.Add(fun _ ->
            this.ClearAllSlots(render)
        )

        filterOpenButton.Click.Add(fun _ ->
            splitPane.IsPaneOpen <- not splitPane.IsPaneOpen
            match filterScroller.VerticalScrollBarVisibility with
            | ScrollBarVisibility.Hidden -> filterScroller.VerticalScrollBarVisibility <- ScrollBarVisibility.Visible
            | ScrollBarVisibility.Visible -> filterScroller.VerticalScrollBarVisibility <- ScrollBarVisibility.Hidden
            | _ -> ()
        )

        let createCraftingList () : ResizeArray<string> =
            let tempList = ResizeArray<string>()
            let emptyGear: FilterGear =
                {
                    Item = Unchecked.defaultof<XivGear>
                    ExdRow = Unchecked.defaultof<xivModdingFramework.Exd.FileTypes.Ex.ExdRow>
                    ItemLevel = 0
                    EquipLevel = 0
                    EquipRestriction = Unchecked.defaultof<EquipRestriction>
                    EquippableBy = Unchecked.defaultof<Set<Job>>
                    CraftingDetails = []
                }
            let headItem = 
                match headSlotCombo.SelectedItem with
                | :? FilterGear as gear -> gear
                | _ -> emptyGear
            let bodyItem =
                match bodySlotCombo.SelectedItem with
                | :? FilterGear as gear -> gear
                | _ -> emptyGear
            let handItem =
                match handSlotCombo.SelectedItem with
                | :? FilterGear as gear -> gear
                | _ -> emptyGear
            let legsItem =
                match legsSlotCombo.SelectedItem with
                | :? FilterGear as gear -> gear
                | _ -> emptyGear
            let feetItem =
                match feetSlotCombo.SelectedItem with
                | :? FilterGear as gear -> gear
                | _ -> emptyGear

            let createListString (item: FilterGear) : string option =
                if item.CraftingDetails.Length < 1 then
                    None
                else
                    Some $"{item.Item.ExdID},null,1"

            let addString (string: string) =
                tempList.Add(string)

            let headString = createListString headItem
            let bodyString = createListString bodyItem
            let handString = createListString handItem
            let legsString = createListString legsItem
            let feetString = createListString feetItem

            do
                match headString with
                | Some string -> addString string
                | None -> ()
                match bodyString with
                | Some string -> addString string
                | None -> ()
                match handString with
                | Some string -> addString string
                | None -> ()
                match legsString with
                | Some string -> addString string
                | None -> ()
                match feetString with
                | Some string -> addString string
                | None -> ()

            tempList


        exportButton.Click.Add(fun _ ->
            let craftList = createCraftingList()
            if craftList.Count < 1 then () else
                let finalString = String.Join(";", craftList)
                let stringBase64 =
                    finalString
                    |> Encoding.UTF8.GetBytes
                    |> Convert.ToBase64String
                let tcURL = $"https://ffxivteamcraft.com/import/{stringBase64}"
                Process.Start(ProcessStartInfo(tcURL, UseShellExecute = true)) |> ignore
        )

        let setupCharPartSelector (selector: ComboBox) (partSlot: EquipmentSlot) (getPartList: unit -> XivCharacter list) =
            selector.SelectionChanged.Add(fun _ ->
                let idx = selector.SelectedIndex
                let parts = getPartList()
                if idx >= 0 && idx < parts.Length then
                    this.ExecuteAssignTriggerAsync(render, partSlot, parts[idx], currentCharacterRace, -1, -1, modelColors) 
                    |> Async.StartImmediate
            )
        setupCharPartSelector hairSelector EquipmentSlot.Hair (fun () -> currentHairList)
        setupCharPartSelector faceSelector EquipmentSlot.Face (fun () -> currentFaceList)
        setupCharPartSelector earSelector EquipmentSlot.Ear (fun () -> currentEarList)
        setupCharPartSelector tailSelector EquipmentSlot.Tail (fun () -> currentTailList)

        let setupGearSlot (
            slotCombo: ListBox, clearButton: Button,
            dye1Combo: ComboBox, dye1ClearButton: Button,
            dye2Combo: ComboBox, dye2ClearButton: Button,
            eqSlot: EquipmentSlot, gearCategory: string) =

            //let getGearList() = allGearCache |> List.filter (fun m -> m.Item.SecondaryCategory = gearCategory)
            //slotCombo.ItemsSource <- getGearList()

            slotCombo.SelectionChanged.Add(fun _ ->
                if slotCombo.SelectedItem <> null then
                    let selectedItem = slotCombo.SelectedItem :?> FilterGear
                    do this.HandleGearSelectionChanged(selectedItem.Item, eqSlot, dye1Combo, dye1ClearButton, dye2Combo, dye2ClearButton, render)|> Async.StartImmediate |> ignore
            )
            //clearButton.Click.Add(fun _ ->
                //this.ClearGearSlot(slotCombo, eqSlot, getGearList(), dye1Combo, dye1ClearButton, dye2Combo, dye2ClearButton, render)
            //)
            dye1ClearButton.Click.Add(fun _ ->
                if slotCombo.SelectedItem <> null then
                    let selectedItem = slotCombo.SelectedItem :?> FilterGear
                    this.ClearDyeChannel(selectedItem.Item, eqSlot, 1, dye1Combo, dye2Combo, render)
            )
            dye2ClearButton.Click.Add(fun _ ->
                if slotCombo.SelectedItem <> null then
                    let selectedItem = slotCombo.SelectedItem :?> FilterGear
                    this.ClearDyeChannel(selectedItem.Item, eqSlot, 2, dye1Combo, dye2Combo, render)
            )
            dye1Combo.SelectionChanged.Add(fun _ ->
                if slotCombo.SelectedItem <> null then
                    let selectedItem = slotCombo.SelectedItem :?> FilterGear
                    if dye1Combo.SelectedIndex >= 0 then
                       this.HandleDyeSelectionChanged(selectedItem.Item, eqSlot, dye1Combo, dye2Combo, render)
            )
            dye2Combo.SelectionChanged.Add(fun _ ->
                if slotCombo.SelectedItem <> null then
                    let selectedItem = slotCombo.SelectedItem :?> FilterGear
                    if dye2Combo.SelectedIndex >= 0 then
                        this.HandleDyeSelectionChanged(selectedItem.Item, eqSlot, dye1Combo, dye2Combo, render)
            )

        let clearSearch (searchBox: TextBox) =
            searchBox.Text <- String.Empty

        headSearchClearButton.Click.Add(fun _ ->
            clearSearch headSlotSearchBox
        )

        bodySearchClearButton.Click.Add(fun _ ->
            clearSearch bodySlotSearchBox
        )

        handSearchClearButton.Click.Add(fun _ ->
            clearSearch handSlotSearchBox
        )

        legsSearchClearButton.Click.Add(fun _ ->
            clearSearch legsSlotSearchBox
        )

        feetSearchClearButton.Click.Add(fun _ ->
            clearSearch feetSlotSearchBox
        )        

        setupGearSlot (headSlotCombo, headClearButton, headDye1Combo, headDye1ClearButton, headDye2Combo, headDye2ClearButton, EquipmentSlot.Head, "Head")
        setupGearSlot (bodySlotCombo, bodyClearButton, bodyDye1Combo, bodyDye1ClearButton, bodyDye2Combo, bodyDye2ClearButton, EquipmentSlot.Body, "Body")
        setupGearSlot (handSlotCombo, handClearButton, handDye1Combo, handDye1ClearButton, handDye2Combo, handDye2ClearButton, EquipmentSlot.Hands, "Hands")
        setupGearSlot (legsSlotCombo, legsClearButton, legsDye1Combo, legsDye1ClearButton, legsDye2Combo, legsDye2ClearButton, EquipmentSlot.Legs, "Legs")
        setupGearSlot (feetSlotCombo, feetClearButton, feetDye1Combo, feetDye1ClearButton, feetDye2Combo, feetDye2ClearButton, EquipmentSlot.Feet, "Feet")

        submitCharacterButton.Click.Add(fun _ -> 
            if not submitCharacterButton.IsEnabled then ()
            else
                submitCharacterButton.IsEnabled <- false
                
                let operation =
                    async {
                        try
                            do! this.OnSubmitCharacter(render)
                        finally
                            Dispatcher.UIThread.Post(fun () ->
                                this.UpdateSubmitButtonState()
                            )
                    }
                Async.StartImmediate(operation)
        )

    member private this.ClearAllSlots(render: VeldridView) =
        let getGearListForCategory cat = allGearCache |> List.filter (fun m -> m.Item.SecondaryCategory = cat)

        modelColors <- ModelTexture.GetCustomColors()
        selectedSwatchBorders.Clear()
        if hairSelector <> null then hairSelector.SelectedIndex <- 0
        if faceSelector <> null then faceSelector.SelectedIndex <- 0
        if earSelector <> null && earSelector.SelectedIndex >= 0 then earSelector.SelectedIndex <- 0
        if tailSelector <> null && tailSelector.SelectedIndex >= 0 then tailSelector.SelectedIndex <- 0
        this.ClearGearSlot(headSlotCombo, EquipmentSlot.Head, getGearListForCategory "Head", headDye1Combo, headDye1ClearButton, headDye2Combo, headDye2ClearButton, render)
        this.ClearGearSlot(bodySlotCombo, EquipmentSlot.Body, getGearListForCategory "Body", bodyDye1Combo, bodyDye1ClearButton, bodyDye2Combo, bodyDye2ClearButton, render)
        this.ClearGearSlot(handSlotCombo, EquipmentSlot.Hands, getGearListForCategory "Hands", handDye1Combo, handDye1ClearButton, handDye2Combo, handDye2ClearButton, render)
        this.ClearGearSlot(legsSlotCombo, EquipmentSlot.Legs, getGearListForCategory "Legs", legsDye1Combo, legsDye1ClearButton, legsDye2Combo, legsDye2ClearButton, render)
        this.ClearGearSlot(feetSlotCombo, EquipmentSlot.Feet, getGearListForCategory "Feet", feetDye1Combo, feetDye1ClearButton, feetDye2Combo, feetDye2ClearButton, render)

        this.OnSubmitCharacter(render)
        |> Async.StartImmediate

    member private this.OnSubmitCharacter(render: VeldridView) =
        async {
            this.IncrementBusyCounter()
            try
                let raceStr =
                    match selectedRaceNameOpt, selectedClanNameOpt, selectedGenderNameOpt with
                    | Some r, Some c, Some g when r = "Hyur" -> Some $"{r}_{c}_{g}"
                    | Some r, _, Some g when r <> "Hyur" -> Some $"{r}_{g}"
                    | _ -> None

                let idStr = 
                    match selectedRaceNameOpt, selectedClanNameOpt, selectedGenderNameOpt with
                    | Some r, Some c, Some g -> $"{r}_{c}_{g}"
                    | _ -> ""
                match Enum.TryParse<raceIds>(idStr) with
                | true, parsedRace -> modelColorId <- parsedRace
                | _ ->
                    ()                    

                match raceStr with
                | Some validRaceStr ->
                    match Enum.TryParse<XivRace>(validRaceStr) with
                    | true, parsedXivRace ->
                        currentCharacterRace <- parsedXivRace
                        render.clearCharacter()

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

                        let mutable allModelUpdateTasks: Async<unit> list = []

                        let assignSelectedOrDefault (parts: XivCharacter list) (slot: EquipmentSlot) (selector: ComboBox) =
                            if selector.IsEnabled then
                                let idx = selector.SelectedIndex
                                let itemToAssign =
                                    if idx >= 0 && idx < List.length parts then
                                        Some parts[idx]
                                    else
                                        List.tryHead parts
                            
                                match itemToAssign with
                                | Some item ->
                                    allModelUpdateTasks <- render.AssignTrigger(slot, item, parsedXivRace, -1, 01, modelColors) :: allModelUpdateTasks
                                | None -> ()

                        do assignSelectedOrDefault currentFaceList EquipmentSlot.Face faceSelector
                        do assignSelectedOrDefault currentHairList EquipmentSlot.Hair hairSelector
                        if currentEarList |> List.isEmpty |> not then do assignSelectedOrDefault currentEarList EquipmentSlot.Ear earSelector
                        if currentTailList |> List.isEmpty |> not then do assignSelectedOrDefault currentTailList EquipmentSlot.Tail tailSelector

                        let reselectIfPopulated (combo: ListBox) = if combo.SelectedIndex >=0 then let s = combo.SelectedIndex in combo.SelectedIndex <- -1; combo.SelectedIndex <- s
                        reselectIfPopulated headSlotCombo
                        reselectIfPopulated bodySlotCombo
                        reselectIfPopulated handSlotCombo
                        reselectIfPopulated legsSlotCombo
                        reselectIfPopulated feetSlotCombo

                        let setDefaultGear (combo: ListBox, category: string, nameFilter: string) =
                            //if combo.SelectedIndex <> null then () else
                            //    let gear = allGearCache |> List.filter(fun g -> g.Item.SecondaryCategory = category)
                            //    match gear |> List.tryFind(fun g -> g.Item.Name.Contains(nameFilter)) with
                            //    | Some idx -> combo.SelectedItem <- idx
                            //    | None -> 
                            //        match gear |> List.tryFind(fun g -> g.Item.Name.Contains("Emperor's")) with
                            //        | Some idx -> combo.SelectedItem <- idx
                            //        | None -> ()
                            match combo.SelectedItem with
                            | :? FilterGear as gear -> ()
                            | _ ->
                                //let gear = allGearCache |> List.filter(fun g -> g.Item.SecondaryCategory = category)
                                let list =
                                    combo.Items
                                    |> Seq.cast<FilterGear>
                                    |> Seq.toList
                                match list |> List.tryFind(fun g -> g.Item.Name.Contains(nameFilter)) with
                                | Some idx -> combo.SelectedItem <- idx
                                | None ->
                                    match list |> List.tryFind(fun g -> g.Item.Name.Contains("Emperor's")) with
                                    | Some idx -> combo.SelectedItem <- idx
                                    | None -> ()

                        setDefaultGear (bodySlotCombo, "Body", "SmallClothes")
                        setDefaultGear (handSlotCombo, "Hands", "SmallClothes")
                        setDefaultGear (legsSlotCombo, "Legs", "SmallClothes")
                        setDefaultGear (feetSlotCombo, "Feet", "SmallClothes")

                        if not (List.isEmpty allModelUpdateTasks) then
                            do! Async.Parallel(allModelUpdateTasks) |> Async.Ignore

                    
                    
                        let! getUiEyePalette = DataHelpers.getUIColorPalette modelColorId paletteOptions.UIEyeColor |> Async.AwaitTask
                        let! getUiLipDark = DataHelpers.getUIColorPalette modelColorId paletteOptions.UILipDark |> Async.AwaitTask
                        let! getUiLipLight = DataHelpers.getUIColorPalette modelColorId paletteOptions.UILipLight |> Async.AwaitTask
                        let! getUiTattoo = DataHelpers.getUIColorPalette modelColorId paletteOptions.UITattoo |> Async.AwaitTask
                        let! getUiFaceDark = DataHelpers.getUIColorPalette modelColorId paletteOptions.UIFaceDark |> Async.AwaitTask
                        let! getUiFaceLight = DataHelpers.getUIColorPalette modelColorId paletteOptions.UIFaceLight |> Async.AwaitTask
                        let! getUiSkinPalette = DataHelpers.getUIColorPalette modelColorId paletteOptions.UISkin |> Async.AwaitTask
                        let! getUiHairPalette = DataHelpers.getUIColorPalette modelColorId paletteOptions.RenderHair |> Async.AwaitTask
                        let! getUiHighlightPalette = DataHelpers.getUIColorPalette modelColorId paletteOptions.UIHighlights |> Async.AwaitTask

                        uiEyePalette <- getUiEyePalette
                        uiLipDark <- getUiLipDark
                        uiLipLight <- getUiLipLight
                        uiTattoo <- getUiTattoo
                        uiFaceDark <- getUiFaceDark
                        uiFaceLight <- getUiFaceLight
                        uiSkinPalette <- getUiSkinPalette
                        uiHairPalette <- getUiHairPalette
                        uiHighlightPalette <- getUiHighlightPalette

                        if skinColorSwatchesControl <> null then
                            skinColorSwatchesControl.ItemsSource <- uiSkinPalette
                        if hairColorSwatchesControl <> null then
                            hairColorSwatchesControl.ItemsSource <- uiHairPalette
                        if highlightEnableControl <> null && highlightEnableControl.IsChecked.GetValueOrDefault(false) && highlightsColorSwatchesControl <> null then
                            highlightsColorSwatchesControl.ItemsSource <- uiHighlightPalette
                        if eyeColorSwatchesControl <> null then
                            eyeColorSwatchesControl.ItemsSource <- uiEyePalette
                        if lipColorSwatchesControl <> null then
                            if lipRadioDarkControl <> null && lipRadioDarkControl.IsChecked.GetValueOrDefault(false) then
                                lipColorSwatchesControl.ItemsSource <- uiLipDark
                            elif lipRadioLightControl <> null && lipRadioLightControl.IsChecked.GetValueOrDefault(false) then
                                lipColorSwatchesControl.ItemsSource <- uiLipLight
                            else
                                lipColorSwatchesControl.ItemsSource <- []
                        if tattooColorSwatchesControl <> null then
                            tattooColorSwatchesControl.ItemsSource <- uiTattoo

                    | false, _ -> ()
                | None -> ()
            finally
                this.DecrementBusyCounter()
        }
    member private this.SetLoadingState (isLoading: bool) =
        Dispatcher.UIThread.Post(fun () ->
            match this.FindControl<ProgressBar>("ModelLoadingBar") with
            | loadingBar when loadingBar <> null ->
                loadingBar.IsIndeterminate <- isLoading
            | _ -> ()
        )

    member private this.IncrementBusyCounter() =
        lock busyLock (fun () ->
            busyOperationCount <- busyOperationCount + 1
            if busyOperationCount = 1 then
                this.SetLoadingState(true)
        )
    member private this.DecrementBusyCounter() =
        lock busyLock (fun () ->
            if busyOperationCount > 0 then
                busyOperationCount <- busyOperationCount - 1
            if busyOperationCount = 0 then
                this.SetLoadingState(false)
        )

    member private this.ExecuteAssignTriggerAsync
        (render: VeldridView, slot: EquipmentSlot, item: IItemModel,
        race: XivRace, dye1: int, dye2: int, currentModelColors: CustomModelColors) : Async<unit> =
        async {
            this.IncrementBusyCounter()
            try
                do! render.AssignTrigger(slot, item, race, dye1, dye2, currentModelColors)
            finally
                this.DecrementBusyCounter()
        }

    member private this.InitializeApplicationAsync(render: VeldridView) =
        async {

            let configPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Hydaelyn Clothiers", "config.json")
            let loadConfig () : Config option =
                if File.Exists(configPath) then
                    try JsonSerializer.Deserialize<Config>(File.ReadAllText(configPath)) |> Some
                    with ex -> None
                else None
            let saveConfig (cfg: Config) =
                try let dir = Path.GetDirectoryName(configPath)
                    if not (Directory.Exists(dir)) then Directory.CreateDirectory(dir) |> ignore
                    File.WriteAllText(configPath, JsonSerializer.Serialize(cfg))
                with ex -> ()
            let isValidGamePath (path: string): bool  =
                let expectedSuffix = Path.Combine("game", "sqpack", "ffxiv")
                not (String.IsNullOrWhiteSpace(path)) &&
                Directory.Exists(path) &&
                path.EndsWith(expectedSuffix, StringComparison.OrdinalIgnoreCase)

            let mutable gamePathFromConfigOrPrompt: string = ""
            let mutable setupSucceeded = false

            match loadConfig() with
            | Some config when isValidGamePath config.GamePath ->
                gamePathFromConfigOrPrompt <- config.GamePath
                setupSucceeded <- true
            | _ ->
                let promptWindow = GamePathPromptWindow()
                let! pathFromDialog = promptWindow.ShowDialog<string option>(this) |> Async.AwaitTask
                match pathFromDialog with
                | Some validPath ->
                    gamePathFromConfigOrPrompt <- Path.Combine(validPath, "game", "sqpack", "ffxiv")
                    saveConfig { GamePath = Path.Combine(validPath, "game", "sqpack", "ffxiv"); CrafterProfile = None; PatreonID = None }
                    setupSucceeded <- true
                | None ->
                    setupSucceeded <- false

            if not setupSucceeded then
                this.Close()
                return ()

            try

                let gameDataRootPath = gamePathFromConfigOrPrompt
                let info = xivModdingFramework.GameInfo(DirectoryInfo(gameDataRootPath), XivLanguage.English)
                XivCache.SetGameInfo(info) |> ignore
                viewModel.WindowHeight <- this.Bounds.Height

                currentTransaction <- ModTransaction.BeginReadonlyTransaction()
                let! chara = render.GetChara()
                allGearCache <- viewModel.GloballyFilteredGear
                allCharaCache <- chara
                let! dyes = DataHelpers.getDyeSwatches() |> Async.AwaitTask
                dyeListCache <- dyes

                this.UpdateSubmitButtonState()
                hairSelector.IsEnabled <- false; faceSelector.IsEnabled <- false
                earSelector.IsEnabled <- false; tailSelector.IsEnabled <- false
                filterScroller.VerticalScrollBarVisibility <- ScrollBarVisibility.Hidden
                
                this.AttachEventHandlers(render)

                match currentTransaction with
                | null -> ()
                | tx ->
                    tx.Dispose()
                    currentTransaction <- null

            with ex ->
                ()
        }
