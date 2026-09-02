using CreatioAutoTests.Analysis;
using CreatioAutoTests.Commands;
using CreatioAutoTests.Configuration;
using CreatioAutoTests.Execution;
using CreatioAutoTests.Frontend.Registry;
using CreatioAutoTests.Tests.Support;
using CreatioAutoTests.Validation;
using Microsoft.Extensions.DependencyInjection;

namespace CreatioAutoTests.Tests;

[TestFixture]
public sealed class PagePathTests
{
    [Test]
    public void A_card_address_yields_the_template_the_record_and_the_schema()
    {
        var path = new PagePath("/0/Shell/#Card/Contacts_FormPage/edit/c4ed336c-3e9b-40fe-8b82-5632476472b4");

        Assert.Multiple(() =>
        {
            Assert.That(path.Url, Is.EqualTo("/0/Shell/#Card/Contacts_FormPage"));
            Assert.That(path.RecordId, Is.EqualTo("c4ed336c-3e9b-40fe-8b82-5632476472b4"));
            Assert.That(path.Schema, Is.EqualTo("Contacts_FormPage"));
            Assert.That(path.DefaultPageName, Is.EqualTo("Contacts_FormPage"));
        });
    }

    [Test]
    public void An_address_without_a_record_keeps_its_path()
    {
        var path = new PagePath("/0/Shell/#Section/Contacts_ListPage");

        Assert.That(path.Url, Is.EqualTo("/0/Shell/#Section/Contacts_ListPage"));
        Assert.That(path.RecordId, Is.Null);
        Assert.That(path.Schema, Is.EqualTo("Contacts_ListPage"));
    }

    [Test]
    public void A_full_url_is_reduced_to_the_part_after_the_host()
    {
        var path = new PagePath("http://desktop:40020/0/Shell/#Card/Accounts_FormPage/edit/" +
                                "405947d0-2ffb-4ded-8675-0475f19f5a81");

        Assert.That(path.Url, Is.EqualTo("/0/Shell/#Card/Accounts_FormPage"));
        Assert.That(path.Schema, Is.EqualTo("Accounts_FormPage"));
    }

    [Test]
    public void A_missing_leading_slash_is_added()
    {
        Assert.That(new PagePath("0/Shell/#Card/X").Url, Is.EqualTo("/0/Shell/#Card/X"));
    }

    [Test]
    public void A_record_can_be_put_back_onto_the_template()
    {
        var path = new PagePath("/0/Shell/#Card/Contacts_FormPage");

        Assert.That(path.WithRecord("abc"), Is.EqualTo("/0/Shell/#Card/Contacts_FormPage/edit/abc"));
    }

    [Test]
    public void An_empty_address_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => _ = new PagePath("  "));
    }
}

[TestFixture]
public sealed class HostTagIndexTests
{
    private readonly HostTagIndex _index = new(UiElementRegistry.Discover());

    [Test]
    public void Every_registered_element_declares_the_tag_it_lives_on()
    {
        var registry = UiElementRegistry.Discover();

        Assert.That(
            registry.Registrations.Where(r => string.IsNullOrWhiteSpace(r.HostTag)).Select(r => r.TypeName),
            Is.Empty,
            "an element type without a HostTag cannot be found by page analysis");
    }

    [Test]
    public void A_tag_resolves_to_the_type_registered_for_it()
    {
        Assert.That(_index.For("crt-combobox").Single().TypeName, Is.EqualTo("Lookup"));
        Assert.That(_index.For("crt-tab-panel-header-item").Single().TypeName, Is.EqualTo("Tab"));
    }

    [Test]
    public void Tags_shared_by_several_types_are_reported_as_ambiguous()
    {
        Assert.Multiple(() =>
        {
            Assert.That(_index.IsAmbiguous("crt-datetimepicker"), Is.True);
            Assert.That(_index.IsAmbiguous("crt-number-input"), Is.True);
            Assert.That(_index.IsAmbiguous("crt-input"), Is.False);
        });
    }

