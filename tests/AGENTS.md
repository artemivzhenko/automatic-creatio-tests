# AGENTS.md — tests

Rules that apply here and differ from `src/`. Read the root `AGENTS.md` first.

## The one hard rule

**No test may open a browser, a socket, or a Creatio instance.** Everything in this project runs in
under a second and works offline. Behaviour that needs a real page belongs in a `.act` case under
`Cases/`, not here.

The one exception already in the tree is `ElementRegistrationTests`, which reads the repository's own
`Configuration/pages` from disk to prove the shipped configuration builds a catalog. Reading a
committed fixture is fine; reaching outside the repository is not.

## Style

NUnit 4 — `Assert.That(actual, Is.EqualTo(expected))`; the classic asserts do not exist. Group related
assertions with `Assert.Multiple` so one run reports every failure.

Test names are sentences describing the behaviour, not the method under test:

```csharp
public void An_unknown_element_is_reported_with_the_codes_that_do_exist()
public void A_read_only_empty_control_is_undecidable_rather_than_pending()
```

Use `[TestCase]` for table-driven cases. Where a test guards a subtle decision, say why in a
`<summary>` above it — several existing tests exist to stop a specific regression, and that intent is
invisible from the assertions alone.

## Helpers

`Support/CatalogBuilder` builds in-memory catalogs; `Support/ExecutionFakes` holds the fake session and
elements. Hand-written doubles only. **Do not add a mocking framework.**

## Documentation

This project sets `GenerateDocumentationFile=false`, so CS1591 does not apply. Do not document members
here for the sake of it.

## Do not

* Do not change production code to make a test easier. If something is genuinely untestable, say which
  and why.
* Do not weaken an assertion to make a failing test pass. A failing test is information.
