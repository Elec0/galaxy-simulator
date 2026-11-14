# Exercises the ordinary authored preview package. No fixture overlay or runtime IDs are supplied.
extends SceneTree

const PRIMARY_STATION_NAME = "Aster Relay"

var main
var panel
var output_dir = ""
var expected_station_names: Array[String] = []
var alternate_station = ""

func _initialize():
    for argument in OS.get_cmdline_user_args():
        if argument.begins_with("--output-dir="):
            output_dir = argument.trim_prefix("--output-dir=")
        elif argument.begins_with("--expected-station="):
            expected_station_names.append(argument.trim_prefix("--expected-station="))
    call_deferred("_run")

func check(condition, description):
    if not condition:
        push_error(description)
        quit(1)
    return condition

func settle():
    await process_frame
    await process_frame
    await process_frame

func control(control_name):
    return panel.find_child(control_name, true, false)

func lines():
    var values: Array[String] = []
    for child in control("Lines").get_children():
        if child is RichTextLabel:
            values.append(child.get_parsed_text())
    return "\n".join(values)

func choice_labels():
    var values: Array[String] = []
    for child in control("Choices").get_children():
        if child is Button:
            values.append(child.text)
    return values

# Activate real keyboard input after native focus-follow scrolling has settled.
func activate(button):
    if not check(button != null and button is BaseButton and not button.disabled,
            "The requested preview action must exist and be enabled"): return false
    if not check(button.is_visible_in_tree(), "The requested preview action must be exposed by its section"): return false
    button.grab_focus()
    await settle()
    if not check(root.gui_get_focus_owner() == button, "Keyboard activation must reach the requested preview action"): return false
    var event = InputEventKey.new()
    event.keycode = KEY_ENTER
    event.pressed = true
    root.push_input(event, true)
    event = event.duplicate()
    event.pressed = false
    root.push_input(event, true)
    await settle()
    return true

func show_chooser():
    var toggle = control("StartSectionToggle")
    if not toggle.button_pressed:
        if not await activate(toggle): return false
    return check(toggle.button_pressed, "The interim explicit conversation chooser must open")

func choose_definition(label):
    if not await show_chooser(): return false
    var selector = control("Definition")
    for index in range(selector.item_count):
        if selector.get_item_text(index) == label:
            selector.select(index)
            selector.emit_signal("item_selected", index)
            await settle()
            return true
    return check(false, "Authored preview conversation is absent: " + label)

# Bind through the public role label, independent of role-control ordering.
func role_picker(role):
    for picker in control("Participants").find_children("*", "OptionButton", true, false):
        if picker.accessibility_name == "Participant: " + role:
            return picker
    check(false, "The authored participant role is absent: " + role)
    return null

func choose_role(role, label):
    var picker = role_picker(role)
    if picker == null: return false
    for index in range(1, picker.item_count):
        if picker.get_item_text(index) == label:
            picker.select(index)
            picker.emit_signal("item_selected", index)
            await settle()
            return true
    return check(false, "The disclosed participant option is absent for " + role + ": " + label)

func start_station_conversation(definition, station):
    if not await choose_definition(definition): return false
    if not check(control("Start").disabled, "Station selection must remain explicit after changing the definition"): return false
    if not await choose_role("station", "Station: " + station): return false
    return await activate(control("Start"))

func press_choice(label):
    for button in control("Choices").get_children():
        if button is Button and button.text == label:
            if not check(not button.disabled, "Authored preview response is unavailable: " + label + " / " + button.tooltip_text): return false
            return await activate(button)
    return check(false, "Authored preview response is absent: " + label)

func pacing_button():
    return main.get_node("Interface/StatusPanel/Margin/Content/PacingControls/PauseOrResume")

func status_text():
    return main.get_node("Interface/StatusPanel/Margin/Content/Status").text

func screenshot(label):
    if output_dir.is_empty(): return true
    if not check(DirAccess.make_dir_recursive_absolute(output_dir) == OK, "Screenshot output directory must be writable"): return false
    await RenderingServer.frame_post_draw
    return check(root.get_texture().get_image().save_png(output_dir.path_join(label + ".png")) == OK,
        "The authored preview screenshot must be saved")

