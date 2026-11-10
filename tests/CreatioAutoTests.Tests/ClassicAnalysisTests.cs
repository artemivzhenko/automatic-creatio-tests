using CreatioAutoTests.Analysis;
using CreatioAutoTests.Configuration;
using CreatioAutoTests.Frontend;
using CreatioAutoTests.Frontend.Classic;
using CreatioAutoTests.Frontend.Registry;
using CreatioAutoTests.Runner;
using Microsoft.Extensions.Logging.Abstractions;

namespace CreatioAutoTests.Tests;

[TestFixture]
public sealed class ClassicPagePathTests
{
    [Test]
    public void A_CardModuleV2_address_is_Classic_and_names_its_schema()
    {
        var path = new PagePath("https://x.creatio.com/0/Shell/#CardModuleV2/QSOrder1Page/edit/27bcc84f-727d-4314-8b0e-99b286253a27");

        Assert.Multiple(() =>
        {
            Assert.That(path.Generation, Is.EqualTo(UiGeneration.ClassicUI));
            Assert.That(path.Schema, Is.EqualTo("QSOrder1Page"));
            Assert.That(path.DefaultPageName, Is.EqualTo("QSOrder1Page"));
            Assert.That(path.RecordId, Is.EqualTo("27bcc84f-727d-4314-8b0e-99b286253a27"));
            Assert.That(path.Url, Is.EqualTo("/0/Shell/#CardModuleV2/QSOrder1Page"));
        });
    }

    [Test]
    public void A_Card_address_stays_Freedom()
    {
        var path = new PagePath("/0/Shell/#Card/Contacts_FormPage/edit/0001525b-d435-43bc-8507-90c28eb96353");

        Assert.That(path.Generation, Is.EqualTo(UiGeneration.FreedomUI));
        Assert.That(path.Schema, Is.EqualTo("Contacts_FormPage"));
    }
}

[TestFixture]
public sealed class ClassicTypeResolutionTests
{
    private static readonly IReadOnlyDictionary<string, string> Map = new Dictionary<string, string>
    {
        ["TextEdit"] = "Text", ["MemoEdit"] = "RichText", ["IntegerEdit"] = "Integer",
        ["FloatEdit"] = "Decimal", ["MoneyEdit"] = "Decimal", ["CheckBoxEdit"] = "Boolean",
        ["DateEdit"] = "Date", ["TimeEdit"] = "Time", ["LookupEdit"] = "Lookup", ["ComboBoxEdit"] = "Lookup",
    };

    /// <summary>
    /// A DateTime column renders two controls under one name; the column type, when known, is what
    /// decides, so that a date control alone on a DateTime column is still half of a DateTime.
    /// </summary>
    [TestCase(new[] { "DateEdit", "TimeEdit" }, 7, "DateTime")]
    [TestCase(new[] { "DateEdit" }, 7, "DateTime")]
    [TestCase(new[] { "DateEdit" }, 8, "Date")]
    [TestCase(new[] { "TimeEdit" }, 9, "Time")]
    [TestCase(new[] { "DateEdit", "TimeEdit" }, null, "DateTime")]
    [TestCase(new[] { "DateEdit" }, null, "Date")]
    [TestCase(new[] { "MoneyEdit" }, 6, "Decimal")]
    [TestCase(new[] { "ComboBoxEdit" }, 10, "Lookup")]
    [TestCase(new[] { "TextEdit" }, 1, "Text")]
    public void Controls_and_column_type_resolve_to_one_element_type(string[] controls, int? dataValueType, string expected)
    {
        Assert.That(ClassicPageAnalyzer.ResolveFieldType(controls, dataValueType, Map), Is.EqualTo(expected));
    }

    [Test]
    public void A_control_no_element_type_claims_is_unresolved()
    {
        Assert.That(ClassicPageAnalyzer.ResolveFieldType(new[] { "ImageEdit" }, 16, Map), Is.Null);
    }

    [Test]
    public void The_registry_supplies_the_same_mapping_the_analyser_uses()
    {
        // DateEdit is declared by both Date and DateTime; the type covering fewer controls wins,
        // which is the rule the analyser applies.
        var classic = UiElementRegistry.Discover().RegistrationsOf(UiGeneration.ClassicUI);
        var fromRegistry = new Dictionary<string, string>();
        foreach (var registration in classic
                     .Where(r => r.ValueKind is not (ValueKind.None or ValueKind.Tab or ValueKind.Stage) && r.TypeName != "Detail")
                     .OrderBy(r => r.Controls.Count).ThenBy(r => r.TypeName, StringComparer.Ordinal))
        {
            foreach (var control in registration.Controls)
            {
                fromRegistry.TryAdd(control, registration.TypeName);
            }
        }

        Assert.That(fromRegistry, Is.EquivalentTo(Map));
    }

