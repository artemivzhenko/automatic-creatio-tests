using CreatioAutoTests.Configuration;
using CreatioAutoTests.Frontend;
using CreatioAutoTests.Frontend.Classic;
using CreatioAutoTests.Frontend.Registry;
using CreatioAutoTests.Frontend.Runtime;
using CreatioAutoTests.Tests.Support;

namespace CreatioAutoTests.Tests;

[TestFixture]
public sealed class ClassicRegistrationTests
{
    private readonly UiElementRegistry _registry = UiElementRegistry.Discover();

    /// <summary>
    /// The same type name lives once per generation. A test author writes "Text" on either kind of
    /// page and the page's generation picks the class.
    /// </summary>
    [TestCase("Text", typeof(CreatioAutoTests.Frontend.Fields.TextField), typeof(CreatioAutoTests.Frontend.Classic.Fields.TextField))]
    [TestCase("Lookup", typeof(CreatioAutoTests.Frontend.Fields.LookupField), typeof(CreatioAutoTests.Frontend.Classic.Fields.LookupField))]
    [TestCase("Button", typeof(CreatioAutoTests.Frontend.Controls.Button), typeof(CreatioAutoTests.Frontend.Classic.Controls.Button))]
    [TestCase("Tab", typeof(CreatioAutoTests.Frontend.Controls.Tab), typeof(CreatioAutoTests.Frontend.Classic.Controls.Tab))]
    public void A_type_name_resolves_to_a_different_class_per_generation(string typeName, Type freedom, Type classic)
    {
        Assert.Multiple(() =>
        {
            Assert.That(_registry.Resolve(typeName).ImplementationType, Is.EqualTo(freedom), "default resolution stays Freedom UI");
            Assert.That(_registry.Resolve(UiGeneration.FreedomUI, typeName).ImplementationType, Is.EqualTo(freedom));
            Assert.That(_registry.Resolve(UiGeneration.ClassicUI, typeName).ImplementationType, Is.EqualTo(classic));
        });
    }

    [TestCase("Text", ValueKind.Text, "TextEdit")]
    [TestCase("RichText", ValueKind.Text, "MemoEdit")]
    [TestCase("Integer", ValueKind.Number, "IntegerEdit")]
    [TestCase("Decimal", ValueKind.Number, "FloatEdit", "MoneyEdit")]
    [TestCase("Boolean", ValueKind.Boolean, "CheckBoxEdit")]
    [TestCase("Date", ValueKind.DateTime, "DateEdit")]
    [TestCase("Time", ValueKind.DateTime, "TimeEdit")]
    [TestCase("DateTime", ValueKind.DateTime, "DateEdit", "TimeEdit")]
    [TestCase("Lookup", ValueKind.Lookup, "LookupEdit", "ComboBoxEdit")]
    public void Classic_field_types_declare_their_value_kind_and_the_controls_that_render_them(
        string typeName, ValueKind kind, params string[] controls)
    {
        var registration = _registry.Resolve(UiGeneration.ClassicUI, typeName);

        Assert.Multiple(() =>
        {
            Assert.That(registration.Generation, Is.EqualTo(UiGeneration.ClassicUI));
            Assert.That(registration.ValueKind, Is.EqualTo(kind));
            Assert.That(registration.Controls, Is.EqualTo(controls));
            Assert.That(registration.HostTag, Is.Empty, "a Classic element has no host tag");
            Assert.That(registration.Capabilities, Does.Contain(typeof(IValueElement)));
            Assert.That(registration.Capabilities, Does.Contain(typeof(IStateAware)));
        });
    }

    [TestCase("CasesBar", ValueKind.Stage, typeof(IValueElement))]
    [TestCase("TabPanel", ValueKind.Tab, typeof(IValueElement))]
    [TestCase("Detail", ValueKind.Number, typeof(IColumned))]
    [TestCase("Detail", ValueKind.Number, typeof(IEditableAware))]
    [TestCase("Detail", ValueKind.Number, typeof(IValueElement))]
    [TestCase("Button", ValueKind.None, typeof(IClickable))]
    [TestCase("Tab", ValueKind.None, typeof(IClickable))]
    public void Classic_composite_types_declare_their_value_kind_and_capabilities(string typeName, ValueKind kind, Type capability)
    {
        var registration = _registry.Resolve(UiGeneration.ClassicUI, typeName);

        Assert.Multiple(() =>
        {
            Assert.That(registration.ValueKind, Is.EqualTo(kind));
            Assert.That(registration.Capabilities, Does.Contain(capability));
            Assert.That(registration.Capabilities, Does.Not.Contain(typeof(IStateAware)), "no required/readonly state on a composite");
        });
    }

