using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ReLunacy.Utility;

public sealed class LoadingProgress
{
    public string status;
    public uint current;
    public uint max;
    public float Progress => current / (float)max;

    public LoadingProgress(string status, uint max)
    {
        this.status = status;
        this.max = max;
        current = 0;
    }

    public LoadingProgress(string status, uint max, uint current)
    {
        this.status = status;
        this.max = max;
        this.current = current;
    }

    public void SetStatus(string newStatus) => status = newStatus;
    public void SetProgress(uint prog) => current = prog;
    public void SetTotal(uint total)
    {
        if(total < 1) total = 1;
        if (total < current) total = current;
        max = total;
    }
}
