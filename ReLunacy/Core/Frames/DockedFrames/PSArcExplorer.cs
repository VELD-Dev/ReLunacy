using Bliss.CSharp.Interact;
using Hexa.NET.ImGui;
using LibreFios;
using ReLunacy.Utility;
using ReLunacy.Utility.Localization;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;

namespace ReLunacy.Core.Frames.DockedFrames;

public class PSArcExplorer : DockedFrame
{
    // Internal node class for tree structure
    private class FileNode
    {
        public string Name { get; set; } = "";
        public string FullPath { get; set; } = "";
        public bool IsDirectory { get; set; }
        public Dictionary<string, FileNode> Children { get; set; } = [];
        public FileNode? Parent { get; set; }

        public void AddChild(FileNode node)
        {
            Children[node.Name] = node;
            node.Parent = this;
        }

        public IEnumerable<FileNode> GetSortedChildren()
        {
            // Directories first, then files, both alphabetically
            return Children.Values
                .OrderBy(n => !n.IsDirectory)
                .ThenBy(n => n.Name);
        }
    }

    protected override ImGuiCond DockingConditions { get; set; } = ImGuiCond.Appearing;
    protected override Vector2 DefaultPosition { get; set; } = ImGui.GetMainViewport().WorkPos + ImGui.GetMainViewport().WorkSize * 0.5f;
    protected override ImGuiWindowFlags WindowFlags { get; set; } = ImGuiWindowFlags.NoScrollbar;

    private PSARC? currentArchive;
    private string currentArchivePath = string.Empty;
    private string archivePathInput = string.Empty;
    private string searchFilter = string.Empty;
    private string selectedFilePath = string.Empty;
    private List<string> filteredFiles = [];
    private string ExtractionPath => string.IsNullOrEmpty(currentArchivePath) || currentArchivePath.Split(Path.DirectorySeparatorChar).Length < 2
        ? Path.Combine(Program.EditorPath, "Extracted", "PSArc")
        : Path.Combine(Program.EditorPath, "Extracted", "PSArc", currentArchivePath.Split(Path.DirectorySeparatorChar)[^2]);
    private string statusMessage = string.Empty;
    private float statusMessageTimer = 0f;

    // Tree structure
    private FileNode rootNode = new() { Name = "Root", IsDirectory = true, FullPath = "" };

    public PSArcExplorer() : base()
    {
        FrameName = "PSArc Explorer";
    }

    public void LoadArchive(string path)
    {
        try
        {
            currentArchive?.Dispose();
            currentArchive = null;

            var stream = File.OpenRead(path);
            currentArchive = new PSARC(stream);
            currentArchivePath = path;

            filteredFiles = [.. currentArchive.Paths];
            BuildFileTree();

            SetStatus($"Loaded archive: {Path.GetFileName(path)} ({filteredFiles.Count} files)");
        }
        catch (Exception ex)
        {
            SetStatus($"Failed to load archive: {ex.Message}");
        }
    }