    [Test]
    public void The_code_is_the_column_unless_taken_then_the_schema_name()
    {
        var taken = new HashSet<string>(StringComparer.Ordinal) { "QSName" };

        Assert.Multiple(() =>
        {
            Assert.That(ClassicPageAnalyzer.ChooseCode("QSTypeCargo", "LOOKUPceebb14a", taken), Is.EqualTo("QSTypeCargo"));
            Assert.That(ClassicPageAnalyzer.ChooseCode("QSName", "QSName823036e9", taken), Is.EqualTo("QSName823036e9"));
            Assert.That(ClassicPageAnalyzer.ChooseCode(null, "STRING25d6", taken), Is.EqualTo("STRING25d6"));
            Assert.That(ClassicPageAnalyzer.ChooseCode("", "DnRecommendedPriceSum", taken), Is.EqualTo("DnRecommendedPriceSum"));
        });
    }
}

[TestFixture]
public sealed class ClassicSnapshotCollectionTests
{
    private static readonly IReadOnlyDictionary<string, string> Map = new Dictionary<string, string>
    {
        ["TextEdit"] = "Text", ["DateEdit"] = "Date", ["TimeEdit"] = "Time", ["ComboBoxEdit"] = "Lookup", ["CheckBoxEdit"] = "Boolean",
    };

    private static System.Text.Json.JsonElement Snapshot(string controls, string buttons = "[]", string details = "[]", string tabs = "null", string stage = "null") =>
        System.Text.Json.JsonDocument.Parse(
            "{\"controls\":" + controls + ",\"buttons\":" + buttons + ",\"details\":" + details + ",\"tabs\":" + tabs + ",\"stage\":" + stage + ",\"unknown\":[]}").RootElement;

    private const string HeaderField =
        "{\"name\":\"LOOKUPceebb14a\",\"control\":\"ComboBoxEdit\",\"column\":\"QSTypeCargo\",\"dataValueType\":10,\"label\":\"Cargo\",\"required\":true,\"readonly\":false,\"visible\":true}";

