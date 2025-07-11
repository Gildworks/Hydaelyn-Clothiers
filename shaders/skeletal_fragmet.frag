# version 450

// Input from Vertex Shader
layout(location = 0) in vec3 fs_Position_VS;
layout(location = 1) in vec4 fs_Color_VS;
layout(location = 2) in vec2 fs_UV_VS;
layout(location = 3) in vec3 fs_Normal_VS;
layout(location = 4) in vec3 fs_Tangent_VS;
layout(location = 5) in vec3 fs_Bitangent_VS;
layout(location = 6) in float fs_MaterialIndex;  // We'll ignore this for now

// TEMPORARY: Use regular 2D textures instead of arrays
layout(set = 3, binding = 0) uniform texture2D tex_Diffuse;    // NOT tex_DiffuseArray
layout(set = 3, binding = 1) uniform texture2D tex_Normal;     // NOT tex_NormalArray
layout(set = 3, binding = 2) uniform texture2D tex_Specular;   // etc.
layout(set = 3, binding = 3) uniform texture2D tex_Emissive;
layout(set = 3, binding = 4) uniform texture2D tex_Alpha;
layout(set = 3, binding = 5) uniform texture2D tex_Roughness;
layout(set = 3, binding = 6) uniform texture2D tex_Metalness;
layout(set = 3, binding = 7) uniform texture2D tex_Occlusion;
layout(set = 3, binding = 8) uniform texture2D tex_Subsurface;
layout(set = 3, binding = 9) uniform sampler SharedSampler;

layout(location = 0) out vec4 fsout_Color;

// Lighting parameters
const vec3 LIGHT_VECTOR_TO_SOURCE = normalize(vec3(0.5, -1.0, 0.6));
const vec3 LIGHT_COLOR = vec3(1.0, 1.0, 0.95);
const vec3 AMBIENT_LIGHT_COLOR = vec3(0.15, 0.15, 0.20);
const float SPECULAR_INTENSITY = 0.1;
const float SHININESS = 16.0;

void main() {
    // TEMPORARY: Just use the first material's textures for everything
    // (Ignore fs_MaterialIndex for now)
    
    // Sample from regular 2D textures
    vec4 diffuseSample = texture(sampler2D(tex_Diffuse, SharedSampler), fs_UV_VS);
    vec3 specularSample = texture(sampler2D(tex_Specular, SharedSampler), fs_UV_VS).rgb;
    vec3 emissiveSample = texture(sampler2D(tex_Emissive, SharedSampler), fs_UV_VS).rgb;
    vec3 N_tex_sampled = texture(sampler2D(tex_Normal, SharedSampler), fs_UV_VS).rgb;
    
    // Alpha test
    if (diffuseSample.a < 0.75) {
        discard;
    }

    // Base material properties
    vec3 baseDiffuseColor = diffuseSample.rgb;
    vec3 materialSpecularColor = specularSample;

    // Normal mapping
    vec3 tangentSpaceNormal = normalize(N_tex_sampled * 2.0 - 1.0);
    vec3 N_geometric = normalize(fs_Normal_VS);
    vec3 T_interpolated = normalize(fs_Tangent_VS);
    vec3 B = normalize(-fs_Bitangent_VS);
    mat3 TBN = mat3(T_interpolated, B, N_geometric);
    vec3 N = normalize(TBN * tangentSpaceNormal);

    // Simple lighting
    vec3 V = normalize(fs_Position_VS);
    vec3 L = LIGHT_VECTOR_TO_SOURCE;
    float NdotL = max(dot(N, L), 0.0);
    vec3 diffuseLighting = NdotL * LIGHT_COLOR;

    // Combine lighting
    vec3 finalColorLinear = (AMBIENT_LIGHT_COLOR * baseDiffuseColor) + (diffuseLighting * baseDiffuseColor);

    // Simple gamma correction
    vec3 finalColorSRGB = pow(finalColorLinear, vec3(1.0/2.2));

    fsout_Color = vec4(finalColorSRGB, diffuseSample.a);
}