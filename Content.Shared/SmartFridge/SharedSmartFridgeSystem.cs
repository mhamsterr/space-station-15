using Robust.Shared.Containers;
using Robust.Shared.Prototypes;
using System.Linq;
using Content.Shared.DoAfter;
using Robust.Shared.Serialization;


namespace Content.Shared.SmartFridge;

public abstract class SharedSmartFridgeSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly IEntityManager _entityManager = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SmartFridgeComponent, ComponentInit>(OnComponentInit);

    }

    protected virtual void OnComponentInit(EntityUid uid, SmartFridgeComponent component, ComponentInit args)
    {
        return;
    }

    /// <summary>
    /// Returns all of the fridge's inventory.
    /// </summary>
    /// <param name="uid"></param>
    /// <param name="component"></param>
    /// <returns></returns>
    public List<EntityUid> GetInventory(EntityUid uid, SmartFridgeComponent? component = null)
    {
        if (!Resolve(uid, ref component))
            return new();
        if (component == null)
        {
            _entityManager.TryGetComponent<SmartFridgeComponent>(uid, out var compo);
            List<EntityUid> returnable = new();
            foreach (var item in compo!.Contained)
            {
                returnable.Add(item);
            }
            return returnable;
        }
        else
        {
            List<EntityUid> returnable = new();
            foreach (var item in component!.Contained)
            {
                returnable.Add(item);
            }
            return returnable;
        }

        // if (component.Current)
        //     inventory.AddRange(component.ContrabandInventory.Values);

    }

    public List<SmartFridgeInventoryGroup> GetGroups(EntityUid uid, SmartFridgeComponent? component = null)
    {
        if (!Resolve(uid, ref component))
            return new();
        if (component == null)
        {
            _entityManager.TryGetComponent<SmartFridgeComponent>(uid, out var compo);
            List<SmartFridgeInventoryGroup> returnable = new();
            foreach (var item in compo!.Groups)
            {
                returnable.Add(item);
            }
            return returnable;
        }
        else
        {
            List<SmartFridgeInventoryGroup> returnable = new();
            foreach (var item in component!.Groups)
            {
                returnable.Add(item);
            }
            return returnable;
        }

        // if (component.Current)
        //     inventory.AddRange(component.ContrabandInventory.Values);

    }

    public List<SmartFridgeInventoryGroup> GetAvailableInventory(EntityUid uid, SmartFridgeComponent? component = null)
    {
        if (!Resolve(uid, ref component))
            return new();
        List<SmartFridgeInventoryGroup> returnable = new();
        List<SmartFridgeInventoryGroup> current = GetGroups(uid, component);
        foreach (var entry in current)
        {
            if (entry.Amount > 0)
            {
                returnable.Add(entry);
            }
            // if ((current.Where(_ => _ == entry).Count() > 0) && (current.Where(_ => _ == entry).Count() > returnable.Where(_ => _ == entry).Count()))
            // {
            //     returnable.Add(entry);
            // }
        }
        return returnable;
    }
}

[Serializable, NetSerializable]
public sealed class SmartFridgeRestockDoAfterEvent : SimpleDoAfterEvent
{
}
