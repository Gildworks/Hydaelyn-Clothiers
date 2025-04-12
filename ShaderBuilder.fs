module ShaderBuilder

open Veldrid

let vertexShaderCode = """
#version 450
layout(set = 0, binding = 0) uniform MVPBuffer {
    mat4 MVP;
};
layout(location = 0) in vec3 Position;
layout(location = 1) in vec4 Color;
layout(location = 2) in vec2 UV;
layout(location = 3) in vec3 Normal;

layout(location = 0) out vec3 fs_Position;
layout(location = 1) out vec4 fs_Color;
layout(location = 2) out vec2 fs_UV;
layout(location = 3) out vec3 fs_Normal;

void main() {
    gl_Position = MVP * vec4(Position, 1.0);
    fs_Position = Position;
    fs_Color = vec4(0.0, 1.0, 0.0, 1.0);
    fs_UV = UV;
    fs_Normal = normalize(Normal);
}
"""

let fragmentShaderCode = """
#version 450
layout(binding = 1) uniform sampler2D tex_Diffuse;

layout(location = 0) in vec3 fs_Position;
layout(location = 1) in vec4 fs_Color;
layout(location = 2) in vec2 fs_UV;
layout(location = 3) in vec3 fs_Normal;

layout(location = 0) out vec4 fsout_Color;

void main() {
    vec3 lightDir = normalize(vec3(-0.5, -1.0, -0.3));
    vec3 normal = normalize(fs_Normal);
    float diff = max(dot(normal, -lightDir), 0.0);
    float spec = 0.0;
    vec4 texColor = texture(tex_Diffuse, fs_UV);
    vec3 litColor = texColor.rgb * diff + vec3(spec);
    fsout_Color = vec4(litColor, texColor.a);
}
"""


let private loadShader (factory: ResourceFactory) (stage: ShaderStages) (source: string) (entry: string) =
    let bytes = System.Text.Encoding.UTF8.GetBytes(source)
    factory.CreateShader(ShaderDescription(stage, bytes, entry))

let createDefaultPipeline
    (device: GraphicsDevice)
    (factory: ResourceFactory)
    (vertexLayout: VertexLayoutDescription)
    (framebufferOutput: OutputDescription)
    (uniformLayout: ResourceLayout)
    (textureLayout: ResourceLayout) : Pipeline =

    let vertexShader = loadShader factory ShaderStages.Vertex vertexShaderCode "main"
    let fragmentShader = loadShader factory ShaderStages.Fragment fragmentShaderCode "main"

    let shaderSet = ShaderSetDescription([| vertexLayout |], [| vertexShader; fragmentShader |])

    let pipelineDescription = GraphicsPipelineDescription(
        BlendStateDescription.SingleOverrideBlend,
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