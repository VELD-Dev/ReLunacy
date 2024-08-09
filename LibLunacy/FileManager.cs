//#define READ_TO_MS

namespace LibLunacy
{
	//Files are loaded here
	public class FileManager
	{
		public string folderPath = string.Empty;

		public Dictionary<string, IGFile?> igfiles = new Dictionary<string, IGFile?>();	//Actual IGFiles
		public Dictionary<string, Stream?> rawfiles = new Dictionary<string, Stream?>();	//Raw data, includes files containing nothing but IGFiles

		public DebugFile? debug = null;

		public bool isOld { get; private set; }		//if true, old filesystem, else new filesystem

		public void LoadFolder(string folderPath)
		{
			this.folderPath = folderPath;
			DirectoryInfo di = new DirectoryInfo(folderPath);
			FileInfo[] files = di.GetFiles();
			isOld = files.Any(x => x.Name == "main.dat");
			if(isOld)
			{
				Console.WriteLine("Luna Engine version: Old");
				//Load IGFiles
				LoadFile("main.dat", false);
				LoadFile("vertices.dat", false);
				LoadFile("gameplay.dat", false);
				LoadFile("debug.dat", false);

				//Load raw files
				LoadFile("textures.dat", true);
				LoadFile("texstream.dat", true);

				if(igfiles["debug.dat"] != null)
				{
					debug = new DebugFile(igfiles["debug.dat"]);
				}
			}
			else
			{
				Console.WriteLine("Luna Engine version: New");
				//Load IGFiles
				LoadFile("gameplay.dat", false);
				LoadFile("assetlookup.dat", false);

				//Load raw files, a lot of these files contain a bunch of IGFiles, however the files themselves are not IGFiles
				LoadFile("mobys.dat", true);
				LoadFile("ties.dat", true);
				LoadFile("textures.dat", true);
				LoadFile("highmips.dat", true);
				LoadFile("shaders.dat", true);
				LoadFile("zones.dat", true);
			}
		}

		public object? LoadFile(string name, bool isRaw)
		{
			//If anyone's wondering, the following basically doubles ram usage but doesn't latch onto files, useful for debugging
			/*FileStream fs = File.Open($"{folderPath}/{name}", FileMode.Open, FileAccess.Read);
			MemoryStream ms = new MemoryStream((int)fs.Length);
			fs.CopyTo(ms);
			fs.Close();*/

			//The following doesn't use as much ram but holds onto files
			Stream? ms = null;
			string path = Path.Combine(folderPath, name);
			if(File.Exists(path))
			{
#if READ_TO_MS
			FileStream fs = File.Open($"{folderPath}/{name}", FileMode.Open, FileAccess.Read);
			ms = new MemoryStream((int)fs.Length);
			fs.CopyTo(ms);
			fs.Close();
#else
				ms = File.Open(path, FileMode.Open, FileAccess.Read);
#endif
			}
			else
			{
				Console.WriteLine($"File '{path}' doesn't exist !");
			}

			if(isRaw)
			{
				rawfiles.Add(name, ms);
				return ms;
			}
			else
			{
				IGFile? file = null;
				if(ms != null)
				{
					file = new IGFile(ms);
				}
				igfiles.Add(name, file);
				return file;
			}
		}
	}
}