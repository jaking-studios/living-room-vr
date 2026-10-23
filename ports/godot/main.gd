extends Node3D
## Living Room VR - Godot 4 version.
## Streams the 120 Blender-baked living rooms (plain .glb + Cycles lightmap) at runtime
## and gives you a first-person body plus a list of chores in each room.

const BAKED := preload("res://shaders/baked.gdshader")
const BAKED_T := preload("res://shaders/baked_transparent.gdshader")
const HILITE := preload("res://shaders/highlight.gdshader")

const LABELS := {
	"pillow": "Put the pillows back on the sofa",
	"book": "Put the books back",
	"frame": "Straighten the picture frames",
	"tv": "Turn off the TV",
	"lamp": "Turn off the lamps",
	"plant": "Water the plants",
	"trash": "Pick up the trash",
}
const PROMPTS := {
	"pillow": "Put pillow back", "book": "Put book back", "frame": "Straighten frame",
	"tv": "Turn TV off", "lamp": "Turn lamp off", "plant": "Water plant", "trash": "Pick up trash",
}
const SAVE_PATH := "user://progress.cfg"

var walk_speed := 2.2
var mouse_sens := 0.0025
var reach := 3.2

var rooms: Array = []
var index := 0
var room: Dictionary = {}
var room_root: Node3D
var tasks: Array = []          # [{type, label, items:[Dictionary]}]
var items: Array = []          # interactable dictionaries
var coins := 0
var done_timer := -1.0
var loading := true
var sx := 1.0
var sz := -1.0
var save := ConfigFile.new()

var body: CharacterBody3D
var cam: Camera3D
var yaw := 0.0
var pitch := 0.0
var target = null              # interactable dictionary or null
var hilite_mat: ShaderMaterial

# touch
var move_finger := -1
var look_finger := -1
var move_start := Vector2.ZERO
var move_vec := Vector2.ZERO
var look_start := Vector2.ZERO
var look_start_t := 0.0
var tap_pending := false
var tap_pos := Vector2.ZERO

# ui
var ui: CanvasLayer
var task_label: RichTextLabel
var prompt_label: Label
var cross: Label
var toast_label: Label
var toast_t := 0.0
var menu: PanelContainer
var loading_panel: ColorRect
var loading_label: Label


func _ready() -> void:
	save.load(SAVE_PATH)
	coins = int(save.get_value("game", "coins", 0))
	_setup_input()
	_setup_world()
	_setup_ui()
	var f := FileAccess.open("res://rooms/rooms_index.data", FileAccess.READ)
	if f == null:
		_set_loading("Missing res://rooms/rooms_index.data")
		return
	rooms = JSON.parse_string(f.get_as_text())
	load_room(int(save.get_value("game", "room", 0)))


# ------------------------------------------------------------------ setup
func _add_key(action: String, keys: Array, joy_buttons: Array = []) -> void:
	if not InputMap.has_action(action):
		InputMap.add_action(action)
	for k in keys:
		var e := InputEventKey.new()
		e.physical_keycode = k
		InputMap.action_add_event(action, e)
	for b in joy_buttons:
		var j := InputEventJoypadButton.new()
		j.button_index = b
		InputMap.action_add_event(action, j)


func _setup_input() -> void:
	_add_key("fwd", [KEY_W, KEY_UP])
	_add_key("back", [KEY_S, KEY_DOWN])
	_add_key("left", [KEY_A, KEY_LEFT])
	_add_key("right", [KEY_D, KEY_RIGHT])
	_add_key("run", [KEY_SHIFT])
	_add_key("use", [KEY_E, KEY_SPACE, KEY_ENTER], [JOY_BUTTON_A])
	_add_key("next_room", [KEY_N, KEY_PAGEDOWN], [JOY_BUTTON_RIGHT_SHOULDER])
	_add_key("prev_room", [KEY_B, KEY_PAGEUP], [JOY_BUTTON_LEFT_SHOULDER])
	_add_key("menu", [KEY_M, KEY_TAB], [JOY_BUTTON_START])


