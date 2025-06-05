#version 450
layout(set = 0, binding = 0) uniform MVPBuffer { // Keep if your empty pipeline has this layout
    mat4 MVP; // Unused, but matches layout
};

// No vertex inputs for this minimal test

void main() {
    // Output a single point/degenerate triangle.
    // For a single point, some drivers/APIs might cull it if no point size is set.
    // A degenerate triangle is safer for just testing pipeline creation.
    gl_Position = vec4(0.0, 0.0, 0.0, 1.0); // Minimal valid output
}