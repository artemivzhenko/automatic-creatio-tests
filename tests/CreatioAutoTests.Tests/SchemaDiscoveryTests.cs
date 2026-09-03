using CreatioAutoTests.Runner;

namespace CreatioAutoTests.Tests;

/// <summary>
/// The address a schema is opened through. Getting this wrong sends `analyse` to the desktop,
/// which answers 200 and shows nothing, so it is worth pinning down.
/// </summary>
[TestFixture]
public sealed class SchemaDiscoveryTests
{
    [Test]
    public void A_form_page_address_carries_a_record_placeholder()
    {
        var address = SchemaDiscoveryService.BuildAddress(
            "Contacts_FormPage", SchemaDiscoveryService.FormPageSuffix);

        Assert.That(address, Is.EqualTo("/0/Shell/#Card/Contacts_FormPage/edit/<recordId>"));
    }

    [Test]
    public void A_section_page_address_needs_no_record()
    {
        var address = SchemaDiscoveryService.BuildAddress(
            "Contacts_ListPage", SchemaDiscoveryService.ListPageSuffix);

        Assert.That(address, Is.EqualTo("/0/Shell/#Section/Contacts_ListPage"));
    }

    /// <summary>A form page address must survive the parser that `analyse` puts it through.</summary>
    [Test]
    public void The_address_parses_back_into_the_schema_it_came_from()
    {
        var address = SchemaDiscoveryService.BuildAddress(
            "Opportunities_FormPage", SchemaDiscoveryService.FormPageSuffix);

        var path = new CreatioAutoTests.Analysis.PagePath(
            address.Replace("<recordId>", "6344d6ce-f889-4361-83f2-9cd71dbd6300"));

        Assert.Multiple(() =>
        {
            Assert.That(path.Schema, Is.EqualTo("Opportunities_FormPage"));
            Assert.That(path.RecordId, Is.EqualTo("6344d6ce-f889-4361-83f2-9cd71dbd6300"));
            Assert.That(path.Url, Is.EqualTo("/0/Shell/#Card/Opportunities_FormPage"));
        });
    }

    [Test]
    public void The_two_suffixes_are_the_ones_creatio_uses()
    {
        Assert.Multiple(() =>
        {
            Assert.That(SchemaDiscoveryService.FormPageSuffix, Is.EqualTo("_FormPage"));
            Assert.That(SchemaDiscoveryService.ListPageSuffix, Is.EqualTo("_ListPage"));
        });
    }
}
