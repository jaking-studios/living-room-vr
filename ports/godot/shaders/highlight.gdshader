shader_type spatial;
render_mode unshaded, blend_add, depth_draw_never, cull_back;
uniform vec4 tint : source_color = vec4(1.0, 0.75, 0.3, 1.0);
uniform float strength = 0.18;
void fragment() {
	float pulse = 0.75 + 0.25 * sin(TIME * 5.0);
	ALBEDO = tint.rgb * strength * pulse;
}