    private void BuildFileTree()
    {
        // Reset root node
        rootNode = new FileNode { Name = "Root", IsDirectory = true, FullPath = "" };

        if (currentArchive == null) return;

        foreach (var filePath in currentArchive.Paths)
        {
            // Split the path into parts
            var parts = filePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) continue;

            var currentNode = rootNode;
            var currentPath = "";

            // Navigate/create directory nodes
            for (int i = 0; i < parts.Length - 1; i++)
            {
                var dirName = parts[i];
                currentPath = string.IsNullOrEmpty(currentPath) ? dirName : $"{currentPath}/{dirName}";

                if (!currentNode.Children.ContainsKey(dirName))
                {
                    var dirNode = new FileNode
                    {
                        Name = dirName,
                        FullPath = currentPath,
                        IsDirectory = true
                    };
                    currentNode.AddChild(dirNode);
                }

                currentNode = currentNode.Children[dirName];
            }

            // Add the file node
            var fileName = parts[^1];
            var fileNode = new FileNode
            {
                Name = fileName,
                FullPath = filePath,
                IsDirectory = false
            };
            currentNode.AddChild(fileNode);
        }
    }

    private void SetStatus(string message)
    {
        statusMessage = message;
        statusMessageTimer = 5f;
    }

    protected override void Render(double deltaTime)
    {
        // Update status message timer
        if (statusMessageTimer > 0)
            statusMessageTimer -= (float)deltaTime;

        RenderToolbar();

        ImGui.Separator();

        // Main content area
        if (currentArchive == null)
        {
            ImGui.TextWrapped("No archive loaded. Click 'Open PSArc' to load an archive file.");
            return;
        }

        // Split view
        if (ImGui.BeginChild("psarc_tree_view", new Vector2(ImGui.GetContentRegionAvail().X / 2, ImGui.GetContentRegionAvail().Y), ImGuiChildFlags.Borders))
        {
            RenderFileTree();
        }
        ImGui.EndChild();
        ImGui.SameLine();
        if (ImGui.BeginChild("psarc_details_view", ImGui.GetContentRegionAvail(), ImGuiChildFlags.Borders))
        {
            RenderFileDetails();
        }
        ImGui.EndChild();
    }

    private void RenderToolbar()
    {
        // Archive selection
        ImGui.Text("PSArc Path:");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(400);
        if (ImGui.InputTextWithHint("##archive_path", "Enter PSArc file path...", ref archivePathInput, 512))
        {
        }

        ImGui.SameLine();
        if (ImGui.Button("Paste"))
        {
            try
            {
                var clipboardText = Input.GetClipboardText();
                if (!string.IsNullOrEmpty(clipboardText))
                    archivePathInput = clipboardText;
            }
            catch (Exception e)
            {
                LunaLog.LogError($"Unable to paste from clipboard: {e}");
            }
        }

        ImGui.SameLine();
        if (ImGui.Button("Load"))
        {
            if (!string.IsNullOrEmpty(archivePathInput) && File.Exists(archivePathInput))
            {
                LoadArchive(archivePathInput);
            }
            else
            {
                SetStatus("Invalid file path");
            }
        }

        ImGui.SameLine();
        if (ImGui.Button("Close"))
        {
            currentArchive?.Dispose();
            currentArchive = null;
            currentArchivePath = string.Empty;
            filteredFiles.Clear();
            rootNode = new FileNode { Name = "Root", IsDirectory = true, FullPath = "" };
            selectedFilePath = string.Empty;
            SetStatus("Archive closed");
        }

        // Second row of toolbar
        var canExtract = currentArchive != null;
        if (!canExtract)
            ImGui.BeginDisabled();

        if (ImGui.Button("Extract All"))
        {
            ExtractAll();
        }

        if (!canExtract)
            ImGui.EndDisabled();

        ImGui.SameLine();
        ImGui.Text($"| Files: {filteredFiles.Count}");

        if (currentArchive != null)
        {
            ImGui.SameLine();
            ImGui.Text($"| Archive: {Path.GetFileName(currentArchivePath)}");
        }

        ImGui.SameLine();
        ImGui.Text($"| Extract to: {ExtractionPath}");

        // Search bar
        ImGui.SetNextItemWidth(300);
        if (ImGui.InputTextWithHint("##search", "Search files...", ref searchFilter, 256))
        {
            ApplySearchFilter();
        }

        if (statusMessageTimer > 0)
        {
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(0.2f, 0.8f, 0.2f, 1f), statusMessage);
        }
    }

    private void ApplySearchFilter()
    {
        if (currentArchive == null) return;

        if (string.IsNullOrWhiteSpace(searchFilter))
        {
            filteredFiles = [.. currentArchive.Paths];
        }
        else
        {
            filteredFiles = [.. currentArchive.Paths.Where(path => path.Contains(searchFilter, StringComparison.OrdinalIgnoreCase))];
        }
    }

    private void RenderFileTree()
    {
        if (currentArchive == null) return;

        ImGui.Text("File Tree:");
        ImGui.Separator();

        if (ImGui.BeginChild("tree_content"))
        {
            // Render children of root node
            RenderNode(rootNode);
        }
        ImGui.EndChild();
    }

    private void RenderNode(FileNode node)
    {
        // For root node, just render its children
        if (node == rootNode)
        {
            foreach (var child in node.GetSortedChildren())
            {
                RenderNode(child);
            }
            return;
        }

        // Apply search filter if active
        if (!string.IsNullOrWhiteSpace(searchFilter))
        {
            bool matchesFilter = MatchesSearchFilter(node);
            if (!matchesFilter)
                return;
        }

        if (node.IsDirectory)
        {
            // Render directory node
            var flags = ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.OpenOnDoubleClick;
            var nodeOpen = ImGui.TreeNodeEx($"{node.Name}/###{node.FullPath}", flags);

            if (nodeOpen)
            {
                // Recursively render children
                foreach (var child in node.GetSortedChildren())
                {
                    RenderNode(child);
                }
                ImGui.TreePop();
            }
        }
        else
        {
            // Render file node
            var isSelected = selectedFilePath == node.FullPath;
            var flags = ImGuiTreeNodeFlags.Leaf | ImGuiTreeNodeFlags.NoTreePushOnOpen;
            if (isSelected)
                flags |= ImGuiTreeNodeFlags.Selected;

            ImGui.TreeNodeEx($"{node.Name}###{node.FullPath}", flags);

            if (ImGui.IsItemClicked())
            {
                selectedFilePath = node.FullPath;
            }

            // Context menu for file
            if (ImGui.BeginPopupContextItem($"file_context_{node.FullPath}"))
            {
                if (ImGui.MenuItem("Extract"))
                {
                    ExtractFile(node.FullPath);
                }
                ImGui.EndPopup();
            }
        }
    }

    private bool MatchesSearchFilter(FileNode node)
    {
        // Check if this node or any of its descendants match the filter
        if (node.Name.Contains(searchFilter, StringComparison.OrdinalIgnoreCase))
            return true;

        if (node.IsDirectory)
        {
            foreach (var child in node.Children.Values)
            {
                if (MatchesSearchFilter(child))
                    return true;
            }
        }

        return false;
    }

    private void RenderFileDetails()
    {
        if (string.IsNullOrEmpty(selectedFilePath) || currentArchive == null)
        {
            ImGui.TextWrapped("Select a file to view details");
            return;
        }

        ImGui.Text("File Details:");
        ImGui.Separator();

        // Get file info
        if (currentArchive.Manifest.TryGetValue(selectedFilePath, out var hash) &&
            currentArchive.FileEntries.TryGetValue(hash, out var fileEntry))
        {
            ImGui.BeginGroup();
            ImGui.Text("Path:");
            ImGui.Text("Hash:");
            ImGui.Text("Size:");
            ImGui.Text("Offset:");
            ImGui.Text("Block Index:");
            ImGui.EndGroup();

            ImGui.SameLine();

            ImGui.BeginGroup();
            ImGui.Text(selectedFilePath);
            ImGui.Text(hash.ToString());
            ImGui.Text($"{FormatBytes((long)fileEntry.DecompressedSize)}");
            ImGui.Text($"0x{((long)fileEntry.Offset):X}");
            ImGui.Text($"{fileEntry.BlockIndex}");
            ImGui.EndGroup();

            ImGui.Separator();

            if (ImGui.Button("Extract File"))
            {
                ExtractFile(selectedFilePath);
            }

        }
        else
        {
            ImGui.Text("Could not find file entry");
        }
    }

    private void ExtractFile(string filePath, string? customPath = null)
    {
        if (currentArchive == null) return;

        try
        {
            var targetDir = string.IsNullOrEmpty(customPath) ? ExtractionPath : customPath;
            var targetPath = Path.Combine([ targetDir, .. filePath.Split(Path.PathSeparator, '/')]);
            var targetDirPath = Path.GetDirectoryName(targetPath);

            if (!string.IsNullOrEmpty(targetDirPath))
                Directory.CreateDirectory(targetDirPath);

            using var buffer = currentArchive.OpenFile(filePath);
            File.WriteAllBytes(targetPath, buffer.Data.ToArray());

            SetStatus($"Extracted {Path.GetFileName(filePath)} => {targetPath}");
        }
        catch (Exception ex)
        {
            SetStatus($"Failed to extract {filePath}: {ex.Message}");
        }
    }

    private void ExtractAll()
    {
        if (currentArchive == null) return;

        try
        {
            var totalFiles = filteredFiles.Count;
            var extracted = 0;

            foreach (var filePath in filteredFiles)
            {
                ExtractFile(filePath);
                extracted++;
            }

            SetStatus($"Extracted {extracted}/{totalFiles} files to {ExtractionPath}");
        }
        catch (Exception ex)
        {
            SetStatus($"Extraction failed: {ex.Message}");
        }
    }

    private string FormatBytes(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len = len / 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }

    public override void RenderAsWindow(double deltaTime)
    {
        ImGui.SetNextWindowPos(DefaultPosition, ImGuiCond.Appearing, new Vector2(0.5f));
        ImGui.SetNextWindowSizeConstraints(new Vector2(800, 600), ImGui.GetMainViewport().Size);
        base.RenderAsWindow(deltaTime);
    }

}
