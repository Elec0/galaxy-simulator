# Exercises the actual native dock, FIFO command handoff and locale rendering.
extends SceneTree

var main
var panel
var output_dir = ""

func _initialize():
    for argument in OS.get_cmdline_user_args():
        if argument.begins_with("--output-dir="):
            output_dir = argument.trim_prefix("--output-dir=")
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
    var values = []
    for child in control("Lines").get_children():
        if child is RichTextLabel:
            values.append(child.get_parsed_text())
    return "\n".join(values)

func choose_definition(label):
    await show_chooser()
    var selector = control("Definition")
    for index in range(selector.item_count):
        if selector.get_item_text(index) == label:
            selector.select(index)
            selector.emit_signal("item_selected", index)
            await settle()
            return true
    return check(false, "Fixture definition is absent from the production-loaded chooser: " + label)

func show_chooser():
    var toggle = control("StartSectionToggle")
    if not toggle.button_pressed:
        toggle.button_pressed = true
        await settle()

func press_choice():
    var choices = control("Choices").get_children()
    if not check(choices.size() == 1 and choices[0] is Button and not choices[0].disabled, "Exactly one offered response must be enabled"):
        return false
    choices[0].grab_focus()
    await settle()
    var event = InputEventKey.new()
    event.keycode = KEY_ENTER
    event.pressed = true
    root.push_input(event, true)
    event = event.duplicate()
    event.pressed = false
    root.push_input(event, true)
    await settle()
    return true

func screenshot(label):
    if output_dir.is_empty(): return
    await RenderingServer.frame_post_draw
    var image = root.get_texture().get_image()
    image.save_png(output_dir.path_join(label + ".png"))

func _run():
    var english = Translation.new()
    english.locale = "en"
    english.add_message("smoke.dialogue.entry", "Set a [b]course[/b] for {name}.")
    english.add_message("smoke.dialogue.speaker", "Captain Vale")
    var arabic = Translation.new()
    arabic.locale = "ar"
    arabic.add_message("smoke.dialogue.entry", "رسالة إلى {name}.")
    arabic.add_message("smoke.dialogue.speaker", "القائد")
    TranslationServer.add_translation(english)
    TranslationServer.add_translation(arabic)
    TranslationServer.set_locale("en")
    main = load("res://Scenes/Main.tscn").instantiate()
    root.add_child(main)
    await settle()
    panel = main.get_node("Interface/DialoguePanel")
    if not await choose_definition("Dialogue smoke response"): return
    var pickers = control("Participants").find_children("*", "OptionButton", true, false)
    if not check(pickers.size() == 2 and control("Start").disabled, "Every authored role needs an explicit selection"): return
    for index in range(pickers.size()):
        if not check(pickers[index].item_count >= 3, "Fixture needs two player-visible ships"): return
        pickers[index].select(index + 1)
        pickers[index].emit_signal("item_selected", index + 1)
    if not check(not control("Start").disabled, "Complete explicit bindings must enable Start"): return
    control("Start").emit_signal("pressed")
    await settle()
    if not check(lines().contains("Captain Vale") and lines().contains("Pilot [b]Vale"), "Localized speaker and literal rich-text argument must render"): return
    var pacing = main.get_node("Interface/StatusPanel/Margin/Content/PacingControls/PauseOrResume")
    if not check(pacing.text == "Resume", "Foreground response-required dialogue must automatically pause"): return
    await screenshot("dialogue-english")

    TranslationServer.set_locale("ar")
    await settle()
    if not check(lines().contains("رسالة") and lines().contains("Pilot [b]Vale"), "Locale switch must render Arabic while preserving literal dynamic names"): return
    await screenshot("dialogue-arabic")
    TranslationServer.set_locale("en")
    TranslationServer.pseudolocalization_enabled = true
    await settle()
    if not check(lines().contains("Pilot [b]Vale") and not lines().contains("smoke.dialogue.entry"), "Pseudolocalization must preserve names and resolve resources rather than display their keys"): return
    await screenshot("dialogue-pseudo")
    TranslationServer.pseudolocalization_enabled = false
    await settle()

    await show_chooser()
    control("Start").emit_signal("pressed")
    await settle()
    if not check(control("Pending").item_count == 1, "A second conversation must stay pending"): return
    control("Close").emit_signal("pressed")
    await settle()
    if not check(control("Pending").item_count == 2 and control("Close").disabled, "Closing must suspend and leave pending conversations unopened"): return
    var pending = control("Pending")
    pending.select(1)
    pending.emit_signal("item_selected", 1)
    control("Open").emit_signal("pressed")
    await settle()
    if not check(pacing.text == "Resume" and lines().contains("Pilot [b]Vale"), "Explicit open must foreground the selected conversation"): return
    if not await press_choice(): return
    if not check(lines().contains("Course request accepted.") and pacing.text == "Resume", "The next node must retain dialogue pause ownership"): return
    var speed_five
    for button in main.get_node("Interface/StatusPanel/Margin/Content/PacingControls/Presets").get_children():
        if button is Button and button.text == "5x": speed_five = button
    if not check(speed_five != null, "The shared 5x pacing control must remain available"): return
    speed_five.emit_signal("pressed")
    await settle()
    if not await press_choice(): return
    if not check(pacing.text == "Pause" and control("Close").disabled, "Completion must preserve a manual speed override and leave pending dialogue closed"): return

    pending = control("Pending")
    pending.select(0)
    pending.emit_signal("item_selected", 0)
    control("Open").emit_signal("pressed")
    await settle()
    if not check(lines().contains("Pilot [b]Vale") and pacing.text == "Resume", "Accepted resume must reopen the original node and reacquire its eligible local pause"): return
    control("Close").emit_signal("pressed")
    await settle()
    if not await choose_definition("Dialogue smoke information"): return
    control("Start").emit_signal("pressed")
    await settle()
    if not check(lines().contains("Navigation channel open.") and pacing.text == "Pause", "Completed informational dialogue must display without a response pause"): return
    await screenshot("dialogue-information")
    control("Close").emit_signal("pressed")
    await settle()
    if not check(not lines().contains("Navigation channel open."), "Dismissed information must not replay on the next refresh"): return
    if not await choose_definition("Dialogue smoke unavailable"): return
    control("Start").emit_signal("pressed")
    await settle()
    var unavailable = control("Choices").get_children()
    if not check(unavailable.size() == 1 and unavailable[0].disabled, "Current admission failure must disable the offered response"): return
    if not check(unavailable[0].tooltip_text != unavailable[0].text and not unavailable[0].tooltip_text.contains("dialogue-"), "Unavailable responses need a user-facing explanation without technical codes"): return
    print("PASS: native dialogue start, pending, suspend/resume, atomic choice, pacing, locale, pseudo and informational display")
    quit(0)
