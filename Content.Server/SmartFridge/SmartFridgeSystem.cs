using Content.Server.Cargo.Systems;
using Content.Server.Popups;
using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Server.Storage.Components;
using Content.Server.Storage.EntitySystems;
using Content.Server.UserInterface;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.Actions;
using Content.Shared.Actions.ActionTypes;
using Content.Shared.Damage;
using Content.Shared.Destructible;
using Content.Shared.DoAfter;
using Content.Shared.Emag.Components;
using Content.Shared.Emag.Systems;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Popups;
using Content.Shared.Storage;
using Content.Shared.Storage.Components;
using Content.Shared.SmartFridge;
using Content.Shared.Throwing;
using Content.Shared.Tools.Components;
using Content.Shared.SmartFridge;
using Robust.Server.Containers;
using Robust.Server.GameObjects;
using Robust.Server.Player;
using Robust.Shared.Audio;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Player;
using System.Linq;
using System.Threading;
using System.Collections.Generic;

using System; // if you are seeing this here then i forgot to remove this thing after performing tests

namespace Content.Server.SmartFridge
{
    public sealed class SmartFridgeSystem : SharedSmartFridgeSystem
    {
        [Dependency] private readonly IRobustRandom _random = default!;
        [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
        [Dependency] private readonly AccessReaderSystem _accessReader = default!;
        [Dependency] private readonly AppearanceSystem _appearanceSystem = default!;
        [Dependency] private readonly AudioSystem _audioSystem = default!;
        [Dependency] private readonly PopupSystem _popupSystem = default!;
        [Dependency] private readonly SharedActionsSystem _action = default!;
        [Dependency] private readonly SharedDoAfterSystem _doAfterSystem = default!;
        [Dependency] private readonly PricingSystem _pricing = default!;
        [Dependency] private readonly ThrowingSystem _throwingSystem = default!;
        [Dependency] private readonly UserInterfaceSystem _userInterfaceSystem = default!;
        [Dependency] private readonly ContainerSystem _container = default!;

        private ISawmill _sawmill = default!;

        public override void Initialize()
        {
            base.Initialize();

            _sawmill = Logger.GetSawmill("vending");
            SubscribeLocalEvent<SmartFridgeComponent, PowerChangedEvent>(OnPowerChanged);
            SubscribeLocalEvent<SmartFridgeComponent, BreakageEventArgs>(OnBreak);
            // SubscribeLocalEvent<SmartFridgeComponent, GotEmaggedEvent>(OnEmagged);
            SubscribeLocalEvent<SmartFridgeComponent, DamageChangedEvent>(OnDamage);
            SubscribeLocalEvent<SmartFridgeComponent, PriceCalculationEvent>(OnVendingPrice);

            SubscribeLocalEvent<SmartFridgeComponent, ActivatableUIOpenAttemptEvent>(OnActivatableUIOpenAttempt);
            SubscribeLocalEvent<SmartFridgeComponent, BoundUIOpenedEvent>(OnBoundUIOpened);
            SubscribeLocalEvent<SmartFridgeComponent, SmartFridgeEjectMessage>(OnInventoryEjectMessage);

            SubscribeLocalEvent<SmartFridgeComponent, SmartFridgeSelfDispenseEvent>(OnSelfDispense);

            SubscribeLocalEvent<SmartFridgeComponent, InteractUsingEvent>(OnInteractUsing);
            SubscribeLocalEvent<SmartFridgeComponent, SmartFridgeRestockDoAfterEvent>(OnDoAfter);
        }

        private void OnVendingPrice(EntityUid uid, SmartFridgeComponent component, ref PriceCalculationEvent args)
        {
            var price = 0.0;
            foreach (var entry in component.Groups)
            {
                price += entry.Amount * _pricing.GetEstimatedPrice(Prototype(entry.IDs[0])!);
            }
            args.Price += price;
        }

        protected override void OnComponentInit(EntityUid uid, SmartFridgeComponent component, ComponentInit args)
        {
            base.OnComponentInit(uid, component, args);

            if (HasComp<ApcPowerReceiverComponent>(component.Owner))
            {
                TryUpdateVisualState(uid, component);
            }

            if (component.Action != null)
            {
                var action = new InstantAction(_prototypeManager.Index<InstantActionPrototype>(component.Action));
                _action.AddAction(uid, action, uid);
            }
            component.Storage = _container.EnsureContainer<Container>(uid,"smartfridge_inventory_container");
            foreach(var entity in component.Storage.ContainedEntities)
            component.Contained.Add(entity);
        }

        private void OnActivatableUIOpenAttempt(EntityUid uid, SmartFridgeComponent component, ActivatableUIOpenAttemptEvent args)
        {
            if (component.Broken)
                args.Cancel();
        }

        private void OnInteractUsing(EntityUid uid, SmartFridgeComponent component, InteractUsingEvent args)
        {
            if (args.Handled)
                return;
            if (!TryComp(args.Used, out ToolComponent? tool) && !component.Broken && this.IsPowered(uid, EntityManager))
            {
                if ((TryComp(args.Used, out ServerStorageComponent? storage) || storage?.Storage != null) && (component.ContainerWhitelist?.IsValid(args.Used, EntityManager) == true))
                {
                    if (IsAuthorized(uid, args.User, component))
                    {
                        var doAfterArgs = new DoAfterArgs(args.User,
                        TimeSpan.FromSeconds((double)(new decimal(0.15 * storage!.Storage!.ContainedEntities.Where(_ => component.Whitelist?.IsValid(_, EntityManager) == true).Count()))),
                        new SmartFridgeRestockDoAfterEvent(),
                        uid)
                        {
                            BreakOnTargetMove = true,
                            BreakOnUserMove = true,
                            BreakOnDamage = true,
                            NeedHand = true
                        };
                        if (!_doAfterSystem.TryStartDoAfter(doAfterArgs))
                            return;
                        _audioSystem.Play(component.SoundInsert, Filter.Pvs(uid, entityManager: EntityManager), uid, true, AudioParams.Default);
                        RestockInventory(uid, args.Used, Comp<ServerStorageComponent>(args.Used), component);
                        UpdateSmartFridgeInterfaceState(component);
                    }
                    else
                    {
                        Deny(uid, component);
                    }
                }
                else if (!(TryComp(args.Used, out ServerStorageComponent? storageComp)) && (component.Whitelist?.IsValid(args.Used, EntityManager) == true))
                {
                    if (IsAuthorized(uid, args.User, component))
                    {
                        _audioSystem.Play(component.SoundInsert, Filter.Pvs(uid, entityManager: EntityManager), uid, true, AudioParams.Default);
                        AddItemToInventory(uid, args.Used, component);
                        UpdateSmartFridgeInterfaceState(component);
                    }
                    else
                    {
                        Deny(uid, component);
                    }
                }
                else
                    return;
            }
        }

        private void OnBoundUIOpened(EntityUid uid, SmartFridgeComponent component, BoundUIOpenedEvent args)
        {
            UpdateSmartFridgeInterfaceState(component);
        }

        private void UpdateSmartFridgeInterfaceState(SmartFridgeComponent component)
        {
            var state = new SmartFridgeInterfaceState(GetGroups(component.Owner, component));
            _userInterfaceSystem.TrySetUiState(component.Owner, SmartFridgeUiKey.Key, state);
        }

        private void OnInventoryEjectMessage(EntityUid uid, SmartFridgeComponent component, SmartFridgeEjectMessage args)
        {
            if (!this.IsPowered(uid, EntityManager))
                return;

            if (args.Session.AttachedEntity is not { Valid: true } entity || Deleted(entity))
                return;

            AuthorizedVend(uid, entity, args.ID, component);
        }

        private void OnPowerChanged(EntityUid uid, SmartFridgeComponent component, ref PowerChangedEvent args)
        {
            TryUpdateVisualState(uid, component);
        }

        private void OnBreak(EntityUid uid, SmartFridgeComponent fridgeComponent, BreakageEventArgs eventArgs)
        {
            fridgeComponent.Broken = true;
            TryUpdateVisualState(uid, fridgeComponent);
        }

        private void OnDamage(EntityUid uid, SmartFridgeComponent component, DamageChangedEvent args)
        {
            if (component.Broken || component.DispenseOnHitCoolingDown ||
                component.DispenseOnHitChance == null || args.DamageDelta == null)
                return;

            if (args.DamageIncreased && args.DamageDelta.Total >= component.DispenseOnHitThreshold &&
                _random.Prob(component.DispenseOnHitChance.Value))
            {
                if (component.DispenseOnHitCooldown > 0f)
                    component.DispenseOnHitCoolingDown = true;
                EjectRandom(uid, throwItem: true, forceEject: true, component);
            }
        }

        private void OnSelfDispense(EntityUid uid, SmartFridgeComponent component, SmartFridgeSelfDispenseEvent args)
        {
            if (args.Handled)
                return;

            args.Handled = true;
            EjectRandom(uid, throwItem: true, forceEject: false, component);
        }


        private void RestockInventory(EntityUid uid, EntityUid performer, ServerStorageComponent storage, SmartFridgeComponent fridgeComponent)
        {
            var stored = storage.StoredEntities?.ToList();
            List<SmartFridgeInventoryGroup>? toRestock = new();

            if (stored != null)
            {
                fridgeComponent.Contained.AddRange(stored);
                foreach (var thing in stored!)
                {
                    var thingProto = Prototype(thing);
                    if (fridgeComponent.Whitelist?.IsValid(thing, EntityManager) == true)
                    {
                    List<EntityUid> tmpList = new(); // I love lists in C#
                    tmpList.Add(thing); // but there definetly must be more elegant way to do that, right?
                    var item = new SmartFridgeInventoryGroup(tmpList, MetaData(thing).EntityName, thingProto!.ID, 1);
                    toRestock.Add(item);
                    fridgeComponent.Storage?.Insert(thing);
                    Console.Write(thing + "\n"); // Was used for tests, need to remove later
                    }
                }
            }
            fridgeComponent.Groups.AddRange(toRestock);
            var sortedEntries = GroupEntries(fridgeComponent.Groups, fridgeComponent);
            fridgeComponent.Groups = sortedEntries.OrderBy(_ => _.Name).ToList<SmartFridgeInventoryGroup>();
            return;
        }
        private void AddItemToInventory(EntityUid uid, EntityUid itemID, SmartFridgeComponent fridgeComponent)
        {
            fridgeComponent.Contained.Add(itemID);
            var thingProto = Prototype(itemID);
            if (fridgeComponent.Whitelist?.IsValid(itemID, EntityManager) == true)
            {
                List<EntityUid> tmpList = new(); // This list is there because of other function that DEFINETLY should not be touched
                tmpList.Add(itemID); // (but there still must be more elegant way to do that, right?)
                var item = new SmartFridgeInventoryGroup(tmpList, MetaData(itemID).EntityName, thingProto!.ID, 1);
                fridgeComponent.Groups.Add(item);
                fridgeComponent.Storage?.Insert(itemID);
                Console.Write(itemID + "\n"); // Was used for tests, need to remove later
            }
            var sortedEntries = GroupEntries(fridgeComponent.Groups, fridgeComponent);
            fridgeComponent.Groups = sortedEntries.OrderBy(_ => _.Name).ToList<SmartFridgeInventoryGroup>();
        }

        private void OnDoAfter(EntityUid uid, SmartFridgeComponent component, DoAfterEvent args)
        {
            // This function exists because i don't know how to get all arguments for using RestockInventory in here
            if (args.Handled || args.Cancelled)
                return;
            args.Handled = true;
        }


        /// <summary>
        /// Sets the <see cref="SmartFridgeComponent.CanShoot"/> property of the vending machine.
        /// </summary>
        public void SetShooting(EntityUid uid, bool canShoot, SmartFridgeComponent? component = null)
        {
            if (!Resolve(uid, ref component))
                return;

            component.CanShoot = canShoot;
        }

        public void Deny(EntityUid uid, SmartFridgeComponent? fridgeComponent = null)
        {
            if (!Resolve(uid, ref fridgeComponent))
                return;

            if (fridgeComponent.Denying)
                return;

            fridgeComponent.Denying = true;
            _audioSystem.PlayPvs(fridgeComponent.SoundDeny, fridgeComponent.Owner, AudioParams.Default.WithVolume(-2f));
            TryUpdateVisualState(uid, fridgeComponent);
        }

        /// <summary>
        /// Checks if the user is authorized to use this vending machine
        /// </summary>
        /// <param name="sender">Entity trying to use the vending machine</param>
        public bool IsAuthorized(EntityUid uid, EntityUid? sender, SmartFridgeComponent? fridgeComponent = null)
        {
            if (!Resolve(uid, ref fridgeComponent) || sender == null)
                return false;

            if (TryComp<AccessReaderComponent?>(fridgeComponent.Owner, out var accessReader))
            {
                if (!_accessReader.IsAllowed(sender.Value, accessReader) && !HasComp<EmaggedComponent>(uid))
                {
                    _popupSystem.PopupEntity(Loc.GetString("vending-machine-component-try-eject-access-denied"), uid);
                    Deny(uid, fridgeComponent);
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Tries to eject the provided item. Will do nothing if the vending machine is incapable of ejecting, already ejecting
        /// or the item doesn't exist in its inventory.
        /// </summary>
        /// <param name="type">The type of inventory the item is from</param>
        /// <param name="itemId">The prototype ID of the item</param>
        /// <param name="throwItem">Whether the item should be thrown in a random direction after ejection</param>
        public void TryEjectVendorItem(EntityUid uid, EntityUid itemID, bool throwItem, SmartFridgeComponent fridgeComponent)
        {
            if (!Resolve(uid, ref fridgeComponent!))
                return;

            if (fridgeComponent.Ejecting || fridgeComponent.Broken || !this.IsPowered(uid, EntityManager))
            {
                return;
            }

            var entry = GetGroupByID(itemID, fridgeComponent);

            if (entry == null)
            {
                _popupSystem.PopupEntity(Loc.GetString("vending-machine-component-try-eject-invalid-item"), uid);
                Deny(uid, fridgeComponent);
                return;
            }

            if (fridgeComponent.Groups.Where(_ => _ == entry).Count() <= 0)
            {
                _popupSystem.PopupEntity(Loc.GetString("vending-machine-component-try-eject-out-of-stock"), uid);
                Deny(uid, fridgeComponent);
                return;
            }

            if (!TryComp<TransformComponent>(fridgeComponent.Owner, out var transformComp))
                return;

            // Start Ejecting, and prevent users from ordering while anim playing
            fridgeComponent.Ejecting = true;
            fridgeComponent.NextItemToEject = entry.IDs[0];
            fridgeComponent.ThrowNextItem = throwItem;
            entry.Amount--;
            if (entry.Amount <= 0)
            {
                DeleteGroup(entry, fridgeComponent);
            }
            else
            {
                entry.IDs.RemoveAt(0);
                fridgeComponent.Contained.RemoveAt(0);
            }
            TryUpdateVisualState(uid, fridgeComponent);
            UpdateSmartFridgeInterfaceState(fridgeComponent);
            _audioSystem.PlayPvs(fridgeComponent.SoundVend, fridgeComponent.Owner, AudioParams.Default.WithVolume(-2f));
        }

        /// <summary>
        /// Checks whether the user is authorized to use the vending machine, then ejects the provided item if true
        /// </summary>
        /// <param name="sender">Entity that is trying to use the vending machine</param>
        /// <param name="type">The type of inventory the item is from</param>
        /// <param name="itemId">The prototype ID of the item</param>
        public void AuthorizedVend(EntityUid uid, EntityUid sender, EntityUid itemId, SmartFridgeComponent component)
        {
            if (IsAuthorized(uid, sender, component))
            {
                TryEjectVendorItem(uid, itemId, component.CanShoot, component);
            }
        }

        /// <summary>
        /// Tries to update the visuals of the component based on its current state.
        /// </summary>
        public void TryUpdateVisualState(EntityUid uid, SmartFridgeComponent? fridgeComponent = null)
        {
            if (!Resolve(uid, ref fridgeComponent))
                return;

            var finalState = SmartFridgeVisualState.Normal;
            if (fridgeComponent.Broken)
            {
                finalState = SmartFridgeVisualState.Broken;
            }
            else if (fridgeComponent.Ejecting)
            {
                finalState = SmartFridgeVisualState.Eject;
            }
            else if (fridgeComponent.Denying)
            {
                finalState = SmartFridgeVisualState.Deny;
            }
            else if (!this.IsPowered(uid, EntityManager))
            {
                finalState = SmartFridgeVisualState.Off;
            }

            if (TryComp<AppearanceComponent>(fridgeComponent.Owner, out var appearance))
            {
                _appearanceSystem.SetData(uid, SmartFridgeVisuals.VisualState, finalState, appearance);
            }
        }

        /// <summary>
        /// Ejects a random item from the available stock. Will do nothing if the vending machine is empty.
        /// </summary>
        /// <param name="throwItem">Whether to throw the item in a random direction after dispensing it.</param>
        /// <param name="forceEject">Whether to skip the regular ejection checks and immediately dispense the item without animation.</param>
        public void EjectRandom(EntityUid uid, bool throwItem, bool forceEject = false, SmartFridgeComponent? fridgeComponent = null)
        {
            if (!Resolve(uid, ref fridgeComponent))
                return;

            var availableItems = GetAvailableInventory(uid, fridgeComponent);
            if (availableItems.Count() <= 0)
            {
                return;
            }

            var item = _random.Pick(availableItems);

            if (forceEject)
            {
                fridgeComponent.NextItemToEject = item.IDs[0];
                fridgeComponent.ThrowNextItem = throwItem;
                var entry = GetGroupByID(item.IDs[0], fridgeComponent);
                EjectItem(fridgeComponent, forceEject);
                if (entry != null)
                    DeleteGroup(entry, fridgeComponent);
            }
            else
                TryEjectVendorItem(uid, item.IDs[0], throwItem, fridgeComponent);
        }

        private void EjectItem(SmartFridgeComponent fridgeComponent, bool forceEject = false)
        {
            // No need to update the visual state because we never changed it during a forced eject
            if (!forceEject)
                TryUpdateVisualState(fridgeComponent.Owner, fridgeComponent);

            // if (fridgeComponent.NextItemToEject == null)
            // {
            //     fridgeComponent.ThrowNextItem = false;
            //     return;
            // }

            // var ent = EntityManager.SpawnEntity(fridgeComponent.NextItemToEject, Transform(fridgeComponent.Owner).Coordinates);

            fridgeComponent.Storage.Remove(
                fridgeComponent.NextItemToEject);
            var ent = fridgeComponent.NextItemToEject;

            if (fridgeComponent.ThrowNextItem && fridgeComponent.NextItemToEject != null)
            {
                var range = fridgeComponent.NonLimitedEjectRange;
                var direction = new Vector2(_random.NextFloat(-range, range), _random.NextFloat(-range, range));
                _throwingSystem.TryThrow(ent, direction, fridgeComponent.NonLimitedEjectForce);
            }

            fridgeComponent.ThrowNextItem = false;
        }

        private void DenyItem(SmartFridgeComponent fridgeComponent)
        {
            TryUpdateVisualState(fridgeComponent.Owner, fridgeComponent);
        }

        private SmartFridgeInventoryGroup? GetGroupByProto(EntityPrototype? entryProto, SmartFridgeComponent? component) // It should get first entry in inventory with given prototype and return its string id, which will be the selected item
        {
            foreach (var posentry in component!.Groups)
            {
                Console.WriteLine($"{posentry.ProtoID} and {entryProto?.ID}");
                if (posentry.ProtoID == entryProto?.ID)
                {
                return posentry;
                break;
                }
            }
            return null;
        }
        private SmartFridgeInventoryGroup? GetGroupByID(EntityUid entryID, SmartFridgeComponent? component) // It should get first entry in inventory with given prototype and return its string id, which will be the selected item
        {
            foreach (var posentry in component!.Groups)
            {
                if (posentry.IDs.Contains(entryID))
                {
                return posentry;
                break;
                }
            }
            return null;
        }
        private SmartFridgeInventoryGroup? GetGroupFromList(List<SmartFridgeInventoryGroup>? inventory, string protoID, SmartFridgeComponent? component) // It should get first entry in inventory with given prototype and return its string id, which will be the selected item
        {
            foreach (var posentry in inventory!)
            {
                if (posentry.ProtoID == protoID)
                {
                return posentry;
                break;
                }
            }
            return null;
        }
        /// <summary>
        ///     __Completly__ delete item from fridge inventory
        ///     **WARNING!** IT WILL DELETE ENTIRE ITEM ENTRY, WHICH MEANS
        ///     IT WILL ALSO DELETE **ALL** ITEMS THAT ENTRY WAS CARRYiNG
        /// </summary>
        private bool DeleteGroup(SmartFridgeInventoryGroup item, SmartFridgeComponent component) // It should get and delete entity from inventory (after it dispenses). Returns true if deleted and false if not (just in case)
        {
            foreach (var posentry in component.Groups)
            {
                if (item.IDs.Contains(posentry.IDs[0]))
                {
                    component.Groups.Remove(posentry);
                    return true;
                    break;
                }
            }
            return false;
        }
        private List<SmartFridgeInventoryGroup> GroupEntries(List<SmartFridgeInventoryGroup> inventory, SmartFridgeComponent component)
        {
            Dictionary<string, SmartFridgeInventoryGroup> buff = new();
            List<SmartFridgeInventoryGroup> finalList = new();
            foreach (var item in inventory)
            {
                if (!buff.ContainsKey(item.Name))
                {
                    buff.Add(item.Name, item);
                }
                else
                {
                    buff[item.Name].IDs.AddRange(item.IDs);
                    buff[item.Name].Amount++;
                }
            }
            foreach (var item in buff.Values)
            {
                finalList.Add(item);
            }
            return finalList;
        }
        private bool GroupExists(EntityPrototype? entryProto, SmartFridgeComponent? component)
        {
            foreach (var posentry in component!.Groups)
            {
                if (posentry.ProtoID == entryProto?.ID)
                {
                return true;
                break;
                }
            }
            return false;
        }

        public override void Update(float frameTime)
        {
            base.Update(frameTime);

            foreach (var comp in EntityQuery<SmartFridgeComponent>())
            {
                if (comp.Ejecting)
                {
                    comp.EjectAccumulator += frameTime;
                    if (comp.EjectAccumulator >= comp.EjectDelay)
                    {
                        comp.EjectAccumulator = 0f;
                        comp.Ejecting = false;

                        EjectItem(comp);
                    }
                }

                if (comp.Denying)
                {
                    comp.DenyAccumulator += frameTime;
                    if (comp.DenyAccumulator >= comp.DenyDelay)
                    {
                        comp.DenyAccumulator = 0f;
                        comp.Denying = false;

                        DenyItem(comp);
                    }
                }

                if (comp.DispenseOnHitCoolingDown)
                {
                    comp.DispenseOnHitAccumulator += frameTime;
                    if (comp.DispenseOnHitAccumulator >= comp.DispenseOnHitCooldown)
                    {
                        comp.DispenseOnHitAccumulator = 0f;
                        comp.DispenseOnHitCoolingDown = false;
                    }
                }
            }
        }
    }
}
