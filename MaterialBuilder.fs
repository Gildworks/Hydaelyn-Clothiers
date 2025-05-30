module MaterialBuilder

open System.Threading.Tasks
open System.Collections.Generic
open System
open Veldrid
open xivModdingFramework.Materials.DataContainers
open xivModdingFramework.Materials.FileTypes
open xivModdingFramework.Models.ModelTextures
open xivModdingFramework.Textures.DataContainers
open xivModdingFramework.Textures.FileTypes
open xivModdingFramework.General.Enums
open Shared

let materialBuilder
    (factory: ResourceFactory)
    (gd: GraphicsDevice)
    (resourceLayout: ResourceLayout)
    (colors: CustomModelColors)
    (mtrl: XivMtrl)
    : Task<PreparedMaterial> =
    task {
        //let dyedMat = mtrl.Clone() :?> XivMtrl
        
        //let! stainTemplate = STM.GetStainingTemplateFile(STM.EStainingTemplate.Dawntrail)
        //let data = dyedMat.ColorSetData.ToArray()

        //for row in 0 .. 15 do
        //    let tplKey = STM.GetTemplateKeyFromMaterialData(dyedMat, row)
        //    let entry = stainTemplate.GetTemplate(tplKey)
            
        //    if entry <> null then
        //        let cols = 8
        //        let rowOff = row * cols * 4
        //        let rowData =
        //            [| 0 .. cols - 1 |]
        //            |> Array.map (fun c ->
        //                Array.init 4 (fun j ->
        //                    data.[rowOff + c * 4 + j]
        //                )
        //            )
        //        for kv in StainingTemplateEntry.TemplateEntryOffsetToColorsetOffset.[STM.EStainingTemplate.Dawntrail] do
        //            let tplOffset, csOffset = kv.Key, kv.Value
        //            let colIdx = csOffset / 4
        //            let compIdx = csOffset % 4
        //            let compHalfs = entry.GetData(tplOffset, 9)
        //            if compHalfs <> null then
        //                for i in 0 .. compHalfs.Length - 1 do
        //                    rowData.[colIdx].[compIdx + i] <- compHalfs.[i]
        //        let raw = rowData |> Array.collect id
        //        Array.Copy(raw, 0, data, rowOff, raw.Length)
        //        Array.Copy(raw, 0, data, rowOff + cols * 4, raw.Length)
        //dyedMat.ColorSetData <- List<SharpDX.Half>(data)
        let dyedMat = mtrl.Clone() :?> XivMtrl
        let! stainTemplate = STM.GetStainingTemplateFile(STM.EStainingTemplate.Dawntrail)
        if dyedMat.ColorSetDyeData <> null && dyedMat.ColorSetDyeData.Length = 128 then
            for rowIndexInColorSet in 0 .. 15 do
                let dyeDataOffset = rowIndexInColorSet * 4
                let b0 = dyedMat.ColorSetDyeData[dyeDataOffset]
                let b2 = dyedMat.ColorSetDyeData[dyeDataOffset + 2]
                let b3 = dyedMat.ColorSetDyeData[dyeDataOffset + 3]
                let templateFile = uint16 b2 ||| (uint16 b3 <<< 8)
                let templateKey = templateFile + 1000us
                let channelSelector = if b3 < 8uy then 0 elif b3 >= 8uy then 1 else -1
                let mutable dyeIdToApply : int = 9

                match dyeIdToApply with
                | n when n >= 0 && templateKey > 1000us && templateKey <> System.UInt16.MaxValue ->
                    match stainTemplate.GetTemplate(templateKey) with
                    | null -> printfn $"STM Template {templateKey} not found for material."
                    | templateEntry ->
                        for mapping in StainingTemplateEntry.TemplateEntryOffsetToColorsetOffset.[STM.EStainingTemplate.Dawntrail] do
                            let templateComponentFileOffset = mapping.Key
                            let colorsetComponentDataOffset = mapping.Value

                            let dyedComponentHalfs = templateEntry.GetData(templateComponentFileOffset, n)
                            if dyedComponentHalfs <> null then
                                let colorsetRowBaseIndex = rowIndexInColorSet * 32
                                let colorSetRowMemoryStride = 16

                                let materialRowBaseHalfsIndex = rowIndexInColorSet * 64

                                for k in 0 .. dyedComponentHalfs.Length - 1 do
                                    let targetIndexColorSetData = materialRowBaseHalfsIndex + colorsetComponentDataOffset + k
                                    if targetIndexColorSetData < dyedMat.ColorSetData.Count then
                                        printfn $"Previous row data: {dyedMat.ColorSetData.[targetIndexColorSetData]} | Target data: {dyedComponentHalfs.[k]}"
                                        dyedMat.ColorSetData.[targetIndexColorSetData] <- dyedComponentHalfs.[k]
                                        printfn $"(Hopefully) New ColorSet Data: {dyedMat.ColorSetData.[targetIndexColorSetData]}"
                                        printfn "ColorSet Row Adjusted Successfully!"
                                    else
                                        printfn "Warning: Tried to write dye data out of bounds for ColorSetData."
                | _ -> ()


        let! modelTex = ModelTexture.GetModelMaps(dyedMat, true)
        let w, h = modelTex.Width, modelTex.Height
        let x, y = w/2, h/2
        let idx = (y * w + x) * 4
        printfn "Center RGBA: %A" (modelTex.Diffuse.[idx..idx + 3])
        printfn "Trying to override the diffuse data"
        let testDiffuseBytes = Array.zeroCreate<byte>(modelTex.Width * modelTex.Height * 4)
        for y_px in 0 .. modelTex.Height - 1 do
            for x_px in 0 .. modelTex.Width - 1 do
                let px_offset = (y_px * modelTex.Width + x_px) * 4
                let testRowIndex = 15
                let materialRowBase = testRowIndex * 64
                let r_h = dyedMat.ColorSetData.[materialRowBase + 0]
                let g_h = dyedMat.ColorSetData.[materialRowBase + 1]
                let b_h = dyedMat.ColorSetData.[materialRowBase + 2]

                let r_f = SharpDX.Half.op_Implicit(r_h)
                let g_f = SharpDX.Half.op_Implicit(g_h)
                let b_f = SharpDX.Half.op_Implicit(b_h)
                

                testDiffuseBytes.[px_offset + 0] <- byte (Math.Sqrt(float r_f) * 255.0)
                testDiffuseBytes.[px_offset + 1] <- byte (Math.Sqrt(float g_f) * 255.0)
                testDiffuseBytes.[px_offset + 2] <- byte (Math.Sqrt(float b_f) * 255.0)
                testDiffuseBytes.[px_offset + 3] <- 255uy
        //modelTex.Diffuse <- testDiffuseBytes




        // --- Helper to convert byte[] to RgbaByte[] ---
        let byteToRgba (bytes: byte[]) : RgbaByte[] =
            let len = bytes.Length / 4
            Array.init len (fun i ->
                let offset = i * 4
                RgbaByte(bytes[offset], bytes[offset + 1], bytes[offset + 2], bytes[offset + 3])
            )

        // --- Helper to create texture from raw bytes ---
        let createTexture (bytes: byte[]) (width: int) (height: int) =
            let rgba = byteToRgba bytes
            let desc = TextureDescription(uint32 width, uint32 height, 1u, 1u, 1u, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Sampled, TextureType.Texture2D)
            let tex = factory.CreateTexture(desc)
            printfn $"[MaterialBuilder] Creating texture {width}x{height} with {bytes.Length} bytes."
            gd.UpdateTexture(tex, rgba, 0u, 0u, 0u, uint32 width, uint32 height, 1u, 0u, 0u)
            if bytes.Length < width * height then
                printfn $"[WARNING] Byte length {bytes.Length} is too small for expected size of {width}x{height}"
            tex

        // --- Dummy fallback texture ---
        let dummyTexture =
            let desc = TextureDescription.Texture2D(1u, 1u, 1u, 1u, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Sampled)
            let tex = factory.CreateTexture(desc)
            gd.UpdateTexture(tex, [| RgbaByte(255uy, 255uy, 255uy, 255uy) |], 0u, 0u, 0u, 1u, 1u, 1u, 0u, 0u)
            tex

        // --- Attempt to create all relevant textures ---
        printfn "Trying to create textures"
        let refLength = modelTex.Width * modelTex.Height * 4
        let diffuseTex =
            if modelTex.Diffuse.Length >= refLength then
                printfn $"Creating Diffuse texture for {mtrl.MTRLPath} of size {modelTex.Diffuse.Length}."
                createTexture modelTex.Diffuse modelTex.Width modelTex.Height
            else
                printfn $"Possible fallback detected in {mtrl.MTRLPath} Diffuse! Length: {modelTex.Diffuse.Length}"
                let length = modelTex.Diffuse.Length / 4
                let side = int (sqrt (float length))
                createTexture modelTex.Diffuse (int (sqrt (float (modelTex.Diffuse.Length / 4) / 2.0))) (int (2.0 * (sqrt (float (modelTex.Diffuse.Length / 4) / 2.0))))

        let normalTex =
            if modelTex.Normal.Length >= refLength then
                printfn $"Creating Normal texture for {mtrl.MTRLPath} of size {modelTex.Normal.Length}."
                createTexture modelTex.Normal modelTex.Width modelTex.Height
            else
                printfn $"Possible fallback detected in {mtrl.MTRLPath} Normal! Length: {modelTex.Normal.Length}"
                createTexture modelTex.Normal (int (sqrt (float (modelTex.Normal.Length / 4) / 2.0))) (int (2.0 * (sqrt (float (modelTex.Normal.Length / 4) / 2.0))))

        let specularTex =
            if modelTex.Specular.Length >= refLength then
                printfn $"Creating Specular texture for {mtrl.MTRLPath} of size {modelTex.Specular.Length}."
                createTexture modelTex.Specular modelTex.Width modelTex.Height
            else
                printfn $"Possible fallback detected in {mtrl.MTRLPath} Specular! Length: {modelTex.Specular.Length}"
                createTexture modelTex.Specular (int (sqrt (float (modelTex.Specular.Length / 4) / 2.0))) (int (2.0 * (sqrt (float (modelTex.Specular.Length / 4) / 2.0))))

        let emissiveTex =
            if modelTex.Emissive.Length >= refLength then
                printfn $"Creating Emissive texture for {mtrl.MTRLPath} of size {modelTex.Emissive.Length}."
                createTexture modelTex.Emissive modelTex.Width modelTex.Height
            else 
                printfn $"Possible fallback detected in {mtrl.MTRLPath} Emissive! Length: {modelTex.Emissive.Length}"
                createTexture modelTex.Emissive (int (sqrt (float (modelTex.Emissive.Length / 4) / 2.0))) (int (2.0 * (sqrt (float (modelTex.Emissive.Length / 4) / 2.0))))

        let alphaTex =
            if modelTex.Alpha.Length >= refLength then
                printfn $"Creating Alpha texture for {mtrl.MTRLPath} of size {modelTex.Alpha.Length}."
                createTexture modelTex.Alpha modelTex.Width modelTex.Height
            else
                printfn $"Possible fallback detected in {mtrl.MTRLPath} Alpha! Length: {modelTex.Alpha.Length}"
                createTexture modelTex.Alpha (int (sqrt (float (modelTex.Alpha.Length / 4) / 2.0))) (int (2.0 * (sqrt (float (modelTex.Alpha.Length / 4) / 2.0))))

        let roughnessTex =
            if modelTex.Roughness.Length >= refLength then
                printfn $"Creating Roughness texture for {mtrl.MTRLPath} of size {modelTex.Roughness.Length}."
                createTexture modelTex.Roughness modelTex.Width modelTex.Height
            else
                printfn $"Possible fallback detected in {mtrl.MTRLPath} Roughness! Length: {modelTex.Roughness.Length}"
                createTexture modelTex.Roughness (int (sqrt (float (modelTex.Roughness.Length / 4) / 2.0))) (int (2.0 * (sqrt (float (modelTex.Roughness.Length / 4) / 2.0))))

        let metalnessTex =
            if modelTex.Metalness.Length >= refLength then
                printfn $"Creating Metalness texture for {mtrl.MTRLPath} of size {modelTex.Metalness.Length}."
                createTexture modelTex.Metalness modelTex.Width modelTex.Height
            else
                printfn $"Possible fallback detected in {mtrl.MTRLPath} Metalness! Length: {modelTex.Metalness.Length}"
                createTexture modelTex.Metalness (int (sqrt (float (modelTex.Metalness.Length / 4) / 2.0))) (int (2.0 * (sqrt (float (modelTex.Metalness.Length / 4) / 2.0))))

        let occlusionTex =
            if modelTex.Occlusion.Length >= refLength then
                printfn $"Creating AO texture for {mtrl.MTRLPath} of size {modelTex.Occlusion.Length}."
                createTexture modelTex.Occlusion modelTex.Width modelTex.Height
            else
                printfn $"Possible fallback detected in {mtrl.MTRLPath} Occlusion! Length: {modelTex.Occlusion.Length}"
                createTexture modelTex.Occlusion (int (sqrt (float (modelTex.Occlusion.Length / 4) / 2.0))) (int (2.0 * (sqrt (float (modelTex.Occlusion.Length / 4) / 2.0))))

        let subsurfaceTex =
            if modelTex.Subsurface.Length >= refLength then
                printfn $"Creating SSS texture for {mtrl.MTRLPath} of size {modelTex.Subsurface.Length}."
                createTexture modelTex.Subsurface modelTex.Width modelTex.Height
            else
                printfn $"Possible fallback detected in {mtrl.MTRLPath} SSS! Length: {modelTex.Subsurface.Length}"
                createTexture modelTex.Subsurface (int (sqrt (float (modelTex.Subsurface.Length / 4) / 2.0))) (int (2.0 * (sqrt (float (modelTex.Subsurface.Length / 4) / 2.0))))

        let sampler = factory.CreateSampler(SamplerDescription.Linear)
        try
            let resourceSet =
                factory.CreateResourceSet(ResourceSetDescription(
                    resourceLayout,
                    diffuseTex :> BindableResource,
                    normalTex :> BindableResource,
                    specularTex :> BindableResource,
                    emissiveTex :> BindableResource,
                    alphaTex :> BindableResource,
                    roughnessTex :> BindableResource,
                    metalnessTex :> BindableResource,
                    occlusionTex :> BindableResource,
                    subsurfaceTex :> BindableResource,
                    sampler :> BindableResource
                ))
            return {
                DiffuseTexture = diffuseTex
                NormalTexture = normalTex
                SpecularTexture = specularTex
                EmissiveTexture = emissiveTex
                AlphaTexture = alphaTex
                RoughnessTexture = roughnessTex
                MetalnessTexture = metalnessTex
                OcclusionTexture = occlusionTex
                SubsurfaceTexture = subsurfaceTex
                ResourceSet = resourceSet
                Mtrl = dyedMat
            }
        with ex ->
            printfn $"[Material Builder] Failed to create resource set: {ex.Message}"
            return raise ex

        
    }
