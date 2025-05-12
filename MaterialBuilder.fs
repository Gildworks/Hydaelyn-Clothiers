module MaterialBuilder

open System.Threading.Tasks
open Veldrid
open xivModdingFramework.Materials.DataContainers
open xivModdingFramework.Materials.FileTypes
open xivModdingFramework.Models.ModelTextures
open xivModdingFramework.Textures.DataContainers
open xivModdingFramework.Textures.FileTypes
open Shared

let materialBuilder
    (factory: ResourceFactory)
    (gd: GraphicsDevice)
    (resourceLayout: ResourceLayout)
    (mtrl: XivMtrl)
    : Task<PreparedMaterial> =
    task {
        let! modelTex = ModelTexture.GetModelMaps(mtrl, pbrMaps = true)


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
                Mtrl = mtrl
            }
        with ex ->
            printfn $"[Material Builder] Failed to create resource set: {ex.Message}"
            return raise ex

        
    }
