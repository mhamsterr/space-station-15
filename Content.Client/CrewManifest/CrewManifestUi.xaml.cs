using System.Linq;
using System.Numerics;
using Content.Shared.CCVar;
using Content.Shared.CrewManifest;
using Content.Shared.Roles;
using Content.Shared.StatusIcon;
using Robust.Client.AutoGenerated;
using Robust.Client.GameObjects;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Client.UserInterface.XAML;
using Robust.Client.Utility;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Client.CrewManifest;

[GenerateTypedNameReferences]
public sealed partial class CrewManifestUi : DefaultWindow
{
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly IResourceCache _resourceCache = default!;
    [Dependency] private readonly IEntitySystemManager _entitySystemManager = default!;
    [Dependency] private readonly IConfigurationManager _configManager = default!;

    private readonly CrewManifestSystem _crewManifestSystem;

    public CrewManifestUi()
    {
        RobustXamlLoader.Load(this);
        IoCManager.InjectDependencies(this);
        _crewManifestSystem = _entitySystemManager.GetEntitySystem<CrewManifestSystem>();

        StationName.AddStyleClass("LabelBig");
    }

    public void Populate(string name, CrewManifestEntries? entries)
    {
        CrewManifestListing.DisposeAllChildren();
        CrewManifestListing.RemoveAllChildren();

        StationNameContainer.Visible = entries != null;
        StationName.Text = name;

        if (entries == null) return;

       var entryList = SortEntries(entries);

        foreach (var item in entryList)
        {
            CrewManifestListing.AddChild(new CrewManifestSection(item.section, item.entries, _resourceCache, _crewManifestSystem));
        }
    }

    private List<(string section, List<CrewManifestEntry> entries)> SortEntries(CrewManifestEntries entries)
    {
        var entryDict = new Dictionary<string, List<CrewManifestEntry>>();

        foreach (var entry in entries.Entries)
        {
            foreach (var department in _prototypeManager.EnumeratePrototypes<DepartmentPrototype>())
            {
                // this is a little expensive, and could be better
                if (department.Roles.Contains(entry.JobPrototype))
                {
                    entryDict.GetOrNew(department.ID).Add(entry);
                }
            }
        }

        var entryList = new List<(string section, List<CrewManifestEntry> entries)>();

        foreach (var (section, listing) in entryDict)
        {
            entryList.Add((section, listing));
        }

        var sortOrder = _configManager.GetCVar(CCVars.CrewManifestOrdering).Split(",").ToList();

        entryList.Sort((a, b) =>
        {
            var ai = sortOrder.IndexOf(a.section);
            var bi = sortOrder.IndexOf(b.section);

            // this is up here so -1 == -1 occurs first
            if (ai == bi)
                return 0;

            if (ai == -1)
                return -1;

            if (bi == -1)
                return 1;

            return ai.CompareTo(bi);
        });

        return entryList;
    }

    private sealed class CrewManifestSection : BoxContainer
    {
        [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
        [Dependency] private readonly IEntitySystemManager _entitySystem = default!;
        private readonly SpriteSystem _spriteSystem = default!;

        public CrewManifestSection(string sectionTitle, List<CrewManifestEntry> entries, IResourceCache cache, CrewManifestSystem crewManifestSystem)
        {
            IoCManager.InjectDependencies(this);
            _spriteSystem = _entitySystem.GetEntitySystem<SpriteSystem>();

            Orientation = LayoutOrientation.Vertical;
            HorizontalExpand = true;

            if (Loc.TryGetString($"department-{sectionTitle}", out var localizedDepart))
                sectionTitle = localizedDepart;

            AddChild(new Label()
            {
                StyleClasses = { "LabelBig" },
                Text = Loc.GetString(sectionTitle)
            });

            var gridContainer = new GridContainer()
            {
                HorizontalExpand = true,
                Columns = 2
            };

            AddChild(gridContainer);

            foreach (var entry in entries)
            {
                var name = new RichTextLabel()
                {
                    HorizontalExpand = true,
                };
                name.SetMessage(entry.Name);

                var titleContainer = new BoxContainer()
                {
                    Orientation = LayoutOrientation.Horizontal,
                    HorizontalExpand = true
                };

                var title = new RichTextLabel();
                title.SetMessage(entry.JobTitle);


                if (_prototypeManager.TryIndex<StatusIconPrototype>(entry.JobIcon, out var jobIcon))
                {
                    var icon = new TextureRect()
                    {
                        TextureScale = new Vector2(2, 2),
                        Stretch = TextureRect.StretchMode.KeepCentered,
                        Texture = _spriteSystem.Frame0(jobIcon.Icon),
                    };

                    titleContainer.AddChild(icon);
                    titleContainer.AddChild(title);
                }
                else
                {
                    titleContainer.AddChild(title);
                }

                gridContainer.AddChild(name);
                gridContainer.AddChild(titleContainer);
            }
        }
    }
}
