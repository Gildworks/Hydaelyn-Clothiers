#version 450

layout(set = 0, binding = 0) uniform TransformsBuffer {
    mat4 u_WorldViewProjection;
    mat4 u_WorldView;
};

// --- ADD THIS NEW BONE TRANSFORMS BUFFER ---
layout(set = 2, binding = 0) uniform BoneTransforms
{
    mat4 u_Bones[256]; // Max 256 bones per mesh
};

// Input from Vertex Buffer
layout(location = 0) in vec3 in_Position_OS;
layout(location = 1) in vec3 in_Normal_OS;
layout(location = 2) in vec4 in_Color;
layout(location = 3) in vec2 in_UV;
layout(location = 4) in vec3 in_Tangent_OS;
layout(location = 5) in vec3 in_BiTangent_OS; // Note: You had a typo here in your original code (Bitangent)
// --- ADD THE NEW SKINNING INPUTS ---
layout(location = 6) in vec4 in_BoneIndices;
layout(location = 7) in vec4 in_BoneWeights;


// Output to Fragment Shader
layout(location = 0) out vec3 fs_Position_VS;
layout(location = 1) out vec4 fs_Color_VS;
layout(location = 2) out vec2 fs_UV_VS;
layout(location = 3) out vec3 fs_Normal_VS;
layout(location = 4) out vec3 fs_Tangent_VS;
layout(location = 5) out vec3 fs_BiTangent_VS;

void main() {
    // --- SKINNING LOGIC ---
    mat4 skinMatrix = mat4(0.0);
    
    // Accumulate bone transformations based on weights
    skinMatrix += in_BoneWeights.x * u_Bones[int(in_BoneIndices.x)];
    skinMatrix += in_BoneWeights.y * u_Bones[int(in_BoneIndices.y)];
    skinMatrix += in_BoneWeights.z * u_Bones[int(in_BoneIndices.z)];
    skinMatrix += in_BoneWeights.w * u_Bones[int(in_BoneIndices.w)];

    // Transform vertex position and normals by the final skinning matrix
    vec4 skinnedPosition = skinMatrix * vec4(in_Position_OS, 1.0);
    vec4 skinnedNormal = skinMatrix * vec4(in_Normal_OS, 0.0); // Use 0.0 for w for normals
    vec4 skinnedTangent = skinMatrix * vec4(in_Tangent_OS, 0.0);
    vec4 skinnedBiTangent = skinMatrix * vec4(in_BiTangent_OS, 0.0);

    // --- STANDARD TRANSFORMATIONS ---
    gl_Position = u_WorldViewProjection * skinnedPosition;

    vec4 position_VS = u_WorldView * skinnedPosition;
    fs_Position_VS = position_VS.xyz;

    mat3 viewModelMatrix3x3 = mat3(u_WorldView);
    fs_Normal_VS = normalize(viewModelMatrix3x3 * skinnedNormal.xyz);
    fs_Tangent_VS = normalize(viewModelMatrix3x3 * skinnedTangent.xyz);
    fs_BiTangent_VS = normalize(viewModelMatrix3x3 * skinnedBiTangent.xyz);
    
    fs_Color_VS = in_Color;
    fs_UV_VS = in_UV;
}