    /// <summary>
    /// A header field is visible on every tab, so every pass meets it again. It must be recorded
    /// once, under its column, and never come back under its schema name as a second element.
    /// </summary>
    [Test]
    public void A_field_met_on_every_pass_is_recorded_once()
    {
        var collector = new ClassicPageAnalyzer.ClassicSnapshotCollector(Map, includeHidden: false);

        collector.Add(Snapshot("[" + HeaderField + "]"));
        collector.Add(Snapshot("[" + HeaderField + "]"));

        Assert.That(collector.Elements, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(collector.Elements[0].Code, Is.EqualTo("QSTypeCargo"));
            Assert.That(collector.Elements[0].Name, Is.EqualTo("LOOKUPceebb14a"));
            Assert.That(collector.Elements[0].Required, Is.True);
        });
    }

    [Test]
    public void A_second_element_on_the_same_column_keeps_its_schema_name_as_code()
    {
        var collector = new ClassicPageAnalyzer.ClassicSnapshotCollector(Map, includeHidden: false);
        const string onTab = "{\"name\":\"QSTypeCargo9bfbea03\",\"control\":\"ComboBoxEdit\",\"column\":\"QSTypeCargo\",\"dataValueType\":10,\"label\":\"Cargo\",\"required\":false,\"readonly\":true,\"visible\":true}";

        collector.Add(Snapshot("[" + HeaderField + "]"));
        collector.Add(Snapshot("[" + HeaderField + "," + onTab + "]"));

        Assert.That(collector.Elements.Select(e => e.Code), Is.EqualTo(new[] { "QSTypeCargo", "QSTypeCargo9bfbea03" }));
        Assert.That(collector.Elements[1].Name, Is.Null, "code and schema name coincide, so no name is written");
    }

    [Test]
    public void A_DateTime_column_folds_its_two_controls_into_one_element()
    {
        var collector = new ClassicPageAnalyzer.ClassicSnapshotCollector(Map, includeHidden: false);
        const string date = "{\"name\":\"DATETIME7fa6\",\"control\":\"DateEdit\",\"column\":\"QSOrderDate\",\"dataValueType\":7,\"label\":\"Date\",\"required\":true,\"readonly\":false,\"visible\":true}";
        const string time = "{\"name\":\"DATETIME7fa6\",\"control\":\"TimeEdit\",\"column\":\"QSOrderDate\",\"dataValueType\":7,\"label\":\"Date\",\"required\":true,\"readonly\":false,\"visible\":true}";

        collector.Add(Snapshot("[" + date + "," + time + "]"));

        Assert.That(collector.Elements, Has.Count.EqualTo(1));
        Assert.That(collector.Elements[0].Type, Is.EqualTo("DateTime"));
        Assert.That(collector.Elements[0].Code, Is.EqualTo("QSOrderDate"));
    }

    [Test]
    public void A_hidden_control_is_skipped_until_a_pass_sees_it_and_recorded_with_hidden_included()
    {
        const string hidden = "{\"name\":\"UsrNote\",\"control\":\"TextEdit\",\"column\":\"UsrNote\",\"dataValueType\":1,\"label\":\"Note\",\"required\":false,\"readonly\":false,\"visible\":false}";
        const string shown = "{\"name\":\"UsrNote\",\"control\":\"TextEdit\",\"column\":\"UsrNote\",\"dataValueType\":1,\"label\":\"Note\",\"required\":false,\"readonly\":false,\"visible\":true}";

        var strict = new ClassicPageAnalyzer.ClassicSnapshotCollector(Map, includeHidden: false);
        strict.Add(Snapshot("[" + hidden + "]"));
        Assert.That(strict.Elements, Is.Empty, "not recorded while hidden");
        strict.Add(Snapshot("[" + shown + "]"));
        Assert.That(strict.Elements, Has.Count.EqualTo(1), "recorded once its tab is open");

        var lenient = new ClassicPageAnalyzer.ClassicSnapshotCollector(Map, includeHidden: true);
        lenient.Add(Snapshot("[" + hidden + "]"));
        Assert.That(lenient.Elements, Has.Count.EqualTo(1));
    }

    [Test]
    public void Details_tabs_stage_and_detail_buttons_are_recorded_with_their_options()
    {
        var collector = new ClassicPageAnalyzer.ClassicSnapshotCollector(Map, includeHidden: false);

        collector.Add(Snapshot("[]",
            buttons: "[{\"name\":\"CloseButton\",\"label\":\"Close\",\"visible\":true}]",
            details: "[{\"schema\":\"UsrProductDetail\",\"caption\":\"Products\",\"columns\":[\"Product\",\"Qty\"],\"editable\":true,\"visible\":true,\"buttons\":[{\"name\":\"AddRecordButton\",\"label\":\"\",\"visible\":true}]}]",
            tabs: "{\"panel\":\"Tabs\",\"items\":[{\"name\":\"GeneralTab\",\"index\":0,\"caption\":\"General\",\"visible\":true,\"active\":true},{\"name\":\"HiddenTab\",\"index\":1,\"caption\":\"\",\"visible\":false,\"active\":false}]}",
            stage: "{\"values\":[\"New\",\"Done\"],\"subvalues\":{\"$id\":\"7\",\"Done\":[\"Done\",\"Cancelled\"]},\"visible\":true}"));

        collector.AddStandardPageButtons();

        var codes = collector.Elements.Select(e => e.Code).ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(codes, Does.Contain("CloseButton"));
            Assert.That(codes, Does.Contain("UsrProductDetail"));
            Assert.That(codes, Does.Contain("UsrProductDetailAddRecordButton"));
            Assert.That(codes, Does.Contain("GeneralTab"));
            Assert.That(codes, Does.Not.Contain("HiddenTab"), "a tab without a header is not addressable");
            Assert.That(codes, Does.Contain("Tabs"));
            Assert.That(codes, Does.Contain("Stage"));
            Assert.That(codes, Does.Contain("SaveButton"));
            Assert.That(codes, Does.Contain("DiscardChangesButton"));
            Assert.That(collector.Elements.Single(e => e.Code == "SaveButton").IsSave, Is.True);
            Assert.That(collector.Elements.Single(e => e.Code == "DiscardChangesButton").IsSave, Is.False);

            var detail = collector.Elements.Single(e => e.Code == "UsrProductDetail");
            Assert.That(detail.Columns, Is.EqualTo(new[] { "Product", "Qty" }));

            var button = collector.Elements.Single(e => e.Code == "UsrProductDetailAddRecordButton");
            Assert.That(button.Detail, Is.EqualTo("UsrProductDetail"));
            Assert.That(button.Name, Is.EqualTo("AddRecordButton"));

            var stage = collector.Elements.Single(e => e.Code == "Stage");
            Assert.That(stage.Stages, Is.EqualTo(new[] { "New", "Done" }));
            Assert.That(stage.Subvalues!["Done"], Is.EqualTo(new[] { "Done", "Cancelled" }));
            Assert.That(stage.Subvalues!.ContainsKey("$id"), Is.False, "Playwright's reference metadata is not a stage");

            Assert.That(collector.Elements.Single(e => e.Code == "Tabs").Tabs, Is.EqualTo(new[] { "General" }));
        });
    }
}

