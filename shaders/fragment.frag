#version 450

layout(set = 1, binding = 0) uniform texture2D tex_Diffuse;
layout(set = 1, binding = 1) uniform texture2D tex_Normal;
layout(set = 1, binding = 2) uniform texture2D tex_Mask;
layout(set = 1, binding = 3) uniform texture2D tex_Index;
layout(set = 1, binding = 4) uniform sampler SharedSampler;
layout(set = 1, binding = 5) uniform ColorSetBuffer {
	vec4 colorSet[256];
};

layout(location = 0) in vec3 fs_Position;
layout(location = 1) in vec4 fs_Color;
layout(location = 2) in vec4 fs_Color2;
layout(location = 3) in vec2 fs_UV;
layout(location = 4) in vec3 fs_Normal;
layout(location = 5) in vec3 fs_BiTangent;
layout(location = 6) in vec3 fs_Unknown1;

layout(location = 0) out vec4 fsout_Color;

vec4 getColorSetColor(float redIndex, float greenIndex) {
	int row = int(redIndex * 31.0 + 0.5);
	int col = int(greenIndex * 7.0 + 0.5);
	int linearIndex = clamp(row * 8 + col, 0, 255);
	return colorSet[linearIndex];
}

void main() {
	vec4 baseColor = texture(sampler2D(tex_Diffuse, SharedSampler), fs_UV);
	vec4 maskSample = texture(sampler2D(tex_Mask, SharedSampler), fs_UV);
	vec3 sampledNormal = texture(sampler2D(tex_Normal, SharedSampler), fs_UV).rgb;
	vec4 indexSample = texture(sampler2D(tex_Index, SharedSampler), fs_UV);

	// Alpha Cutout
	float alphaCutoff = 0.5; // ideally passed as a uniform
	if (sampledNormal.b < alphaCutoff)
		discard;


	// Decode normal
	vec3 normal = normalize(sampledNormal * 2.0 - 1.0);
	vec3 worldNormal = normalize(normal);

	// --- Lighting ---
	vec3 lightDir = normalize(vec3(-0.4, -1.0, -0.3));
	float diffuse = max(dot(worldNormal, -lightDir), 0.0);

	vec3 fillDir = normalize(vec3(0.3, 1.0, 0.2));
	float fill = max(dot(worldNormal, -fillDir), 0.0) * 0.3;

	float rim = pow(1 - max(dot(worldNormal, normalize(fs_Position)), 0.0), 8.0) / 8.0;

	// ColorSet tinting (if you want it visually)
	vec4 colorSetColor = getColorSetColor(indexSample.r, indexSample.g);

	vec3 litColor = baseColor.rgb * (diffuse + fill + 0.65) + colorSetColor.rgb * 0.005 + rim * maskSample.b;

	fsout_Color =  vec4(litColor, baseColor.a);
}
