namespace LibLunacy.Legacy
{
    public class DebugFile
    {
        IGFile file;

        public DebugFile(IGFile file)
        {
            this.file = file;
        }

        public DebugInstanceName[] GetMobyInstanceNames()
        {
            IGFile.SectionHeader mobyinstNames = file.QuerySection(0x73C0);
            file.sh.Seek(mobyinstNames.offset);
            return FileUtils.ReadStructureArray<DebugInstanceName>(file.sh, mobyinstNames.count);
        }
        public DebugInstanceName[] GetTieInstanceNames()
        {
            IGFile.SectionHeader tieinstNames = file.QuerySection(0x72C0);
            file.sh.Seek(tieinstNames.offset);
            return FileUtils.ReadStructureArray<DebugInstanceName>(file.sh, tieinstNames.count);
        }
        public DebugAssetName GetMobyPrototypeName(uint i)
        {
            IGFile.SectionHeader mobyNames = file.QuerySection(0x9480);
            file.sh.Seek(mobyNames.offset + i * 0x10);
            return FileUtils.ReadStructure<DebugAssetName>(file.sh);
        }
        public DebugAssetName GetTiePrototypeName(uint i)
        {
            IGFile.SectionHeader tieNames = file.QuerySection(0x9280);
            file.sh.Seek(tieNames.offset + i * 0x10);
            return FileUtils.ReadStructure<DebugAssetName>(file.sh);
        }
        public DebugShaderName GetShaderName(uint i)
        {
            IGFile.SectionHeader shaderNames = file.QuerySection(0x5D00);
            file.sh.Seek(shaderNames.offset + i * 0x30);
            return FileUtils.ReadStructure<DebugShaderName>(file.sh);
        }

        [FileStructure(0x18)]
        public struct DebugInstanceName
        {
            [FileOffset(0x00)] public ulong tuid1;
            [FileOffset(0x08)] public ulong tuid2;
            [FileOffset(0x10), Reference] public string name;
            [FileOffset(0x14)] public uint unk;
        }

        [FileStructure(0x10)]
        public struct DebugAssetName
        {
            [FileOffset(0x00)] public ulong tuid;
            [FileOffset(0x08), Reference] public string name;
        }
        [FileStructure(0x30)]
        public struct DebugShaderName
        {
            [FileOffset(0x00)] public ulong shaderTuid;
            [FileOffset(0x08), Reference] public string shaderName;
            [FileOffset(0x10)] public uint albedoTuid;
            [FileOffset(0x14)] public uint normalTuid;
            [FileOffset(0x18)] public uint expensiveTuid;
            [FileOffset(0x1C)] public uint wthTuid;
            [FileOffset(0x20), Reference] public string albedoName;
            [FileOffset(0x24), Reference] public string normalName;
            [FileOffset(0x28), Reference] public string expensiveName;
            [FileOffset(0x2C), Reference] public string wthName;
        }
    }
}