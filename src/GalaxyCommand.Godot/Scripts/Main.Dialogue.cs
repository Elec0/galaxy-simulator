using GalaxyCommand.Client;
using GalaxyCommand.Simulation;
using Godot;

namespace GalaxyCommand.GodotClient;

public partial class Main
{
    private readonly DialoguePresentationInbox _dialogueInbox = new(32);
    private readonly List<DialogueConversationId> _dialoguePendingRows = [];
    private readonly HashSet<DialogueConversationId> _informationalForegrounded = [];
    private readonly List<DialogueRoleSelector> _dialogueRoleSelectors = [];
    private readonly Dictionary<Control, string> _dialogueApplicationLabels = [];
    private DialoguePresentationController _dialoguePresentation = null!;
    private PanelContainer _dialoguePanel = null!;
    private ScrollContainer _dialogueScroll = null!;
    private VBoxContainer _dialogueCurrent = null!;
    private VBoxContainer _dialoguePendingSection = null!;
    private VBoxContainer _dialogueStartSection = null!;
    private Button _dialogueStartToggle = null!;
    private OptionButton _dialogueDefinition = null!;
    private VBoxContainer _dialogueRoles = null!;
    private ItemList _dialoguePending = null!;
    private VBoxContainer _dialogueSegments = null!;
    private VBoxContainer _dialogueChoices = null!;
    private Label _dialogueResult = null!;
    private Button _dialogueStart = null!;
    private Button _dialogueOpen = null!;
    private Button _dialogueClose = null!;
    private DialogueConversationId? _renderedDialogue;
    private IReadOnlyList<DialogueSegment> _renderedDialogueSegments = [];
    private IReadOnlyList<DialogueChoiceSnapshot> _renderedDialogueChoices = [];
    private string _renderedDialogueLocale = "";
    private bool _renderedDialoguePseudo;
    private bool _renderedDialoguePseudoRtl;
    private string[] _dialoguePendingLabels = [];

