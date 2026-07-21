using System.Numerics;
using System.Reflection;

namespace ReLunacy.Engine.Loading.IO;

// Reflection-based struct deserializer emulating C struct layout without marshalling,
// which would break on pointer-indirected fields. See StreamHelper for the read primitives.

[AttributeUsage(AttributeTargets.Struct, Inherited = true, AllowMultiple = false)]
public class FileStructure(uint size) : Attribute
{
    public uint Size = size;
}

[AttributeUsage(AttributeTargets.Field, Inherited = true, AllowMultiple = false)]
public class FileOffset(uint offset) : Attribute
{
    public uint Offset = offset;
}

[AttributeUsage(AttributeTargets.Field, Inherited = true, AllowMultiple = false)]
public class Reference : Attribute
{
    private readonly uint _count;
    private readonly string _countCalculator = string.Empty;

    public Reference() { }
    public Reference(string countPropertyName) => _countCalculator = countPropertyName;
    public Reference(uint count) => _count = count;

    // Some array counts require a calculation on the partially-built struct instead of a
    // literal field (e.g. Moby bangle count), hence a property name rather than a field name.
    public uint GetArrayCount(object? instance = null)
    {
        if (_countCalculator == string.Empty) return _count;
        return (uint)instance!.GetType().GetProperty(_countCalculator)!.GetValue(instance)!;
    }
}

public static class FileUtils
{
    public static T ReadStructure<T>(StreamHelper sh, uint size = 0) where T : struct
    {
        if (size == 0)
        {
            size = typeof(T).GetCustomAttribute<FileStructure>()!.Size;
        }

        long initialOffset = sh.BaseStream.Position;
        object tstructure = Activator.CreateInstance<T>();
        FieldInfo[] fields = typeof(T).GetFields();
        List<FieldInfo> arrays = [];

        for (int i = 0; i < fields.Length; i++)
        {
            if (fields[i].IsStatic) continue;
            FileOffset? offset = fields[i].GetCustomAttribute<FileOffset>();
            if (offset == null) continue;

            object field;

            sh.Seek(initialOffset + offset.Offset);

            // Array fields are entirely handled by the second pass below, whose own byte[] branch
            // reads `count` bytes directly with no pointer indirection at all for a literal/property
            // [Reference(count)] (only non-byte[] array types there are genuinely pointer-indirected).
            // Falling through to the Reference check below for an array field would instead read
            // the field's own first 4 bytes as if they were a pointer and seek to that garbage value.
            if (fields[i].FieldType.IsArray)
            {
                arrays.Add(fields[i]);
                continue;
            }

            Reference? reference = fields[i].GetCustomAttribute<Reference>();
            if (reference != null)
            {
                uint referenceOffset = sh.ReadUInt32();
                if (referenceOffset == 0) continue;
                sh.Seek(referenceOffset);
            }

                 if (fields[i].FieldType == typeof(uint))       field = sh.ReadUInt32();
            else if (fields[i].FieldType == typeof(ushort))     field = sh.ReadUInt16();
            else if (fields[i].FieldType == typeof(ulong))      field = sh.ReadUInt64();
            else if (fields[i].FieldType == typeof(float))      field = sh.ReadSingle();
            else if (fields[i].FieldType == typeof(string))     field = sh.ReadString();
            else if (fields[i].FieldType == typeof(byte))       field = sh.ReadByte();
            else if (fields[i].FieldType == typeof(int))        field = sh.ReadInt32();
            else if (fields[i].FieldType == typeof(short))      field = sh.ReadInt16();
            else if (fields[i].FieldType == typeof(long))       field = sh.ReadInt64();
            else if (fields[i].FieldType == typeof(double))     field = sh.ReadDouble();
            else if (fields[i].FieldType == typeof(Half))       field = sh.ReadHalf();
            else if (fields[i].FieldType == typeof(Vector2))    field = new Vector2(sh.ReadSingle(), sh.ReadSingle());
            else if (fields[i].FieldType == typeof(Vector3))    field = new Vector3(sh.ReadSingle(), sh.ReadSingle(), sh.ReadSingle());
            else if (fields[i].FieldType == typeof(Vector4))    field = new Vector4(sh.ReadSingle(), sh.ReadSingle(), sh.ReadSingle(), sh.ReadSingle());
            else if (fields[i].FieldType == typeof(Quaternion)) field = new Quaternion(sh.ReadSingle(), sh.ReadSingle(), sh.ReadSingle(), sh.ReadSingle());
            else if (fields[i].FieldType == typeof(Matrix4x4))  field = new Matrix4x4(
                sh.ReadSingle(), sh.ReadSingle(), sh.ReadSingle(), sh.ReadSingle(),
                sh.ReadSingle(), sh.ReadSingle(), sh.ReadSingle(), sh.ReadSingle(),
                sh.ReadSingle(), sh.ReadSingle(), sh.ReadSingle(), sh.ReadSingle(),
                sh.ReadSingle(), sh.ReadSingle(), sh.ReadSingle(), sh.ReadSingle());
            else if (fields[i].FieldType.IsValueType) field = sh.ReadStruct(fields[i].FieldType);
            else
            {
                throw new Exception($"FileUtils.ReadStructure: Unimplemented type {fields[i].FieldType}");
            }

            fields[i].SetValue(tstructure, field);
        }

        for (int i = 0; i < arrays.Count; i++)
        {
            FileOffset offset = arrays[i].GetCustomAttribute<FileOffset>()!;
            sh.Seek(initialOffset + offset.Offset);

            Reference? reference = arrays[i].GetCustomAttribute<Reference>();

            object field;

            if (arrays[i].FieldType == typeof(byte[]))
            {
                if (reference == null)
                    throw new Exception("FileUtils.ReadStructure: byte[] fields must have [Reference(count)] attribute");

                uint count = reference.GetArrayCount(tstructure);
                field = sh.ReadFromOffset((int)count, (uint)(initialOffset + offset.Offset));
                arrays[i].SetValue(tstructure, field);
                continue;
            }

            uint referenceOffset = sh.ReadUInt32();
            if (referenceOffset == 0) continue;
            sh.Seek(referenceOffset);

            field = ReadStructureArray(arrays[i].FieldType.GetElementType()!, sh, reference!.GetArrayCount(tstructure));
            arrays[i].SetValue(tstructure, field);
        }

        sh.Seek(initialOffset + size);

        return (T)tstructure;
    }

    public static object ReadStructureArray(Type t, StreamHelper sh, uint count)
    {
        return typeof(FileUtils).GetMethod(nameof(ReadStructureArray), [typeof(StreamHelper), typeof(uint)])!
            .MakeGenericMethod(t).Invoke(null, [sh, count])!;
    }

    public static T[] ReadStructureArray<T>(StreamHelper sh, uint count) where T : struct
    {
        T[] items = new T[count];
        uint size = typeof(T).GetCustomAttribute<FileStructure>()!.Size;
        for (uint i = 0; i < count; i++)
        {
            items[i] = ReadStructure<T>(sh, size);
        }
        return items;
    }
}
