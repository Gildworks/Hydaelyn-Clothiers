module MaterialBuilder

open System.Threading.Tasks
open System
open System.IO
open System.Drawing
open System.Drawing.Imaging
open Veldrid
open xivModdingFramework.Materials.DataContainers
open xivModdingFramework.Materials.FileTypes
open xivModdingFramework.Models.ModelTextures
open xivModdingFramework.Materials.DataContainers
open Shared

let materialBuilder
    (factory: ResourceFactory)
    (gd: GraphicsDevice)
    (resourceLayout: ResourceLayout)
    (dye1: int)
    (dye2: int)
    (colors: CustomModelColors)
    (mtrl: XivMtrl)
    (materialFor: string)
    : Task<PreparedMaterial> =
    task {
        let dyedMat = mtrl.Clone() :?> XivMtrl
        let! stainTemplate = STM.GetStainingTemplateFile(STM.EStainingTemplate.Dawntrail)
        if dyedMat.ColorSetDyeData <> null && dyedMat.ColorSetDyeData.Length = 128 && dyedMat.ColorSetData <> null && dyedMat.ColorSetData.Count >= 1024 then
            for dyeInstructionIndex in 0 .. 31 do
                let conceptualRowForInstructions = dyeInstructionIndex / 2
                let isBPartInstructions = (dyeInstructionIndex % 2) = 1

                let dyeDataOffset = dyeInstructionIndex * 4
                let b0_flags = dyedMat.ColorSetDyeData[dyeDataOffset + 0]
                let b2_template_part1 = dyedMat.ColorSetDyeData[dyeDataOffset + 2]
                let b3_template_part2 = dyedMat.ColorSetDyeData[dyeDataOffset + 3]
                let templateOffset = if b3_template_part2 >= 8uy then 8uy else 0uy

                let templateFile = uint16 b2_template_part1 ||| (uint16 (b3_template_part2 - templateOffset) <<< 8)
                let templateKey = (templateFile % 1000us) + 1000us
                
                let dyeToApply : int =
                    if b3_template_part2 >= 8uy then dye2 else dye1

                match dyeToApply with
                | n when n >= 0 && templateKey > 1000us && templateKey <> UInt16.MaxValue ->

                    match stainTemplate.GetTemplate(templateKey) with
                    | null ->
                        ()
                    | templateEntry ->
                        for mapping in StainingTemplateEntry.TemplateEntryOffsetToColorsetOffset.[STM.EStainingTemplate.Dawntrail] do
                            let templateComponentFileOffset = mapping.Key
                            let colorsetComponentDataOffset = mapping.Value

                            let dyedComponentHalfs = templateEntry.GetData(templateComponentFileOffset, n)
                            if dyedComponentHalfs <> null && dyedComponentHalfs.Length > 0 then
                                let conceptualRowBaseInColorSetData = conceptualRowForInstructions * 64
                                let partSpecificBaseInColorSetData = conceptualRowBaseInColorSetData + (if isBPartInstructions then 32 else 0)

                                for k in 0 .. dyedComponentHalfs.Length - 1 do
                                    let targetIndexColorSetData = partSpecificBaseInColorSetData + colorsetComponentDataOffset + k
                                    if targetIndexColorSetData < dyedMat.ColorSetData.Count then
                                        dyedMat.ColorSetData.[targetIndexColorSetData] <- dyedComponentHalfs.[k]

                | _ ->
                    ()
        
        let! modelTex = ModelTexture.GetModelMaps(dyedMat, true, colors)

        let getNextFilename (folder: string) (baseName: string) (extension: string) =
            let mutable counter = 0
            let mutable filename = Path.Combine(folder, $"{baseName}.{extension}")
    
            while File.Exists(filename) do
                counter <- counter + 1
                filename <- Path.Combine(folder, $"{baseName}_{counter:D3}.{extension}")
    
            filename

        // Helper function to convert RGBA bytes to System.Drawing.Bitmap
        let rgbaToBitmap (rgba: byte[]) (width: int) (height: int) =
            let bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb)
            let bitmapData = bitmap.LockBits(System.Drawing.Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb)
    
            try
                // Convert RGBA to BGRA for System.Drawing
                let bgra = Array.zeroCreate<byte> rgba.Length
                for i in 0 .. 4 .. rgba.Length - 4 do
                    bgra.[i] <- rgba.[i + 2]     // B
                    bgra.[i + 1] <- rgba.[i + 1] // G
                    bgra.[i + 2] <- rgba.[i]     // R
                    bgra.[i + 3] <- rgba.[i + 3] // A
        
                System.Runtime.InteropServices.Marshal.Copy(bgra, 0, bitmapData.Scan0, bgra.Length)
            finally
                bitmap.UnlockBits(bitmapData)
    
            bitmap

        // --- Helper to convert byte[] to RgbaByte[] ---
        let byteToRgba (bytes: byte[]) : RgbaByte[] =
            let len = bytes.Length / 4
            Array.init len (fun i ->
                let offset = i * 4
                RgbaByte(bytes[offset], bytes[offset + 1], bytes[offset + 2], bytes[offset + 3])
            )

        // === Helpers for texure format ===
        let linearFormat = PixelFormat.R8_G8_B8_A8_UNorm
        let srgbFormat = PixelFormat.R8_G8_B8_A8_UNorm_SRgb

        // --- Helper to create texture from raw bytes ---
        let createTexture (bytes: byte[]) (width: int) (height: int) (format: PixelFormat) (textureType: string) =
            let rgba = byteToRgba bytes
            let desc = TextureDescription(uint32 width, uint32 height, 1u, 1u, 1u, format, TextureUsage.Sampled, TextureType.Texture2D)
            let tex = factory.CreateTexture(desc)
            gd.UpdateTexture(tex, rgba, 0u, 0u, 0u, uint32 width, uint32 height, 1u, 0u, 0u)
            //let debugFolder : string option = Some "textureDebug"
            //match debugFolder with
            //| Some folder when Directory.Exists(folder) ->
            //    try
            //        if materialFor.Contains("Hair") then
            //            let bitmap = rgbaToBitmap bytes width height
            //            let filename = getNextFilename folder $"{textureType}" "png"
            //            bitmap.Save(filename, ImageFormat.Png)
            //            bitmap.Dispose()
            //    with
            //    | ex -> printfn $"Failed to save deug texture: {ex.Message}"
            tex

        // --- Dummy fallback texture ---
        let dummyTexture =
            let desc = TextureDescription.Texture2D(1u, 1u, 1u, 1u, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Sampled)
            let tex = factory.CreateTexture(desc)
            gd.UpdateTexture(tex, [| RgbaByte(255uy, 255uy, 255uy, 255uy) |], 0u, 0u, 0u, 1u, 1u, 1u, 0u, 0u)
            tex

        // --- Attempt to create all relevant textures ---
        let refLength = modelTex.Width * modelTex.Height * 4
        let diffuseTex =
            if modelTex.Diffuse.Length >= refLength then
                createTexture modelTex.Diffuse modelTex.Width modelTex.Height srgbFormat "Diffuse"
            else
                let length = modelTex.Diffuse.Length / 4
                let side = int (sqrt (float length))
                createTexture modelTex.Diffuse (int (sqrt (float (modelTex.Diffuse.Length / 4) / 2.0))) (int (2.0 * (sqrt (float (modelTex.Diffuse.Length / 4) / 2.0)))) srgbFormat "Diffuse"

        let normalTex =
            if modelTex.Normal.Length >= refLength then
 
                createTexture modelTex.Normal modelTex.Width modelTex.Height linearFormat "Normal"
            else
          
                createTexture modelTex.Normal (int (sqrt (float (modelTex.Normal.Length / 4) / 2.0))) (int (2.0 * (sqrt (float (modelTex.Normal.Length / 4) / 2.0)))) linearFormat "Normal"

        let specularTex =
            if modelTex.Specular.Length >= refLength then
     
                createTexture modelTex.Specular modelTex.Width modelTex.Height srgbFormat"Spec"
            else
              
                createTexture modelTex.Specular (int (sqrt (float (modelTex.Specular.Length / 4) / 2.0))) (int (2.0 * (sqrt (float (modelTex.Specular.Length / 4) / 2.0)))) srgbFormat"Spec"

        let emissiveTex =
            if modelTex.Emissive.Length >= refLength then
     
                createTexture modelTex.Emissive modelTex.Width modelTex.Height srgbFormat "Emissive"
            else 
              
                createTexture modelTex.Emissive (int (sqrt (float (modelTex.Emissive.Length / 4) / 2.0))) (int (2.0 * (sqrt (float (modelTex.Emissive.Length / 4) / 2.0)))) srgbFormat "Emissive"

        let alphaTex =
            if modelTex.Alpha.Length >= refLength then
                createTexture modelTex.Alpha modelTex.Width modelTex.Height linearFormat "Alpha"
            else
        
                createTexture modelTex.Alpha (int (sqrt (float (modelTex.Alpha.Length / 4) / 2.0))) (int (2.0 * (sqrt (float (modelTex.Alpha.Length / 4) / 2.0)))) linearFormat "Alpha"

        let roughnessTex =
            if modelTex.Roughness.Length >= refLength then
       
                createTexture modelTex.Roughness modelTex.Width modelTex.Height linearFormat "Roughness"
            else
                
                createTexture modelTex.Roughness (int (sqrt (float (modelTex.Roughness.Length / 4) / 2.0))) (int (2.0 * (sqrt (float (modelTex.Roughness.Length / 4) / 2.0)))) linearFormat "Roughness"

        let metalnessTex =
            if modelTex.Metalness.Length >= refLength then
       
                createTexture modelTex.Metalness modelTex.Width modelTex.Height linearFormat "Metalness"
            else
                
                createTexture modelTex.Metalness (int (sqrt (float (modelTex.Metalness.Length / 4) / 2.0))) (int (2.0 * (sqrt (float (modelTex.Metalness.Length / 4) / 2.0)))) linearFormat "Metalness"

        let occlusionTex =
            if modelTex.Occlusion.Length >= refLength then

                createTexture modelTex.Occlusion modelTex.Width modelTex.Height linearFormat "AO"
            else
                
                createTexture modelTex.Occlusion (int (sqrt (float (modelTex.Occlusion.Length / 4) / 2.0))) (int (2.0 * (sqrt (float (modelTex.Occlusion.Length / 4) / 2.0)))) linearFormat "AO"

        let subsurfaceTex =
            if modelTex.Subsurface.Length >= refLength then
  
                createTexture modelTex.Subsurface modelTex.Width modelTex.Height linearFormat "SSS"
            else
           
                createTexture modelTex.Subsurface (int (sqrt (float (modelTex.Subsurface.Length / 4) / 2.0))) (int (2.0 * (sqrt (float (modelTex.Subsurface.Length / 4) / 2.0)))) linearFormat "SSS"

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
            return raise ex

        
    }
