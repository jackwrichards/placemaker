// (core) The central shelf (Recipes/shelf/<category>.json, Core/Tool/Shelf.cs): the shipped files parse, cover every type of the
// Make row exactly once, keep Transport hidden (Industrial is shown again, 2026-10-19) and make Beach its own category; hiding a type or a style is a file
// edit that the catalog the panel gets reflects; the game's restore of a hidden last pick is the panel's first type.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using Placemaker.Core;
using Placemaker.Core.Tool;
using Xunit;

namespace Placemaker.Tests
{
    public class ShelfTests
    {
        private static Shelf Shipped()
        {
            var dir = Path.Combine(TestKit.RepoRoot(), "Recipes", Shelf.FolderName);
            var files = Directory.GetFiles(dir, "*.json").OrderBy(f => f).Select(f => new KeyValuePair<string, string>(Path.GetFileName(f), File.ReadAllText(f)));
            return Shelf.Parse(files);
        }

        [Fact]
        public void ShippedShelfHasTheCategoriesInOrderAndEveryTypeOnce()
        {
            var shelf = Shipped();
            Assert.Empty(shelf.Warnings);
            var shown = shelf.Categories.Where(c => !c.Hidden).Select(c => c.Id).ToArray();
            Assert.Equal(new[] { "zoning", "roads", "parking", "parks", "beach", "shops", "plazas", "nature", "farms", "sports", "industry", "ai" }, shown);
            Assert.False(shelf.Category("industry").Hidden);   // Industrial (2026-10-19): its own category again
            Assert.True(shelf.Category("transport").Hidden);
            foreach (var g in GeneratorRegistry.All)
            {
                if (GeneratorRegistry.IsHidden(g) || g.Kind != LayerKind.Fill) continue;
                var grp = GeneratorGroups.Of(g);
                if (string.IsNullOrEmpty(grp)) continue;
                Assert.NotEqual(Shelf.Other, shelf.CategoryOf(g.Id, grp));
            }
            Assert.Equal("beach", shelf.CategoryOf("beach", "water"));
            Assert.Equal("beach", shelf.CategoryOf("waterfront", "water"));
            Assert.Equal("parks", shelf.CategoryOf("zenGarden", "gardens"));
            Assert.Equal("plazas", shelf.CategoryOf("plaza", "squares"));
            Assert.Equal("plazas", shelf.CategoryOf("mallPark", "commercial"));
            Assert.Equal("shops", shelf.CategoryOf("fairground", "events"));
            Assert.False(shelf.IsTypeShelved("quarry", "industrial"));   // the makeover (2026-10-30): rebuilt on vanilla pieces
            Assert.Equal("industry", shelf.CategoryOf("containerYard", "industrial"));
            Assert.True(shelf.IsTypeShelved("restArea", "transport"));
            Assert.False(shelf.IsTypeShelved("park", "parks"));
        }

        [Fact]
        public void HidingTypesStylesAndMovingTypesIsAFileEdit()
        {
            var files = new[]
            {
                new KeyValuePair<string, string>("parks.json", "{ \"id\": \"parks\", \"name\": \"Parks\", \"order\": 3, \"groups\": [\"parks\"], \"hiddenTypes\": [\"dogPark\"], \"hiddenStyles\": { \"park\": [\"pocket\"] }, \"types\": [\"picnicGrounds\"] }"),
                new KeyValuePair<string, string>("beach.json", "{ \"name\": \"Beach\", \"order\": 4, \"groups\": [\"water\"] }"),
                new KeyValuePair<string, string>("bad.json", "{ not json"),
            };
            var shelf = Shelf.Parse(files);
            Assert.Single(shelf.Warnings);   // the unreadable file
            Assert.Equal("beach", shelf.Categories[1].Id);   // the file name is the id
            Assert.True(shelf.IsTypeShelved("dogPark", "parks"));
            Assert.False(shelf.IsTypeShelved("park", "parks"));
            Assert.True(shelf.IsStyleShelved("park", "parks", "pocket"));
            Assert.False(shelf.IsStyleShelved("park", "parks", "formal"));
            Assert.Equal("parks", shelf.CategoryOf("picnicGrounds", "somethingElse"));
            Assert.Equal(Shelf.Other, shelf.CategoryOf("quarry", "industrial"));
        }

        [Fact]
        public void TheCatalogCarriesCategoriesAndShelvedFlags()
        {
            var shelf = Shelf.Parse(new[]
            {
                new KeyValuePair<string, string>("parks.json", "{ \"id\": \"parks\", \"name\": \"Parks\", \"order\": 1, \"groups\": [\"parks\", \"gardens\"], \"hiddenTypes\": [\"dogPark\"], \"hiddenStyles\": { \"park\": [\"pocket\"] } }"),
                new KeyValuePair<string, string>("industry.json", "{ \"id\": \"industry\", \"name\": \"Industry\", \"order\": 9, \"hidden\": true, \"groups\": [\"industrial\"] }"),
            });
            var recipes = new[] { "park", "dogPark", "quarry", "zenGarden" }.Select(id => new RecipeListEntry { Id = id, Name = id });
            var json = JsonValue.Parse(UiModels.Catalog(GeneratorRegistry.All, recipes, null, shelf).ToString());
            JsonValue R(string id) => json["recipes"].Items.First(r => r["id"].AsString() == id);
            Assert.Equal("parks", R("park")["category"].AsString());
            Assert.False(R("park")["shelved"].AsBool());
            Assert.True(R("dogPark")["shelved"].AsBool());
            Assert.True(R("quarry")["shelved"].AsBool());
            Assert.Equal("parks", R("zenGarden")["category"].AsString());
            var park = json["layers"].Items.First(l => l["id"].AsString() == "park");
            Assert.True(park["presets"].Items.First(p => p["id"].AsString() == "pocket")["shelved"].AsBool());
            Assert.False(park["presets"].Items.First(p => p["id"].AsString() == "formal")["shelved"].AsBool());
            var cats = json["categories"].Items.Select(c => c["id"].AsString()).ToArray();
            Assert.Equal(new[] { "parks", "industry" }, cats);
            Assert.True(json["categories"].Items[1]["hidden"].AsBool());
        }
    }
}
