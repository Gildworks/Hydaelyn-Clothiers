#version 450

// Input from Vertex Shader (same as your existing setup)
layout(location = 0) in vec3 fs_Position_VS; // View-space position
layout(location = 1) in vec4 fs_Color_VS;    // Vertex color
layout(location = 2) in vec2 fs_UV_VS;
layout(location = 3) in vec3 fs_Normal_VS;   // Interpolated vertex normal (view-space)
layout(location = 4) in vec3 fs_Tangent_VS;  // Interpolated vertex tangent (view-space)
layout(location = 5) in vec3 fs_Bitangent_VS; // Interpolated vertex bitangent (view-space)

// Textures - set 2: Same as your existing setup
layout(set = 2, binding = 0) uniform texture2D tex_Diffuse;
layout(set = 2, binding = 1) uniform texture2D tex_NormalMap;
layout(set = 2, binding = 2) uniform texture2D tex_SpecularMap;
layout(set = 2, binding = 3) uniform texture2D tex_EmissiveMap;
layout(set = 2, binding = 4) uniform texture2D tex_Alpha;
layout(set = 2, binding = 5) uniform texture2D tex_Roughness;
layout(set = 2, binding = 6) uniform texture2D tex_Metalness;
layout(set = 2, binding = 7) uniform texture2D tex_Occlusion;
layout(set = 2, binding = 8) uniform texture2D tex_Subsurface;
layout(set = 2, binding = 9) uniform sampler SharedSampler;

// Output
layout(location = 0) out vec4 fsout_Color;

// Lighting parameters (same as your existing setup)
const vec3 LIGHT_VECTOR_TO_SOURCE = normalize(vec3(0.5, -1.0, 0.6));
const vec3 LIGHT_COLOR = vec3(1.0, 1.0, 0.95);
const vec3 AMBIENT_LIGHT_COLOR = vec3(0.15, 0.15, 0.20);
const float SPECULAR_INTENSITY = 0.1;
const float SHININESS = 16.0;
const float GAMMA_INV = 1.0/2.2;

void main() {
    // === TEXTURE SAMPLING ===
    vec4 diffuseSample = texture(sampler2D(tex_Diffuse, SharedSampler), fs_UV_VS);
    vec3 specularSample = texture(sampler2D(tex_SpecularMap, SharedSampler), fs_UV_VS).rgb;
    vec4 specularAlpha = texture(sampler2D(tex_SpecularMap, SharedSampler), fs_UV_VS);
    vec3 emissiveSample = texture(sampler2D(tex_EmissiveMap, SharedSampler), fs_UV_VS).rgb;
    vec3 N_tex_sampled = texture(sampler2D(tex_NormalMap, SharedSampler), fs_UV_VS).rgb;

    // Alpha test
    if (diffuseSample.a < 0.75) {
        discard;
    }

    // Base material properties (linear space)
    vec3 baseDiffuseColor = diffuseSample.rgb;
    vec3 materialSpecularColor = specularSample;

    // === NORMAL MAPPING ===
    vec3 tangentSpaceNormal = normalize(N_tex_sampled * 2.0 - 1.0);

    // Prepare TBN basis vectors
    vec3 N_geometric = normalize(fs_Normal_VS);
    vec3 T_interpolated = normalize(fs_Tangent_VS);
    vec3 B = normalize(-fs_Bitangent_VS);

    // Create TBN matrix (transforms from tangent space to view space)
    mat3 TBN = mat3(T_interpolated, B, N_geometric);

    // Transform normal from tangent space to view space
    vec3 N = normalize(TBN * tangentSpaceNormal);

    // === LIGHTING CALCULATIONS ===
    vec3 V = normalize(fs_Position_VS); // View vector
    vec3 L = LIGHT_VECTOR_TO_SOURCE;     // Light vector

    // Diffuse
    float NdotL = max(dot(N, L), 0.0);
    vec3 diffuseLighting = NdotL * LIGHT_COLOR;

    // Specular (Blinn-Phong)
    vec3 H = normalize(L + V);
    float NdotH = max(dot(N, H), 0.0);
    float specularFactor = pow(NdotH, SHININESS);
    vec3 specularLighting = specularFactor * LIGHT_COLOR * SPECULAR_INTENSITY;

    // Subsurface scattering
    float sss_amount = texture(sampler2D(tex_SpecularMap, SharedSampler), fs_UV_VS).b;
    float sss_wrap = 0.2; 
    float NdotL_wrapped = dot(N, L) + sss_wrap;
    float light_intensity = max(0.0, NdotL_wrapped) / (1.0 + sss_wrap);
    light_intensity = pow(light_intensity, 2.0);
    vec3 sss_light = light_intensity * baseDiffuseColor;

    // Combine lighting
    vec3 finalColorLinear = (AMBIENT_LIGHT_COLOR * baseDiffuseColor)
                          + (diffuseLighting * baseDiffuseColor)
                          + sss_light
                          + specularLighting * materialSpecularColor;

    // Tone mapping
    float maxWhite = 2.5;
    vec3 conversion = finalColorLinear * (1.0 + (finalColorLinear / vec3(maxWhite * maxWhite)));
    vec3 toneMapped = conversion / (1.0 + finalColorLinear);
    
    // Gamma correction
    bvec3 cutoff = lessThan(toneMapped.rgb, vec3(0.0031308));
    vec3 higher = vec3(1.055)*pow(toneMapped.rgb, vec3(1.0/2.4)) - vec3(0.055);
    vec3 lower = toneMapped.rgb * vec3(12.92);
    vec3 finalColorSRGB = vec3(mix(higher, lower, cutoff));

    fsout_Color = vec4(finalColorSRGB, diffuseSample.a);
}