func _setup_world() -> void:
	var env := Environment.new()
	env.background_mode = Environment.BG_COLOR
	env.background_color = Color(0.05, 0.045, 0.04)
	env.ambient_light_source = Environment.AMBIENT_SOURCE_COLOR
	env.ambient_light_color = Color(0.75, 0.7, 0.62)
	env.ambient_light_energy = 1.4
	env.tonemap_mode = Environment.TONE_MAPPER_ACES
	env.tonemap_exposure = 1.0
	var we := WorldEnvironment.new()
	we.environment = env
	add_child(we)

	var sun := DirectionalLight3D.new()
	sun.light_energy = 0.35
	sun.rotation_degrees = Vector3(-55, 30, 0)
	add_child(sun)

	body = CharacterBody3D.new()
	body.name = "Player"
	var shape := CollisionShape3D.new()
	var cap := CapsuleShape3D.new()
	cap.radius = 0.22
	cap.height = 1.7
	shape.shape = cap
	shape.position.y = 0.85
	body.add_child(shape)
	body.floor_snap_length = 0.3
	cam = Camera3D.new()
	cam.position.y = 1.62
	cam.fov = 70
	cam.near = 0.05
	cam.far = 60
	body.add_child(cam)
	add_child(body)

	hilite_mat = ShaderMaterial.new()
	hilite_mat.shader = HILITE


func _setup_ui() -> void:
	ui = CanvasLayer.new()
	add_child(ui)
	var panel := PanelContainer.new()
	panel.position = Vector2(12, 12)
	panel.custom_minimum_size = Vector2(360, 0)
	panel.add_theme_stylebox_override("panel", _box())
	task_label = RichTextLabel.new()
	task_label.bbcode_enabled = true
	task_label.fit_content = true
	task_label.scroll_active = false
	task_label.custom_minimum_size = Vector2(340, 40)
	task_label.mouse_filter = Control.MOUSE_FILTER_IGNORE
	panel.add_child(task_label)
	panel.mouse_filter = Control.MOUSE_FILTER_IGNORE
	ui.add_child(panel)

	cross = _center_label("·", 28)
	prompt_label = _center_label("", 22)
	prompt_label.position.y += 34
	toast_label = _center_label("", 26)
	toast_label.anchor_top = 0.18
	toast_label.anchor_bottom = 0.18

	var bar := HBoxContainer.new()
	bar.set_anchors_preset(Control.PRESET_TOP_RIGHT)
	bar.position = Vector2(-250, 12)
	bar.add_theme_constant_override("separation", 8)
	var b1 := Button.new(); b1.text = "Rooms"; b1.custom_minimum_size = Vector2(110, 40); b1.pressed.connect(_toggle_menu)
	var b2 := Button.new(); b2.text = "Next ▶"; b2.custom_minimum_size = Vector2(110, 40); b2.pressed.connect(func(): load_room(index + 1))
	b1.focus_mode = Control.FOCUS_NONE
	b2.focus_mode = Control.FOCUS_NONE
	bar.add_child(b1); bar.add_child(b2)
	ui.add_child(bar)

	var help := Label.new()
	help.text = "WASD/stick move · mouse/right-stick look · E/click/A do chore · N/B next/prev room · M rooms"
	help.set_anchors_preset(Control.PRESET_BOTTOM_LEFT)
	help.position = Vector2(12, -30)
	help.add_theme_color_override("font_color", Color(1, 1, 1, 0.7))
	ui.add_child(help)

	loading_panel = ColorRect.new()
	loading_panel.color = Color(0.05, 0.04, 0.035, 0.92)
	loading_panel.set_anchors_preset(Control.PRESET_FULL_RECT)
	loading_label = Label.new()
	loading_label.set_anchors_preset(Control.PRESET_FULL_RECT)
	loading_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	loading_label.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
	loading_label.add_theme_font_size_override("font_size", 28)
	loading_panel.add_child(loading_label)
	ui.add_child(loading_panel)


func _box() -> StyleBoxFlat:
	var s := StyleBoxFlat.new()
	s.bg_color = Color(0.05, 0.04, 0.035, 0.72)
	s.set_corner_radius_all(8)
	s.set_content_margin_all(10)
	return s


func _center_label(t: String, size: int) -> Label:
	var l := Label.new()
	l.text = t
	l.set_anchors_preset(Control.PRESET_CENTER)
	l.anchor_left = 0.0
	l.anchor_right = 1.0
	l.offset_left = 0
	l.offset_right = 0
	l.offset_top = -18
	l.offset_bottom = 18
	l.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	l.add_theme_font_size_override("font_size", size)
	l.add_theme_color_override("font_outline_color", Color.BLACK)
	l.add_theme_constant_override("outline_size", 6)
	l.mouse_filter = Control.MOUSE_FILTER_IGNORE
	ui.add_child(l)
	return l


