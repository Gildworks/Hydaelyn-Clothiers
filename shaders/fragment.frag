#version 450

layout(location = 0) in vec3 fs_Position_VS;
layout(location = 1) in vec4 fs_Color_VS;
layout(location = 2) in vec2 fs_UV_VS;
layout(location = 3) in vec3 fs_Normal_VS;
layout(location = 4) in vec3 fs_Tangent_VS;
layout(location = 5) in vec3 fs_Bitangent_VS;

layout(set = 1, binding = 0) uniform texture2D tex_Diffuse;
layout(set = 1, binding = 1) uniform texture2D tex_Normal;
layout(set = 1, binding = 2) uniform texture2D tex_Specular;   // rgb: spec color, a: gloss (0..1)
layout(set = 1, binding = 3) uniform texture2D tex_Emissive;
layout(set = 1, binding = 4) uniform texture2D tex_Alpha;
layout(set = 1, binding = 5) uniform texture2D tex_Roughness;  // r: roughness (0..1)
layout(set = 1, binding = 6) uniform texture2D tex_Metalness;  // r: metalness (0..1)
layout(set = 1, binding = 7) uniform texture2D tex_Occlusion;  // r: ao
layout(set = 1, binding = 8) uniform texture2D tex_Subsurface; // rgb: tint, a: thickness/strength
layout(set = 1, binding = 9) uniform sampler     SharedSampler;

layout(set = 1, binding = 10) uniform MaterialParameters {
    float u_ShaderPackId;   // 1=IRIS, 2=SKIN, 3=HAIR
    float u_MaterialFlags;
    float u_AlphaThreshold;
    float u_SpecularPower;  // used by default branch (as a bias)
} materialParams;

layout(location = 0) out vec4 fsout_Color;

const vec3 LIGHT_VECTOR_TO_SOURCE = normalize(-vec3(10.5, 10.8, -10.6));
const vec3 LIGHT_COLOR         = vec3(1.0, 0.947, 0.888);
const vec3 AMBIENT_LIGHT_COLOR = vec3(0.154, 0.203, 0.33);

const float IRIS = 1.0;
const float SKIN = 2.0;
const float HAIR = 3.0;

vec3 safeNormalize(vec3 v){ float len = max(length(v), 1e-8); return v / len; }
vec3 fresnelSchlick(float cosTheta, vec3 F0){ return F0 + (1.0 - F0) * pow(1.0 - cosTheta, 5.0); }

float burleyDiffuse(float NdotL, float NdotV, float LdotH, float roughness){
    float F90 = 0.5 + 2.0 * LdotH * LdotH * roughness;
    float NL  = clamp(NdotL,0.0,1.0);
    float NV  = clamp(NdotV,0.0,1.0);
    return (1.0 + (F90-1.0)*pow(1.0-NL,5.0)) * (1.0 + (F90-1.0)*pow(1.0-NV,5.0)) * NL;
}

