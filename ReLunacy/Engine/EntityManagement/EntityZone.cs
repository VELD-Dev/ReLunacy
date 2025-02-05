namespace ReLunacy.Engine.EntityManagement;

public class EntityZone
{
    public readonly EntityCluster TieInstances = new();
    public readonly EntityCluster UFrags = new();

    public int TieCount { get => TieInstances.Size; } 
    public int UFragsCount { get => UFrags.Size; }

    public bool AllowRender = true;

    public string ZoneName = string.Empty;
    public ulong ZoneTUID = 0;

    public EntityZone() { }

    public EntityZone(Zone zone)
    {
        ZoneName = zone.Name;
        ZoneTUID = zone.TUID;

        LunaLog.LogDebug($"Zone {ZoneName} ({ZoneTUID}) has {zone.tieInstances.Length} tie instances and {zone.ufrags.Length} UFrags.");
        foreach (var tie in zone.tieInstances)
        {
            TieInstances.Add(tie);
        }
        for (int i = 0; i < zone.ufrags.Length; i++)
        {
            var uf = zone.ufrags[i];
            UFrags.Add(uf, zone.TUID, i);
        }
    }

    public void Render()
    {
        if (!AllowRender) return;

        TieInstances.Render();
        UFrags.Render();
    }

    // FOR DEBUG PURPOSE
    public Drawable[] GetDrawables()
    {
        return [.. UFrags.GetDrawables(), .. TieInstances.GetDrawables()];
    }
}
