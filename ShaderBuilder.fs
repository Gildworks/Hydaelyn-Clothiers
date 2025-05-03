module ShaderBuilder

open Veldrid
open System.IO

let private loadShader (factory: ResourceFactory) (stage: ShaderStages) (path: string) =
    let bytes = File.ReadAllBytes(path)
    factory.CreateShader(ShaderDescription(stage, bytes, "main", true))

let createDefaultPipeline
    (device: GraphicsDevice)
    (factory: ResourceFactory)
    (vertexLayout: VertexLayoutDescription)
    (framebufferOutput: OutputDescription)
    (uniformLayout: ResourceLayout)
    (textureLayout: ResourceLayout) : Pipeline =

    let vertexShader = loadShader factory ShaderStages.Vertex "shaders/vertex.spv"
    let fragmentShader = loadShader factory ShaderStages.Fragment "shaders/fragment.spv"

    let shaderSet = ShaderSetDescription([| vertexLayout |], [| vertexShader; fragmentShader |])

    let blendState = BlendStateDescription(
        RgbaFloat(0.0f, 0.0f, 0.0f, 0.0f),
        BlendAttachmentDescription(
            true,
            BlendFactor.SourceAlpha,
            BlendFactor.InverseSourceAlpha,
            BlendFunction.Add,
            BlendFactor.One,
            BlendFactor.InverseSourceAlpha,
            BlendFunction.Add
        )
    )

    let pipelineDescription = GraphicsPipelineDescription(
        blendState,
        DepthStencilStateDescription(
            depthTestEnabled = true,
            depthWriteEnabled = true,
            comparisonKind = ComparisonKind.LessEqual
        ),
        RasterizerStateDescription(
            cullMode = FaceCullMode.None,
            fillMode = PolygonFillMode.Solid,
            frontFace = FrontFace.Clockwise,
            depthClipEnabled = false,
            scissorTestEnabled = false
        ),
        PrimitiveTopology.TriangleList,
        shaderSet,
        [| uniformLayout; textureLayout |],
        framebufferOutput
    )

    factory.CreateGraphicsPipeline(pipelineDescription)