func _set_loading(t: String) -> void:
	loading_panel.visible = t != ""
	loading_label.text = t


func _toast(t: String) -> void:
	toast_label.text = t
	toast_t = 2.5


# ------------------------------------------------------------------ rooms
func load_room(i: int) -> void:
	if rooms.is_empty():
		return
	index = posmod(i, rooms.size())
	room = rooms[index]
	loading = true
	done_timer = -1.0
	if menu:
		menu.queue_free()
		menu = null
	_set_loading("Loading %s…" % room.name)
	save.set_value("game", "room", index)
	save.save(SAVE_PATH)
	await get_tree().process_frame
	await get_tree().process_frame

	if room_root:
		room_root.queue_free()
		room_root = null
	tasks.clear()
	items.clear()
	target = null

	var bytes := FileAccess.get_file_as_bytes("res://rooms/%s.glbdata" % room.id)
	if bytes.is_empty():
		_set_loading("Missing res://rooms/%s.glbdata" % room.id)
		return
	var doc := GLTFDocument.new()
	var state := GLTFState.new()
	var err := doc.append_from_buffer(bytes, "", state)
	if err != OK:
		_set_loading("Could not read %s (error %d)" % [room.id, err])
		return
	room_root = Node3D.new()
	room_root.name = room.id
	add_child(room_root)
	var scene := doc.generate_scene(state)
	room_root.add_child(scene)

	var lm: Texture2D = null
	var lmb := FileAccess.get_file_as_bytes("res://rooms/%s_lightmap.lmjpg" % room.id)
	if not lmb.is_empty():
		var img := Image.new()
		if img.load_jpg_from_buffer(lmb) == OK:
			img.generate_mipmaps()
			lm = ImageTexture.create_from_image(img)

	_prepare_room(scene, _read_extras(bytes), lm)
	_setup_tasks()
	loading = false
	_set_loading("")
	_toast(room.name)
	_refresh_tasks()


func _read_extras(glb: PackedByteArray) -> Dictionary:
	var res := {}
	var n := glb.decode_u32(12)
	var j = JSON.parse_string(glb.slice(20, 20 + n).get_string_from_utf8())
	if j is Dictionary and j.has("nodes"):
		for node in j.nodes:
			if node.has("name") and node.has("extras"):
				res[node.name] = node.extras
	return res


func _b2g(x: float, y: float, z: float) -> Vector3:
	return Vector3(sx * x, z, sz * y)


func _all_nodes(n: Node, out: Array) -> Array:
	out.append(n)
	for c in n.get_children():
		_all_nodes(c, out)
	return out


func _meshes(n: Node) -> Array:
	return _all_nodes(n, []).filter(func(x): return x is MeshInstance3D)


