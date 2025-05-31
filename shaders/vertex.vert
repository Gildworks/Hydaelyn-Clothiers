#version 450

// Uniforms
layout(set = 0, binding = 0) uniform TransformsBuffer {
    mat4 u_WorldViewProjection; // For gl_Position
    mat4 u_WorldView;         // For transforming positions, normals, tangents to View Space
};

// Input from Vertex Buffer
layout(location = 0) in vec3 in_Position_OS;  // Object Space (Model Space)
layout(location = 1) in vec4 in_Color;
layout(location = 2) in vec2 in_UV;
layout(location = 3) in vec3 in_Normal_OS;    // Object Space
layout(location = 4) in vec3 in_Tangent_OS;   // Object Space
layout(location = 5) in vec3 in_BiTangent_OS; // Object Space (optional if deriving in FS)

// Output to Fragment Shader
layout(location = 0) out vec3 fs_Position_VS; // View Space
layout(location = 1) out vec4 fs_Color_VS;
layout(location = 2) out vec2 fs_UV_VS;
layout(location = 3) out vec3 fs_Normal_VS;   // View Space
layout(location = 4) out vec3 fs_Tangent_VS;  // View Space
layout(location = 5) out vec3 fs_BiTangent_VS; // View Space (optional to pass, can derive in FS)

void main() {
    // Transform position to Clip Space for output to rasterizer
    gl_Position = u_WorldViewProjection * vec4(in_Position_OS, 1.0);

    // Transform position to View Space for lighting calculations in Fragment Shader
    vec4 position_VS = u_WorldView * vec4(in_Position_OS, 1.0);
    fs_Position_VS = position_VS.xyz;

    // Transform normal, tangent, and bitangent to View Space
    // For direction vectors, use the 3x3 part of the WorldView matrix.
    // If u_WorldView might have non-uniform scaling, a proper Normal Matrix
    // (transpose(inverse(mat3(u_WorldView)))) should be used.
    // For uniform scaling or just rotation/translation, mat3(u_WorldView) is okay for directions.
    mat3 viewModelMatrix3x3 = mat3(u_WorldView);
    fs_Normal_VS = normalize(viewModelMatrix3x3 * in_Normal_OS);
    fs_Tangent_VS = normalize(viewModelMatrix3x3 * in_Tangent_OS);
    // fs_BiTangent_VS = normalize(viewModelMatrix3x3 * in_BiTangent_OS); // If passing bitangent

    // Pass through color and UV
    fs_Color_VS = in_Color;
    fs_UV_VS = in_UV;
}