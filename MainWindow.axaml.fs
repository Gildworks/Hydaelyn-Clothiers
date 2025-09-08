namespace fs_mdl_viewer

open System
open System.Numerics
open System.Diagnostics
open System.Text
open System.Text.Json
open System.IO

open Serilog

open Avalonia
open Avalonia.Controls
open Avalonia.Controls.Primitives
open Avalonia.VisualTree
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

type PaletteList = {
    PaletteColors: Vector4 list
    HairLightColor: Vector4 list
}

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
            let modelTask =
                try
                    Mdl.GetTTModel(item, characterRace)
                with ex ->
                    Log.Warning("Could not find model")
                    raise ex
            not modelTask.IsFaulted
        with _ ->
            false

    let getColorPalette (race: raceIds) (palette: paletteOptions) =
        task {
            Log.Information("Starting color palette acquisition")
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

            let numberOfBlocks =
                let colorList =
                    colorDataBytes
                    |> Seq.toList
                let totalRacialBytes = (colorList.Length - sharedOffset) / blockSize
                totalRacialBytes

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
            let hairLightColor =
                if (colorDataBytes |> Seq.length) >= (paletteStartIndex + paletteLength) * 4 && paletteLength > 0 && palette = paletteOptions.RenderHair then
                    colorDataBytes
                    |> Seq.chunkBySize 4
                    |> Seq.filter (fun chunk -> chunk.Length = 4)
                    |> Seq.skip paletteStartIndex
                    |> (fun seq ->
                        seq
                        |> Seq.mapi (fun i el -> i, el)
                        |> Seq.filter (fun (i, _) -> not (i % 2 = 0))
                        |> Seq.map snd
                    )
                    |> Seq.take paletteLength
                    |> Seq.map (fun chunk ->
                        Vector4(float32 chunk.[0], float32 chunk.[1], float32 chunk.[2], float32 chunk.[3])
                    )
                    |> List.ofSeq
                else
                    List.Empty
            Log.Information("Color palette acquired")
            return { PaletteColors = colors; HairLightColor = hairLightColor}
            
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

    let vec4ToLinearDXColor (input: Vector4) : SharpDX.Color =
        let normalizedSrgb = new Vector4(input.X / 255.0f, input.Y / 255.0f, input.Z / 255.0f, input.W / 255.0f)
        SharpDX.Color(
            srgbToLinear(normalizedSrgb.X),
            srgbToLinear(normalizedSrgb.Y),
            srgbToLinear(normalizedSrgb.Z),
            srgbToLinear(normalizedSrgb.W)
        )
    let vec4ToDXColor (input: Vector4) : SharpDX.Color =
        SharpDX.Color(
            byte (Math.Clamp(input.X, 0.0f, 255.0f)),
            byte (Math.Clamp(input.Y, 0.0f, 255.0f)),
            byte (Math.Clamp(input.Z, 0.0f, 255.0f)),
            byte (Math.Clamp(input.W, 0.0f, 255.0f))
        )

    let getUIColorPalette (race: raceIds) (palette: paletteOptions) =
        task {
            Log.Information("Getting UI color palette")
            let! vec4List = getColorPalette race palette |> Async.AwaitTask
            let uiColors =
                vec4List.PaletteColors
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
            Log.Information("UI Color palette acquired!")
            return avaloniaColors
        }


type MainWindow () as this =
    inherit Window ()
    let viewModel = new VeldridWindowViewModel()

    let mutable currentCharacterRace : XivRace = XivRace.Hyur_Midlander_Male
    let mutable currentTribe : XivSubRace = XivSubRace.Hyur_Midlander
    let mutable selectedRaceNameOpt: string option = Some "Hyur"
    let mutable selectedClanNameOpt: string option = Some "Midlander"
    let mutable selectedGenderNameOpt: string option = Some "Male"
    let mutable characterCustomizations: CharacterCustomizations =
        {
            Height = 50.0f
            BustSize = 50.0f
            FaceScale = 1.0f
            MuscleDefinition = 1.0f
        }
    let mutable userLanguage: XivLanguage = XivLanguage.English
    let mutable modelColors: CustomModelColors = ModelTexture.GetCustomColors()
    let mutable selectedSwatchBorders: System.Collections.Generic.Dictionary<paletteOptions, Border option> = System.Collections.Generic.Dictionary()
    let mutable modelColorId: raceIds = raceIds.AuRa_Xaela_Female

    let mutable disabledSlots: Map<EquipmentSlot, ListBox> = Map.empty

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

    let mutable bustSlider: Slider = null

    let mutable filterOpenButton: Button = null
    let mutable splitPane: SplitView = null
    let mutable filterScroller: ScrollViewer = null

    let mutable exportButton: MenuItem = null

    let mutable veldridRenderView: VeldridView option = None

    do
        this.InitializeComponent()
        this.FindGuiControls()

        Log.Information("Setting DataContext for window on thread {ThreadID}. UI Thread status: {IsUIThread}",
            System.Threading.Thread.CurrentThread.ManagedThreadId,
            Avalonia.Threading.Dispatcher.UIThread.CheckAccess()
        )

        viewerControl.DataContext <- viewModel
        this.DataContext <- viewModel

        Log.Information("wWindow DataContext set.")

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

        bustSlider <- this.FindControl<Slider>("BustSize")

        filterOpenButton <- this.FindControl<Button>("FilterOpenButton")
        splitPane <- this.FindControl<SplitView>("FilterPanel")
        filterScroller <- this.FindControl<ScrollViewer>("FilterScroller")

    member private this.UpdateSubmitButtonState() =
        let raceOk = selectedRaceNameOpt.IsSome
        let genderOk = selectedGenderNameOpt.IsSome
        let clanOk = selectedClanNameOpt.IsSome
        let cacheOk = XivCache.CacheWorkerEnabled
        submitCharacterButton.IsEnabled <- raceOk && genderOk && clanOk && cacheOk
        clearAllButton.IsEnabled <- raceOk && genderOk && clanOk && cacheOk

    member private this.DisableSlot(slot: ListBox) =
        slot.IsEnabled <- false

    member private this.EnableSlot(slot: EquipmentSlot) =
        let listBox: ListBox option = 
            match slot with
            | Head -> Some headSlotCombo
            | Body -> Some bodySlotCombo
            | Hands -> Some handSlotCombo
            | Legs -> Some legsSlotCombo
            | Feet -> Some feetSlotCombo
            | _ -> None
        if listBox.IsSome then
            listBox.Value.IsEnabled <- true

    member private this.UpdateSlotStates() =
        EquipmentSlot.all
        |> List.iter (fun slot ->
            match Map.tryFind slot disabledSlots with
            | Some listBox -> this.DisableSlot(listBox)
            | None -> this.EnableSlot(slot)
        )

    member private this.UpdateDyeChannelsForItem(item: IItemModel, itemSlot: EquipmentSlot, tx: ModTransaction) =
        task {
            Log.Information("Updating dye slots for {item}", item.Name)
            try
                let mutable dyeChannel1 = false
                let mutable dyeChannel2 = false

                let! dyeModel = TTModelLoader.loadTTModel item currentCharacterRace itemSlot
                                |> Async.AwaitTask

                for matName in dyeModel.Materials do
                    // *** FIXED HERE ***
                    let! material = (TTModelLoader.resolveMtrl dyeModel currentCharacterRace currentTribe matName item tx |> Async.StartAsTask)
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
            with ex ->
                Log.Error("Failed to update dye channels for {Item}", item.Name)
                return raise ex
        }

    member private this.HandleGearSelectionChanged(
        item: IItemModel, eqSlot: EquipmentSlot,
        dye1Combo: ComboBox, dye1ClearButton: Button,
        dye2Combo: ComboBox, dye2ClearButton: Button,
        render: VeldridView) : Async<unit> =
        async {
            this.IncrementBusyCounter()
            Log.Information("Adding {Gear} to model list", item.Name)
            let helperItem = item :?> XivGear
            Log.Information("Checking if any slots should be disabled...")

            if item.SecondaryCategory.Contains("Body") || item.SecondaryCategory.Contains("Legs") then
                disabledSlots <- Map.empty
                match item.SecondaryCategory with
                | c when c.Contains("Head") ->
                    disabledSlots <-
                        disabledSlots
                        |> Map.add Head headSlotCombo
                | c when c.Contains("Hands") ->
                    disabledSlots <-
                        disabledSlots
                        |> Map.add Hands handSlotCombo
                | c when c.Contains("Legs") && c.Contains("Body") ->
                    disabledSlots <-
                        disabledSlots
                        |> Map.add Legs legsSlotCombo
                | c when c.Contains("Feet") ->
                    disabledSlots <-
                        disabledSlots
                        |> Map.add Feet feetSlotCombo
                | _ -> ()
            do this.UpdateSlotStates()
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
                    try
                        do! this.ExecuteAssignTriggerAsync(render, eqSlot, item, currentCharacterRace, -1, -1, modelColors)
                    with ex ->
                        Log.Error("Failed to handle gear selection changing. {Message}", ex.Message)
                        raise ex
                else
                    let dye1ToApply = if currentDye1Index >= 0 then currentDye1Index else -1
                    let dye2ToApply = if currentDye2Index >= 0 then currentDye2Index else -1
                    try
                        do! this.ExecuteAssignTriggerAsync(render, eqSlot, item, currentCharacterRace, dye1ToApply, dye2ToApply, modelColors)
                    with ex ->
                        Log.Error("Failed to handle gear selection changing. {Message}", ex.Message)
                        raise ex
            finally
                this.DecrementBusyCounter()
        }

    member private this.updateModelWithSelectedColor(palette: paletteOptions, index: int, render: VeldridView) =
        Log.Information("Updating a model with a color")
        Async.StartImmediate(
            async {
                try                    
                    let! renderPalette = DataHelpers.getColorPalette modelColorId palette |> Async.AwaitTask
                    let selectedColor = //DataHelpers.vec4ToLinearDXColor renderPalette.[index]
                        match int palette with
                        | 0 | 4 | 14 | 15 ->
                            DataHelpers.vec4ToLinearDXColor renderPalette.PaletteColors.[index]
                        | _ ->
                            DataHelpers.vec4ToDXColor renderPalette.PaletteColors.[index]
                    let hairStrandsOriginal =
                        if renderPalette.HairLightColor.Length > 0 then
                            DataHelpers.vec4ToLinearDXColor renderPalette.HairLightColor.[index]
                        else
                            modelColors.LightColor
                    let hairStrands = SharpDX.Color(hairStrandsOriginal.R / 255uy, hairStrandsOriginal.G / 255uy, hairStrandsOriginal.B / 255uy, hairStrandsOriginal.A / 255uy)

                    match int palette with
                    | 15 ->
                        modelColors.HairColor <- selectedColor
                        if not (highlightEnableControl.IsChecked.GetValueOrDefault(false)) then
                            modelColors.HairHighlightColor <- Nullable selectedColor
                        modelColors.LightColor <- DataHelpers.vec4ToLinearDXColor renderPalette.HairLightColor.[index]
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
                
                    do! render.UpdateMaterialsForColors modelColors currentCharacterRace currentTribe
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

        let smallClothesNamePartOriginal =
            match eqSlot with
            | EquipmentSlot.Body -> "Body" | EquipmentSlot.Hands -> "Hands"
            | EquipmentSlot.Legs -> "Legs" | EquipmentSlot.Feet -> "Feet"
            | _ -> null

        let smallClothesNamePart =
            match smallClothesNamePartOriginal with
            | "Body" -> "Rumpf"
            | "Hands" -> "Hände"
            | "Legs" -> "Beine"
            | "Feet" -> "Füße"
            | _ -> "Error"

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
            | :? ComboOption as clanCombo ->
                selectedClanNameOpt <- Some clanCombo.Value
                match clanCombo.Value with
                | "Raen" -> currentTribe <- XivSubRace.AuRa_Raen
                | "Xaela" -> currentTribe <- XivSubRace.AuRa_Xaela
                | _ -> currentTribe <- XivSubRace.Hyur_Midlander
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
                    modelColors.LipColor <- xivModdingFramework.Models.ModelTextures.ModelTexture.GetCustomColors().LipColor
                    async {
                        do! this.OnSubmitCharacter(render)
                    } |> Async.StartImmediate
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
                modelColors.HairHighlightColor <- Nullable modelColors.HairColor
                async {
                    do! this.OnSubmitCharacter(render)
                } |> Async.StartImmediate
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
                let noneColor = xivModdingFramework.Models.ModelTextures.ModelTexture.GetCustomColors().LipColor
                if modelColors.LipColor = noneColor then
                    ()
                else
                    modelColors.LipColor <- noneColor
                    async {
                        do! this.OnSubmitCharacter(render)
                    } |> Async.StartImmediate
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
                | :? FilterGear as gear -> 
                    if gear.Item.Name.Contains("SmallClothes") then
                        None
                    else
                        Some gear
                | _ -> None
            let bodyItem =
                match bodySlotCombo.SelectedItem with
                | :? FilterGear as gear -> 
                    if gear.Item.Name.Contains("SmallClothes") then
                        None
                    else
                        Some gear
                | _ -> None
            let handItem =
                match handSlotCombo.SelectedItem with
                | :? FilterGear as gear -> 
                    if gear.Item.Name.Contains("SmallClothes") then
                        None
                    else
                        Some gear
                | _ -> None
            let legsItem =
                match legsSlotCombo.SelectedItem with
                | :? FilterGear as gear -> 
                    if gear.Item.Name.Contains("SmallClothes") then
                        None
                    else
                        Some gear
                | _ -> None
            let feetItem =
                match feetSlotCombo.SelectedItem with
                | :? FilterGear as gear -> 
                    if gear.Item.Name.Contains("SmallClothes") then
                        None
                    else
                        Some gear
                | _ -> None

            let createListString (item: FilterGear option) : string option =
                match item with
                | Some gear -> Some $"{gear.Item.ExdID}, null, 1"
                | None -> None

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
            eqSlot: EquipmentSlot, gearCategoryInput: string) =

            let gearCategory =
                match userLanguage with
                | XivLanguage.German ->
                    match gearCategoryInput with
                    | "Head" -> "Kopf"
                    | "Body" -> "Rumpf"
                    | "Hands" -> "Hände"
                    | "Legs" -> "Beine"
                    | "Feet" -> "Füße"
                    | _ -> "Error"
                | _ -> gearCategoryInput
            let getGearList() = allGearCache |> List.filter (fun m -> m.Item.SecondaryCategory = gearCategory)

            slotCombo.SelectionChanged.Add(fun _ ->
                if slotCombo.SelectedItem <> null then
                    let selectedItem = slotCombo.SelectedItem :?> FilterGear
                    do this.HandleGearSelectionChanged(selectedItem.Item, eqSlot, dye1Combo, dye1ClearButton, dye2Combo, dye2ClearButton, render)|> Async.StartImmediate |> ignore
            )
            clearButton.Click.Add(fun _ ->
                this.ClearGearSlot(slotCombo, eqSlot, getGearList(), dye1Combo, dye1ClearButton, dye2Combo, dye2ClearButton, render)
            )
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
    

        let handleSliderChange (slider: Slider) (onReleased: float32 -> unit) =
            let thumb =
                slider.GetVisualDescendants()
                |> Seq.tryPick (fun v ->
                    match v with
                    | :? Thumb as t -> Some t
                    | _ -> None
                )
            match thumb with
            | Some t ->
                t.DragCompleted.Add(fun _ ->
                    let finalValue = float32 slider.Value
                    onReleased finalValue
                )
            | None -> ()

        handleSliderChange bustSlider (fun finalValue ->
            async{
                characterCustomizations <- { characterCustomizations with BustSize = finalValue}
                do! this.OnSubmitCharacter(render)
            } |> Async.StartImmediate
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
            Log.Information("Submit logic finished entirely")
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
        do render.clearCharacter()
        async {
            let mutable allModelUpdateTasks: Async<unit> list = []
            this.IncrementBusyCounter()
            try
                try
                    Log.Information("Submit character button pressed, processing...")
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

                            let getParts (cat: string) : XivCharacter list option =
                                let partsList =
                                    try
                                        Some (DataHelpers.getCustomizableParts parsedXivRace cat allCharaCache currentCharacterRace)
                                    with ex ->
                                        None
                                partsList

                            let faceList =
                                match userLanguage with
                                | XivLanguage.German ->
                                    match getParts "Face" with
                                    | Some charaList when charaList.Length > 0 ->
                                        Log.Information("Found faces using Face")
                                        charaList
                                    | _ -> 
                                        match getParts "Gesicht" with
                                        | Some charaList when charaList.Length > 0 -> 
                                            Log.Information("Found faces using Gesicht")
                                            charaList
                                        | _ ->
                                            Log.Information("No face models found")
                                            List<XivCharacter>.Empty
                                | _ ->
                                    match getParts "Face" with
                                    | Some charaList -> charaList
                                    | None -> List<XivCharacter>.Empty

                            let hairList =
                                match userLanguage with
                                | XivLanguage.German ->
                                    match getParts "Hair" with
                                    | Some charaList when charaList.Length > 0 -> charaList
                                    | _ -> 
                                        match getParts "Haar" with
                                        | Some charaList when charaList.Length > 0 -> charaList
                                        | _ ->
                                            List<XivCharacter>.Empty
                                | _ ->
                                    match getParts "Hair" with
                                    | Some charaList -> charaList
                                    | None -> List<XivCharacter>.Empty

                            let earList =
                                match userLanguage with
                                | XivLanguage.German ->
                                    match getParts "Ear" with
                                    | Some charaList when charaList.Length > 0 -> charaList
                                    | _ -> List<XivCharacter>.Empty
                                | _ ->
                                    match getParts "Ear" with
                                    | Some charaList -> charaList
                                    | None -> List<XivCharacter>.Empty

                            let tailList =
                                match userLanguage with
                                | XivLanguage.German ->
                                    match getParts "Tail" with
                                    | Some charaList when charaList.Length > 0 -> charaList
                                    | _ -> 
                                        match getParts "Schwanz" with
                                        | Some charaList when charaList.Length > 0 -> charaList
                                        | _ -> List<XivCharacter>.Empty
                                | _ ->
                                    match getParts "Tail" with
                                    | Some charaList -> charaList
                                    | None -> List<XivCharacter>.Empty
                                    
                            currentFaceList <- faceList
                            currentHairList <- hairList
                            currentEarList <- earList
                            currentTailList <- tailList

                            let populateSelector (selector: ComboBox) (parts: XivCharacter list) defaultToFirst =
                                selector.ItemsSource <- parts |> List.map (fun p -> p.Name)
                                selector.IsEnabled <- not (List.isEmpty parts)
                                if defaultToFirst && not (List.isEmpty parts) && selector.SelectedIndex < 0 then
                                    selector.SelectedIndex <- 0

                            try
                                populateSelector faceSelector currentFaceList true
                                printfn $"Face Selector items source length: {currentFaceList.Length}"
                            with ex -> 
                                Log.Error("Could not populate face selector: {Message}", ex.Message)
                            try
                                populateSelector hairSelector currentHairList true
                            with ex ->
                                Log.Error("Could not populate hair selector: {Message}", ex.Message)
                            try
                                populateSelector earSelector currentEarList true
                            with ex ->
                                Log.Error("Could not populate ear selector: {Message}", ex.Message)
                            try
                                populateSelector tailSelector currentTailList true
                            with ex ->
                                Log.Error("Could not populate tail selector: {Message}", ex.Message)

                            

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
                                        printfn $"item to assign: {item.Name}"
                                        try
                                            allModelUpdateTasks <- render.AssignTrigger(slot, item, parsedXivRace, currentTribe, -1, 01, modelColors, characterCustomizations, userLanguage) :: allModelUpdateTasks
                                            Log.Information("Successfully loaded {Model}!", item.Name)
                                        with ex ->
                                            Log.Error("Failed to load {Model}", item.Name)
                                    | None -> ()

                            do assignSelectedOrDefault currentFaceList EquipmentSlot.Face faceSelector
                            do assignSelectedOrDefault currentHairList EquipmentSlot.Hair hairSelector
                            if currentEarList |> List.isEmpty |> not then do assignSelectedOrDefault currentEarList EquipmentSlot.Ear earSelector
                            if currentTailList |> List.isEmpty |> not then do assignSelectedOrDefault currentTailList EquipmentSlot.Tail tailSelector

                            let reselectIfPopulated (combo: ListBox) = if combo.SelectedIndex >= 0 then let s = combo.SelectedIndex in combo.SelectedIndex <- -1; combo.SelectedIndex <- s
                            reselectIfPopulated headSlotCombo
                            reselectIfPopulated bodySlotCombo
                            reselectIfPopulated handSlotCombo
                            reselectIfPopulated legsSlotCombo
                            reselectIfPopulated feetSlotCombo                           

                            let setDefaultGear (combo: ListBox, category: string, nameFilter: string) =
                                Log.Information("Setting default gear for {Slot}", category)
                                match combo.SelectedItem with
                                | :? FilterGear as gear -> ()
                                | _ ->
                                    let list =
                                        combo.Items
                                        |> Seq.cast<FilterGear>
                                        |> Seq.toList
                                    try
                                        try
                                            match list |> List.tryFind(fun g -> g.Item.Name.Contains(nameFilter)) with
                                            | Some idx -> combo.SelectedItem <- idx
                                            | None ->
                                                match list |> List.tryFind(fun g -> g.Item.Name.Contains("Emperor's")) with
                                                | Some idx -> combo.SelectedItem <- idx
                                                | None -> ()
                                        with ex ->
                                            Log.Error("Could not find default gear for {Slot} slot", category)
                                    finally
                                        Log.Information("Successfully set default gear for {Slot} slot", category)

                            setDefaultGear (bodySlotCombo, "Body", "SmallClothes")
                            setDefaultGear (handSlotCombo, "Hands", "SmallClothes")
                            setDefaultGear (legsSlotCombo, "Legs", "SmallClothes")
                            setDefaultGear (feetSlotCombo, "Feet", "SmallClothes")

                            if not (List.isEmpty allModelUpdateTasks) then
                                do! Async.Parallel(allModelUpdateTasks) |> Async.Ignore

                            Log.Information("Reach palette loading")

                    
                    
                            let! getUiEyePalette = 
                                try
                                    DataHelpers.getUIColorPalette modelColorId paletteOptions.UIEyeColor |> Async.AwaitTask
                                with ex ->
                                    Log.Error("Failed to get color palette")
                                    raise ex
                            let! getUiLipDark = 
                                try
                                    DataHelpers.getUIColorPalette modelColorId paletteOptions.UILipDark |> Async.AwaitTask
                                with ex ->
                                    Log.Error("Failed to get color palette")
                                    raise ex
                            let! getUiLipLight = 
                                try
                                    DataHelpers.getUIColorPalette modelColorId paletteOptions.UILipLight |> Async.AwaitTask
                                with ex ->
                                    Log.Error("Failed to get color palette")
                                    raise ex
                            let! getUiTattoo = 
                                try
                                    DataHelpers.getUIColorPalette modelColorId paletteOptions.UITattoo |> Async.AwaitTask
                                with ex ->
                                    Log.Error("Failed to get color palette")
                                    raise ex
                            let! getUiFaceDark = 
                                try
                                    DataHelpers.getUIColorPalette modelColorId paletteOptions.UIFaceDark |> Async.AwaitTask
                                with ex ->
                                    Log.Error("Failed to get color palette")
                                    raise ex
                            let! getUiFaceLight = 
                                try
                                    DataHelpers.getUIColorPalette modelColorId paletteOptions.UIFaceLight |> Async.AwaitTask
                                with ex ->
                                    Log.Error("Failed to get color palette")
                                    raise ex
                            let! getUiSkinPalette = 
                                try
                                    DataHelpers.getUIColorPalette modelColorId paletteOptions.UISkin |> Async.AwaitTask
                                with ex ->
                                    Log.Error("Failed to get color palette")
                                    raise ex
                            let! getUiHairPalette = 
                                try
                                    DataHelpers.getUIColorPalette modelColorId paletteOptions.RenderHair |> Async.AwaitTask
                                with ex ->
                                    Log.Error("Failed to get color palette")
                                    raise ex
                            let! getUiHighlightPalette = 
                                try
                                    DataHelpers.getUIColorPalette modelColorId paletteOptions.UIHighlights |> Async.AwaitTask
                                with ex ->
                                    Log.Error("Failed to get color palette")
                                    raise ex

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

                            Log.Information("Finished palette loading")

                        | false, _ -> ()
                    | None -> ()
                finally
                    Log.Information("Completed character submission actions!")
                    this.DecrementBusyCounter()
            with ex ->
                Log.Error("Could not submit character! {MEssage}", ex.Message)
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
                Log.Information("Attempting to assign {Gear} to {Slot}", item.Name, slot.ToString())
                try
                    do! render.AssignTrigger(slot, item, race, currentTribe, dye1, dye2, currentModelColors, characterCustomizations, userLanguage)
                with ex ->
                    Log.Fatal("Assign trigger failed! Model will not load! {Message}", ex.Message)
                    raise ex
            finally
                Log.Information("{Gear} assignment to {Slot} successful, moving on ({ModelCount} models remaining)...", item.Name, slot.ToString(), busyOperationCount)
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
                let! pathFromDialog = promptWindow.ShowDialog<ConfigData option>(this) |> Async.AwaitTask
                match pathFromDialog with
                | Some validPath ->
                    gamePathFromConfigOrPrompt <- Path.Combine(validPath.GamePath, "game", "sqpack", "ffxiv")
                    saveConfig { GamePath = Path.Combine(validPath.GamePath, "game", "sqpack", "ffxiv"); CrafterProfile = None; PatreonID = None; GameLanguage = Some validPath.Language }
                    setupSucceeded <- true
                | None ->
                    setupSucceeded <- false

            if not setupSucceeded then
                this.Close()
                return ()

            try

                let gameDataRootPath = gamePathFromConfigOrPrompt
                let language =
                    match loadConfig() with
                    | Some config ->
                        match config.GameLanguage with
                        | Some "English" -> XivLanguage.English
                        | Some "German" -> XivLanguage.German
                        | Some "French" -> XivLanguage.French
                        | Some "Japanese" -> XivLanguage.Japanese
                        | Some "Korean" -> XivLanguage.Korean
                        | Some "Chinese" -> XivLanguage.Chinese
                        | _ -> XivLanguage.English
                    | None -> XivLanguage.English
                userLanguage <- language
                let info = xivModdingFramework.GameInfo(DirectoryInfo(gameDataRootPath), language)
                Log.Information("XIV Cache information: Rebuilding - {RebuildStatus} | Cache Worker Status - {WorkerStatus} | Is Initialized - {Initialized}", XivCache.IsRebuilding, XivCache.CacheWorkerEnabled, XivCache.Initialized)
                
                Log.Information("XIV Cache information, post rebuild: Rebuilding - {RebuildStatus} | Cache Worker Status - {WorkerStatus} | Is Initialized - {Initialized}", XivCache.IsRebuilding, XivCache.CacheWorkerEnabled, XivCache.Initialized)
                XivCache.SetGameInfo(info) |> ignore
                //XivCache.RebuildCache(info.GameVersion, XivCache.CacheRebuildReason.LanguageChanged) |> Async.AwaitTask |> ignore
                Log.Information("XIV Cache information post set: Rebuilding - {RebuildStatus} | Cache Worker Status - {WorkerStatus} | Is Initialized - {Initialized}", XivCache.IsRebuilding, XivCache.CacheWorkerEnabled, XivCache.Initialized)
                viewModel.WindowHeight <- this.Bounds.Height

                do! viewModel.InitializeDataAsync(render)

                currentTransaction <- ModTransaction.BeginReadonlyTransaction()
                let! chara = render.GetChara()
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
                Log.Information("Application successfully initialized")
                Log.Information("Checking head gear list for correct initialization. The head gear list exists: {IsNull} | Total item count: {Count}",
                    not (obj.ReferenceEquals(headSlotCombo.ItemsSource, null)),
                    if headSlotCombo.ItemsSource <> null then headSlotCombo.ItemsSource |> Seq.cast<obj> |> Seq.length else -1
                )

            with ex ->
                Log.Fatal("Application failed to initialize! {Message}", ex.Message)
        }