func _prepare_room(scene: Node, extras: Dictionary, lm: Texture2D) -> void:
	var all := _all_nodes(scene, [])
	var by_name := {}
	for n in all:
		by_name[String(n.name)] = n

	# glTF -> Godot axis signs, checked against the door marker
	var door_pos := Vector3(0, 0, room.D / 2.0)
	if by_name.has("DOOR"):
		door_pos = room_root.to_local(by_name["DOOR"].global_position)
	var dw: String = room.door.wall if room.has("door") else "S"
	var t: float = float(room.door.t) if room.has("door") else 0.0
	if dw == "S" or dw == "N":
		var by: float = -room.D / 2.0 if dw == "S" else room.D / 2.0
		if absf(door_pos.z) > 0.3: sz = signf(door_pos.z / by)
		if absf(t) > 0.15 and absf(door_pos.x) > 0.1: sx = signf(door_pos.x / t)
	else:
		var bx: float = room.W / 2.0 if dw == "E" else -room.W / 2.0
		if absf(door_pos.x) > 0.3: sx = signf(door_pos.x / bx)
		if absf(t) > 0.15 and absf(door_pos.z) > 0.1: sz = signf(door_pos.z / t)

	# baked shell -> lightmap shader
	var lm_scale := float(room.get("lm_scale", 1.0)) * 0.62
	var cache := {}
	if by_name.has("STATIC"):
		for mi in _meshes(by_name["STATIC"]):
			for s in mi.mesh.get_surface_count():
				var src: Material = mi.get_active_material(s)
				if src == null:
					continue
				if not cache.has(src):
					cache[src] = _baked_material(src, lm, lm_scale)
				mi.set_surface_override_material(s, cache[src])

	# outside backdrops: unshaded picture trimmed to the wall
	for n in all:
		if String(n.name).begins_with("OUTSIDE") and n is Node3D:
			var parts := String(n.name).split("_")
			var w := parts[1] if parts.size() > 1 else "N"
			var L: float = room.W if (w == "N" or w == "S") else room.D
			n.scale.x *= (L + 0.3) / (L + 6.0)
			for mi in _meshes(n):
				for s in mi.mesh.get_surface_count():
					var src = mi.get_active_material(s)
					if src is BaseMaterial3D:
						var m: BaseMaterial3D = src.duplicate()
						m.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
						if m.albedo_texture == null and m.emission_texture != null:
							m.albedo_texture = m.emission_texture
						m.albedo_color = Color(1.05, 1.05, 1.05)
						mi.set_surface_override_material(s, m)

	# floor + furniture colliders from the Blender footprints
	var floor := StaticBody3D.new()
	var fs := CollisionShape3D.new()
	var fb := BoxShape3D.new()
	fb.size = Vector3(room.W + 4, 0.1, room.D + 4)
	fs.shape = fb
	fs.position.y = -0.05
	floor.add_child(fs)
	room_root.add_child(floor)
	for key in extras:
		var ex: Dictionary = extras[key]
		if float(ex.get("collide", 0)) < 0.5:
			continue
		var kind = ex.get("kind", "")
		if kind == "rug" or kind == "wall" or not ex.has("bb"):
			continue
		var bb: Array = ex.bb
		var a := _b2g(bb[0], bb[1], bb[2])
		var b := _b2g(bb[3], bb[4], bb[5])
		var sb := StaticBody3D.new()
		var cs := CollisionShape3D.new()
		var box := BoxShape3D.new()
		box.size = Vector3(maxf(absf(a.x - b.x) - 0.08, 0.05), maxf(absf(a.y - b.y), 0.05), maxf(absf(a.z - b.z) - 0.08, 0.05))
		cs.shape = box
		sb.position = (a + b) / 2.0
		sb.add_child(cs)
		room_root.add_child(sb)

	# interactables
	for n in all:
		var nm := String(n.name)
		if not nm.begins_with("INT_") or not (n is Node3D):
			continue
		var ex: Dictionary = extras.get(nm, {})
		var type: String = ex.get("task", nm.split("_")[1])
		if not LABELS.has(type):
			continue
		var it := {"node": n, "type": type, "bad": false, "home_parent": n.get_parent(), "home": n.transform}
		_own_materials(n)
		_add_pick_area(it)
		items.append(it)
	for it in items:
		if it.type == "lamp": _set_lamp(it, false)
		if it.type == "tv": _set_tv(it, false)

	# spawn inside the door, facing the room
	var inward := Vector3(-door_pos.x, 0, -door_pos.z).normalized()
	if inward == Vector3.ZERO:
		inward = Vector3.FORWARD

	# closed door leaf in the doorway (single-room mode has no hallway behind it)
	var dwid: float = float(room.door.w) if room.has("door") else 1.0
	var dh: float = float(room.door.h) if room.has("door") else 2.1
	var leaf := MeshInstance3D.new()
	var bm := BoxMesh.new()
	bm.size = Vector3(dwid, dh, 0.045) if (dw == "S" or dw == "N") else Vector3(0.045, dh, dwid)
	leaf.mesh = bm
	var lmat := StandardMaterial3D.new()
	lmat.albedo_color = Color(0.36, 0.24, 0.15)
	lmat.roughness = 0.6
	leaf.material_override = lmat
	room_root.add_child(leaf)
	leaf.position = Vector3(door_pos.x, dh / 2.0, door_pos.z) - inward * 0.05
	body.global_position = _clamp_room(Vector3(door_pos.x, 0.05, door_pos.z) + inward * 0.9)
	body.velocity = Vector3.ZERO
	yaw = atan2(-inward.x, -inward.z)
	pitch = 0.0


