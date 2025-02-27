// Rename Serializable.DebugView / DebugViewText
// Rename CrossLanguageSettings
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using static Newtonsoft.Json.JsonConvert;

// ReSharper disable once CheckNamespace
namespace System
{
    /// <summary>
    /// 用于单元测试输出文本的JSON序列化与反序列化
    /// </summary>
    public static class Serializable2
    {
        static readonly Lazy<JsonSerializerSettings> mDebugViewTextSettings = new(GetDebugViewTextSettings);

        static JsonSerializerSettings GetDebugViewTextSettings() => new()
        {
            ContractResolver = new IgnoreJsonPropertyContractResolver(),
            Converters = new List<JsonConverter>
            {
                new StringEnumConverter(),
            },
        };

        /// <summary>
        /// 序列化 JSON 模型，使用原键名，仅调试使用
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static string S(object? value)
            => SerializeObject(value, Formatting.Indented, mDebugViewTextSettings.Value);

        /// <summary>
        /// 反序列化 JSON 模型，使用原键名，仅调试使用
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="value"></param>
        /// <returns></returns>
        [return: MaybeNull]
        public static T D<T>(string value)
            => DeserializeObject<T>(value, mDebugViewTextSettings.Value);
    }
}