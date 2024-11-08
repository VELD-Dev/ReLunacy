using ReLunacy.Engine.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ReLunacy.Engine.Tools;

// Credits to github.com/RatchetModding/Replanetizer/

public class TransformSpace(int key, string humanName) : EnhancedEnum<TransformSpace>(key, humanName) 
{
    public static readonly TransformSpace Global = new(0, "Global");
    public static readonly TransformSpace Local = new(1, "Local");
}