func _baked_material(src: Material, lm: Texture2D, lm_scale: float) -> ShaderMaterial:
	var m := ShaderMaterial.new()
	var mname := String(src.resource_name).to_lower()
	var glass := mname.contains("glass") or mname.contains("crys") or mname.contains("bot") or mname.contains("sheer")
	var col := Color.WHITE
	var emissive := false
	if src is BaseMaterial3D:
		var s: BaseMaterial3D = src
		col = s.albedo_color
		if s.transparency != BaseMaterial3D.TRANSPARENCY_DISABLED or glass:
			m.shader = BAKED_T
			if glass and col.a > 0.5:
				col.a = 0.55 if mname.contains("sheer") else 0.22
		else:
			m.shader = BAKED
		if s.albedo_texture:
			m.set_shader_parameter("albedo_tex", s.albedo_texture)
		if s.emission_enabled:
			emissive = s.emission.get_luminance() > 0.01
			m.set_shader_parameter("emission_color", s.emission)
			m.set_shader_parameter("emission_energy", s.emission_energy_multiplier)
			if s.emission_texture:
				m.set_shader_parameter("emission_tex", s.emission_texture)
	else:
		m.shader = BAKED
	m.set_shader_parameter("albedo_color", col)
	m.set_shader_parameter("lightmap", lm)
	m.set_shader_parameter("lm_scale", 0.0 if emissive else lm_scale)
	return m


func _own_materials(n: Node) -> void:
	for mi in _meshes(n):
		for s in mi.mesh.get_surface_count():
			var src = mi.get_active_material(s)
			if src:
				mi.set_surface_override_material(s, src.duplicate())


func _world_aabb(n: Node) -> AABB:
	var first := true
	var box := AABB()
	for mi in _meshes(n):
		var a: AABB = mi.global_transform * mi.get_aabb()
		box = a if first else box.merge(a)
		first = false
	return box


func _add_pick_area(it: Dictionary) -> void:
	var n: Node3D = it.node
	var wb := _world_aabb(n)
	if wb.size == Vector3.ZERO:
		return
	var area := Area3D.new()
	area.collision_layer = 2
	area.collision_mask = 0
	area.monitoring = false
	var cs := CollisionShape3D.new()
	var box := BoxShape3D.new()
	box.size = Vector3(maxf(wb.size.x, 0.06), maxf(wb.size.y, 0.06), maxf(wb.size.z, 0.06))
	cs.shape = box
	area.add_child(cs)
	area.set_meta("item", it)
	n.add_child(area)
	area.global_transform = Transform3D(Basis(), wb.get_center())
	it.area = area


func _clamp_room(p: Vector3) -> Vector3:
	var hx: float = room.W / 2.0 - 0.3
	var hz: float = room.D / 2.0 - 0.3
	return Vector3(clampf(p.x, -hx, hx), p.y, clampf(p.z, -hz, hz))


# ------------------------------------------------------------------ chores
func _setup_tasks() -> void:
	var by_type := {}
	for it in items:
		if not by_type.has(it.type):
			by_type[it.type] = []
		by_type[it.type].append(it)
	var options := []
	for k in ["book", "frame", "tv", "lamp", "plant"]:
		if by_type.has(k):
			options.append(k)
	options.shuffle()
	var pick := ["trash"]
	if by_type.has("pillow"):
		pick.append("pillow")
	var want: int = 6 if room.W * room.D > 34 else 5
	for o in options:
		if pick.size() >= want:
			break
		pick.append(o)

	for type in pick:
		var task := {"type": type, "label": LABELS[type], "items": []}
		match type:
			"pillow", "book":
				var l: Array = by_type[type].duplicate()
				l.shuffle()
				for it in l.slice(0, 3):
					_scatter(it)
					task.items.append(it)
			"frame":
				for it in by_type[type].slice(0, 3):
					_tilt(it)
					task.items.append(it)
			"lamp":
				for it in by_type[type]:
					_set_lamp(it, true)
					task.items.append(it)
			"tv":
				for it in by_type[type]:
					_set_tv(it, true)
					task.items.append(it)
			"plant":
				var l: Array = by_type[type].duplicate()
				l.shuffle()
				for it in l.slice(0, 3):
					task.items.append(it)
			"trash":
				for i in randi_range(4, 6):
					task.items.append(_spawn_trash(i))
		for it in task.items:
			it.bad = true
		if not task.items.is_empty():
			tasks.append(task)


