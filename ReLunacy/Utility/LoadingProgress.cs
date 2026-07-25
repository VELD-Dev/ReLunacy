namespace ReLunacy.Utility;

public class LoadingProgress(string status, uint max, bool percentage)
{
    public string status = status;
    public uint current = 0;
    public uint max = max;
    public bool isPercentage = percentage;

    public float Progress => current / (float)max;

    public float GetPercents() => current / (float)max * 100;
    public void SetStatus(string newStatus) => status = newStatus;
    public void SetProgress(uint prog) => current = prog;
    public void SetTotal(uint total)
    {
        if (total < 1) total = 1;
        if (total < current) total = current;
        max = total;
    }
}