    /// <summary>A detail's value is its row count, so 'clear' must not be offered: the catalog says so through the capability.</summary>
    [Test]
    public void A_detail_cannot_be_cleared()
    {
        Assert.That(_registry.Resolve(UiGeneration.ClassicUI, "Detail").Capabilities, Does.Not.Contain(typeof(IClearable)));
    }

    [Test]
    public void A_Freedom_only_type_is_reported_as_such_for_a_Classic_page()
    {
        var error = Assert.Throws<InvalidOperationException>(() => _registry.Resolve(UiGeneration.ClassicUI, "Email"))!;

        Assert.Multiple(() =>
        {
            Assert.That(error.Message, Does.Contain("Email"));
            Assert.That(error.Message, Does.Contain("FreedomUI pages only"));
            Assert.That(error.Message, Does.Contain("Lookup"), "the Classic names are listed");
        });
    }

    [Test]
    public void The_same_name_twice_within_one_generation_is_still_rejected()
    {
        var duplicate = new[]
        {
            new UiElementRegistration("Text", typeof(CreatioAutoTests.Frontend.Classic.Fields.TextField), ValueKind.Text, string.Empty, null, false, UiGeneration.ClassicUI, new[] { "TextEdit" }),
            new UiElementRegistration("Text", typeof(CreatioAutoTests.Frontend.Classic.Fields.RichTextField), ValueKind.Text, string.Empty, null, false, UiGeneration.ClassicUI, new[] { "MemoEdit" })
        };

        var error = Assert.Throws<InvalidOperationException>(() => _ = new UiElementRegistry(duplicate))!;
        Assert.That(error.Message, Does.Contain("ClassicUI"));
    }

    [Test]
    public void Every_Classic_element_declares_the_controls_it_renders_as()
    {
        Assert.That(
            _registry.RegistrationsOf(UiGeneration.ClassicUI).Where(r => r.Controls.Count == 0).Select(r => r.TypeName),
            Is.Empty,
            "a Classic element without Controls cannot build its DOM id");
    }
}

[TestFixture]
public sealed class ClassicCatalogTests
{
    private static PageDefinition ClassicPage(string? schema = "ContactPageV2", params ElementDefinition[] elements) => new()
    {
        Name = "ContactClassic",
        Url = "/0/Shell/#CardModuleV2/ContactPageV2",
        Type = "ClassicUI",
        Schema = schema ?? string.Empty,
        SourceFile = "ContactClassic.json",
        Elements = elements.ToList(),
    };

    [Test]
    public void A_Classic_page_resolves_its_elements_in_the_Classic_registry()
    {
        var page = ClassicPage(elements: new[]
        {
            CatalogBuilder.Element("Name", "Text", "ContactClassic"),
            CatalogBuilder.Element("Account", "Lookup", "ContactClassic"),
            CatalogBuilder.Element("CloseButton", "Button", "ContactClassic"),
        });

        var catalog = CatalogBuilder.BuildPages(page);

        Assert.Multiple(() =>
        {
            Assert.That(catalog.TryGetElement("ContactClassic", "Name", out var name), Is.True);
            Assert.That(name.ElementType, Is.EqualTo(typeof(CreatioAutoTests.Frontend.Classic.Fields.TextField)));
            Assert.That(name.ValueKind, Is.EqualTo(ValueKind.Text));
            Assert.That(name.Supports(typeof(IValueElement)), Is.True);
            Assert.That(catalog.TryGetElement("ContactClassic", "CloseButton", out var button), Is.True);
            Assert.That(button.Supports(typeof(IClickable)), Is.True);
        });
    }

    [Test]
    public void A_Classic_page_without_a_schema_is_rejected_with_an_example()
    {
        var page = ClassicPage(schema: null, CatalogBuilder.Element("Name", "Text", "ContactClassic"));

        var error = Assert.Throws<InvalidOperationException>(() => CatalogBuilder.BuildPages(page))!;

        Assert.Multiple(() =>
        {
            Assert.That(error.Message, Does.Contain("ClassicUI"));
            Assert.That(error.Message, Does.Contain("schema"));
            Assert.That(error.Message, Does.Contain("ContactPageV2"));
        });
    }

