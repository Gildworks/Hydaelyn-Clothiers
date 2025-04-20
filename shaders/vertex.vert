#version 450
layout(set = 0, binding = 0) uniform MVPBuffer {
    mat4 MVP;
};
layout(location = 0) in vec3 Position;
layout(location = 1) in vec4 Color;
layout(location = 2) in vec2 UV;
layout(location = 3) in vec3 Normal;

layout(location = 0) out vec3 fs_Position;
layout(location = 1) out vec4 fs_Color;
layout(location = 2) out vec2 fs_UV;
layout(location = 3) out vec3 fs_Normal;

void main() {
    gl_Position = MVP * vec4(Position, 1.0);
    fs_Position = Position;
    fs_Color = vec4(0.0, 1.0, 0.0, 1.0);
    fs_UV = UV;
    fs_Normal = normalize(Normal);
}