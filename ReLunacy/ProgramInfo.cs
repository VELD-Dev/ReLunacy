using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

// AssemblyInformationalVersion, not AssemblyVersion: the latter only accepts
// major[.minor[.build[.revision]]], which a prerelease tag like "0.06-alpha.1" is not (CS7034).
// The SDK generates this attribute from $(Version) by default, so ReLunacy.csproj turns that
// generation off - the const below stays the one place the version is written.
[assembly: AssemblyInformationalVersion(ProgramInfo.Version)]

namespace ReLunacy;

public static class ProgramInfo
{
    public const string Name = "ReLunacy";
    public const string DisplayName = "ReLunacy";
    public const string Version = "0.06-alpha.1";
    public const string GithubURL = "https://github.com/VELD-Dev/ReLunacy/";
}