    /// <summary>Builds the approved native dock and interim explicit conversation/participant picker.</summary>
    private void ConfigureDialogueControls()
    {
        _dialoguePresentation = new(_pacing, _pacingPreferences.PauseWhenResponseRequiredDialogueOpens);
        _dialoguePanel = new PanelContainer { Name = "DialoguePanel", MouseFilter = Control.MouseFilterEnum.Stop,
            AccessibilityRegion = true, AccessibilityName = UiLocalization.Text("ui.dialogue.heading"),
            LayoutDirection = Control.LayoutDirectionEnum.ApplicationLocale, AutoTranslateMode = AutoTranslateModeEnum.Disabled };
        _dialoguePanel.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color("0d1e2b") });
        GetNode<CanvasLayer>("Interface").AddChild(_dialoguePanel);
        var margin = new MarginContainer();
        foreach (string side in new[] { "left", "right", "top", "bottom" }) margin.AddThemeConstantOverride("margin_" + side, 10);
        _dialoguePanel.AddChild(margin);
        _dialogueScroll = new ScrollContainer { FollowFocus = true, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        margin.AddChild(_dialogueScroll);
        var content = new VBoxContainer { Name = "Content", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        content.AddThemeConstantOverride("separation", 6);
        _dialogueScroll.AddChild(content);
        AddDialogueLabel(content, "ui.dialogue.heading");

        // Current speech and responses precede the interim chooser so starting a
        // conversation cannot leave its actual content below an unrelated form.
        _dialogueCurrent = new VBoxContainer { Name = "Current", Visible = false };
        content.AddChild(_dialogueCurrent);
        _dialogueSegments = new VBoxContainer { Name = "Lines" };
        _dialogueCurrent.AddChild(_dialogueSegments);
        _dialogueChoices = new VBoxContainer { Name = "Choices" };
        _dialogueCurrent.AddChild(_dialogueChoices);
        _dialogueClose = AddDialogueButton(_dialogueCurrent, "ui.dialogue.close", RequestCloseDialogue);
        _dialogueClose.Name = "Close";
        _dialogueResult = AddCargoLabel(content, "ui.dialogue.ready");
        _dialogueResult.Name = "Result";
        _dialogueResult.AccessibilityLive = AccessibilityServer.AccessibilityLiveMode.Polite;

        _dialoguePendingSection = new VBoxContainer { Name = "PendingSection", Visible = false };
        content.AddChild(_dialoguePendingSection);
        AddDialogueLabel(_dialoguePendingSection, "ui.dialogue.pending");
        _dialoguePending = AddSemanticList(_dialoguePendingSection, "ui.dialogue.pending", 66);
        _dialoguePending.Name = "Pending";
        _dialoguePending.AutoTranslateMode = AutoTranslateModeEnum.Disabled;
        _dialoguePending.ItemSelected += _ => _dialogueOpen.Disabled = _dialoguePending.GetSelectedItems().Length == 0;
        _dialoguePending.ItemActivated += _ => RequestOpenSelectedDialogue();
        _dialogueOpen = AddDialogueButton(_dialoguePendingSection, "ui.dialogue.open", RequestOpenSelectedDialogue);
        _dialogueOpen.Name = "Open";
        _dialogueOpen.Disabled = true;

        string startSectionLabel = UiLocalization.Text("ui.dialogue.start_section");
        _dialogueStartToggle = new Button { Name = "StartSectionToggle", Text = startSectionLabel,
            AccessibilityName = startSectionLabel, ToggleMode = true, ButtonPressed = true, ClipText = true,
            FocusMode = Control.FocusModeEnum.All };
        AddCargoFocusStyle(_dialogueStartToggle);
        _dialogueApplicationLabels.Add(_dialogueStartToggle, "ui.dialogue.start_section");
        content.AddChild(_dialogueStartToggle);
        _dialogueStartSection = new VBoxContainer { Name = "StartSection" };
        content.AddChild(_dialogueStartSection);
        _dialogueStartToggle.Toggled += expanded =>
        {
            _dialogueStartSection.Visible = expanded;
            if (expanded) _dialogueDefinition.GrabFocus();
        };
        AddDialogueLabel(_dialogueStartSection, "ui.dialogue.start_help");
        _dialogueDefinition = new OptionButton { Name = "Definition", AccessibilityName = UiLocalization.Text("ui.dialogue.definition"),
            FocusMode = Control.FocusModeEnum.All, AutoTranslateMode = AutoTranslateModeEnum.Disabled,
            FitToLongestItem = false, ClipText = true };
        _dialogueDefinition.AddItem(UiLocalization.Text("ui.dialogue.choose_definition"));
        foreach (DialogueDefinition definition in _session.DialogueDefinitions.Definitions)
            _dialogueDefinition.AddItem(UiLocalization.ContentName(definition.Key, _contentNames));
        AddCargoFocusStyle(_dialogueDefinition);
        _dialogueStartSection.AddChild(_dialogueDefinition);
        _dialogueRoles = new VBoxContainer { Name = "Participants" };
        _dialogueStartSection.AddChild(_dialogueRoles);
        _dialogueDefinition.ItemSelected += _ => RebuildDialogueRoleSelectors();
        _dialogueStart = AddDialogueButton(_dialogueStartSection, "ui.dialogue.start", StartSelectedDialogue);
        _dialogueStart.Name = "Start";
        GetViewport().SizeChanged += LayoutDialoguePanel;
        LayoutDialoguePanel();
    }

    /// <summary>Releases local dialogue pause ownership before this application scene is replaced.</summary>
    public override void _ExitTree()
    {
        _dialoguePresentation?.Close();
        GetViewport().SizeChanged -= LayoutDialoguePanel;
    }

    /// <summary>Refreshes presentation labels on locale change without touching conversation state or explicit participant selections.</summary>
    public override void _Notification(int what)
    {
        if (what != NotificationTranslationChanged || _dialoguePanel is null) return;
        foreach ((Control control, string key) in _dialogueApplicationLabels)
        {
            string text = UiLocalization.Text(key);
            if (control is Label label) label.Text = text;
            else if (control is Button button) button.Text = text;
            control.AccessibilityName = text;
        }
        _dialoguePanel.AccessibilityName = UiLocalization.Text("ui.dialogue.heading");
        _dialogueDefinition.AccessibilityName = UiLocalization.Text("ui.dialogue.definition");
        _dialogueDefinition.SetItemText(0, UiLocalization.Text("ui.dialogue.choose_definition"));
        for (int index = 0; index < _session.DialogueDefinitions.Definitions.Count; index++)
            _dialogueDefinition.SetItemText(index + 1, UiLocalization.ContentName(_session.DialogueDefinitions.Definitions[index].Key, _contentNames));
        foreach (DialogueRoleSelector selector in _dialogueRoleSelectors)
        {
            string text = UiLocalization.Text("ui.dialogue.role", ("role", selector.Role.Id));
            selector.Label.Text = text;
            selector.Picker.AccessibilityName = text;
            selector.Picker.SetItemText(0, UiLocalization.Text("ui.dialogue.choose_participant"));
        }
        _dialoguePending.AccessibilityName = UiLocalization.Text("ui.dialogue.pending");
        if (_presentation is not null) RefreshDialogueControls(new([], null, null, false));
    }

    /// <summary>Fits the dock between existing header and pacing surfaces while leaving the central map interactive.</summary>
    private void LayoutDialoguePanel()
    {
        Rect2 header = GetNode<Control>("Interface/Header").GetGlobalRect();
        Rect2 status = GetNode<Control>("Interface/StatusPanel").GetGlobalRect();
        Vector2 viewport = GetViewport().GetVisibleRect().Size;
        float top = header.End.Y + 8;
        float bottom = Math.Min(status.Position.Y - 8, viewport.Y - 12);
        float width = Math.Min(320, viewport.X * 0.25f);
        Rect2 cargo = GetNode<Control>("Interface/CargoPanel").GetGlobalRect();
        bool cargoOnLeft = cargo.GetCenter().X < viewport.X * 0.5f;
        // Offsets are logical. Position would undo Godot's RTL anchor mirroring;
        // account for the panel's direction when choosing the opposite cargo edge.
        bool anchorRight = cargoOnLeft != _dialoguePanel.IsLayoutRtl();
        _dialoguePanel.SetAnchor(Side.Left, anchorRight ? 1 : 0);
        _dialoguePanel.SetAnchor(Side.Right, anchorRight ? 1 : 0);
        _dialoguePanel.OffsetLeft = anchorRight ? -16 - width : 16;
        _dialoguePanel.OffsetRight = anchorRight ? -16 : 16 + width;
        _dialoguePanel.OffsetTop = top;
        _dialoguePanel.OffsetBottom = Math.Max(top + 40, bottom);
    }

    /// <summary>Refreshes only disclosed live data and terminal notices, preserving local focus and scroll between unchanged frames.</summary>
    private void RefreshDialogueControls(GamePresentationFactReadResult facts)
    {
        _dialogueInbox.Update(_session.CaptureDialogue(), facts, _session.DialogueDefinitions);
        _dialoguePresentation.Update(_dialogueInbox.Entries.Select(entry => new DialoguePresentationEntry(entry.Id,
            entry.IsSuspended, entry.ResponseRequired, entry.IsCompleted)));
        RefreshDialogueRoleOptions();
        RefreshDialoguePending();
        RenderForegroundDialogue();
        _dialogueClose.Disabled = _dialoguePresentation.ForegroundConversationId is null;
        LayoutDialoguePanel();
        var retained = _dialogueInbox.Entries.Select(entry => entry.Id).ToHashSet();
        _informationalForegrounded.RemoveWhere(id => !retained.Contains(id));
        if (_dialoguePresentation.ForegroundConversationId is { } foreground
            && _dialogueInbox.Entries.FirstOrDefault(entry => entry.Id == foreground) is { ResponseRequired: false }
            && _informationalForegrounded.Add(foreground))
            _eventPacingInbox.Enqueue(new(ApplicationPacingEventCategories.InformationalDialogueForegrounded,
                new ApplicationPacingEventSubjectId("dialogue:" + foreground)));
    }

    /// <summary>Rebuilds role controls only when the player explicitly changes the selected conversation definition.</summary>
    private void RebuildDialogueRoleSelectors()
    {
        ClearDialogueChildren(_dialogueRoles);
        _dialogueRoleSelectors.Clear();
        if (SelectedDialogueDefinition is not { } definition) { _dialogueStart.Disabled = true; return; }
        foreach (DialogueParticipantRole role in definition.Roles)
        {
            string label = UiLocalization.Text("ui.dialogue.role", ("role", role.Id));
            var roleLabel = new Label { Text = label, AutoTranslateMode = AutoTranslateModeEnum.Disabled,
                AutowrapMode = TextServer.AutowrapMode.WordSmart };
            _dialogueRoles.AddChild(roleLabel);
            var picker = new OptionButton { AccessibilityName = label, FocusMode = Control.FocusModeEnum.All,
                AutoTranslateMode = AutoTranslateModeEnum.Disabled, FitToLongestItem = false, ClipText = true };
            picker.AddItem(UiLocalization.Text("ui.dialogue.choose_participant"));
            AddCargoFocusStyle(picker);
            _dialogueRoles.AddChild(picker);
            _dialogueRoleSelectors.Add(new(role, picker, roleLabel));
            picker.ItemSelected += _ => RefreshDialogueStartAvailability();
        }
        RefreshDialogueRoleOptions();
    }

    private DialogueDefinition? SelectedDialogueDefinition => _dialogueDefinition.Selected <= 0 ? null
        : _session.DialogueDefinitions.Definitions[_dialogueDefinition.Selected - 1];

    /// <summary>Offers disclosed ships, current or last-observed stations, and public principals without strengthening authored conditions.</summary>
    private void RefreshDialogueRoleOptions()
    {
        if (_presentation is null) return;
        foreach (DialogueRoleSelector selector in _dialogueRoleSelectors)
        {
            DialogueParticipant? selected = selector.Picker.Selected > 0 ? selector.Candidates[selector.Picker.Selected - 1].Participant : null;
            var candidates = new List<(DialogueParticipant Participant, string Label)>();
            if (selector.Role.AcceptedKinds.Contains(DialogueParticipantKind.Ship))
                candidates.AddRange(_presentation.World.Ships.OrderBy(ship => ship.Id.Value).Select(ship =>
                    ((DialogueParticipant)new DialogueParticipant.Ship(ship.Id), UiLocalization.Text("ui.dialogue.ship", ("id", Id(ship.Id.Value))))));
            if (selector.Role.AcceptedKinds.Contains(DialogueParticipantKind.Station))
                candidates.AddRange(DialogueParticipantChoices.KnownStations(_presentation.World.Stations)
                    .Select(station => ((DialogueParticipant)new DialogueParticipant.Station(station.View.Id),
                        UiLocalization.Text(station.State == SensorObservationState.Stale ? "ui.dialogue.station_last_observed" : "ui.dialogue.station",
                            ("name", station.View.Name)))));
            if (selector.Role.AcceptedKinds.Contains(DialogueParticipantKind.Principal))
                candidates.AddRange(_presentation.Relationships.Principals.OrderBy(principal => principal.Id.Value).Select(principal =>
                    ((DialogueParticipant)new DialogueParticipant.Principal(principal.Id), UiLocalization.Text("ui.dialogue.principal", ("name", principal.Name)))));
            if (selector.Candidates.SequenceEqual(candidates)) continue;
            selector.Candidates = candidates;
            selector.Picker.Clear();
            selector.Picker.AddItem(UiLocalization.Text("ui.dialogue.choose_participant"));
            foreach ((DialogueParticipant _, string label) in candidates) selector.Picker.AddItem(label);
            selector.Picker.Select(selected is null ? 0 : candidates.FindIndex(candidate => candidate.Participant == selected) + 1);
        }
        RefreshDialogueStartAvailability();
    }

    /// <summary>Requires the explicit definition and every explicit role selection before capturing start intent.</summary>
    private void RefreshDialogueStartAvailability() => _dialogueStart.Disabled = SelectedDialogueDefinition is null
        || _dialogueRoleSelectors.Any(selector => selector.Picker.Selected <= 0);

    /// <summary>Captures selected stable bindings in the common input FIFO; the session revalidates all eligibility.</summary>
    private void StartSelectedDialogue()
    {
        if (_dialogueStart.Disabled || SelectedDialogueDefinition is not { } definition) return;
        EnqueueDialogue(new StartDialogueCommand(definition.Key, _dialogueRoleSelectors.Select(selector =>
            new DialogueParticipantBinding(selector.Role.Id, selector.Candidates[selector.Picker.Selected - 1].Participant))));
    }

    /// <summary>Preserves selected pending identity across live updates without ordering by translated labels.</summary>
    private void RefreshDialoguePending()
    {
        int selectedIndex = _dialoguePending.GetSelectedItems().FirstOrDefault(-1);
        DialogueConversationId? selected = selectedIndex >= 0 && selectedIndex < _dialoguePendingRows.Count ? _dialoguePendingRows[selectedIndex] : null;
        DialogueDisplayConversation[] entries = _dialogueInbox.Entries.Where(entry => entry.Id != _dialoguePresentation.ForegroundConversationId).ToArray();
        _dialoguePendingSection.Visible = entries.Length > 0;
        string[] labels = entries.Select(entry => UiLocalization.Text("ui.dialogue.pending_entry",
            ("conversation", UiLocalization.ContentName(entry.DefinitionKey, _contentNames)), ("id", Id(entry.Id.Value)),
            ("state", UiLocalization.Text(entry.IsCompleted ? "ui.dialogue.completed" : entry.IsSuspended ? "ui.dialogue.suspended" : "ui.dialogue.active")))).ToArray();
        if (_dialoguePendingRows.SequenceEqual(entries.Select(entry => entry.Id)) && _dialoguePendingLabels.SequenceEqual(labels)) return;
        _dialoguePendingRows.Clear();
        _dialoguePending.Clear();
        _dialoguePendingLabels = labels;
        for (int index = 0; index < entries.Length; index++)
        {
            _dialoguePendingRows.Add(entries[index].Id);
            _dialoguePending.AddItem(labels[index]);
            if (entries[index].Id == selected) _dialoguePending.Select(index);
        }
        _dialogueOpen.Disabled = _dialoguePending.GetSelectedItems().Length == 0;
    }

    /// <summary>Resumes suspended conversations through admission; other local foreground selections remain FIFO-ordered.</summary>
    private void RequestOpenSelectedDialogue()
    {
        int selected = _dialoguePending.GetSelectedItems().FirstOrDefault(-1);
        if (selected < 0 || selected >= _dialoguePendingRows.Count) return;
        DialogueConversationId id = _dialoguePendingRows[selected];
        DialogueDisplayConversation? entry = _dialogueInbox.Entries.FirstOrDefault(item => item.Id == id);
        if (entry is null) return;
        if (entry.IsSuspended) EnqueueDialogue(new ResumeDialogueCommand(id));
        else _input.EnqueueDialoguePresentation(new DialoguePresentationAction.Open(id));
    }

    /// <summary>Live close waits for accepted suspension; completed information may be dismissed only from local history.</summary>
    private void RequestCloseDialogue()
    {
        if (_dialoguePresentation.ForegroundConversationId is not { } id) return;
        if (_dialogueInbox.Entries.FirstOrDefault(entry => entry.Id == id) is { IsCompleted: true })
            _input.EnqueueDialoguePresentation(new DialoguePresentationAction.Close(id));
        else EnqueueDialogue(new SuspendDialogueCommand(id));
    }

    /// <summary>Queues authoritative dialogue intent without converting labels or display order into command identity.</summary>
    private void EnqueueDialogue(GameplayCommand command)
    {
        _input.EnqueueGameplay(_player, command);
        _dialogueResult.Text = UiLocalization.Text("ui.dialogue.queued");
    }

    /// <summary>Refreshes admission outcomes before opening resumed state; rejected suspend/resume cannot alter local ownership.</summary>
    private void HandleDialogueCommandResult(GameplayCommandRecord record)
    {
        if (record.Envelope.Command is not (StartDialogueCommand or SelectDialogueChoiceCommand or SuspendDialogueCommand or ResumeDialogueCommand)) return;
        _dialogueResult.Text = record.Result.Status == CommandResultStatus.Accepted ? UiLocalization.Text("ui.dialogue.accepted")
            : UiLocalization.Text("ui.dialogue.rejected", ("reason", DescribeDialogueRejection(record.Result.RejectionCode)));
        bool started = record.Result.Status == CommandResultStatus.Accepted && record.Envelope.Command is StartDialogueCommand;
        if (started) _dialogueStartToggle.ButtonPressed = false;
        RefreshPresentation();
        if (record.Result.Status == CommandResultStatus.Accepted && record.Envelope.Command is ResumeDialogueCommand resumed)
        {
            _dialoguePresentation.Open(resumed.ConversationId);
            RefreshDialogueControls(new([], null, null, false));
        }
        if (started) FocusForegroundDialogue();
    }

    /// <summary>Applies captured local foreground intent against the refreshed inbox, leaving stale actions harmless.</summary>
    private void ApplyDialoguePresentationAction(DialoguePresentationAction action)
    {
        if (action is DialoguePresentationAction.Open) _dialoguePresentation.Open(action.ConversationId);
        else if (_dialoguePresentation.ForegroundConversationId == action.ConversationId
            && _dialogueInbox.Dismiss(action.ConversationId)) _dialoguePresentation.Close();
        RefreshDialogueControls(new([], null, null, false));
    }

    /// <summary>Rebuilds only changed authored content; choices keep their explicit authored order and current admission availability.</summary>
    private void RenderForegroundDialogue()
    {
        DialogueDisplayConversation? entry = _dialogueInbox.Entries.FirstOrDefault(item => item.Id == _dialoguePresentation.ForegroundConversationId);
        _dialogueCurrent.Visible = entry is not null;
        IReadOnlyList<DialogueSegment> segments = entry?.Segments ?? [];
        IReadOnlyList<DialogueChoiceSnapshot> choices = entry?.Choices ?? [];
        string locale = TranslationServer.GetLocale();
        TranslationDomain domain = TranslationServer.GetOrAddDomain("");
        bool pseudo = domain.PseudolocalizationEnabled;
        bool pseudoRtl = pseudo && domain.PseudolocalizationFakeBidiEnabled;
        _dialoguePanel.LayoutDirection = pseudoRtl ? Control.LayoutDirectionEnum.Rtl : Control.LayoutDirectionEnum.ApplicationLocale;
        if (_renderedDialogue == entry?.Id && _renderedDialogueSegments.SequenceEqual(segments)
            && _renderedDialogueChoices.SequenceEqual(choices) && _renderedDialogueLocale == locale
            && _renderedDialoguePseudo == pseudo && _renderedDialoguePseudoRtl == pseudoRtl) return;
        Control? priorFocus = GetViewport().GuiGetFocusOwner();
        bool restoreFocus = priorFocus is not null && (_dialogueSegments.IsAncestorOf(priorFocus) || _dialogueChoices.IsAncestorOf(priorFocus));
        bool newForeground = entry is not null && _renderedDialogue != entry.Id;
        ClearDialogueChildren(_dialogueSegments);
        ClearDialogueChildren(_dialogueChoices);
        _renderedDialogue = entry?.Id;
        _renderedDialogueSegments = segments;
        _renderedDialogueChoices = choices;
        _renderedDialogueLocale = locale;
        _renderedDialoguePseudo = pseudo;
        _renderedDialoguePseudoRtl = pseudoRtl;
        foreach (DialogueSegment segment in segments)
        {
            if (segment.Speaker is { } speaker) AddDialogueText(_dialogueSegments, speaker);
            AddDialogueText(_dialogueSegments, segment.Text);
        }
        if (entry is not null)
            foreach (DialogueChoiceSnapshot choice in choices.OrderBy(choice => choice.Order))
            {
                string label = DialogueTextFormatter.ToPlainText(UiLocalization.Dialogue(choice.Label), choice.Label.Markup);
                string unavailable = choice.IsAvailable ? "" : DescribeDialogueRejection(choice.UnavailableReason);
                var button = new Button { Text = label, AccessibilityName = label, Disabled = !choice.IsAvailable,
                    FocusMode = Control.FocusModeEnum.All, AutoTranslateMode = AutoTranslateModeEnum.Disabled, ClipText = true,
                    TooltipText = choice.IsAvailable ? label : unavailable, AccessibilityDescription = unavailable };
                AddCargoFocusStyle(button);
                DialogueConversationId id = entry.Id;
                string choiceId = choice.Id;
                button.Pressed += () => EnqueueDialogue(new SelectDialogueChoiceCommand(id, choiceId));
                _dialogueChoices.AddChild(button);
            }
        if (newForeground || restoreFocus) FocusForegroundDialogue();
    }

    /// <summary>Explains only the disclosed rejection category; unknown codes use a safe human-readable fallback.</summary>
    private static string DescribeDialogueRejection(CommandRejectionCode? reason) => UiLocalization.Text(reason?.Value switch
    {
        "invalid-source" or "dialogue-invalid-source" => "ui.dialogue.unavailable.source",
        "invalid-state" or "dialogue-invalid-state" or "conflict" => "ui.dialogue.unavailable.state",
        "actor-overridden" => "ui.dialogue.unavailable.control",
        "stale-control-revision" => "ui.dialogue.unavailable.control_changed",
        "invalid-intent" or "dialogue-invalid-bindings" or "dialogue-invalid-consequence" => "ui.dialogue.unavailable.action",
        "dialogue-no-offers" => "ui.dialogue.unavailable.next_response",
        "dialogue-participant-unavailable" => "ui.dialogue.unavailable.participant",
        "dialogue-suspended" => "ui.dialogue.unavailable.closed",
        "dialogue-not-found" or "dialogue-definition-not-found" => "ui.dialogue.unavailable.conversation",
        "dialogue-choice-unavailable" or "dialogue-choice-not-offered" => "ui.dialogue.unavailable.response",
        "dialogue-repeatability" => "ui.dialogue.unavailable.used",
        "dialogue-start-condition" => "ui.dialogue.unavailable.start",
        _ => "ui.dialogue.unavailable.generic",
    });

    /// <summary>Moves focus and scroll once when foreground content changes, leaving ordinary frame refreshes alone.</summary>
    private void FocusForegroundDialogue()
    {
        if (_dialoguePresentation.ForegroundConversationId is null) return;
        Control focus = _dialogueSegments.GetChildren().OfType<RichTextLabel>().FirstOrDefault()
            ?? (Control?)_dialogueChoices.GetChildren().OfType<Button>().FirstOrDefault(button => !button.Disabled) ?? _dialogueClose;
        focus.GrabFocus();
        // Locale or foreground refresh may detach this control before deferred
        // scrolling runs. Scroll only while both objects and their ancestry survive.
        Callable.From(() =>
        {
            if (GodotObject.IsInstanceValid(_dialogueScroll) && GodotObject.IsInstanceValid(focus)
                && _dialogueScroll.IsAncestorOf(focus)) _dialogueScroll.EnsureControlVisible(focus);
        }).CallDeferred();
    }

    /// <summary>Displays approved markup with literal dynamic values and an equivalent plain accessible name.</summary>
    private static void AddDialogueText(VBoxContainer parent, DialogueText resource)
    {
        DialogueFormattedText formatted = UiLocalization.Dialogue(resource);
        var label = new RichTextLabel { Text = formatted.Text, BbcodeEnabled = resource.Markup == DialogueMarkup.RestrictedRichText,
            FitContent = true, ScrollActive = false, SelectionEnabled = true, AutoTranslateMode = AutoTranslateModeEnum.Disabled,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, FocusMode = Control.FocusModeEnum.All,
            TextDirection = formatted.IsRightToLeft ? Control.TextDirection.Rtl : Control.TextDirection.Ltr,
            AccessibilityName = DialogueTextFormatter.ToPlainText(formatted, resource.Markup) };
        parent.AddChild(label);
    }

    /// <summary>Detaches replaced controls immediately so deferred deletion cannot duplicate keyboard targets in the current frame.</summary>
    private static void ClearDialogueChildren(Node parent)
    {
        foreach (Node child in parent.GetChildren()) { parent.RemoveChild(child); child.QueueFree(); }
    }

    private Label AddDialogueLabel(BoxContainer parent, string key)
    {
        Label label = AddCargoLabel(parent, key);
        _dialogueApplicationLabels.Add(label, key);
        return label;
    }

    private Button AddDialogueButton(BoxContainer parent, string key, Action pressed)
    {
        Button button = AddCargoButton(parent, key, pressed);
        button.ClipText = true;
        _dialogueApplicationLabels.Add(button, key);
        return button;
    }

    private sealed class DialogueRoleSelector(DialogueParticipantRole role, OptionButton picker, Label label)
    {
        internal DialogueParticipantRole Role { get; } = role;
        internal OptionButton Picker { get; } = picker;
        internal Label Label { get; } = label;
        internal List<(DialogueParticipant Participant, string Label)> Candidates { get; set; } = [];
    }
}
