using Content.Client.SmartFridge.UI;
using Content.Shared.SmartFridge;
using Robust.Client.GameObjects;
using Robust.Client.UserInterface.Controls;
using System.Linq;

namespace Content.Client.SmartFridge
{
    public sealed class SmartFridgeBoundUserInterface : BoundUserInterface
    {
        [ViewVariables]
        private SmartFridgeMenu? _menu;

        private List<SmartFridgeInventoryGroup> _cachedInventory = new();

        public SmartFridgeBoundUserInterface(ClientUserInterfaceComponent owner, Enum uiKey) : base(owner, uiKey)
        {
        }

        protected override void Open()
        {
            base.Open();

            var entMan = IoCManager.Resolve<IEntityManager>();
            var SmartFridgeSys = entMan.System<SmartFridgeSystem>();

            _cachedInventory = SmartFridgeSys.GetGroups(Owner.Owner);

            _menu = new SmartFridgeMenu {Title = entMan.GetComponent<MetaDataComponent>(Owner.Owner).EntityName};

            _menu.OnClose += Close;
            _menu.OnItemSelected += OnItemSelected;

            _menu.Populate(_cachedInventory);

            _menu.OpenCentered();
        }

        protected override void UpdateState(BoundUserInterfaceState state)
        {
            base.UpdateState(state);

            if (state is not SmartFridgeInterfaceState newState)
                return;

            _cachedInventory = newState.Inventory;

            _menu?.Populate(_cachedInventory);
        }

        private void OnItemSelected(ItemList.ItemListSelectedEventArgs args)
        {
            if (_cachedInventory.Count == 0)
                return;

            var selectedItem = _cachedInventory.ElementAtOrDefault(args.ItemIndex);

            if (selectedItem == null)
                return;

            SendMessage(new SmartFridgeEjectMessage(selectedItem.IDs[0]));
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (!disposing)
                return;

            if (_menu == null)
                return;

            _menu.OnItemSelected -= OnItemSelected;
            _menu.OnClose -= Close;
            _menu.Dispose();
        }
    }
}