# All three player station names come from the ordinary production-loaded picker.
func verify_station_options():
    if not await choose_definition("Station greeting"): return false
    var picker = role_picker("station")
    if picker == null: return false
    var names: Array[String] = []
    for index in range(1, picker.item_count):
        var label = picker.get_item_text(index)
        if not check(label.begins_with("Station: ") and not label.contains("last observed"),
                "Owned authored preview stations must have current named options"): return false
        var station = label.trim_prefix("Station: ")
        if not check(not station.is_empty() and not names.has(station), "Authored station names must be present and distinct"): return false
        names.append(station)
    if not check(names.size() == 3 and names.has(PRIMARY_STATION_NAME), "The authored preview must expose its three player stations including " + PRIMARY_STATION_NAME): return false
    for expected in expected_station_names:
        if not check(names.has(expected), "Expected authored station is absent: " + expected): return false
    for station in names:
        if station != PRIMARY_STATION_NAME:
            alternate_station = station
            break
    print("PREVIEW_STATION_OPTIONS ", ", ".join(names))
    return true

# A receipt from one explicit station binding must not become another station's memory.
func verify_greeting_memory():
    if not await start_station_conversation("Station greeting", PRIMARY_STATION_NAME): return false
    if not check(choice_labels().has("Introduce yourself") and not choice_labels().has("Good to hear from you again"),
            "First station contact must offer introduction"): return false
    if not await press_choice("Introduce yourself"): return false
    if not check(lines().contains("Your introduction is noted"), "Introduction must advance to its authored acknowledgement"): return false
    if not await press_choice("Thank you"): return false
    if not await start_station_conversation("Station greeting", PRIMARY_STATION_NAME): return false
    if not check(not choice_labels().has("Introduce yourself") and choice_labels().has("Good to hear from you again"),
            "Returning to the same station must use its binding-scoped memory"): return false
    if not await press_choice("Good to hear from you again"): return false
    if not check(lines().contains("We remember your introduction"), "The returning branch must render its authored line"): return false
    if not await press_choice("Until next time"): return false
    if not await start_station_conversation("Station greeting", alternate_station): return false
    if not check(choice_labels().has("Introduce yourself") and not choice_labels().has("Good to hear from you again"),
            "A new station binding must retain independent introduction memory"): return false
    if not await press_choice("Request a channel check"): return false
    if not check(lines().contains("Your message arrived"), "The alternative branch must remain independently usable"): return false
    return await press_choice("Received")

func open_pending_welcome():
    var pending = control("Pending")
    for index in range(pending.item_count):
        if pending.get_item_text(index).contains("First contact welcome"):
            pending.select(index)
            pending.emit_signal("item_selected", index)
            return await activate(control("Open"))
    return check(false, "The unresolved station welcome must remain in the pending list")

# Rejected repeat starts cannot replace the original unresolved conversation identity.
func verify_once_binding_welcome():
    if not await start_station_conversation("First contact welcome", PRIMARY_STATION_NAME): return false
    if not check(lines().contains("Safe travels"), "First welcome must start from authored content"): return false
    if not await activate(control("Close")): return false
    var pending = control("Pending")
    if not check(pending.item_count == 1, "Closing the welcome must suspend exactly one conversation"): return false
    var original_pending_label = pending.get_item_text(0)
    if not await start_station_conversation("First contact welcome", PRIMARY_STATION_NAME): return false
    if not check(status_text().contains("dialogue-repeatability") and control("Close").disabled,
            "The same binding must reject a repeat start while its welcome is unresolved"): return false
    if not check(control("Pending").item_count == 1 and control("Pending").get_item_text(0) == original_pending_label,
            "A rejected repeat must preserve the existing pending identity"): return false
    if not check(not control("Result").text.contains("dialogue-repeatability"), "The dock must explain rejection without its technical code"): return false
    if not await open_pending_welcome(): return false
    if not check(lines().contains("Safe travels"), "Resume must reopen the original welcome node"): return false
    if not await activate(control("Close")): return false
    if not check(control("Pending").get_item_text(0) == original_pending_label,
            "Closing a resumed welcome must retain the same conversation identity"): return false
    if not await open_pending_welcome(): return false
    if not await press_choice("Acknowledge welcome"): return false
    if not await start_station_conversation("First contact welcome", PRIMARY_STATION_NAME): return false
    if not check(status_text().contains("dialogue-repeatability"), "Completed once-binding welcome must still reject reuse"): return false
    if not await start_station_conversation("First contact welcome", alternate_station): return false
    if not check(lines().contains("Safe travels"), "Another station binding must have its own welcome eligibility"): return false
    return await press_choice("Acknowledge welcome")

