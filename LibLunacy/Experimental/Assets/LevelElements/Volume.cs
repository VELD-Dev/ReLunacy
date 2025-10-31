using LibLunacy.Experimental.Core.Interfaces;
using LibLunacy.Experimental.Core.IO;
using LibLunacy.Experimental.Core.Primitives;
using LibLunacy.Legacy;
using LibLunacy.Numerics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LibLunacy.Experimental.Assets.LevelElements;

public class Volume : IVolumeData
{
    public ulong Id { get; init; }

    public string? Name { get; set; }

    public Mat4 transform;

    public bool IsLoaded { get; set; }
    public ushort group { get; set; }

    public Volume(ulong id, Mat4 transform, string? name = null, ushort group = 0)
    {
        Id = id;
        Name = name;
        this.transform = transform;
        IsLoaded = true;
    }
}
