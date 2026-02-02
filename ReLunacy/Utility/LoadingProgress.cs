using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ReLunacy.Utility;

public class LoadingProgress
{
    public string status;
    public uint current;
    public uint max;
    public float Progress => current / (float)max;
    public bool isPercentage = false;

    public LoadingProgress(string status, uint max, bool percentage)
    {
        this.status = status;
        this.max = max;
        isPercentage = percentage;
        current = 0;
    }

    public LoadingProgress(string status, uint max, uint current, bool percentage)
    {
        this.status = status;
        this.max = max;
        this.current = current;
        isPercentage = percentage;
    }

    public float GetPercents() => ((float)current / (float)max) * 100;
    public void SetStatus(string newStatus) => status = newStatus;
    public void SetProgress(uint prog) => current = prog;
    public void SetTotal(uint total)
    {
        if (total < 1) total = 1;
        if (total < current) total = current;
        max = total;
    }
}
