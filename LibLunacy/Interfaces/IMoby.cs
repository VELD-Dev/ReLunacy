using LibLunacy.Objects;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LibLunacy.Interfaces;

public interface IMoby : ILunaObject, ILunaSerializable
{
    public MobyBangle[] Bangles { get; set; }
    // public MobyBone[] Bones { get; set; }
}
