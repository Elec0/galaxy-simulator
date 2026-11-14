using GalaxyCommand.Simulation;

namespace GalaxyCommand.Simulation.Tests;

public sealed class DialogueParticipantTests
{
    private static readonly string[] CanonicalRoles = ["actor", "respondent"];
    private static readonly string[] CaseSensitiveRoles = ["Actor", "actor"];

    [Fact]
    public void ConversationIdentityUsesTheSharedPositiveIdContract()
    {
        var id = DialogueConversationId.Create(12);

        Assert.Equal(new DialogueConversationId(12), id);
        Assert.Equal(12UL, id.Value);
        Assert.Equal("12", id.ToString());
        Assert.Throws<ArgumentOutOfRangeException>(() => new DialogueConversationId(0));
    }

    [Fact]
    public void SameNumericIdentityDoesNotConflateParticipantKinds()
    {
        DialogueParticipant ship = new DialogueParticipant.Ship(new ShipId(1));
        DialogueParticipant station = new DialogueParticipant.Station(new StationId(1));
        DialogueParticipant principal = new DialogueParticipant.Principal(new PrincipalId(1));

        Assert.NotEqual(ship, station);
        Assert.NotEqual(station, principal);
        Assert.Equal(ship, new DialogueParticipant.Ship(new ShipId(1)));
        Assert.Equal(DialogueParticipantKind.Ship, ship.Kind);
        Assert.Equal(DialogueParticipantKind.Station, station.Kind);
        Assert.Equal(DialogueParticipantKind.Principal, principal.Kind);
    }

    [Fact]
    public void DefaultRuntimeIdentitiesCannotBecomeParticipants()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DialogueParticipant.Ship(default));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DialogueParticipant.Station(default));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DialogueParticipant.Principal(default));
    }

    [Fact]
    public void BindingNormalizationIgnoresCallerOrderAndPreservesRoleAssignment()
    {
        DialogueParticipantRole[] roles = [Role("respondent"), Role("actor")];
        DialogueParticipantBinding[] bindings = [Bind("respondent", 2), Bind("actor", 1)];

        var first = new DialogueParticipantBindings(roles, bindings);
        var second = new DialogueParticipantBindings(roles.Reverse(), bindings.Reverse());
        var swapped = new DialogueParticipantBindings(roles, [Bind("actor", 2), Bind("respondent", 1)]);

        Assert.Equal(CanonicalRoles, first.Items.Select(item => item.RoleId));
        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.NotEqual(first, swapped);
        bindings[0] = Bind("respondent", 99);
        roles[0] = Role("changed");
        Assert.Equal(second, first);
    }

    [Fact]
    public void MissingExtraDuplicateAndWrongKindBindingsReject()
    {
        DialogueParticipantRole[] roles = [Role("actor")];

        Assert.Throws<ArgumentException>(() => new DialogueParticipantBindings(roles, []));
        Assert.Throws<ArgumentException>(() => new DialogueParticipantBindings(roles, [Bind("other", 1)]));
        Assert.Throws<ArgumentException>(() => new DialogueParticipantBindings(roles, [Bind("actor", 1), Bind("actor", 2)]));
        Assert.Throws<ArgumentException>(() => new DialogueParticipantBindings([Role("actor"), Role("actor")], [Bind("actor", 1)]));
        Assert.Throws<ArgumentException>(() => new DialogueParticipantBindings(roles,
            [new DialogueParticipantBinding("actor", new DialogueParticipant.Station(new StationId(1)))]));
    }

    [Fact]
    public void RoleKindsAreExplicitImmutableAndValidated()
    {
        DialogueParticipantKind[] kinds = [DialogueParticipantKind.Ship, DialogueParticipantKind.Station];
        var role = new DialogueParticipantRole("respondent", kinds);
        kinds[0] = DialogueParticipantKind.Principal;

        Assert.Equal(new[] { DialogueParticipantKind.Ship, DialogueParticipantKind.Station }, role.AcceptedKinds);
        Assert.Throws<ArgumentException>(() => new DialogueParticipantRole("actor", []));
        Assert.Throws<ArgumentException>(() => new DialogueParticipantRole("actor", [DialogueParticipantKind.Ship, DialogueParticipantKind.Ship]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DialogueParticipantRole("actor", [(DialogueParticipantKind)99]));
        Assert.Throws<ArgumentException>(() => Role(" "));
        Assert.Throws<ArgumentException>(() => new DialogueParticipantBinding(" ", new DialogueParticipant.Ship(new ShipId(1))));
        Assert.Throws<ArgumentNullException>(() => new DialogueParticipantBinding("actor", null!));
    }

    [Fact]
    public void RoleIdentityIsOrdinalAndOneParticipantMayFillDistinctRoles()
    {
        var bindings = new DialogueParticipantBindings([Role("actor"), Role("Actor")],
            [Bind("actor", 1), Bind("Actor", 1)]);

        Assert.Equal(CaseSensitiveRoles, bindings.Items.Select(item => item.RoleId));
        Assert.Equal(bindings.Items[0].Participant, bindings.Items[1].Participant);
    }

    [Fact]
    public void NullCollectionsAndNullElementsRejectAtTheBindingBoundary()
    {
        Assert.Throws<ArgumentNullException>(() => new DialogueParticipantRole("actor", null!));
        Assert.Throws<ArgumentNullException>(() => new DialogueParticipantBindings(null!, []));
        Assert.Throws<ArgumentNullException>(() => new DialogueParticipantBindings([], null!));
        Assert.Throws<ArgumentException>(() => new DialogueParticipantBindings([null!], []));
        Assert.Throws<ArgumentException>(() => new DialogueParticipantBindings([Role("actor")], [null!]));
    }

    private static DialogueParticipantRole Role(string id) => new(id, [DialogueParticipantKind.Ship]);
    private static DialogueParticipantBinding Bind(string role, ulong id) => new(role, new DialogueParticipant.Ship(new ShipId(id)));
}
