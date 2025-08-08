#version 450

layout(location = 0) in vec3 fs_Position_VS;
layout(location = 1) in vec4 fs_Color_VS;
layout(location = 2) in vec2 fs_UV_VS;
layout(location = 3) in vec3 fs_Normal_VS;
layout(location = 4) in vec3 fs_Tangent_VS;
layout(location = 5) in vec3 fs_Bitangent_VS;

layout(set = 1, binding = 0) uniform texture2D tex_Diffuse;
layout(set = 1, binding = 1) uniform texture2D tex_Normal;
layout(set = 1, binding = 2) uniform texture2D tex_Specular;   // rgb: spec color, a: gloss (0..1, higher=glossier)
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
    float u_SpecularPower;
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

/* ===== Anisotropic GGX (Heitz) – for hair =====
   ax = alpha along T, ay = alpha along B (alpha = roughness^2)
*/
float D_GGX_Aniso(vec3 N, vec3 H, vec3 T, vec3 B, float ax, float ay){
    float NxH = max(dot(N,H), 0.0);
    if (NxH <= 0.0) return 0.0;
    float TxH = dot(T,H);
    float BxH = dot(B,H);
    float a2 = (TxH*TxH)/(ax*ax) + (BxH*BxH)/(ay*ay) + (NxH*NxH);
    return 1.0 / (3.14159265 * ax * ay * a2 * a2);
}

float lambda_GGX_Aniso(vec3 N, vec3 X, vec3 T, vec3 B, float ax, float ay){
    float NxX = max(dot(N,X), 0.0);
    float TxX = dot(T,X);
    float BxX = dot(B,X);
    float tan2 = ( (TxX*TxX)/(ax*ax) + (BxX*BxX)/(ay*ay) ) / max(NxX*NxX, 1e-6);
    return 0.5 * (-1.0 + sqrt(1.0 + tan2));
}

float G_Smith_Aniso(vec3 N, vec3 V, vec3 L, vec3 T, vec3 B, float ax, float ay){
    float lambdaV = lambda_GGX_Aniso(N, V, T, B, ax, ay);
    float lambdaL = lambda_GGX_Aniso(N, L, T, B, ax, ay);
    return 1.0 / (1.0 + lambdaV + lambdaL);
}

