using ReLunacy.Core.EntityManagement;

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

            if (oldEntity != null)
                oldEntity.selected = false;

            _selectedEntity = value;

            if (value != null)
                value.selected = true;

            SelectionChanged?.Invoke(oldEntity, value);
        }
    }

    public event Action<Entity?, Entity?>? SelectionChanged;

    private SelectionManager() { }

    public void Select(Entity? entity)
    {
        SelectedEntity = entity;
    }

    public void Deselect()
    {
        SelectedEntity = null;
    }

    public bool IsSelected(Entity entity)
    {
        return SelectedEntity == entity;
    }
}