func _random_floor_point(near: Vector3, max_d := 2.2) -> Vector3:
	var space := get_world_3d().direct_space_state
	var q := PhysicsShapeQueryParameters3D.new()
	var sph := SphereShape3D.new()
	sph.radius = 0.28
	q.shape = sph
	q.collision_mask = 1
	for i in 80:
		var p: Vector3
		if i < 50:
			var a := randf() * TAU
			var d := randf_range(0.5, max_d)
			p = Vector3(near.x + cos(a) * d, 0, near.z + sin(a) * d)
		else:
			p = Vector3(randf_range(-room.W / 2.0 + 0.5, room.W / 2.0 - 0.5), 0, randf_range(-room.D / 2.0 + 0.5, room.D / 2.0 - 0.5))
		if absf(p.x) > room.W / 2.0 - 0.45 or absf(p.z) > room.D / 2.0 - 0.45:
			continue
		q.transform = Transform3D(Basis(), p + Vector3.UP * 0.33)
		if space.intersect_shape(q, 1).is_empty():
			return p
	return Vector3.ZERO


func _scatter(it: Dictionary) -> void:
	var n: Node3D = it.node
	var p: Vector3 = _random_floor_point(n.global_position)
	n.reparent(room_root, true)
	n.global_position = p
	var roll := PI / 2 if it.type == "book" else deg_to_rad(randf_range(-100, -80))
	n.global_basis = Basis.from_euler(Vector3(deg_to_rad(randf_range(-15, 15)), randf() * TAU, roll)).scaled(n.global_basis.get_scale())
	_settle(n)


func _settle(n: Node3D) -> void:
	var wb := _world_aabb(n)
	n.global_position.y += 0.004 - wb.position.y


func _tilt(it: Dictionary) -> void:
	var n: Node3D = it.node
	var wb := _world_aabb(n)
	var axis := Vector3.RIGHT if wb.size.x < wb.size.z else Vector3.BACK
	var ang := deg_to_rad(randf_range(10, 18)) * (1 if randf() < 0.5 else -1)
	var c := wb.get_center()
	var tr := n.global_transform
	tr.origin -= c
	tr = Transform3D(Basis(axis, ang), Vector3.ZERO) * tr
	tr.origin += c
	n.global_transform = tr


func _set_emission(it: Dictionary, keys: Array, col: Color, energy: float) -> void:
	for mi in _meshes(it.node):
		for s in mi.mesh.get_surface_count():
			var m = mi.get_surface_override_material(s)
			if m is BaseMaterial3D:
				var nm := String(m.resource_name).to_lower()
				for k in keys:
					if nm.contains(k):
						m.emission_enabled = energy > 0
						m.emission = col
						m.emission_energy_multiplier = energy


func _set_lamp(it: Dictionary, on: bool) -> void:
	_set_emission(it, ["bulb"], Color(1, 0.72, 0.42), 4.0 if on else 0.0)
	_set_emission(it, ["shade"], Color(1, 0.72, 0.42), 1.5 if on else 0.0)


func _set_tv(it: Dictionary, on: bool) -> void:
	_set_emission(it, ["screen"], Color(0.35, 0.55, 0.95), 1.6 if on else 0.0)


func _spawn_trash(i: int) -> Dictionary:
	var can := i % 3 == 2
	var mi := MeshInstance3D.new()
	var mat := StandardMaterial3D.new()
	if can:
		var cyl := CylinderMesh.new()
		cyl.top_radius = 0.033
		cyl.bottom_radius = 0.033
		cyl.height = 0.12
		mi.mesh = cyl
		mat.albedo_color = Color(0.75, 0.08, 0.06)
		mat.metallic = 0.6
		mat.roughness = 0.35
	else:
		var sp := SphereMesh.new()
		var r := randf_range(0.035, 0.05)
		sp.radius = r
		sp.height = r * 2
		mi.mesh = sp
		mat.albedo_color = Color(0.88, 0.86, 0.8)
		mat.roughness = 0.9
	mi.material_override = mat
	var holder := Node3D.new()
	holder.name = "TRASH_%d" % i
	holder.add_child(mi)
	room_root.add_child(holder)
	holder.global_position = _random_floor_point(body.global_position, 3.0) + Vector3.UP * (0.033 if can else 0.04)
	if can:
		holder.rotation = Vector3(0, randf() * TAU, PI / 2)
	var it := {"node": holder, "type": "trash", "bad": false, "home_parent": room_root, "home": holder.transform}
	_add_pick_area(it)
	return it


