using System.Reflection;
using System.Numerics;

namespace LibLunacy
{
	//Now, you may be asking why i've done this instead of using marshalling.
	//The reason is quite simple, marshalling will cause issues with references, or at the very least it'll be a tad annoying.
	//This system effectively emulates struct marshalling whilst also allowing for pointers to function, allowing for easier interacting with the data structures

	[AttributeUsage(AttributeTargets.Struct, Inherited = true, AllowMultiple = false)]
	public class FileStructure : Attribute
	{
		public uint Size;
		public FileStructure(uint size)
		{
			this.Size = size;
		}
	}

	[AttributeUsage(AttributeTargets.Field, Inherited = true, AllowMultiple = false)]
	public class FileOffset : Attribute
	{
		public uint Offset;

		public FileOffset(uint offset)
		{
			this.Offset = offset;
		}
	}

	[AttributeUsage(AttributeTargets.Field, Inherited = true, AllowMultiple = false)]
	public class Reference : Attribute
	{
		private uint Count;
		private string CountCalculator = string.Empty;
		public Reference(){}
		public Reference(string countPropertyName)
		{
			CountCalculator = countPropertyName;
		}
		public Reference(uint count)
		{
			Count = count;
		}
		//Alright so because the length of an array can change, one should specify the name of the property used to calculate the count. When the array is being read, this function will be called to get the array.
		//If you're asking why a property is needed instead of the name of the variable that'd hold the array length, that is because sometimes calculations are done to get the actual length *cough* *cough* moby bangle count
		public uint GetArrayCount(object instance = null)
		{
			if(CountCalculator == string.Empty) return Count;
			else
			{
				return (uint)instance.GetType().GetProperty(CountCalculator).GetValue(instance);
			}
		}
	}

	public static class FileUtils
	{
		public static T ReadStructure<T>(StreamHelper sh, uint size = 0) where T : struct
		{
			if(size == 0)	//just an attempt to cache this one attribute
			{
				size = typeof(T).GetCustomAttribute<FileStructure>().Size;
			}
			long initialOffset = sh.BaseStream.Position;
			object tstructure = (object)Activator.CreateInstance<T>();
			FieldInfo[] fields = typeof(T).GetFields();
			List<FieldInfo> arrays = new List<FieldInfo>();
			for(int i = 0; i < fields.Length; i++)
			{
				if(fields[i].IsStatic) continue;
				FileOffset offset = fields[i].GetCustomAttribute<FileOffset>();
				if(offset == null) continue;
				
				object field;

				sh.Seek(initialOffset + offset.Offset);

				Reference reference = fields[i].GetCustomAttribute<Reference>();
				if(reference != null)
				{
					uint referenceOffset = sh.ReadUInt32();
					if(referenceOffset == 0) continue;
					sh.Seek(referenceOffset);
				}


				     if(fields[i].FieldType == typeof(uint))                 field = sh.ReadUInt32();
				else if(fields[i].FieldType == typeof(ushort))               field = sh.ReadUInt16();
				else if(fields[i].FieldType == typeof(ulong))                field = sh.ReadUInt64();
				else if(fields[i].FieldType == typeof(float))                field = sh.ReadSingle();
				else if(fields[i].FieldType == typeof(string))               field = sh.ReadString();
				else if(fields[i].FieldType == typeof(Vector3))              field = new Vector3(sh.ReadSingle(), sh.ReadSingle(), sh.ReadSingle());
				else if (fields[i].FieldType == typeof(Vector4))			 field = new Vector4(sh.ReadSingle(), sh.ReadSingle(), sh.ReadSingle(), sh.ReadSingle());	
				else if(fields[i].FieldType == typeof(Matrix4x4))            field = new Matrix4x4(
					sh.ReadSingle(), sh.ReadSingle(), sh.ReadSingle(), sh.ReadSingle(),
					sh.ReadSingle(), sh.ReadSingle(), sh.ReadSingle(), sh.ReadSingle(),
					sh.ReadSingle(), sh.ReadSingle(), sh.ReadSingle(), sh.ReadSingle(),
					sh.ReadSingle(), sh.ReadSingle(), sh.ReadSingle(), sh.ReadSingle()
					);
				else if(fields[i].FieldType == typeof(int))                  field = sh.ReadInt32();
				else if(fields[i].FieldType == typeof(short))                field = sh.ReadInt16();
				else if(fields[i].FieldType == typeof(long))                 field = sh.ReadInt64();
				else if(fields[i].FieldType == typeof(double))               field = sh.ReadDouble();
				else if(fields[i].FieldType.IsValueType)                     field = sh.ReadStruct(fields[i].FieldType);
				else if(fields[i].FieldType.IsArray)
				{
					arrays.Add(fields[i]);
					continue;
				}
				else
				{
					throw new Exception("unimplemented type");
				}

				fields[i].SetValue(tstructure, field);
			}

			for(int i = 0; i < arrays.Count; i++)
			{
				FileOffset offset = arrays[i].GetCustomAttribute<FileOffset>();
				sh.Seek(initialOffset + offset.Offset);

				Reference reference = arrays[i].GetCustomAttribute<Reference>();
				uint referenceOffset = sh.ReadUInt32();
				if(referenceOffset == 0) continue;
				sh.Seek(referenceOffset);

				object field = FileUtils.ReadStructureArray(arrays[i].FieldType.GetElementType(), sh, reference.GetArrayCount(tstructure));
				arrays[i].SetValue(tstructure, field);
			}

			sh.Seek(initialOffset + size);

			return (T)tstructure;
		}
		public static object ReadStructureArray(Type t, StreamHelper sh, uint count)
		{
			return typeof(FileUtils).GetMethod("ReadStructureArray", new Type[2]{typeof(StreamHelper), typeof(uint)}).MakeGenericMethod(t).Invoke(null, new object[2]{sh, count});
		}
		public static T[] ReadStructureArray<T>(StreamHelper sh, uint count) where T : struct
		{
			T[] items = new T[count];
			uint size = typeof(T).GetCustomAttribute<FileStructure>().Size;
			for(uint i = 0; i < count; i++)
			{
				items[i] = ReadStructure<T>(sh, size);
			}
			return items;
		}
	}
}