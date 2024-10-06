using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LibLunacy.Interfaces;

public interface ILunaSerializable
{

    /// <summary>
    /// Converts the UFrag Metadatas to position. Creates an error if the built array of bytes doesn't match the original structure size.
    /// </summary>
    /// <param name="isOld">Wether convert to old or new engine metadatas format.</param>
    /// <param name="additionalParams">Additional parameters, if need is.</param>
    /// <returns>An unmanaged array of byte that must be disposed to the ArrayPool after being written to file.</returns>
    /// <exception cref="InvalidOperationException">Occurs if the built array of bytes doesn't match the constant UFragMetadata size. This should NEVER happen.</exception>
    public byte[] ToBytes(bool isOld, params object[]? additionalParams);
}
