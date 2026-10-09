using System;
using System.Globalization;

namespace KerbalTogether.Core
{
    public static class PersistentScalar
    {
        public static bool Supports(Type type) => type.IsEnum || type == typeof(string) || type == typeof(bool) ||
            type == typeof(byte) || type == typeof(sbyte) || type == typeof(short) || type == typeof(ushort) ||
            type == typeof(int) || type == typeof(uint) || type == typeof(long) || type == typeof(ulong) ||
            type == typeof(float) || type == typeof(double) || type == typeof(decimal);

        public static string Format(object value, Type type)
        {
            if (!Supports(type)) throw new ArgumentException("Unsupported persistent field type: " + type.FullName);
            if (value == null) return "";
            if (type == typeof(float)) return ((float)value).ToString("R", CultureInfo.InvariantCulture);
            if (type == typeof(double)) return ((double)value).ToString("R", CultureInfo.InvariantCulture);
            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        public static object Parse(string value, Type type)
        {
            if (!Supports(type)) throw new ArgumentException("Unsupported persistent field type: " + type.FullName);
            if (type.IsEnum) return Enum.Parse(type, value, false);
            if (type == typeof(string)) return value;
            return Convert.ChangeType(value, type, CultureInfo.InvariantCulture);
        }
    }
}
