#version 450
layout(set = 0, binding = 0) uniform MVPBuffer {
    mat4 MVP;
};
layout(location = 0) in vec3 Position;
layout(location = 1) in vec4 Color;
//layout(location = 2) in vec4 Color2;
layout(location = 2) in vec2 UV;
layout(location = 3) in vec3 Normal;
//layout(location = 5) in vec3 BiTangent;
//layout(location = 6) in vec3 Unknown1;

layout(location = 0) out vec3 fs_Position;
layout(location = 1) out vec4 fs_Color;
//layout(location = 2) out vec4 fs_Color2;
layout(location = 2) out vec2 fs_UV;
layout(location = 3) out vec3 fs_Normal;
//layout(location = 5) out vec3 fs_BiTangent;
//layout(location = 6) out vec3 fs_Unknown1;

void main() {
    gl_Position = MVP * vec4(Position, 1.0);
    fs_Position = Position;
    fs_Color = Color;
    //fs_Color2 = Color2;
    fs_UV = UV;
    fs_Normal = normalize(Normal);
    //fs_BiTangent = BiTangent;
    //fs_Unknown1 = Unknown1;
}