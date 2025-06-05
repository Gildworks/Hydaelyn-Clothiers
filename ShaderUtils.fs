module ShaderUtils

open Veldrid
open System
open System.IO

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
