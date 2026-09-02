using System.Runtime.CompilerServices;

// The analyser's type resolution is a pure function worth testing directly; it stays internal
// because nothing outside the framework should depend on how a tag maps to an element type.
[assembly: InternalsVisibleTo("CreatioAutoTests.Tests")]
