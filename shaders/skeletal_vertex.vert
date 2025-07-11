#version 450

// Uniforms - set 0: Transform matrices (matching your existing setup)
layout(set = 0, binding = 0) uniform TransformsBuffer {
    mat4 u_WorldViewProjection; // For gl_Position
    mat4 u_WorldView;           // For transforming positions, normals, tangents to View Space
};

// Uniforms - set 1: Bone matrices for skeletal animation
layout(set = 1, binding = 0) uniform BoneMatrices {
    mat4 u_BoneMatrices[256];   // Up to 256 bones
};

// Input from Vertex Buffer (updated with bone data)
layout(location = 0) in vec3 in_Position_OS;    // Object Space (Model Space)
layout(location = 1) in vec4 in_Color;
layout(location = 2) in vec2 in_UV;
layout(location = 3) in vec3 in_Normal_OS;      // Object Space
layout(location = 4) in vec3 in_Tangent_OS;     // Object Space
layout(location = 5) in vec3 in_BiTangent_OS;   // Object Space
layout(location = 6) in vec4 in_BoneIndices;    // Bone indices (up to 4 bones per vertex)
layout(location = 7) in vec4 in_BoneWeights;    // Bone weights (normalized 0-1)

// Output to Fragment Shader (same as your existing setup)
layout(location = 0) out vec3 fs_Position_VS;   // View Space
layout(location = 1) out vec4 fs_Color_VS;
layout(location = 2) out vec2 fs_UV_VS;
layout(location = 3) out vec3 fs_Normal_VS;     // View Space
layout(location = 4) out vec3 fs_Tangent_VS;    // View Space
layout(location = 5) out vec3 fs_BiTangent_VS;  // View Space

void main() {
    // === SKELETAL ANIMATION CALCULATIONS ===
    
    // Calculate skinned position
    vec4 skinnedPosition = vec4(0.0, 0.0, 0.0, 0.0);
    vec3 skinnedNormal = vec3(0.0, 0.0, 0.0);
    vec3 skinnedTangent = vec3(0.0, 0.0, 0.0);
    vec3 skinnedBitangent = vec3(0.0, 0.0, 0.0);
    
    // Apply bone transformations (up to 4 bones per vertex)
    for (int i = 0; i < 4; i++) {
        int boneIndex = int(in_BoneIndices[i]);
        float weight = in_BoneWeights[i];
        
        if (weight > 0.0 && boneIndex < 256) {
            mat4 boneMatrix = u_BoneMatrices[boneIndex];
            
            skinnedPosition += (boneMatrix * vec4(in_Position_OS, 1.0)) * weight;
            skinnedNormal += (mat3(boneMatrix) * in_Normal_OS) * weight;
            skinnedTangent += (mat3(boneMatrix) * in_Tangent_OS) * weight;
            skinnedBitangent += (mat3(boneMatrix) * in_BiTangent_OS) * weight;
        }
    }
    
    // Normalize the transformed vectors
    skinnedNormal = normalize(skinnedNormal);
    skinnedTangent = normalize(skinnedTangent);
    skinnedBitangent = normalize(skinnedBitangent);
    
    // === STANDARD VERTEX PROCESSING (using skinned values) ===
    
    // Transform position to Clip Space for output to rasterizer
    gl_Position = u_WorldViewProjection * skinnedPosition;

    // Transform position to View Space for lighting calculations in Fragment Shader
    vec4 position_VS = u_WorldView * skinnedPosition;
    fs_Position_VS = position_VS.xyz;

    // Transform normal, tangent, and bitangent to View Space
    mat3 viewModelMatrix3x3 = mat3(u_WorldView);
    fs_Normal_VS = normalize(viewModelMatrix3x3 * skinnedNormal);
    fs_Tangent_VS = normalize(viewModelMatrix3x3 * skinnedTangent);
    fs_BiTangent_VS = normalize(viewModelMatrix3x3 * skinnedBitangent);

    // Pass through color and UV
    fs_Color_VS = in_Color;
    fs_UV_VS = in_UV;
}