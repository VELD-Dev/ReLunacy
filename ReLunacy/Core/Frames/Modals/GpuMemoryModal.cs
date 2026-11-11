using System.Numerics;
using ReLunacy.Engine.Rendering.Vulkan;
using ReLunacy.Utility;
using ReLunacy.Utility.Localization;

namespace ReLunacy.Core.Frames.Modals;

/// <summary>Shown when a level's textures do not fit the GPU's memory: either they were reduced to fit
/// before upload (a warning, the level draws normally at lower resolution), or a texture could not be
/// allocated at all (an error, it draws with the default texture). Both name the numbers, because the
/// fix - a smaller level, fewer applications holding video memory, a bigger GPU - depends on them.</summary>
public class GpuMemoryModal : Modal
{
    protected override ImGuiWindowFlags WindowFlags { get; set; } = ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoDocking;

    private readonly string message;
    private readonly string? budgetLine;

    /// <summary>The textures were reduced to fit.</summary>
    public GpuMemoryModal(TextureBudgetReport report)
    {
        FrameName = LM.Get("GUI_Frame_GpuMemory_ReducedTitle");
        message = LM.Get("GUI_Frame_GpuMemory_Reduced", report.FullBytes >> 20, report.Budget.TextureAllowance >> 20,
            report.ReducedCount, report.TextureCount, report.MaxDimension, report.FittedBytes >> 20);
        budgetLine = BudgetLine(report.Budget);
    }

    /// <summary>A texture could not be allocated.</summary>
    public GpuMemoryModal(string failure, GpuMemoryBudget? budget)
    {
        FrameName = LM.Get("GUI_Frame_GpuMemory_FailedTitle");
        message = LM.Get("GUI_Frame_GpuMemory_Failed", failure);
        budgetLine = budget is { } b ? BudgetLine(b) : null;
    }

    private static string BudgetLine(GpuMemoryBudget budget) =>
        LM.Get(budget.FromBudgetExtension ? "GUI_Frame_GpuMemory_BudgetFromDriver" : "GUI_Frame_GpuMemory_BudgetFromHeap",
            budget.DeviceName, budget.HeapBytes >> 20, budget.BudgetBytes >> 20);

    protected override void Render(double deltaTime)
    {
        ImGui.PushTextWrapPos(ImGui.GetFontSize() * 36f);
        ImGui.TextWrapped(message);
        if (budgetLine != null)
        {
            ImGui.Spacing();
            ImGui.TextDisabled(budgetLine);
        }
        ImGui.PopTextWrapPos();

        ImGui.Spacing();
        if (ImGui.Button(LM.Get("GUI_Common_CloseWord")))
            isOpen = false;
    }

    public override void RenderAsWindow(double deltaTime)
    {
        ImGui.SetNextWindowPos(ImGui.GetWorkCenter(ImGui.GetMainViewport()), ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
        base.RenderAsWindow(deltaTime);
    }
}
