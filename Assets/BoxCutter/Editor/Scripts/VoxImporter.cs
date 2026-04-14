using UnityEditor.AssetImporters;

namespace BoxCutter
{
    [ScriptedImporter(1, "vox", AllowCaching = true)]
    public class VoxImporter : MagicaVoxelImporterBase
    {
        public override void OnImportAsset(AssetImportContext ctx)
        {
            ImportVoxAsset(ctx);
        }
    }
}
