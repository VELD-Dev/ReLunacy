namespace ReLunacy.Engine.EntityManagement;

public class EntityZone
{
    public readonly EntityCluster TieInstances = new();
    public readonly EntityCluster UFrags = new();

    public int TieCount { get => TieInstances.Size; } 
    public int UFragsCount { get => UFrags.Size; }

    public bool AllowRender = true;

    public string ZoneName = string.Empty;
    public int ZoneIndex = -1;

    public EntityZone() { }

    public EntityZone(CZone zone)
    {
        ZoneName = zone.name;
        ZoneIndex = zone.index;

        LunaLog.LogDebug($"Zone {ZoneName} ({ZoneIndex}) has {zone.tieInstances.Count} tie instances and {zone.ufrags.Length} UFrags.");
        foreach (var tie in zone.tieInstances)
        {
            TieInstances.Add(tie.Value);
        }
        for (int i = 0; i < zone.ufrags.Length; i++)
        {
            var uf = zone.ufrags[i];
            UFrags.Add(uf, (ulong)zone.index, i);
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
