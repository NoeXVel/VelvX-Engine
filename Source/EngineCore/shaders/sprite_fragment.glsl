#version 330 core
// VelvX Engine — fragmento 2D: cor/tint + textura opcional com alpha.
in vec2 vUV;

uniform vec4 color;
uniform int useTexture;
uniform sampler2D tex;

out vec4 FragColor;

void main() {
    vec4 c = color;
    if (useTexture == 1) {
        c *= texture(tex, vUV);
    }
    FragColor = c;
}