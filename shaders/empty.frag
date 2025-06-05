#version 450
layout(location = 0) out vec4 fsout_Color;
void main() {
    fsout_Color = vec4(0.0, 1.0, 0.0, 1.0); // Green, to distinguish from clear
}