func _fix(it: Dictionary) -> void:
	if not it.bad:
		return
	it.bad = false
	var n: Node3D = it.node
	_set_highlight(it, false)
	match it.type:
		"pillow", "book", "frame":
			var parent: Node = it.home_parent
			n.reparent(parent, true)
			var tw := create_tween().set_trans(Tween.TRANS_SINE).set_ease(Tween.EASE_IN_OUT)
			tw.tween_property(n, "transform", it.home, 0.45)
		"lamp":
			_set_lamp(it, false)
		"tv":
			_set_tv(it, false)
		"plant":
			var s0 := n.scale
			var tw := create_tween()
			tw.tween_property(n, "scale", s0 * 1.05, 0.12)
			tw.tween_property(n, "scale", s0, 0.3)
		"trash":
			var tw := create_tween()
			tw.tween_property(n, "scale", Vector3.ONE * 0.01, 0.25)
			tw.tween_callback(n.hide)
	coins += 10
	for task in tasks:
		if task.items.has(it) and _left(task) == 0:
			coins += 25
			_toast("✔ " + task.label)
	var all_done := tasks.all(func(tk): return _left(tk) == 0)
	if all_done and not tasks.is_empty():
		coins += 50
		_toast("Room clean! +50")
		save.set_value("done", room.id, true)
		done_timer = 4.0
	save.set_value("game", "coins", coins)
	save.save(SAVE_PATH)
	_refresh_tasks()


func _left(task: Dictionary) -> int:
	return task.items.filter(func(x): return x.bad).size()


func _refresh_tasks() -> void:
	var s := "[b]%s[/b]  [color=#c9a46a]#%d/%d[/color]\n[color=#e8b04a]● %d coins[/color]\n" % [room.get("name", ""), index + 1, rooms.size(), coins]
	for t in tasks:
		var left := _left(t)
		if left == 0:
			s += "[color=#7fcf8a]✔ %s[/color]\n" % t.label
		else:
			s += "☐ %s  [color=#c9a46a](%d/%d)[/color]\n" % [t.label, t.items.size() - left, t.items.size()]
	task_label.text = s


func _set_highlight(it, on: bool) -> void:
	if it == null or not is_instance_valid(it.node):
		return
	for mi in _meshes(it.node):
		mi.material_overlay = hilite_mat if on else null


# ------------------------------------------------------------------ input & movement
func _unhandled_input(event: InputEvent) -> void:
	if event.is_action_pressed("menu"):
		_toggle_menu()
		return
	if event is InputEventKey and event.pressed and event.keycode == KEY_ESCAPE:
		if menu:
			_toggle_menu()
		else:
			Input.mouse_mode = Input.MOUSE_MODE_VISIBLE
		return
	if loading or menu:
		return
	if event.is_action_pressed("next_room"):
		load_room(index + 1)
	elif event.is_action_pressed("prev_room"):
		load_room(index - 1)
	elif event.is_action_pressed("use"):
		if target != null:
			_fix(target)
	elif event is InputEventMouseMotion and Input.mouse_mode == Input.MOUSE_MODE_CAPTURED:
		yaw -= event.relative.x * mouse_sens
		pitch = clampf(pitch - event.relative.y * mouse_sens, -1.4, 1.4)
	elif event is InputEventMouseButton and event.pressed and event.button_index == MOUSE_BUTTON_LEFT:
		if target != null:
			_fix(target)
		elif Input.mouse_mode != Input.MOUSE_MODE_CAPTURED:
			Input.mouse_mode = Input.MOUSE_MODE_CAPTURED
	elif event is InputEventScreenTouch:
		var vp := get_viewport().get_visible_rect().size
		if event.pressed:
			if event.position.x < vp.x * 0.4 and move_finger < 0:
				move_finger = event.index
				move_start = event.position
				move_vec = Vector2.ZERO
			elif look_finger < 0:
				look_finger = event.index
				look_start = event.position
				look_start_t = Time.get_ticks_msec() / 1000.0
		else:
			if event.index == move_finger:
				move_finger = -1
				move_vec = Vector2.ZERO
			elif event.index == look_finger:
				if Time.get_ticks_msec() / 1000.0 - look_start_t < 0.3 and event.position.distance_to(look_start) < vp.y * 0.03:
					tap_pending = true
					tap_pos = event.position
				look_finger = -1
	elif event is InputEventScreenDrag:
		var vp := get_viewport().get_visible_rect().size
		if event.index == move_finger:
			move_vec = ((event.position - move_start) / (vp.y * 0.12)).limit_length(1.0)
		elif event.index == look_finger:
			yaw -= event.relative.x * (PI / vp.y)
			pitch = clampf(pitch - event.relative.y * (PI / vp.y), -1.4, 1.4)


