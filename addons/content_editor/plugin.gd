@tool
extends EditorPlugin

## Оболочка плагина на GDScript.
##
## EditorPlugin нельзя держать на C#: при горячей перезагрузке сборки Godot выгружает
## AssemblyLoadContext, а C#-узел плагина ещё стоит в дереве редактора.
## C#-мост нельзя создавать в _enter_tree: среда C# в этот момент ещё не готова.
## Создание откладывается; при неудаче повторяется со следующего кадра.

const BridgePath := "res://addons/content_editor/ContentEditorPlugin.cs"
const BridgeName := "RedPlanetContentEditorBridge"
const MainName := "RedPlanetContentEditor"
const WindowName := "RedPlanetContentEditorWindow"

var _bridge: Node
var _want_visible := false
var _attach_queued := false
var _retry_frames := 0


func _enter_tree() -> void:
	set_process(true)
	_queue_attach()


func _exit_tree() -> void:
	_attach_queued = false
	set_process(false)
	_detach_bridge()
	_free_named(_editor_screen(), MainName)
	_free_named(_base_control(), WindowName)


func _process(_delta: float) -> void:
	if is_instance_valid(_bridge):
		return
	_retry_frames -= 1
	if _retry_frames > 0:
		return
	_retry_frames = 20
	_queue_attach()


func _has_main_screen() -> bool:
	return true


func _get_plugin_name() -> String:
	return "Content"


## Иконка вкладки главного экрана.
##
## Godot запрашивает её при регистрации плагина, то есть до того, как C#-мост создан
## отложенным вызовом. Обращаться за иконкой к мосту поэтому нельзя, и она берётся
## из темы редактора здесь. Состав EditorIcons меняется между версиями движка,
## поэтому отсутствующее имя приводит к возврату null, а не к ошибке.
func _get_plugin_icon() -> Texture2D:
	var control := _base_control() as Control
	if control == null or not control.has_theme_icon("ResourcePreloader", "EditorIcons"):
		return null
	return control.get_theme_icon("ResourcePreloader", "EditorIcons")


func _make_visible(visible: bool) -> void:
	_want_visible = visible
	if not is_instance_valid(_bridge):
		_queue_attach()
		return
	_apply_visible()


func _queue_attach() -> void:
	if _attach_queued or is_instance_valid(_bridge):
		return
	_attach_queued = true
	call_deferred("_attach_bridge")


func _attach_bridge() -> void:
	_attach_queued = false
	if not is_inside_tree() or is_instance_valid(_bridge):
		return

	if not ResourceLoader.exists(BridgePath):
		push_error("[Content editor] C# script not found: %s" % BridgePath)
		return

	var script := load(BridgePath)
	if script == null:
		push_error("[Content editor] failed to load C# script")
		return

	var created = script.new()
	if created == null:
		push_error("[Content editor] CSharpScript.new() returned null")
		return
	if not (created is Node):
		push_error("[Content editor] C# instance is not a Node: %s" % created)
		return

	created.name = BridgeName
	add_child(created)
	if not is_instance_valid(created):
		push_error("[Content editor] C# bridge was freed during AddChild")
		return

	_bridge = created
	_apply_visible()


func _apply_visible() -> void:
	if not is_instance_valid(_bridge):
		return
	_bridge.call("MakeVisible", _want_visible)


func _detach_bridge() -> void:
	if is_instance_valid(_bridge):
		remove_child(_bridge)
		_bridge.free()
	_bridge = null


func _editor_screen() -> Node:
	var editor := get_editor_interface()
	return editor.get_editor_main_screen() if editor else null


func _base_control() -> Node:
	var editor := get_editor_interface()
	return editor.get_base_control() if editor else null


func _free_named(parent: Node, node_name: String) -> void:
	if parent == null:
		return
	var node := parent.get_node_or_null(node_name)
	if node == null:
		return
	parent.remove_child(node)
	node.free()
