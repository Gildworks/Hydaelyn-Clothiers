#version 450

// Input from Vertex Shader
layout(location = 0) in vec3 fs_Position_VS; // View-space position
layout(location = 1) in vec4 fs_Color_VS;    // Vertex color (currently unused for diffuse base)
layout(location = 2) in vec2 fs_UV_VS;
layout(location = 3) in vec3 fs_Normal_VS;   // Interpolated vertex normal (view-space)
layout(location = 4) in vec3 fs_Tangent_VS;  // Interpolated vertex tangent (view-space)
// If you are passing Bitangent from Vertex Shader:
layout(location = 5) in vec3 fs_Bitangent_VS; // Interpolated vertex bitangent (view-space)

// Textures
layout(set = 1, binding = 0) uniform texture2D tex_Diffuse;
layout(set = 1, binding = 1) uniform texture2D tex_NormalMap;
layout(set = 1, binding = 2) uniform texture2D tex_SpecularMap;
layout(set = 1, binding = 3) uniform texture2D tex_EmissiveMap;
layout(set = 1, binding = 9) uniform sampler SharedSampler;

// Output
layout(location = 0) out vec4 fsout_Color;

// --- Lighting Parameters ---
// Your "perfect" light direction for projection was L = normalize(vec3(0.5, 0.8, 0.6))
// This L is the vector FROM surface TO light.
// So, if LIGHT_POINTS_TO_SURFACE is the vector the light rays travel along:
// const vec3 LIGHT_POINTS_TO_SURFACE = normalize(vec3(-0.5, -0.8, -0.6)); // Example: opposite of your L

// Let's define LIGHT_VECTOR_TO_SOURCE as the vector from surface point TO the light source
// This is what you directly used as 'L' previously.
const vec3 LIGHT_VECTOR_TO_SOURCE = normalize(-vec3(10.5, 10.8, -10.6)); // Your "perfect" L for projection
                                                                  // For lighting, you might want to adjust this.
                                                                  // e.g., for a light more from the front:
                                                                  // const vec3 LIGHT_VECTOR_TO_SOURCE = normalize(vec3(0.3, 0.7, -0.5));


const vec3 LIGHT_COLOR = vec3(1.0, 0.947, 0.888);
const vec3 AMBIENT_LIGHT_COLOR = vec3(0.154, 0.203, 0.33); // Slightly increased ambient
const float SPECULAR_INTENSITY = 0.1;
const float SHININESS = 16.0;
const float GAMMA_INV = 1.0/2.2;