func _physics_process(delta: float) -> void:
	if toast_t > 0:
		toast_t -= delta
		if toast_t <= 0:
			toast_label.text = ""
	if loading or menu:
		return
	if done_timer > 0:
		done_timer -= delta
		if done_timer <= 0:
			load_room(index + 1)
			return

	# right stick look
	var rs := Vector2(Input.get_joy_axis(0, JOY_AXIS_RIGHT_X), Input.get_joy_axis(0, JOY_AXIS_RIGHT_Y))
	if rs.length() > 0.15:
		yaw -= rs.x * 2.2 * delta
		pitch = clampf(pitch - rs.y * 2.2 * delta, -1.4, 1.4)
	body.rotation = Vector3(0, yaw, 0)
	cam.rotation = Vector3(pitch, 0, 0)

	var mv := Input.get_vector("left", "right", "fwd", "back")
	var ls := Vector2(Input.get_joy_axis(0, JOY_AXIS_LEFT_X), Input.get_joy_axis(0, JOY_AXIS_LEFT_Y))
	if ls.length() > 0.15:
		mv += ls
	mv += move_vec
	mv = mv.limit_length(1.0)
	var speed := walk_speed * (1.6 if Input.is_action_pressed("run") else 1.0)
	var dir := body.global_basis * Vector3(mv.x, 0, mv.y)
	body.velocity.x = dir.x * speed
	body.velocity.z = dir.z * speed
	body.velocity.y = 0.0 if body.is_on_floor() else body.velocity.y - 9.8 * delta
	body.move_and_slide()
	var p := _clamp_room(body.global_position)
	if p.y < -2:
		p.y = 0.05
	body.global_position = p

	# aim: centre of screen when captured / touch / pad, otherwise the mouse pointer
	var vp := get_viewport().get_visible_rect().size
	var aim := vp / 2.0
	if Input.mouse_mode != Input.MOUSE_MODE_CAPTURED and DisplayServer.is_touchscreen_available() == false:
		aim = get_viewport().get_mouse_position()
	if tap_pending:
		aim = tap_pos
	var hit = _pick(aim)
	if hit != target:
		_set_highlight(target, false)
		target = hit
		_set_highlight(target, true)
	if tap_pending:
		tap_pending = false
		if target != null:
			_fix(target)
	cross.text = "◎" if target != null else "·"
	prompt_label.text = PROMPTS.get(target.type, "") if target != null else ""


func _pick(screen: Vector2):
	var from := cam.project_ray_origin(screen)
	var to := from + cam.project_ray_normal(screen) * reach
	var q := PhysicsRayQueryParameters3D.create(from, to, 2)
	q.collide_with_areas = true
	q.collide_with_bodies = false
	var space := get_world_3d().direct_space_state
	var exclude: Array[RID] = []
	for i in 6:
		q.exclude = exclude
		var r := space.intersect_ray(q)
		if r.is_empty():
			return null
		var it = r.collider.get_meta("item") if r.collider.has_meta("item") else null
		if it != null and it.bad:
			return it
		exclude.append(r.rid)
	return null


func _toggle_menu() -> void:
	if menu:
		menu.queue_free()
		menu = null
		return
	Input.mouse_mode = Input.MOUSE_MODE_VISIBLE
	menu = PanelContainer.new()
	menu.add_theme_stylebox_override("panel", _box())
	menu.set_anchors_preset(Control.PRESET_FULL_RECT)
	menu.anchor_left = 0.08
	menu.anchor_right = 0.92
	menu.anchor_top = 0.1
	menu.anchor_bottom = 0.9
	var sc := ScrollContainer.new()
	var grid := GridContainer.new()
	grid.columns = 4
	grid.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	for i in rooms.size():
		var b := Button.new()
		var done := bool(save.get_value("done", rooms[i].id, false))
		b.text = ("✔ " if done else "") + rooms[i].name
		b.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		b.custom_minimum_size = Vector2(0, 38)
		b.pressed.connect(load_room.bind(i))
		grid.add_child(b)
	sc.add_child(grid)
	menu.add_child(sc)
	ui.add_child(menu)
	(grid.get_child(index) as Button).grab_focus.call_deferred()
