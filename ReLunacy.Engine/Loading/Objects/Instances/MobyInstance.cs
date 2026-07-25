using ReLunacy.Engine.Loading.Interfaces;

namespace ReLunacy.Engine.Loading.Objects.Instances;

public class MobyInstance
{
    public ulong TUID { get; set; }
    public string name;
    public IMobyInstance instanceData;
    public InstanceMetadata? metadata;
    public bool isOld;

    public Moby Moby { get; set; }

    public MobyInstance(IMobyInstance instance, Moby referredMoby, uint index)
    {
        TUID = index;
        instanceData = instance;
        Moby = referredMoby;
        isOld = true;
        name = $"Moby_{Moby.TUID}_{index}";
    }

    public MobyInstance(IMobyInstance instance, InstanceMetadata data, Moby referredMoby, string instanceName)
    {
        instanceData = instance;
        metadata = data;
        TUID = data.TUID;
        Moby = referredMoby;
        name = instanceName;
        isOld = false;
    }
}