void main() {
    // 1. Sample Textures (GPU converts sRGB textures to linear here)
    vec4 diffuseSample = texture(sampler2D(tex_Diffuse, SharedSampler), fs_UV_VS);
    vec3 specularSample = texture(sampler2D(tex_SpecularMap, SharedSampler), fs_UV_VS).rgb;
    vec3 emissiveSample = texture(sampler2D(tex_EmissiveMap, SharedSampler), fs_UV_VS).rgb;
    vec3 N_tex_sampled = texture(sampler2D(tex_NormalMap, SharedSampler), fs_UV_VS).rgb;

    // Alpha test
    if (diffuseSample.a < 0.75) {
        discard;
    }

    // Base material properties (linear space)
    vec3 baseDiffuseColor = diffuseSample.rgb; // Not using fs_Color_VS for base diffuse now
    vec3 materialSpecularColor = specularSample;

    // 2. Normal Calculation (incorporating Normal Mapping)
    // Decode sampled normal map value from [0,1] to [-1,1]
    // Green channel inversion: Check if ModelTexture.cs (colors.InvertNormalGreen)
    // or your FFXIV source normal map convention requires this.
    // If ModelTexture.cs already inverted it, DON'T do it here.
    // N_tex_sampled.g = 1.0 - N_tex_sampled.g;
    vec3 tangentSpaceNormal = normalize(N_tex_sampled * 2.0 - 1.0);

    // Prepare TBN basis vectors (ensure they are normalized and orthogonal)
    vec3 N_geometric = normalize(fs_Normal_VS);
    vec3 T_interpolated = normalize(fs_Tangent_VS);

    // Calculate Bitangent (B).
    // Ensure correct handedness. cross(N, T) is common.
    // If normal map details look inverted (dents as bumps on one side), try cross(T, N).
    // Also, sometimes fs_Tangent_VS might have a .w component indicating handedness.
    vec3 B_calculated = normalize(cross(T_interpolated, N_geometric));
    // If you passed fs_Bitangent_VS from the vertex shader:
    // vec3 B_passed = normalize(fs_Bitangent_VS);
    // You might need to ensure B_passed is orthogonal to T_interpolated and N_geometric,
    // or reconcile it with B_calculated (e.g., dot(cross(N_geometric, T_interpolated), B_passed) > 0 ? B_passed : -B_passed).
    // For now, let's use the calculated B:
    vec3 B = normalize(fs_Bitangent_VS);

    // Create TBN matrix (transforms from tangent space to view space)
    mat3 TBN = mat3(T_interpolated, B, N_geometric);

    // Transform normal from tangent space to view space
    vec3 N = normalize(TBN * tangentSpaceNormal); // THIS IS THE FINAL PER-PIXEL NORMAL

    // 3. Lighting Calculations (in View Space)
    vec3 V = normalize(-fs_Position_VS); // View vector
    vec3 L = LIGHT_VECTOR_TO_SOURCE;     // Vector from surface point TO light source

    // Diffuse
    float NdotL = max(dot(N, L), 0.0);
    vec3 diffuseLighting = NdotL * LIGHT_COLOR;

    // Specular (Blinn-Phong)
    vec3 H = normalize(L + V); // Half-vector
    float NdotH = max(dot(N, H), 0.0);
    float specularFactor = pow(NdotH, SHININESS);
    vec3 specularLighting = specularFactor * LIGHT_COLOR * SPECULAR_INTENSITY;

    // SSS
    float sss_amount = texture(sampler2D(tex_SpecularMap, SharedSampler), fs_UV_VS).b;

    // 'wrap' is a parameter you can tune (e.g., 0.2 is a good start)
    // It controls how far the light "wraps" around the object.
    float sss_wrap = .5; 
    float NdotL_wrapped = dot(N, L) + sss_wrap;
    float light_intensity = max(0.0, NdotL_wrapped) / (1.0 + sss_wrap);
    light_intensity = pow(light_intensity, 2.0); // Squaring it makes the falloff look nicer

    // The SSS light is tinted by the skin color
    vec3 sss_light = light_intensity * baseDiffuseColor; // * sss_amount;

    // Combine lighting
    vec3 finalColorLinear = (AMBIENT_LIGHT_COLOR * baseDiffuseColor)
                          + (diffuseLighting * baseDiffuseColor)
                          + sss_light
                          + specularLighting * materialSpecularColor
                          ;

    
    // === Reinhard Tone Mapping ===
    //vec3 toneMapped = finalColorLinear;
    //vec3 toneMapped = finalColorLinear / (finalColorLinear + vec3(1.0));

    // === Extended Reinhard Tone Mapping
    float maxWhite = 2.5;
    vec3 conversion = finalColorLinear * (1.0 + (finalColorLinear / vec3(maxWhite * maxWhite)));
    vec3 toneMapped = conversion / (1.0 + finalColorLinear);
    
    
    // 4. Gamma Correction for non-sRGB framebuffer
    bvec3 cutoff = lessThan(toneMapped.rgb, vec3(0.0031308));
    vec3 higher = vec3(1.055)*pow(toneMapped.rgb, vec3(1.0/2.4)) - vec3(0.055);
    vec3 lower = toneMapped.rgb * vec3(12.92);

    vec3 finalColorSRGB = vec3 (mix(higher, lower, cutoff));

    fsout_Color = vec4(finalColorSRGB, diffuseSample.a);
}