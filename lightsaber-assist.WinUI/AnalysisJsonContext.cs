using System.Text.Json.Serialization;
using lightsaber_assist.Core.Analysis;

namespace lightsaber_assist.WinUI;

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(AnalysisResult))]
internal sealed partial class AnalysisJsonContext : JsonSerializerContext
{
}