    [Test]
    public void A_Freedom_only_type_on_a_Classic_page_names_the_mistake()
    {
        var page = ClassicPage(elements: new[] { CatalogBuilder.Element("Email", "Email", "ContactClassic") });

        var error = Assert.Throws<InvalidOperationException>(() => CatalogBuilder.BuildPages(page))!;

        Assert.Multiple(() =>
        {
            Assert.That(error.Message, Does.Contain("'Email'"));
            Assert.That(error.Message, Does.Contain("FreedomUI pages only"));
        });
    }

    [Test]
    public void An_unknown_page_type_is_rejected_with_the_known_ones()
    {
        var page = ClassicPage();
        page.Type = "MobileUI";

        var error = Assert.Throws<InvalidOperationException>(() => CatalogBuilder.BuildPages(page))!;

        Assert.Multiple(() =>
        {
            Assert.That(error.Message, Does.Contain("MobileUI"));
            Assert.That(error.Message, Does.Contain("FreedomUI, ClassicUI"));
        });
    }

    [Test]
    public void Classic_elements_accept_the_schema_name_as_an_option()
    {
        var element = CatalogBuilder.Element("QSTypeCargo", "Lookup", "ContactClassic");
        element.Extra = new Dictionary<string, System.Text.Json.JsonElement>
        {
            ["name"] = System.Text.Json.JsonDocument.Parse("\"LOOKUPceebb14a-c7fc-443b-a434-6d47069519d4\"").RootElement,
        };

        var catalog = CatalogBuilder.BuildPages(ClassicPage(elements: new[] { element }));

        Assert.That(catalog.TryGetElement("ContactClassic", "QSTypeCargo", out var entry), Is.True);
        Assert.That(entry.Definition.BindOptions<ClassicElementOptions>().Name, Is.EqualTo("LOOKUPceebb14a-c7fc-443b-a434-6d47069519d4"));
    }

    [Test]
    public void A_Freedom_page_still_rejects_an_unknown_member_a_Classic_element_would_take()
    {
        var element = CatalogBuilder.Element("Name", "Text");
        element.Extra = new Dictionary<string, System.Text.Json.JsonElement>
        {
            ["name"] = System.Text.Json.JsonDocument.Parse("\"UsrName\"").RootElement,
        };

        Assert.Throws<InvalidOperationException>(() => CatalogBuilder.Build(element));
    }
}

[TestFixture]
public sealed class ClassicIdTests
{
    [Test]
    public void Ids_follow_schema_name_control_and_suffix()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ClassicIds.Component("ContactPageV2", "Name", "TextEdit"), Is.EqualTo("ContactPageV2NameTextEdit"));
            Assert.That(ClassicIds.Element("ContactPageV2", "Name", "TextEdit"), Is.EqualTo("ContactPageV2NameTextEdit-el"));
            Assert.That(ClassicIds.Wrap("ContactPageV2", "Name", "TextEdit"), Is.EqualTo("ContactPageV2NameTextEdit-wrap"));
            Assert.That(ClassicIds.Label("ContactPageV2", "Name"), Is.EqualTo("ContactPageV2NameLabel"));
            Assert.That(ClassicIds.ButtonText("ContactPageV2", "Close"), Is.EqualTo("ContactPageV2CloseButton-textEl"));
            Assert.That(ClassicIds.ButtonWrapper("UsrDetail", "AddRecord"), Is.EqualTo("UsrDetailAddRecordButton-wrapperEl"));
            Assert.That(ClassicIds.TabItems("ContactPageV2", "Tabs"), Is.EqualTo("ContactPageV2TabsTabPanel-tabpanel-items"));
        });
    }

    /// <summary>Everything inside a detail is prefixed by the detail schema, never by the page.</summary>
    [Test]
    public void Detail_ids_are_prefixed_by_the_detail_schema()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ClassicIds.DetailContainer("UsrOrderProductDetail"), Is.EqualTo("UsrOrderProductDetailContainer"));
            Assert.That(ClassicIds.DetailCaption("UsrOrderProductDetail"), Is.EqualTo("UsrOrderProductDetailDetailControlGroup-caption"));
            Assert.That(ClassicIds.DetailGrid("UsrOrderProductDetail"), Is.EqualTo("grid-UsrOrderProductDetailDataGridGrid-wrap"));
            Assert.That(ClassicIds.DetailGridComponent("UsrOrderProductDetail"), Is.EqualTo("UsrOrderProductDetailDataGridGrid"));
        });
    }

    /// <summary>Wizard names carry GUIDs, so the selector has to be the attribute form, not #id.</summary>
    [Test]
    public void The_selector_is_an_attribute_match_that_needs_no_escaping()
    {
        var id = ClassicIds.Element("QSOrder1Page", "LOOKUPceebb14a-c7fc-443b-a434-6d47069519d4", "ComboBoxEdit");

        Assert.That(ClassicIds.Selector(id), Is.EqualTo("[id=\"QSOrder1PageLOOKUPceebb14a-c7fc-443b-a434-6d47069519d4ComboBoxEdit-el\"]"));
    }

    [Test]
    public void Alternatives_are_braced_in_the_description()
    {
        Assert.That(ClassicIds.Describe("P", "N", new[] { "LookupEdit", "ComboBoxEdit" }), Is.EqualTo("PN{LookupEdit|ComboBoxEdit}-el"));
        Assert.That(ClassicIds.Describe("P", "N", new[] { "TextEdit" }), Is.EqualTo("PNTextEdit-el"));
    }
}