    [Test]
    public void The_tag_list_is_what_analysis_queries_the_dom_for()
    {
        Assert.That(_index.Tags, Does.Contain("crt-input"));
        Assert.That(_index.Tags, Does.Contain("crt-entity-stage-progress-bar"));
        Assert.That(_index.Tags, Has.Count.LessThanOrEqualTo(UiElementRegistry.Discover().Registrations.Count));
    }
}

[TestFixture]
public sealed class AnalyseValidationTests
{
    private ScriptValidator _validator = null!;
    private ScriptParser _parser = null!;
    private ElementCatalog _catalog = null!;

    [SetUp]
    public void SetUp()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ValueCompatibilityPolicy>();

        _catalog = CatalogBuilder.Default();
        _parser = new ScriptParser();
        _validator = new ScriptValidator(CommandRegistry.Discover(), services.BuildServiceProvider());
    }

    private ExecutionPlan Validate(params string[] lines)
    {
        var context = new ValidationContext(
            _catalog,
            new Dictionary<string, string>(),
            new[] { "tester" },
            new Dictionary<string, ObjectDefinition>());

        return _validator.Validate("analysis", "analysis.act", _parser.Parse(lines), context);
    }

    [Test]
    public void An_address_that_is_not_configured_yet_still_validates()
    {
        var plan = Validate("use tester", "analyse \"/0/Shell/#Card/Leads_FormPage/edit/x\"");

        Assert.That(plan.IsValid, Is.True, plan.DescribeIssues());
    }

    [Test]
    public void A_page_name_may_be_given_alongside_the_address()
    {
        var plan = Validate("use tester", "analyse \"/0/Shell/#Card/Leads_FormPage\" LeadForm");

        Assert.That(plan.IsValid, Is.True, plan.DescribeIssues());
    }

    [Test]
    public void Without_an_address_it_analyses_the_open_page()
    {
        var plan = Validate("use tester", "open TestPage", "analyse");

        Assert.That(plan.IsValid, Is.True, plan.DescribeIssues());
    }

    [Test]
    public void Without_an_address_and_without_an_open_page_it_says_so()
    {
        var plan = Validate("use tester", "analyse");

        Assert.That(plan.IsValid, Is.False);
        Assert.That(plan.Issues[0].Message, Does.Contain("'open' must come first"));
    }

    [Test]
    public void Too_many_arguments_are_rejected()
    {
        var plan = Validate("use tester", "analyse \"/a\" Name Extra");

        Assert.That(plan.IsValid, Is.False);
        Assert.That(plan.Issues[0].Message, Does.Contain("at most"));
    }

    /// <summary>
    /// The limit worth stating: a script is validated in full before any of it runs, so elements
    /// that analysis is about to discover are not addressable later in the same file.
    /// </summary>
    [Test]
    public void Elements_analysis_would_discover_are_not_usable_in_the_same_script()
    {
        var plan = Validate(
            "use tester",
            "analyse \"/0/Shell/#Card/Leads_FormPage/edit/x\" LeadForm",
            "set LeadName \"someone\"");

        Assert.That(plan.IsValid, Is.False);
        Assert.That(plan.Issues[0].Message, Does.Contain("LeadName"));
    }
}

[TestFixture]
public sealed class DateTypeResolutionTests
{
    private readonly CreatioAutoTests.Analysis.PageAnalyzer _analyzer = new(
        UiElementRegistry.Discover(),
        Microsoft.Extensions.Logging.Abstractions.NullLogger<CreatioAutoTests.Analysis.PageAnalyzer>.Instance);

    private const string Picker = "crt-datetimepicker";