void main(){
    // --- Texture samples ---
    vec4  albedo       = texture(sampler2D(tex_Diffuse,   SharedSampler), fs_UV_VS);
    vec4  normalSample = texture(sampler2D(tex_Normal,    SharedSampler), fs_UV_VS);
    vec4  specSample   = texture(sampler2D(tex_Specular,  SharedSampler), fs_UV_VS); // rgb color, a gloss
    vec3  emissive     = texture(sampler2D(tex_Emissive,  SharedSampler), fs_UV_VS).rgb;
    float roughMap     = texture(sampler2D(tex_Roughness, SharedSampler), fs_UV_VS).r;
    float metalness    = texture(sampler2D(tex_Metalness, SharedSampler), fs_UV_VS).r;
    float occlusion    = texture(sampler2D(tex_Occlusion, SharedSampler), fs_UV_VS).r;
    vec4  subsample    = texture(sampler2D(tex_Subsurface,SharedSampler), fs_UV_VS);

    // --- Alpha policy ---
    float outAlpha;
    if (materialParams.u_ShaderPackId == HAIR){
        if (albedo.a < 0.8) discard;      // hair: only albedo.a, hard cut
        outAlpha = albedo.a;
    } else {
        if (albedo.a < materialParams.u_AlphaThreshold) discard;
        outAlpha = albedo.a;
    }

    // --- Base colors ---
    vec3 baseColor     = albedo.rgb;// * occlusion;
    vec3 specColorMap  = specSample.rgb;
    float gloss        = specSample.a;                 // 0..1, higher = glossier
    float roughnessIn  = clamp(roughMap, 0.0, 1.0);

    // combine roughness + gloss
    float roughFromGloss = 1.0 - gloss;
    float roughCombined  = clamp(mix(roughnessIn, roughFromGloss, 0.7), 0.0, 1.0);

    // --- TBN & normal ---
    //normalSample.g = 1.0 - normalSample.g; // uncomment if your maps are Y-inverted
    vec3 n_ts = normalize(normalSample.xyz * 2.0 - 1.0);
    vec3 N = normalize(fs_Normal_VS);
    vec3 T = normalize(fs_Tangent_VS);
    vec3 B = normalize(fs_Bitangent_VS);
    mat3 TBN = mat3(T,B,N);
    N = normalize(TBN * n_ts);

    // --- Vectors ---
    vec3 V = normalize(-fs_Position_VS);
    vec3 L = LIGHT_VECTOR_TO_SOURCE;
    vec3 H = normalize(L + V);

    float NdotL = max(dot(N,L),0.0);
    float NdotV = max(dot(N,V),0.0);
    float LdotH = max(dot(L,H),0.0);
    float NdotH = max(dot(N,H),0.0);

    // --- F0 & strength ---
    float specStrength = clamp(max(max(specColorMap.r,specColorMap.g), specColorMap.b), 0.0, 1.0);
    vec3  dielectricF0 = mix(vec3(0.02), vec3(0.06), specStrength); // low floor de-shines fabrics
    vec3  F0_specTint  = mix(dielectricF0, specColorMap, specStrength * 0.9);
    vec3  F0_base      = mix(F0_specTint, baseColor, clamp(metalness,0.0,1.0));

    vec3 lightingLinear = vec3(0.0);

    // ===== SKIN (kept from the tuned version) =====
    if (materialParams.u_ShaderPackId == SKIN){
        // Use your combined roughness, but don't let skin get glassy
        float roughSkin = max(roughCombined, 0.55);

        // Map roughness/gloss to shininess (tighter highlight when glossier)
        // gloss is 0..1 from tex_Specular.a (in your inputs)
        float glossBias = clamp(gloss, 0.0, 1.0);
        float shininessSkin = mix(8.0, 128.0, clamp(1.0 - roughSkin, 0.0, 1.0));
        //shininessSkin = mix(shininessSkin, shininessSkin * 1.5, glossBias); // small extra pop from gloss

        // Specular color: keep your specular tint map; clamp a touch so skin doesn't blow out
        vec3 specTint = clamp(specColorMap, 0.0, 1.0);

        // --- Light wrap diffuse ---
        // 0 = Lambert, 0.2~0.5 = noticeable wrap around the terminator
        // You can expose this as a uniform if you want to tweak per-material.
        float wrap = 1.0;
        float NdotL_wrap = (dot(N, L) + wrap) / (1.0 + wrap);
        float diffuseWrap = max(NdotL_wrap, 0.0);
        // Optional softening like your old code:
        diffuseWrap = diffuseWrap * diffuseWrap; // nicer falloff

        // --- Specular (Blinn–Phong) ---
        float specBP = pow(NdotH, shininessSkin);
        vec3  specularTerm = specBP * specTint * 0.025;

        // --- Compose ---
        vec3 diffuseTerm = diffuseWrap * baseColor;
        vec3 ambient     = AMBIENT_LIGHT_COLOR * baseColor;

        lightingLinear = ambient
                       + (diffuseTerm  * LIGHT_COLOR)
                       + (specularTerm * LIGHT_COLOR)
                       + emissive;
    }
    // ===== IRIS (your F0=5 mix restored, medium-tight highlight) =====
    else if (materialParams.u_ShaderPackId == IRIS){
        float roughEye = clamp(mix(roughCombined, 0.15, 0.5), 0.10, 0.22);
        float shininessEye = mix(160.0, 300.0, clamp(1.0 - roughEye, 0.0, 1.0));

        vec3  F0_eye_base = mix(vec3(0.05), vec3(5.0), specStrength); // intentional high F0 mix
        vec3  F0_eye      = mix(F0_eye_base, specColorMap, specStrength * 0.3);
        vec3  F_eye       = fresnelSchlick(max(dot(H,V),0.0), F0_eye);

        float specEye = pow(NdotH, shininessEye);
        vec3  specularTerm = specEye * F_eye * 1.2;

        vec3 diffuseTerm = NdotL * baseColor; // full diffuse (your request)
        vec3 ambient = AMBIENT_LIGHT_COLOR * baseColor * 0.5;
        lightingLinear = ambient + (diffuseTerm * LIGHT_COLOR) + (specularTerm * LIGHT_COLOR) + emissive;
    }
    // ===== HAIR (back to Blinn–Phong, stable on cards) =====
    else if (materialParams.u_ShaderPackId == HAIR){
        baseColor = albedo.rgb;
        float roughHair     = max(roughCombined, 0.60);                    // avoid glassy strands
        float shininessHair = mix(64.0, 128.0, clamp(1.0 - roughHair,0.0,1.0));
        float specBP        = pow(NdotH, shininessHair);
        vec3  specularTerm  = specBP * specColorMap * 0.02;                       // tinted by hair spec map

        vec3 diffuseTerm = (1.0 * NdotL) * baseColor;                     // small diffuse bed
        vec3 ambient     = AMBIENT_LIGHT_COLOR * baseColor;

        lightingLinear = ambient + (diffuseTerm * LIGHT_COLOR) + (specularTerm * LIGHT_COLOR) + emissive;
    }
    // ===== DEFAULT / GEAR =====
    else {
        float specMask = dot(specSample.rgb, vec3(1.0));
        specMask = smoothstep(0.2, 1.0, specMask);
        // Default / gear: spec map controls tint + gloss, metalness respected
        float NdotH = max(dot(N, H), 0.0);
        float specBP = pow(NdotH, max(materialParams.u_SpecularPower, 1.0));
        vec3  specularTerm = (specBP * specMask) * F0_base;

        vec3 diffuseTerm = NdotL * baseColor;
        vec3 ambient     = AMBIENT_LIGHT_COLOR * baseColor;

        lightingLinear = ambient + (diffuseTerm * LIGHT_COLOR) + (specularTerm * LIGHT_COLOR) + emissive;
    }

    // Tone map + gamma
    float maxWhite = 2.5;
    vec3 conv = lightingLinear * (1.0 + (lightingLinear / vec3(maxWhite * maxWhite)));
    vec3 toneMapped = conv / (1.0 + lightingLinear);

    bvec3 cutoff = lessThan(toneMapped, vec3(0.0031308));
    vec3 higher = vec3(1.055) * pow(toneMapped, vec3(1.0/2.4)) - vec3(0.055);
    vec3 lower  = toneMapped * vec3(12.92);
    vec3 finalSRGB = mix(higher, lower, cutoff);

    fsout_Color = vec4(finalSRGB, outAlpha);
}
