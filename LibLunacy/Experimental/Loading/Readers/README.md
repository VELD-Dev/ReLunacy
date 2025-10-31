# Game Entity Readers

This directory contains readers that bridge the legacy Luna Engine file formats with the modern experimental asset types.

## Overview

The readers load game entities from IGFile formats (both Old and New engine) and convert them to the experimental type system:

- **MobyReader** - Reads dynamic game objects (characters, NPCs, enemies, vehicles) with Bangle support
- **TieReader** - Reads static game objects (buildings, props, landscape elements)
- **ZoneReader** - Reads spatial chunks containing UFrags and TieInstances
- **RegionReader** - Reads level partitions with Old/New engine distinction
- **LevelReader** - Main orchestrator for loading complete levels

## Usage

```csharp
using LibLunacy.Experimental.Loading.Readers;

// Create a FileManager from your level files
var fileManager = new FileManager("path/to/level");

// Create the level reader
var levelReader = new LevelReader(fileManager);

// Load the complete level with progress callback
var levelData = levelReader.LoadLevel((status, progress) => {
    Console.WriteLine($"{status}: {progress * 100}%");
});

// Access loaded assets
Console.WriteLine(levelData.GetSummary());
Console.WriteLine($"Loaded {levelData.Mobys.Count} mobys");
Console.WriteLine($"Loaded {levelData.Ties.Count} ties");
Console.WriteLine($"Loaded {levelData.Zones.Count} zones");

// Populate an AssetLibrary
var library = new AssetLibrary();
levelData.PopulateLibrary(library);

// Query assets
var oldEngineUFrags = library.UFrags.GetOldEngine();
var mobysWithVariations = library.Mobys.GetByBangleCount(2);
```

## Architecture

### Reading Pipeline

```
Legacy IGFile Format
        ↓
FileManager (manages .dat files)
        ↓
Entity Readers (convert to experimental types)
   ├── MobyReader → Moby (with Bangles)
   ├── TieReader → Tie
   ├── ZoneReader → Zone (with UFrags + TieInstances)
   └── RegionReader → Region (with Old/New distinction)
        ↓
LevelData (complete level)
        ↓
AssetLibrary (organized collections)
```

### Engine Format Support

#### Old Engine:
- Assets read from `main.dat` sections
- Simple direct section-based loading
- Regions contain only moby instances

#### New Engine:
- Assets read from separate .dat files (`mobys.dat`, `ties.dat`, `zones.dat`)
- AssetPointers in `assetlookup.dat` provide TUID + offset
- Regions contain zones + moby instances

## Implementation Details

### Material Handling

The `MaterialReader` provides placeholder materials for converted meshes. In a full implementation, you would:
1. Load textures from the texture system
2. Load shaders and create proper materials
3. Link materials to meshes using shader indices

### Transform Conversion

Transforms are converted from legacy formats:
- **Mat4** (TieInstance) → Transform3D with Euler rotation
- **Vec3 + Vec3 + float** (MobyInstance) → Transform3D with scale

Note: Mat4 rotation extraction is simplified. For accurate rotation, implement proper matrix decomposition to Euler angles.

### Geometry Extraction

Meshes are extracted from legacy formats:
- **MobyMesh**: Supports VertexFormat0 and VertexFormat1
- **TieMesh**: Uses VertexFormat0
- **UFrag**: Direct geometry data with material

All geometry is converted to flat float arrays (positions, UVs) and uint indices for the experimental GeometryData type.

## Reader Classes

### LevelReader

Main entry point that orchestrates all readers:
```csharp
public class LevelReader
{
    public LevelData LoadLevel(Action<string, float>? progressCallback = null)
    public IReadOnlyDictionary<ulong, Moby> Mobys { get; }
    public IReadOnlyDictionary<ulong, Tie> Ties { get; }
    public IReadOnlyDictionary<ulong, Zone> Zones { get; }
    public Region? Region { get; }
}
```

### MobyReader

Reads mobys from IGFile and converts bangles/meshes:
```csharp
public Dictionary<ulong, Moby> ReadAllMobys()
```

Key features:
- Reads bangles with multiple mesh groups
- Extracts vertices (Format0 and Format1)
- Calculates bounding spheres
- Converts to experimental Moby with Bangle list

### TieReader

Reads ties from IGFile:
```csharp
public Dictionary<ulong, Tie> ReadAllTies()
```

Key features:
- Reads tie meshes
- Extracts VertexFormat0 data
- Averages Vec3 scale to single float

### ZoneReader

Reads zones with UFrags and TieInstances:
```csharp
public Dictionary<ulong, Zone> ReadAllZones()
```

Key features:
- Reads UFrags (Old/New variants)
- Reads TieInstances with transforms
- Resolves Tie references via TUID lookup (New) or index (Old)
- Converts Mat4 transforms

### RegionReader

Reads regions with Old/New engine distinction:
```csharp
public Region ReadRegion()
```

Key features:
- Old Engine: Only moby instances
- New Engine: Zones + moby instances
- Resolves Moby references
- Converts Vec3-based transforms

## Extending the Readers

To add support for new entity types:

1. Create a new reader class (e.g., `ShrubReader.cs`)
2. Follow the same pattern:
   - `Read[Entity]Old()` for old engine
   - `Read[Entity]New()` for new engine
   - `Convert[Entity]()` to experimental type
3. Add to LevelReader pipeline
4. Update LevelData with new collection

## Known Limitations

1. **Material System**: Uses placeholder materials. Need to integrate with actual texture/shader loading.
2. **Rotation Extraction**: Mat4 to Euler conversion is simplified. Full matrix decomposition recommended.
3. **Bounding Spheres**: Calculated from vertices. Legacy data may have precomputed values that should be used when available.
4. **Error Handling**: Minimal error handling. Production code should handle missing files, corrupt data, etc.

## Future Improvements

- [ ] Integrate with texture loading system
- [ ] Proper shader/material resolution
- [ ] Accurate rotation extraction from Mat4
- [ ] Support for additional entity types (Shrubs, Sprites, etc.)
- [ ] Async loading for large levels
- [ ] Progress reporting with cancellation support
- [ ] Validation and error recovery
- [ ] Memory pooling for large assets
- [ ] Streaming for huge levels