    /// <summary>
    /// The picker icon announces which picker the control opens, and it is the only signal that
    /// works on an empty control. Getting this wrong silently produces the wrong element type.
    /// </summary>
    [TestCase("Calendar", "Date")]
    [TestCase("Time", "Time")]
    [TestCase("Date/Time", "DateTime")]
    public void The_picker_icon_decides_the_type_even_when_the_control_is_empty(string icon, string expected)
    {
        var (type, uncertainty, _) = _analyzer.ResolveType(Picker, string.Empty, icon);

        Assert.That(type, Is.EqualTo(expected));
        Assert.That(uncertainty, Is.Null);
    }

    [Test]
    public void The_icon_wins_over_what_the_control_happens_to_display()
    {
        var (type, _, _) = _analyzer.ResolveType(Picker, "11/30/2025 7:35 AM", "Calendar");

        Assert.That(type, Is.EqualTo("Date"));
    }

    [TestCase("11/30/2025", "Date")]
    [TestCase("7:36 AM", "Time")]
    [TestCase("11/30/2025 7:35 AM", "DateTime")]
    public void Without_an_icon_the_displayed_value_still_decides(string value, string expected)
    {
        var (type, uncertainty, _) = _analyzer.ResolveType(Picker, value, string.Empty);

        Assert.That(type, Is.EqualTo(expected));
        Assert.That(uncertainty, Is.Null);
    }

    [Test]
    public void An_empty_editable_control_asks_a_person_to_open_the_picker()
    {
        var (type, uncertainty, undecidable) =
            _analyzer.ResolveType(Picker, string.Empty, string.Empty, isReadOnly: false);

        Assert.Multiple(() =>
        {
            Assert.That(type, Is.EqualTo("DateTime"));
            Assert.That(uncertainty, Does.Contain("open the picker"));
            Assert.That(undecidable, Is.False, "a person can settle this one");
        });
    }

    /// <summary>
    /// A read-only empty control renders no picker icon and holds no value, so its exact type is
    /// beyond anyone. It is recorded so its presence and caption stay assertable, and it must not
    /// be reported as a decision waiting to be made.
    /// </summary>
    [Test]
    public void A_read_only_empty_control_is_undecidable_rather_than_pending()
    {
        var (type, uncertainty, undecidable) =
            _analyzer.ResolveType(Picker, string.Empty, string.Empty, isReadOnly: true);

        Assert.Multiple(() =>
        {
            Assert.That(type, Is.EqualTo("DateTime"));
            Assert.That(undecidable, Is.True);
            Assert.That(uncertainty, Does.Contain("presence"));
        });
    }

    [Test]
    public void A_read_only_control_that_shows_a_value_is_still_decided_by_it()
    {
        var (type, uncertainty, undecidable) =
            _analyzer.ResolveType(Picker, "11/30/2025", string.Empty, isReadOnly: true);

        Assert.Multiple(() =>
        {
            Assert.That(type, Is.EqualTo("Date"));
            Assert.That(uncertainty, Is.Null);
            Assert.That(undecidable, Is.False);
        });
    }

    [Test]
    public void An_empty_read_only_number_is_undecidable_too()
    {
        var (_, _, undecidable) =
            _analyzer.ResolveType("crt-number-input", string.Empty, string.Empty, isReadOnly: true);

        Assert.That(undecidable, Is.True);
    }

    /// <summary>An icon label follows the interface language, so an unknown one must not be guessed at.</summary>
    [Test]
    public void An_unrecognised_icon_label_falls_back_instead_of_guessing()
    {
        var (type, _, _) = _analyzer.ResolveType(Picker, "7:36 AM", "Календарь");

        Assert.That(type, Is.EqualTo("Time"), "the value should decide when the label is not understood");
    }

    [Test]
    public void A_tag_that_maps_to_one_type_ignores_the_icon()
    {
        var (type, uncertainty, _) = _analyzer.ResolveType("crt-combobox", string.Empty, "Calendar");

        Assert.That(type, Is.EqualTo("Lookup"));
        Assert.That(uncertainty, Is.Null);
    }
}
