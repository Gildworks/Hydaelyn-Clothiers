module ShaderUtils

open Veldrid
open System
open System.IO

let private loadShader (factory: ResourceFactory) (stage: ShaderStages) (path: string) =
    let bytes = File.ReadAllBytes(path)
    factory.CreateShader(ShaderDescription(stage, bytes, "main", true))

let getEmptyShaderSet (factory: ResourceFactory) : Shader[] =

    [|
        loadShader factory ShaderStages.Vertex "shaders/empty.vert.spv"
        loadShader factory ShaderStages.Fragment "shaders/empty.frag.spv"
    |]

let getStandardShaderSet (factory: ResourceFactory) : Shader[] =
    [|
        loadShader factory ShaderStages.Vertex "shaders/vertex.spv"
        loadShader factory ShaderStages.Fragment "shaders/fragment.spv"
    |]
