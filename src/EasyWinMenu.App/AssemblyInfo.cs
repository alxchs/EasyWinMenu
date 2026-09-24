using System.Runtime.CompilerServices;
using System.Windows;

[assembly: ThemeInfo(
    ResourceDictionaryLocation.None,
    ResourceDictionaryLocation.SourceAssembly
)]

// The group logic worth asserting on — monitor rescue geometry, group lookup, entry counting —
// is internal because nothing outside this assembly drives it. Opening it to the test project
// keeps it that way while still letting the tests reach it.
[assembly: InternalsVisibleTo("EasyWinMenu.Tests")]
