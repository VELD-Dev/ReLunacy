using ReLunacy.Core;
using ReLunacy.Core.Frames;
using ReLunacy.Core.Frames.DockedFrames;
using ReLunacy.Core.Selection;
using ReLunacy.Engine.Assets.Audio;
using ReLunacy.Engine.Assets.Browser;
using ReLunacy.Engine.Assets.Effects;
using ReLunacy.Engine.Assets.Foliage;
using ReLunacy.Engine.Assets.Interfaces;
using ReLunacy.Engine.Scene;
using ReLunacy.Engine.Text;
using ReLunacy.MenuBar;

namespace ReLunacy.Utility;

/// <summary>
/// Opens an Assets Browser entry in the frame that shows its kind: classes, UFrags, effects and foliage in the Asset
/// Viewer, textures in a Texture Viewer each, shaders in a Shader Viewer each, sounds in Sound Banks, text tables in Text
/// Tables. The one place that decides which viewer an asset gets.
/// </summary>
public static class AssetOpener
{
    /// <summary>Whether <see cref="Open"/> has a viewer for this kind.</summary>
    public static bool CanOpen(AssetEntry entry) => entry.Kind switch
    {
        AssetKind.Shrub => false,
        AssetKind.Shader => !entry.Imported,
        _ => true,
    };

    /// <summary>Opens and focuses the viewer on the asset; false when nothing could show it.</summary>
    public static bool Open(AssetEntry entry)
    {
        if (!CanOpen(entry)) return false;
        switch (entry.Kind)
        {
            case AssetKind.Moby:
                return Select<AssetViewer>(v => v.SelectMobyById(entry.Id));
            case AssetKind.Tie:
                return Select<AssetViewer>(v => v.SelectTieById(entry.Id));
            case AssetKind.UFrag:
                return entry.Tag is IUFrag ufrag && Select<AssetViewer>(v => v.SelectUFrag(ufrag));
            case AssetKind.Effect:
                return entry.Tag is EffectPackage package && Select<AssetViewer>(v => v.SelectEffectPackage(package));
            case AssetKind.Foliage:
                return entry.Tag is Foliage foliage && Select<AssetViewer>(v => v.SelectFoliage(foliage.Id));
            case AssetKind.Texture:
            case AssetKind.EffectTexture:
                return TextureViewerFrame.Open(entry);
            case AssetKind.Shader:
                return ShaderViewerFrame.Show(entry.Id);
            case AssetKind.Sound:
            {
                if (entry.Tag is not BankSound sound) return false;
                var frame = Ensure<SoundBanksFrame>(() => new SoundBanksFrame());
                frame.ShowSound(sound.NameIndex);
                frame.Focus();
                return true;
            }
            case AssetKind.TextTable:
            {
                if (entry.Tag is not TextTableRef table) return false;
                var frame = Ensure<TextTablesFrame>(() => new TextTablesFrame());
                frame.ShowLanguage(table.Language);
                frame.Focus();
                return true;
            }
            default:
                return false;
        }
    }

    /// <summary>The ids a level texture is known by in the GPU cache: its own, then the binding-specific wrappers made from it.</summary>
    public static IEnumerable<ulong> TextureIds(ulong id)
    {
        yield return id;
        if (LunaWindow.Instance.Level is not { } level) yield break;
        foreach (var (runtime, source) in level.TextureBindingSources)
            if (source == id) yield return runtime;
    }

    /// <summary>The scene's instances of a class or UFrag entry.</summary>
    public static List<Entity> Instances(AssetEntry entry)
    {
        var found = new List<Entity>();
        foreach (var entity in EntityManager.Singleton.AllEntities())
        {
            bool match = entry.Kind switch
            {
                AssetKind.Moby => entity is EntityMoby moby && moby.BaseMoby.Id == entry.Id,
                AssetKind.Tie => entity is EntityTie tie && tie.BaseTie.Id == entry.Id,
                AssetKind.Foliage => entity is EntityFoliage foliage && foliage.BaseFoliage.Id == entry.Id,
                AssetKind.UFrag => entity is EntityUFrag ufrag && ReferenceEquals(ufrag.UFrag, entry.Tag),
                _ => false,
            };
            if (match) found.Add(entity);
        }
        return found;
    }

    /// <summary>Selects the entry's instances in the 3D view; false when the scene has none.</summary>
    public static bool SelectInstances(AssetEntry entry)
    {
        var instances = Instances(entry);
        if (instances.Count == 0) return false;
        SelectionManager.Singleton.Set(instances, instances[0]);
        if (LunaWindow.Instance.GetFirstFrame<View3D>() is { } view)
        {
            view.SelectedEntity = instances[0];
            view.Focus();
        }
        return true;
    }

    private static T Ensure<T>(Func<T> create) where T : Frame
    {
        var window = LunaWindow.Instance;
        if (window.IsAnyFrameOpened<T>()) return window.GetFirstFrame<T>()!;
        var frame = create();
        window.AddFrame(frame);
        return frame;
    }

    private static bool Select<T>(Func<T, bool> select) where T : Frame
    {
        if (ViewMenuDraw.EnsureFrameTypeOpen(typeof(T).Name) is not T frame) return false;
        bool ok = select(frame);
        frame.Focus();
        return ok;
    }
}
