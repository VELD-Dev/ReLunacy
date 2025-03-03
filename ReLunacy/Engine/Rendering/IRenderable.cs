using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ReLunacy.Engine.Rendering;

public interface IRenderable
{
    public float[] Vertices { get; set; }
    public uint[] Indices { get; set; }
}