vec3 spec_ggx_aniso(vec3 N, vec3 V, vec3 L, vec3 T, vec3 B, vec3 F0, float ax, float ay){
    vec3 H = safeNormalize(V + L);
    float NdotV = max(dot(N,V), 0.0);
    float NdotL = max(dot(N,L), 0.0);
    if (NdotV <= 0.0 || NdotL <= 0.0) return vec3(0.0);
    float  D = D_GGX_Aniso(N, H, T, B, ax, ay);
    float  G = G_Smith_Aniso(N, V, L, T, B, ax, ay);
    vec3   F = fresnelSchlick(max(dot(H,V),0.0), F0);
    return (D * G) * F / max(4.0 * NdotV * NdotL, 1e-6);
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
        outAlpha = albedo.a;              // still ignoring other alpha maps for now
    }

    // --- Base colors ---
    vec3 baseColor     = albedo.rgb * occlusion;
    vec3 specColorMap  = specSample.rgb;
    float gloss        = specSample.a;                // 0..1, higher = glossier
    float roughnessIn  = clamp(roughMap, 0.0, 1.0);

    // Combine roughness with gloss (let either drive highlight width)
    // weight=0.7 favors gloss when present but still respects roughness map.
    float roughFromGloss = 1.0 - gloss;
    float roughCombined  = clamp(mix(roughnessIn, roughFromGloss, 0.7), 0.0, 1.0);

    // --- TBN & normal ---
    // normalSample.g = 1.0 - normalSample.g; // uncomment if needed by your normal map convention
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

    // --- F0: bring spec map back into play (also metalness) ---
    // specStrength = how strong/tinted the spec is from the map
    float specStrength = clamp(max(max(specColorMap.r,specColorMap.g), specColorMap.b), 0.0, 1.0);
    vec3  dielectricF0 = mix(vec3(0.03), vec3(0.08), specStrength); // 0.03..0.08 range driven by spec map
    vec3  F0_specTint  = mix(dielectricF0, specColorMap, specStrength * 0.9); // tint toward map color
    vec3  F0_base      = mix(F0_specTint, baseColor, clamp(metalness,0.0,1.0));

    vec3 lightingLinear = vec3(0.0);

    if (materialParams.u_ShaderPackId == SKIN){
        // Keep the de-shined skin from last version
        float roughSkin = max(roughCombined, 0.55);
        float shininessSkin = mix(8.0, 128.0, clamp(1.0 - roughSkin,0.0,1.0));
        vec3  F0_skin = vec3(0.028);
        vec3  F_skin  = fresnelSchlick(max(dot(H,V),0.0), F0_skin);
        F_skin = min(F_skin, vec3(0.08));
        float specBP  = pow(NdotH, shininessSkin);
        vec3  specularTerm = specBP * F_skin;

        float thickness = subsample.a;
        float wrap = mix(0.08, 0.4, clamp(thickness,0.0,1.0));
        float NL_wrap = clamp((dot(N,L) + wrap) / (1.0 + wrap), 0.0, 1.0);
        float back = max(dot(-N, L), 0.0);
        float backScatter = back * thickness * pow(NdotV, 0.6) * 0.6;

        float fd = burleyDiffuse(NdotL, NdotV, LdotH, roughSkin);
        float avgF = clamp((F_skin.r + F_skin.g + F_skin.b) / 3.0, 0.0, 1.0);
        vec3  diffuseEpidermal = fd * baseColor * (1.0 - avgF);

        vec3  sssColor = mix(baseColor, subsample.rgb, clamp(thickness,0.0,1.0));
        vec3  sssTerm  = (0.25 * NL_wrap + 0.5 * backScatter) * sssColor;

        vec3 direct  = (diffuseEpidermal + sssTerm) * LIGHT_COLOR + specularTerm * LIGHT_COLOR;
        vec3 ambient = AMBIENT_LIGHT_COLOR * baseColor;
        lightingLinear = ambient + direct + emissive;
    }
    else if (materialParams.u_ShaderPackId == IRIS){
        // Mid-tight highlight (between previous two), full diffuse as requested
        float roughEye = clamp(mix(roughCombined, 0.15, 0.5), 0.08, 0.22); // clamp to mid-glossy range
        float shininessEye = mix(160.0, 300.0, clamp(1.0 - roughEye, 0.0, 1.0));

        // Eye spec color: prefer neutral/whitish but allow map tint/strength
        vec3  F0_eye_base = mix(vec3(0.05), vec3(5.0), specStrength);
        vec3  F0_eye      = mix(F0_eye_base, specColorMap, specStrength * 0.3); // small tint allowance
        vec3  F_eye       = fresnelSchlick(max(dot(H,V),0.0), F0_eye);

        float specEye = pow(NdotH, shininessEye);
        vec3  specularTerm = specEye * F_eye * 1.2; // small boost

        // Full diffuse
        vec3 diffuseTerm = NdotL * baseColor;

        vec3 ambient = AMBIENT_LIGHT_COLOR * baseColor * 0.5;
        lightingLinear = ambient + (diffuseTerm * LIGHT_COLOR) + (specularTerm * LIGHT_COLOR) + emissive;
    }
    else if (materialParams.u_ShaderPackId == HAIR){
        // ===== Anisotropic GGX hair =====
        // pick a base alpha from roughness, then stretch along tangent with "anisotropy"
        float a = max(roughCombined, 0.25);       // don’t get too glassy
        float aniso = 0.7;                        // 0=iso, + stretches along T
        float ax = a*a * (1.0 + aniso);
        float ay = a*a * max(0.2, (1.0 - aniso));

        vec3 specAniso = spec_ggx_aniso(N, V, L, T, B, F0_base, ax, ay);

        // tiny diffuse bed so strands aren’t pitch black
        vec3 diffuseHair = (0.25 * NdotL) * baseColor;
        diffuseHair = albedo.rgb;

        vec3 hairLit = (diffuseHair + specAniso) * LIGHT_COLOR;
        vec3 ambient = AMBIENT_LIGHT_COLOR * baseColor;
        lightingLinear = ambient + hairLit + emissive;
    }
    else {
        // Default / gear: spec map controls tint + gloss, metalness respected
        float NdotH = max(dot(N, H), 0.0);
        float specBP = pow(NdotH, max(materialParams.u_SpecularPower, 1.0));
        vec3  specularTerm = specBP * F0_base;

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
