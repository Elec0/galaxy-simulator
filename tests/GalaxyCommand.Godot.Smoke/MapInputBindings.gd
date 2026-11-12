# Exercises actual pointer/button routing against the authored preview at its default viewport size.
extends SceneTree

var main

func _initialize():
    call_deferred("_run")

func check(condition, description):
    if not condition:
        push_error(description)
        quit(1)
    return condition

func click(position, button, shift = false):
    var event = InputEventMouseButton.new()
    event.position = position
    event.global_position = position
    event.button_index = button
    event.shift_pressed = shift
    event.pressed = true
    root.push_input(event, true)
    event = event.duplicate()
    event.pressed = false
    root.push_input(event, true)
    await process_frame
    await process_frame

func _run():
    TranslationServer.set_locale("en")
    main = load("res://Scenes/Main.tscn").instantiate()
    root.add_child(main)
    await process_frame
    await process_frame
    var pause = main.get_node("Interface/StatusPanel/Margin/Content/PacingControls/PauseOrResume")
    if pause.text == "Pause":
        pause.emit_signal("pressed")
        await process_frame
        await process_frame
    await click(Vector2(380, 270), MOUSE_BUTTON_LEFT)
    await click(Vector2(540, 400), MOUSE_BUTTON_LEFT)
    var inspector = main.get_node("Interface/StatusPanel/Margin/Content/Inspector")
    var status = main.get_node("Interface/StatusPanel/Margin/Content/Status")
    if not check(inspector.text.contains("no current order"), "Ship selection must not admit movement"): return
    await click(Vector2(700, 430), MOUSE_BUTTON_LEFT)
    if not check(inspector.text.contains("no current order"), "Left-click on empty space must not move the selected ship"): return
    await click(Vector2(700, 430), MOUSE_BUTTON_RIGHT)
    if not check(inspector.text.contains("Active") and status.text.contains("accepted"), "Right-click must admit a move order"): return
    var cancel
    for button in main.find_children("*", "Button", true, false):
        if button.text.contains("Cancel"):
            cancel = button
            break
    if not check(cancel != null and not cancel.disabled, "The cancel-order button must be available for an active order"): return
    await click(Vector2(720, 450), MOUSE_BUTTON_RIGHT, true)
    if not check(status.text.contains("QUEUE 1"), "Shift-right-click must append a destination"): return
    main.get_node("Interface/Header/Content/ViewControls/Galaxy").emit_signal("pressed")
    await process_frame
    await click(Vector2(640, 470), MOUSE_BUTTON_LEFT)
    await click(Vector2(670, 290), MOUSE_BUTTON_RIGHT)
    if not check(inspector.text.contains("S1") and status.text.contains("ship.move accepted"), "Right-click on a ship must move without changing selection"): return
    await click(Vector2(670, 290), MOUSE_BUTTON_LEFT, true)
    if not check(inspector.text.contains("S2"), "Shift-left-click must focus the second selected ship"): return
    await click(Vector2(700, 430), MOUSE_BUTTON_RIGHT)
    if not check(status.text.contains("ship.move-group accepted"), "Right-click must move the captured multi-selection"): return
    if not check(cancel.text == "Cancel selected orders", "Existing cancel button must describe selected-group scope"): return
    cancel.emit_signal("pressed")
    await process_frame
    await process_frame
    if not check(status.text.contains("ship.cancel-group-current accepted"), "Existing cancel button must submit group cancellation"): return
    if not check(inspector.text.contains("Cancelled"), "Group cancellation must cancel the focused member"): return
    var reported = false
    for label in main.find_children("*", "Label", true, false):
        if label.text.contains("Command accepted."):
            reported = true
    if not check(reported, "The existing cancel button must report group command outcomes in its result panel"): return
    main.get_node("Interface/Header/Content/ViewControls/Galaxy").emit_signal("pressed")
    await process_frame
    await click(Vector2(380, 270), MOUSE_BUTTON_LEFT)
    await click(Vector2(540, 400), MOUSE_BUTTON_LEFT)
    if not check(inspector.text.contains("S1") and inspector.text.contains("Cancelled"), "Group cancellation must also cancel the other selected member"): return
    await click(Vector2(700, 430), MOUSE_BUTTON_RIGHT)
    cancel.emit_signal("pressed")
    await process_frame
    await process_frame
    if not check(inspector.text.contains("Cancelled"), "The cancel-order button must cancel the single focused order"): return
    print("BINDINGS_VERIFIED selection-only left; right move; shift-right append; button single/group cancel")
    quit(0)
