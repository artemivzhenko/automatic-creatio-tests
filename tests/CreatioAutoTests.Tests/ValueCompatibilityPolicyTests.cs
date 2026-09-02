using CreatioAutoTests.Frontend;
using CreatioAutoTests.Validation;

namespace CreatioAutoTests.Tests;

[TestFixture]
public class ValueCompatibilityPolicyTests
{
    private readonly ValueCompatibilityPolicy _policy = new();

    [TestCase(ValueKind.Text, ValueKind.Text)]
    [TestCase(ValueKind.Number, ValueKind.Text)]
    [TestCase(ValueKind.Lookup, ValueKind.Text)]
    [TestCase(ValueKind.Text, ValueKind.Lookup)]
    [TestCase(ValueKind.Lookup, ValueKind.Stage)]
    public void Allowed_copies_are_accepted(ValueKind source, ValueKind target)
    {
        Assert.That(_policy.CanCopy(source, target, out _), Is.True);
    }

    [TestCase(ValueKind.Text, ValueKind.Number)]
    [TestCase(ValueKind.Number, ValueKind.Boolean)]
    [TestCase(ValueKind.Text, ValueKind.DateTime)]
    [TestCase(ValueKind.Lookup, ValueKind.Number)]
    public void Disallowed_copies_are_rejected_with_a_reason(ValueKind source, ValueKind target)
    {
        Assert.That(_policy.CanCopy(source, target, out var error), Is.False);
        Assert.That(error, Is.Not.Empty);
    }

    [Test]
    public void A_button_cannot_take_part_in_a_copy()
    {
        Assert.That(_policy.CanCopy(ValueKind.Text, ValueKind.None, out _), Is.False);
        Assert.That(_policy.CanCopy(ValueKind.None, ValueKind.Text, out _), Is.False);
    }

    [TestCase("true", true)]
    [TestCase("false", true)]
    [TestCase("yes", false)]
    [TestCase("1", false)]
    public void Boolean_literals_are_restricted_to_true_and_false(string literal, bool expected)
    {
        Assert.That(_policy.IsLiteralAssignable(literal, ValueKind.Boolean, out _), Is.EqualTo(expected));
    }

    [TestCase("123", true)]
    [TestCase("-4.5", true)]
    [TestCase("", true)]
    [TestCase("twelve", false)]
    public void Number_literals_must_parse(string literal, bool expected)
    {
        Assert.That(_policy.IsLiteralAssignable(literal, ValueKind.Number, out _), Is.EqualTo(expected));
    }

    [TestCase("2025-11-30T07:35:00", true)]
    [TestCase("0001-01-01T07:36:00", true)]
    [TestCase("30 November", false)]
    public void Date_literals_must_be_iso_8601(string literal, bool expected)
    {
        Assert.That(_policy.IsLiteralAssignable(literal, ValueKind.DateTime, out _), Is.EqualTo(expected));
    }

    [Test]
    public void Ordering_operators_are_offered_only_where_they_mean_something()
    {
        Assert.That(_policy.OperatorsFor(ValueKind.Number), Does.Contain("gt"));
        Assert.That(_policy.OperatorsFor(ValueKind.Text), Does.Not.Contain("gt"));
        Assert.That(_policy.OperatorsFor(ValueKind.Text), Does.Contain("contains"));
        Assert.That(_policy.OperatorsFor(ValueKind.Boolean), Is.EquivalentTo(new[] { "eq", "ne" }));
    }
}
