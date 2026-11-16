#version 330 core
// VelvX Engine — shader de vértice da pass 2D (ortho em pixels).
layout (location = 0) in vec2 aPos;
layout (location = 1) in vec2 aUV;

uniform mat4 projection;
uniform mat4 model;

out vec2 vUV;

void main() {
    vUV = aUV;
    gl_Position = projection * model * vec4(aPos, 0.0, 1.0);
}