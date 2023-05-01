using Robust.Shared.Serialization;
using Robust.Shared.Prototypes;

namespace Content.Shared.SmartFridge
{
    [NetSerializable, Serializable]
    public sealed class SmartFridgeInterfaceState : BoundUserInterfaceState
    {
        public List<SmartFridgeInventoryGroup> Inventory;

        public SmartFridgeInterfaceState(List<SmartFridgeInventoryGroup> inventory)
        {
            Inventory = inventory;
        }
    }

    [Serializable, NetSerializable]
    public sealed class SmartFridgeEjectMessage : BoundUserInterfaceMessage
    {
        public readonly EntityUid ID;
        public SmartFridgeEjectMessage(EntityUid id)
        {
            ID = id;
        }
    }

    [Serializable, NetSerializable]
    public enum SmartFridgeUiKey
    {
        Key,
    }
}
