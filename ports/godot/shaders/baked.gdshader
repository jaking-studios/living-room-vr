// Living Room VR: Cycles-baked lightmap on UV2 (lightmap jpg stores (light/scale)^(1/2.2))
shader_type spatial;
render_mode unshaded, cull_back;

uniform sampler2D albedo_tex : source_color, hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
uniform vec4 albedo_color : source_color = vec4(1.0);
uniform sampler2D lightmap : source_color, hint_default_white, filter_linear_mipmap;
uniform float lm_scale = 1.0;
uniform sampler2D emission_tex : source_color, hint_default_white, filter_linear_mipmap, repeat_enable;
uniform vec4 emission_color : source_color = vec4(0.0, 0.0, 0.0, 1.0);
uniform float emission_energy = 1.0;

void fragment() {
	vec4 a = texture(albedo_tex, UV) * albedo_color;
	vec3 lm = texture(lightmap, UV2).rgb * lm_scale;
	ALBEDO = a.rgb * lm + texture(emission_tex, UV).rgb * emission_color.rgb * emission_energy;
	
}
