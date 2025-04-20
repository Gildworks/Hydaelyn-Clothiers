#version 450

layout(set = 1, binding = 0) uniform texture2D tex_Diffuse;
layout(set = 1, binding = 1) uniform sampler sampler_Diffuse;

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
    vec4 texColor = texture(sampler2D(tex_Diffuse, sampler_Diffuse), fs_UV);
    vec3 litColor = texColor.rgb * diff + vec3(spec);
    fsout_Color = vec4(litColor, texColor.a);
}