using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ReLunacy.Frames.DockedFrames;

public class HierarchyExplorer : DockedFrame
{
    protected override ImGuiCond DockingConditions { get; set; } = ImGuiCond.Appearing;
    protected override System.Numerics.Vector2 DefaultPosition { get; set; } = ImGui.GetMainViewport().GetWorkCenter();
    protected override ImGuiWindowFlags WindowFlags { get; set; } = ImGuiWindowFlags.None;
    public List<Entity> entities;

    public List<DirectoryNode> Hierarchy { get; private set; } = [];

    public struct DirectoryNode (string fullPath, string fileName, bool isDirectory, Entity? entity = null)
    {
        public string FullPath = fullPath;
        public string FileName = fileName;
        public List<DirectoryNode> Children = [];
        public bool IsDirectory = isDirectory;
        public Entity? Entity = entity;
        public readonly bool TryGetNode(string path, out DirectoryNode outNode) // with uint depth param ????
        {
            var splitPath = path.Split("/").ToList();
            splitPath.RemoveAt(0);
            outNode = default;

            var nextDir = Children.Find(n => n.FileName == splitPath[0]);
            if (nextDir == default) return false;

            if (splitPath.Count == 1)
            {
                outNode = nextDir;
                return true;
            }
            else if(splitPath.Count < 1)
            {
                throw new ArgumentException("The path must contain at least one object.", nameof(path));
            }

            return nextDir.TryGetNode(splitPath.Stringify("/"), out outNode);
        }

        public static bool operator ==(DirectoryNode a, DirectoryNode b)
        {
            var flags = (a.FullPath == b.FullPath && a.FileName == b.FileName && a.Children == b.Children && a.IsDirectory == b.IsDirectory && a.Entity == b.Entity);
            return flags;
        }

        public static bool operator !=(DirectoryNode a, DirectoryNode b)
        {
            return !(a == b);
        }
    }

    public HierarchyExplorer() {}

    protected override void Render(float deltaTime)
    {
    }

    public override void RenderAsWindow(float deltaTime)
    {
        ImGui.SetNextWindowPos(DefaultPosition, ImGuiCond.Once);
        base.RenderAsWindow(deltaTime); 
    }

    public void SetHierarchy(List<Entity> newHierarchy)
    {
        entities = newHierarchy;
    }

    public void RebuildHierarchy()
    {
        var entityPaths = entities.Select(e => e.name);
        
        foreach(var path in entityPaths)
        {
            var nodeStrings = path.Split("/");
            var filter = Hierarchy.Find(n => n.FileName == nodeStrings[0]);
            if(filter == default)
            {
                filter = new(nodeStrings[0] + "/", nodeStrings[0], nodeStrings.Length > 1);
                Hierarchy.Add(filter);
            }
            else if (filter.TryGetNode(nodeStrings[1..].Stringify("/"), out var gottenNode))
            {
                continue;
            }

            DirectoryNode currentNode = filter;
            for (int i = 0; i < nodeStrings.Length; i++)
            {
                if (nodeStrings.Length >= i+1)
                {
                    if (currentNode.Children.Exists(c => c.FileName == nodeStrings[i + 1]))
                    {
                        currentNode = currentNode.Children.Find(n => n.FileName == nodeStrings[i + 1]);
                    }
                    else
                    {
                        var newNode = new DirectoryNode(nodeStrings[..(i + 1)].Stringify("/"), nodeStrings[i + 1], true);
                        currentNode.Children.Add(newNode);
                        currentNode = newNode;
                    }
                }

                if(nodeStrings.Length == i - 1)
                {

                }

            }
        }
    }
}
