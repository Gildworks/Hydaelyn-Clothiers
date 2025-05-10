module MaterialBuilder

open System.Threading.Tasks
open Veldrid
open xivModdingFramework.Materials.DataContainers
open xivModdingFramework.Models.ModelTextures

open Shared

let materialBuilder
    (factory: ResourceFactory)
    (gd: GraphicsDevice)
    (resourceLayout: ResourceLayout)
    (mtrl: XivMtrl)
    : Task<PreparedMaterial> =
    task {
        let! modelTex = ModelTexture.GetModelMaps(mtrl, pbrMaps = true)

        let createTexture name width height pixelBytes =
            // --- Quick helper to convert xivModdingFrameworks byte array to an RgbaByte arra ---
            let byteToRgba (bytes: byte[]) : RgbaByte[] =
                let len = bytes.Length / 4
                Array.init len (fun i ->
                    let offset = i * 4
                    RgbaByte(bytes[offset], bytes[offset + 1], bytes[offset + 2], bytes[offset + 3])
                )
            let rgbaBytes = byteToRgba pixelBytes

            let textureDesc =
                TextureDescription(uint32 width, uint32 height, 1u, 1u, 1u, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Sampled, TextureType.Texture2D)
            let tex = factory.CreateTexture(textureDesc)
            gd.UpdateTexture(tex, rgbaBytes, 0u, 0u, 0u, uint32 width, uint32 height, 1u, 0u, 0u)
            tex

        let diffuseTex =
            createTexture "diffuse" modelTex.Width modelTex.Height modelTex.Diffuse

        let normalTex =
            if modelTex.Normal.Length > 0 then
                Some (createTexture "normal" modelTex.Width modelTex.Height modelTex.Normal)
            else None

        let specularTex =
            if modelTex.Specular.Length > 0 then
                Some (createTexture "specular" modelTex.Width modelTex.Height modelTex.Specular)
            else None

        let emissiveTex =
            if modelTex.Emissive.Length > 0 then
                Some (createTexture "emissive" modelTex.Width modelTex.Height modelTex.Emissive)
            else None

        let alphaTex =
            if modelTex.Alpha.Length > 0 then
                Some (createTexture "alpha" modelTex.Width modelTex.Height modelTex.Alpha)
            else None

        let roughnessTex =
            if modelTex.Roughness.Length > 0 then
                Some (createTexture "roughness" modelTex.Width modelTex.Height modelTex.Roughness)
            else None

        let metalnessTex =
            if modelTex.Metalness.Length > 0 then
                Some (createTexture "metalness" modelTex.Width modelTex.Height modelTex.Metalness)
            else None

        let occlusionTex =
            if modelTex.Occlusion.Length > 0 then
                Some (createTexture "occlusion" modelTex.Width modelTex.Height modelTex.Occlusion)
            else None

        let subsurfaceTex =
            if modelTex.Subsurface.Length > 0 then
                Some (createTexture "subsurface" modelTex.Width modelTex.Height modelTex.Subsurface)
            else None

        let sampler = factory.CreateSampler(SamplerDescription.Linear)

        let resourceSet =
            factory.CreateResourceSet(ResourceSetDescription(
                resourceLayout,
                diffuseTex :> BindableResource,
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
    }