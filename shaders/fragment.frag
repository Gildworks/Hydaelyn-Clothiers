#version 450

layout(set = 1, binding = 0) uniform texture2D tex_Diffuse;
layout(set = 1, binding = 1) uniform texture2D tex_Normal;
layout(set = 1, binding = 2) uniform texture2D tex_Specular;
layout(set = 1, binding = 3) uniform texture2D tex_Emissive;
layout(set = 1, binding = 4) uniform texture2D tex_Alpha;
layout(set = 1, binding = 5) uniform texture2D tex_Roughness;
layout(set = 1, binding = 6) uniform texture2D tex_Metalness;
layout(set = 1, binding = 7) uniform texture2D tex_Occlusion;
layout(set = 1, binding = 8) uniform texture2D tex_Subsurface;
layout(set = 1, binding = 9) uniform sampler SharedSampler;

layout(location = 0) in vec3 fs_Position;
layout(location = 1) in vec4 fs_Color;
layout(location = 2) in vec2 fs_UV;
layout(location = 3) in vec3 fs_Normal;

layout(location = 0) out vec4 fsout_Color;

void main() {
    vec4 baseColor = texture(sampler2D(tex_Diffuse, SharedSampler), fs_UV);
    vec4 baseSpec = texture(sampler2D(tex_Specular, SharedSampler), fs_UV);
    vec3 sampledNormal = texture(sampler2D(tex_Normal, SharedSampler), fs_UV).rgb;
    vec3 emissiveColor = texture(sampler2D(tex_Emissive, SharedSampler), fs_UV).rgb;
    vec3 specularColor = texture(sampler2D(tex_Specular, SharedSampler), fs_UV).rgb;

    // Cutout alpha test
    float alphaCutoff = 0.5;
    if (baseColor.a < alphaCutoff)
        discard;

    // Decode normals from normal map (tangent-space to [-1,1])
    sampledNormal.g = 1.0 - sampledNormal.g;
    vec3 normal = normalize(sampledNormal * 2.0);
    vec3 viewNormal = normalize(normal);

    // Directional light from top-left of camera view
    vec3 lightDir = normalize(vec3(-0.4, -1.0, -0.3));
    float diff = max(dot(viewNormal, -lightDir), 0.0);

    // Simple specular from light reflection
    vec3 viewDir = normalize(-fs_Position);
    vec3 reflectDir = reflect(lightDir, viewNormal);
    float spec = pow(max(dot(viewDir, reflectDir), 0.0), 32.0);

    // Soft fill light
    vec3 fillDir = normalize(vec3(0.3, 1.0, 0.2));
    float fill = max(dot(viewNormal, -fillDir), 0.0) * 0.3;

    // Optional rim light (can remove if not desired)
    float rim = pow(1.0 - max(dot(viewNormal, viewDir), 0.0), 3.0) * 0.15;

    vec3 lighting = (diff + fill + 0.15) * baseColor.rgb + spec * 0.1 * specularColor;

    // Emissive is added *unlit*
    lighting += emissiveColor;

    fsout_Color = vec4(lighting, baseColor.a);

    // Texture testing out color
    //fsout_Color = vec4(baseSpec);
}
