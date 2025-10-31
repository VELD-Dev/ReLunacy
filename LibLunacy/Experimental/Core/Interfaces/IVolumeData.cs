using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LibLunacy.Experimental.Core.Interfaces;

internal interface IVolumeData : IAsset
{
    public ushort group { get; set; }
}