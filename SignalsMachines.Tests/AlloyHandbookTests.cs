using System.Reflection;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.ServerMods.NoObf;

namespace SignalsMachines.Tests;

public class AlloyHandbookTests
{
    [Fact]
    public void AlloyOutputResolvesToExistingMetalBitsInHandbook()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../SignalsMachines/assets"));
        var ingot = Resolve(Path.Combine(root, "game/itemtypes/resource/ingot-hardenedbronze.json"));
        var bits = Resolve(Path.Combine(root, "game/itemtypes/resource/metalbit-hardenedbronze.json"));
        var recipe = JObject.Parse(File.ReadAllText(Path.Combine(root, "signalsmachines/recipes/alloy/hardenedbronze.json")));

        Assert.Equal((string)recipe["output"]["code"], ingot.Code.ToString());
        Assert.Equal("game:ingot-hardenedbronze", ingot.Code.ToString());
        Assert.Equal("game:metalbit-hardenedbronze", bits.Code.ToString());
        // Vanilla addAlloyForInfo uses the output's metal variant, not its code suffix.
        var handbookTarget = new AssetLocation(ingot.Code.Domain, "metalbit-" + ingot.CodeParts["metal"]);
        Assert.Equal(bits.Code, handbookTarget);
        Assert.Equal("hardenedbronze", bits.CodeParts["metal"]);
    }

    private static ResolvedVariant Resolve(string path)
    {
        var json = JObject.Parse(File.ReadAllText(path));
        Assert.NotNull(json["variantgroups"]);
        var code = new AssetLocation("game", (string)json["code"]);
        // Exercise the installed game's variant expansion, including the resulting saved item codes.
        var gather = typeof(ModRegistryObjectTypeLoader).GetMethod("GatherVariants", BindingFlags.Instance | BindingFlags.NonPublic);
        var groups = json["variantgroups"].ToObject(gather.GetParameters()[1].ParameterType);
        var variants = (List<ResolvedVariant>)gather.Invoke(new ModRegistryObjectTypeLoader(), new object[] { code, groups, code, null, null });
        return Assert.Single(variants);
    }
}
