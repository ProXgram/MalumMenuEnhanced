using MalumMenuEnhanced.Setup.Core;

namespace MalumMenuEnhanced.Setup;

internal sealed class SetupForm
{
    internal static readonly SetupCatalog Catalog = new("1.0", new(new("https://example.test/loader.zip"), new string('0', 64), "Loader"),
        new(new("https://example.test/plugin.zip"), new string('0', 64), "Plugin"));
}