[TestFixture]
public sealed class PageRuntimeTests
{
    [TestCase("FreedomUI", UiGeneration.FreedomUI)]
    [TestCase("classicui", UiGeneration.ClassicUI)]
    [TestCase("", UiGeneration.FreedomUI)]
    [TestCase(null, UiGeneration.FreedomUI)]
    public void Page_types_parse_case_insensitively_and_default_to_Freedom(string? text, UiGeneration expected)
    {
        Assert.That(UiGenerations.TryParse(text, out var generation), Is.True);
        Assert.That(generation, Is.EqualTo(expected));
    }

    [Test]
    public void An_unknown_page_type_does_not_parse()
    {
        Assert.That(UiGenerations.TryParse("Mobile", out _), Is.False);
    }

    [TestCase("/0/Shell/#CardModuleV2/QSOrder1Page/edit/1", UiGeneration.ClassicUI)]
    [TestCase("/0/Shell/#SectionModuleV2/ContactSectionV2", UiGeneration.ClassicUI)]
    [TestCase("/0/Nui/ViewModule.aspx#CardModuleV2/ContactPageV2", UiGeneration.ClassicUI)]
    [TestCase("/0/Shell/#Card/Contacts_FormPage/edit/1", UiGeneration.FreedomUI)]
    public void A_raw_address_reveals_its_generation(string address, UiGeneration expected)
    {
        Assert.That(PageRuntimes.ForAddress(address).Generation, Is.EqualTo(expected));
    }

    [Test]
    public void Both_runtimes_build_record_addresses_the_same_way()
    {
        Assert.Multiple(() =>
        {
            Assert.That(PageRuntimes.For(UiGeneration.ClassicUI).BuildUrl("/0/Shell/#CardModuleV2/QSOrder1Page", "abc"),
                Is.EqualTo("/0/Shell/#CardModuleV2/QSOrder1Page/edit/abc"));
            Assert.That(PageRuntimes.For(UiGeneration.FreedomUI).BuildUrl("0/Shell/#Card/Contacts_FormPage", null),
                Is.EqualTo("/0/Shell/#Card/Contacts_FormPage"));
        });
    }
}

[TestFixture]
public sealed class UserAliasTests
{
    [Test]
    public void A_user_is_addressed_by_its_alias_when_it_has_one_and_by_its_login_otherwise()
    {
        var aliased = new UserCredentials { Username = "jane@example.com", Name = "Supervisor" };
        var plain = new UserCredentials { Username = "jane@example.com" };

        Assert.Multiple(() =>
        {
            Assert.That(aliased.DisplayName, Is.EqualTo("Supervisor"));
            Assert.That(aliased.Matches("supervisor"), Is.True);
            Assert.That(aliased.Matches("jane@example.com"), Is.True, "the login keeps working");
            Assert.That(plain.DisplayName, Is.EqualTo("jane@example.com"));
            Assert.That(plain.Matches("Supervisor"), Is.False);
        });
    }
}
