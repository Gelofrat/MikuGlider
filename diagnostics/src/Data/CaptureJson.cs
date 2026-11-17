using System;
using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using UnityEngine;

namespace MikuGlider.Diagnostics;

internal static class CaptureJson
{
    private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
    {
        Culture = CultureInfo.InvariantCulture,
        NullValueHandling = NullValueHandling.Ignore,
        StringEscapeHandling = StringEscapeHandling.EscapeHtml,
        Converters = { new VectorConverter(), new FiniteNumberConverter(), new StringEnumConverter() }
    };

    internal static string Write(object data)
    {
        return JsonConvert.SerializeObject(data, Settings);
    }

    internal static T Read<T>(string json)
    {
        return JsonConvert.DeserializeObject<T>(json, Settings);
    }

    private sealed class FiniteNumberConverter : JsonConverter
    {
        public override bool CanRead => false;

        public override bool CanConvert(Type type)
        {
            return type == typeof(float) || type == typeof(double);
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            double number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
            if (double.IsNaN(number) || double.IsInfinity(number))
                writer.WriteNull();
            else
                writer.WriteValue(number);
        }

        public override object ReadJson(JsonReader reader, Type type, object existing, JsonSerializer serializer)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class VectorConverter : JsonConverter
    {
        public override bool CanRead => false;

        public override bool CanConvert(Type type)
        {
            return type == typeof(Vector2) || type == typeof(Vector3) || type == typeof(Quaternion);
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            writer.WriteStartArray();
            if (value is Vector2 uv)
            {
                Number(writer, uv.x);
                Number(writer, uv.y);
            }
            else if (value is Vector3 vector)
            {
                Number(writer, vector.x);
                Number(writer, vector.y);
                Number(writer, vector.z);
            }
            else
            {
                var rotation = (Quaternion)value;
                Number(writer, rotation.x);
                Number(writer, rotation.y);
                Number(writer, rotation.z);
                Number(writer, rotation.w);
            }
            writer.WriteEndArray();
        }

        public override object ReadJson(JsonReader reader, Type type, object existing, JsonSerializer serializer)
        {
            throw new NotSupportedException();
        }

        private static void Number(JsonWriter writer, float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                writer.WriteNull();
            else
                writer.WriteValue(value);
        }
    }
}
