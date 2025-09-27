module ShaderUtils

open Veldrid
open System
open System.IO
open Veldrid.SPIRV

let private loadShader (factory: ResourceFactory) (stage: ShaderStages) (path: string) =
    let bytes = File.ReadAllBytes(path)
    factory.CreateShader(ShaderDescription(stage, bytes, "main", true))

let getEmptyShaderSet (factory: ResourceFactory) : Shader[] =
    match factory.BackendType with
    | GraphicsBackend.Metal
    | GraphicsBackend.Vulkan ->
        [|
            loadShader factory ShaderStages.Vertex "shaders/empty.vert.spv"
            loadShader factory ShaderStages.Fragment "shaders/empty.frag.spv"
        |]
    | GraphicsBackend.Direct3D11 ->
        [|
            loadShader factory ShaderStages.Vertex "shaders/empty.vert.cso"
            loadShader factory ShaderStages.Fragment "shaders/empty.frag.cso"
        |]
    | GraphicsBackend.OpenGL
    | GraphicsBackend.OpenGLES ->
        [|
            loadShader factory ShaderStages.Vertex "shaders/empty.vert.glsl"
            loadShader factory ShaderStages.Fragment "shaders/empty.frag.glsl"
        |]
    | _ -> failwith "Failed to get a graphics backend for some reason?"

let getStandardShaderSet (factory: ResourceFactory) : Shader[] =
    match factory.BackendType with
    | GraphicsBackend.Metal
    | GraphicsBackend.Vulkan ->
        [|
            loadShader factory ShaderStages.Vertex "shaders/vertex.spv"
            loadShader factory ShaderStages.Fragment "shaders/fragment.spv"
        |]
    | GraphicsBackend.Direct3D11 ->
        [|
            loadShader factory ShaderStages.Vertex "shaders/vertex.cso"
            loadShader factory ShaderStages.Fragment "shaders/fragment.cso"
        |]
    | GraphicsBackend.OpenGL
    | GraphicsBackend.OpenGLES ->
        [|
            loadShader factory ShaderStages.Vertex "shaders/vertex.vert.glsl"
            loadShader factory ShaderStages.Fragment "shaders/fragment.frag.glsl"
        |]
    | _ -> failwith "Failed to get a graphics backend for some reason?"

let getCrossEmptyShaderSet (factory: ResourceFactory) : Shader[] =
    let vertSpv = File.ReadAllBytes("shaders/empty.vert.spv")
    let fragSpv = File.ReadAllBytes("shaders/empty.frag.spv")

    let crossOutput = CrossCompileOptions(false, false)

    let shaders : Shader[] =
        factory.CreateFromSpirv(
            ShaderDescription(ShaderStages.Vertex, vertSpv, "main"),
            ShaderDescription(ShaderStages.Fragment, fragSpv, "main"),
            crossOutput
        )
    shaders

let getCrossStandardShaderSet (factory: ResourceFactory) : Shader[] =
    let vertSpv = File.ReadAllBytes("shaders/vertex.spv")
    let fragSpv = File.ReadAllBytes("shaders/fragment.spv")

    let crossOutput = CrossCompileOptions(false, true)

    let shaders : Shader[] =
        factory.CreateFromSpirv(
            ShaderDescription(ShaderStages.Vertex, vertSpv, "main"),
            ShaderDescription(ShaderStages.Fragment, fragSpv, "main"),
            crossOutput
        )
    shaders
