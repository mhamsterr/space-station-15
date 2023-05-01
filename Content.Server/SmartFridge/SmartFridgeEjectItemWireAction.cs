using Content.Server.Wires;
using Content.Shared.SmartFridge;
using Content.Shared.Wires;

namespace Content.Server.SmartFridge;

public sealed class SmartFridgeEjectItemWireAction : ComponentWireAction<SmartFridgeComponent>
{
    private SmartFridgeSystem _SmartFridgeSystem = default!;

    public override Color Color { get; set; } = Color.Red;
    public override string Name { get; set; } = "wire-name-vending-eject";

    public override object? StatusKey { get; } = EjectWireKey.StatusKey;

    public override StatusLightState? GetLightState(Wire wire, SmartFridgeComponent comp)
        => comp.CanShoot ? StatusLightState.BlinkingFast : StatusLightState.On;

    public override void Initialize()
    {
        base.Initialize();

        _SmartFridgeSystem = EntityManager.System<SmartFridgeSystem>();
    }

    public override bool Cut(EntityUid user, Wire wire, SmartFridgeComponent vending)
    {
        _SmartFridgeSystem.SetShooting(wire.Owner, true, vending);
        return true;
    }

    public override bool Mend(EntityUid user, Wire wire, SmartFridgeComponent vending)
    {
        _SmartFridgeSystem.SetShooting(wire.Owner, false, vending);
        return true;
    }

    public override void Pulse(EntityUid user, Wire wire, SmartFridgeComponent vending)
    {
        _SmartFridgeSystem.EjectRandom(wire.Owner, true, fridgeComponent: vending);
    }
}
