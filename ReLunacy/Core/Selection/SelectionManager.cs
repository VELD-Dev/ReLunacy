using ReLunacy.Engine.Scene;
using ReLunacy.Utility;

namespace ReLunacy.Core.Selection;

public class SelectionManager
{
    private static readonly Lazy<SelectionManager> lazy = new(() => new SelectionManager());
    public static SelectionManager Singleton => lazy.Value;

    private Entity? _selectedEntity;

    public Entity? SelectedEntity
    {
        get => _selectedEntity;
        private set
        {
            if (_selectedEntity == value) return;

            var oldEntity = _selectedEntity;
            if (oldEntity != null) oldEntity.selected = false;

            _selectedEntity = value;
            if (value != null) value.selected = true;

            SelectionChanged?.Invoke(oldEntity, value);
            LunaLog.LogDebug($"Selection changed: {value?.Name ?? "none"}");
        }
    }

    /// <summary>Entity1: old entity; Entity2: new entity.</summary>
    public event Action<Entity?, Entity?>? SelectionChanged;

    private SelectionManager() { }

    public void Select(Entity? entity) => SelectedEntity = entity;
    public void Deselect() => SelectedEntity = null;
    public bool IsSelected(Entity entity) => SelectedEntity == entity;
}