func verify_bulletin():
    if not check(pacing_button().text == "Pause", "The informational lane begins while running"): return false
    if not await start_station_conversation("Station channel bulletin", PRIMARY_STATION_NAME): return false
    if not check(lines().contains("Station bulletin") and lines().contains("No response required."),
            "The completed rich-text bulletin must remain available for display"): return false
    if not check(control("Choices").get_child_count() == 0 and pacing_button().text == "Pause",
            "A purely informational bulletin must not acquire a response pause"): return false
    if not await screenshot("preview-station-bulletin"): return false
    return await activate(control("Close"))

# Select the same user-visible ship through the existing native map inspection list.
func inspect_aster_ship():
    var cargo = main.get_node("Interface/CargoPanel")
    for list in cargo.find_children("*", "ItemList", true, false):
        if list.accessibility_name != "Ships: select a focused ship": continue
        for index in range(list.item_count):
            if list.get_item_text(index) == "Aster: ship 1":
                list.select(index)
                list.emit_signal("item_selected", index)
                await settle()
                return check(main.get_node("Interface/Header/Content/View").text.contains("SYSTEM ASTER"),
                    "The existing ship list must open Aster for station-map inspection")
    return check(false, "The authored Aster ship must remain available through the existing map controls")

# Recenter follows the selected ship. Pan through ordinary map input to expose
# the station's complete exterior label beside the existing cargo surface.
func pan_for_station_screenshot():
    var start = Vector2(740, 430)
    var finish = Vector2(460, 410)
    var button = InputEventMouseButton.new()
    button.button_index = MOUSE_BUTTON_MIDDLE
    button.position = start
    button.global_position = start
    button.pressed = true
    root.push_input(button, true)
    await process_frame
    var motion = InputEventMouseMotion.new()
    motion.position = finish
    motion.global_position = finish
    motion.relative = finish - start
    motion.button_mask = MOUSE_BUTTON_MASK_MIDDLE
    root.push_input(motion, true)
    button = button.duplicate()
    button.position = finish
    button.global_position = finish
    button.pressed = false
    root.push_input(button, true)
    await settle()

func verify_station_navigation():
    if not await choose_definition("Navigate to station"): return false
    if not check(control("Start").disabled, "Both navigation participants require explicit choices"): return false
    if not await choose_role("ship", "Ship 1"): return false
    if not check(control("Start").disabled, "Selecting only the ship cannot infer a station"): return false
    if not await choose_role("station", "Station: " + PRIMARY_STATION_NAME): return false
    if not await activate(control("Start")): return false
    if not await inspect_aster_ship(): return false
    if not check(pacing_button().text == "Resume", "The response-required station course request must pause in foreground"): return false
    if not await press_choice("Set course for this station"): return false
    if not check(lines().contains("Navigation request accepted.") and pacing_button().text == "Resume",
            "The accepted move and acknowledgement node must retain one continuous dialogue pause"): return false
    var inspector = main.get_node("Interface/StatusPanel/Margin/Content/Inspector").text
    if not check(inspector.contains("Active") and inspector.contains("Entity"),
            "The ordinary owned-ship order must target the explicitly selected station entity"): return false
    await pan_for_station_screenshot()
    if not await screenshot("preview-stations-and-navigation"): return false
    if not await press_choice("Close channel"): return false
    return check(control("Close").disabled and pacing_button().text == "Pause",
        "The separate acknowledgement must end dialogue and release only its automatic pause")

func _run():
    TranslationServer.set_locale("en")
    TranslationServer.pseudolocalization_enabled = false
    main = load("res://Scenes/Main.tscn").instantiate()
    root.add_child(main)
    await settle()
    panel = main.get_node("Interface/DialoguePanel")
    if not await verify_station_options(): return
    if not await verify_greeting_memory(): return
    if not await verify_once_binding_welcome(): return
    if not await verify_bulletin(): return
    if not await verify_station_navigation(): return
    print("PASS: authored preview station names, binding memory, once-binding resume/rejection, bulletin and ship-to-station course")
    quit(0)
