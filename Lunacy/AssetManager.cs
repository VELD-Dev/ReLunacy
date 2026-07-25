using LibLunacy.Legacy;

namespace Lunacy
{
    //This class converts the games assets into things that OpenGL can deal with, along with caching them to cut down on memory and loads
    public class AssetManager
	{
		private static readonly Lazy<AssetManager> lazy = new Lazy<AssetManager>(() => new AssetManager());
		public static AssetManager Singleton => lazy.Value;

		public Dictionary<ulong, DrawableListList> mobys = new Dictionary<ulong, DrawableListList>();
		public Dictionary<ulong, DrawableList> ties = new Dictionary<ulong, DrawableList>();
		public Dictionary<uint, Texture> textures = new Dictionary<uint, Texture>();

		public void Initialize(AssetLoader al)
		{
			//TODO: cache materials
			foreach(KeyValuePair<uint, CTexture> ctex in al.textures)
			{
				textures.Add(ctex.Key, new Texture(ctex.Value));
			}
			foreach(KeyValuePair<ulong, CMoby> moby in al.mobys)
			{
				mobys.Add(moby.Key, new DrawableListList(moby.Value));
			}
			foreach(KeyValuePair<ulong, CTie> tie in al.ties)
			{
				ties.Add(tie.Key, new DrawableList(tie.Value));
			}
		}

		public void ConsolidateMobys()
		{
			foreach(KeyValuePair<ulong, DrawableListList> moby in AssetManager.Singleton.mobys)
			{
				moby.Value.ConsolidateDrawCalls();
			}
		}
		public void ConsolidateTies()
		{
			foreach(KeyValuePair<ulong, DrawableList> tie in AssetManager.Singleton.ties)
			{
				tie.Value.ConsolidateDrawCalls();
			}
		}
	}
}