[TestFixture]
public sealed class ClassicPageFileWritingTests
{
    private string _directory = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), "creatio-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    /// <summary>
    /// What the analyser writes must be what the catalog reads back: the generation, the schema, the
    /// schema name of a renamed field and the detail of a detail button all round-trip.
    /// </summary>
    [Test]
    public void A_Classic_analysis_writes_a_page_file_the_catalog_accepts()
    {
        var analysis = new PageAnalysis
        {
            PageName = "OrderClassic",
            Url = "/0/Shell/#CardModuleV2/UsrOrder1Page",
            Generation = UiGeneration.ClassicUI,
            Schema = "UsrOrder1Page",
            Elements = new[]
            {
                new DiscoveredElement("UsrType", "Type", "Lookup", "ComboBoxEdit", true, false, true, null, null, null, null, false, Name: "LOOKUPceebb14a"),
                new DiscoveredElement("UsrName", "Number", "Text", "TextEdit", true, true, true, null, null, null, null, false),
                new DiscoveredElement("UsrProductDetail", "Products", "Detail", "Detail", false, false, true, null, new[] { "Product", "Quantity" }, null, null, false),
                new DiscoveredElement("UsrProductDetailAddRecordButton", "", "Button", "Button", false, false, true, null, null, null, null, false, Name: "AddRecordButton", Detail: "UsrProductDetail"),
                new DiscoveredElement("Tabs", "", "TabPanel", "TabPanel", false, false, true, null, null, new[] { "General", "Products" }, null, false),
                new DiscoveredElement("SaveButton", "", "Button", "Button", false, false, true, null, null, null, null, false, IsSave: true),
                new DiscoveredElement("Stage", "", "CasesBar", "BaseStageControl", false, false, true, new[] { "New", "Done" }, null, null, null, false,
                    Subvalues: new Dictionary<string, IReadOnlyList<string>> { ["Done"] = new[] { "Done", "Cancelled" } }),
            },
            UnknownControls = Array.Empty<UnknownControl>(),
        };

        var writer = new PageConfigWriter(NullLogger<PageConfigWriter>.Instance);
        var written = writer.Write(analysis, _directory);

        var json = File.ReadAllText(written.FilePath);
        var pages = new ConfigurationLoader(Path.GetDirectoryName(_directory)!).LoadPages(Path.GetFileName(_directory));
        var catalog = ElementCatalog.Build(pages, UiElementRegistry.Discover());

        Assert.Multiple(() =>
        {
            Assert.That(json, Does.Contain("\"type\": \"ClassicUI\""));
            Assert.That(json, Does.Contain("\"schema\": \"UsrOrder1Page\""));
            Assert.That(json, Does.Contain("\"name\": \"LOOKUPceebb14a\""));
            Assert.That(json, Does.Contain("\"detail\": \"UsrProductDetail\""));
            Assert.That(json, Does.Contain("\"subvalues\""));
            Assert.That(json, Does.Contain("\"isSave\": true"));
            Assert.That(catalog.TryGetElement("OrderClassic", "UsrType", out var type), Is.True);
            Assert.That(type.Definition.BindOptions<ClassicElementOptions>().Name, Is.EqualTo("LOOKUPceebb14a"));
            Assert.That(catalog.TryGetElement("OrderClassic", "UsrProductDetailAddRecordButton", out var button), Is.True);
            Assert.That(button.Definition.BindOptions<ClassicButtonOptions>().Detail, Is.EqualTo("UsrProductDetail"));
            Assert.That(catalog.TryGetElement("OrderClassic", "Stage", out var stage), Is.True);
            Assert.That(stage.Definition.BindOptions<ClassicCasesBarOptions>().Subvalues!["Done"], Is.EqualTo(new[] { "Done", "Cancelled" }));
        });
    }

    [Test]
    public void A_Freedom_analysis_writes_no_schema_line()
    {
        var analysis = new PageAnalysis
        {
            PageName = "ContactForm",
            Url = "/0/Shell/#Card/Contacts_FormPage",
            Elements = new[] { new DiscoveredElement("JobTitle", "Job title", "Text", "crt-input", false, false, true, null, null, null, null, false) },
            UnknownControls = Array.Empty<UnknownControl>(),
        };

        var written = new PageConfigWriter(NullLogger<PageConfigWriter>.Instance).Write(analysis, _directory);
        var json = File.ReadAllText(written.FilePath);

        Assert.That(json, Does.Contain("\"type\": \"FreedomUI\""));
        Assert.That(json, Does.Not.Contain("schema"));
    }
}

[TestFixture]
public sealed class ClassicSchemaNameTests
{
    [TestCase("ContactPageV2", true)]
    [TestCase("QSOrder1Page", true)]
    [TestCase("UsrTestPage", true)]
    [TestCase("Contacts_FormPage", false)]
    [TestCase("QSOrderNewUI_ListPage", false)]
    [TestCase("BasePageV2", false)]
    [TestCase("ContactSectionV2", false)]
    public void Classic_page_schemas_are_told_by_their_spelling(string name, bool expected)
    {
        Assert.That(SchemaDiscoveryService.IsClassicPageName(name), Is.EqualTo(expected));
    }
}
