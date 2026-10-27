// AI builder (docs/AI-BUILDER.md, docs/research/ai-asset-pictures.md): real pictures of assets for the AI, without
// rendering anything. In order: the Asset Icon Library mod's ready-made picture of the prefab (ModsData/AssetIconLibrary/
// .Thumbnails/StaticObjectPrefab.<name>.png, a front-left view; read on this machine, never shipped), else the prefab's
// own icon (an assetdb:// image inside its mod package, or a coui://ail/ file). Bytes as they are (PNG or JPG): the
// model decodes them. Runs on the agent's worker; file reads only.

using System;
using System.IO;
using Colossal.IO.AssetDatabase;
using Colossal.PSI.Environment;

namespace Placemaker.Game.Ai
{
    public static class AiPictures
    {
        public sealed class Picture
        {
            public byte[] Bytes;
            public string Mime;
            public string Source;   // "asset icon library" | "mod icon"
        }

        private static string AilFolder => Path.Combine(EnvPath.kUserDataPath, "ModsData", "AssetIconLibrary", ".Thumbnails");

        /// <summary>A picture of the asset, or null (<paramref name="icon"/>: the prefab's icon url, "" = none).</summary>
        public static Picture For(string name, string icon)
        {
            try
            {
                var ail = Path.Combine(AilFolder, "StaticObjectPrefab." + name + ".png");
                if (File.Exists(ail)) return new Picture { Bytes = File.ReadAllBytes(ail), Mime = "image/png", Source = "asset icon library" };
                if (string.IsNullOrEmpty(icon)) return null;
                if (icon.StartsWith("coui://ail/", StringComparison.OrdinalIgnoreCase))
                {
                    var f = Path.Combine(AilFolder, icon.Substring("coui://ail/".Length).Replace('/', Path.DirectorySeparatorChar));
                    if (File.Exists(f)) return new Picture { Bytes = File.ReadAllBytes(f), Mime = MimeOf(f), Source = "asset icon library" };
                }
                if (icon.StartsWith("assetdb://", StringComparison.OrdinalIgnoreCase)
                    && AssetDatabase.global.TryGetAsset<IAssetData>(new Uri(icon), out var data) && data is FileAsset file)
                {
                    using (var s = file.GetReadStream())
                    using (var m = new MemoryStream())
                    {
                        s.CopyTo(m);
                        var bytes = m.ToArray();
                        if (bytes.Length > 0) return new Picture { Bytes = bytes, Mime = Sniff(bytes), Source = "mod icon" };
                    }
                }
            }
            catch (Exception e) { Mod.Log.Warn($"Placemaker AI: no picture of {name} ({e.GetType().Name}: {e.Message})"); }
            return null;
        }

        private static string MimeOf(string path) => path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ? "image/jpeg" : "image/png";

        private static string Sniff(byte[] b) => b.Length > 3 && b[0] == 0xFF && b[1] == 0xD8 ? "image/jpeg" : "image/png";
    }
}
