// 第 50 轮：把"提案补丁 A"里的合并算法**独立验证**一遍（不碰仓库、不改源码）。
// 用 .NET 10 file-based app 跑：`dotnet run merge-check.cs`
// 断言：父版本 arguments.jvm 的 -Djava.library.path=${natives_directory} 必须保留，子版本条目追加在后。
using System.Text;
using System.Text.Json;

string parentJson = """
{"id":"1.18.2","mainClass":"net.minecraft.client.main.Main",
 "arguments":{"jvm":["-Djava.library.path=${natives_directory}","-cp","${class_path}"],"game":["--width","${resolution_width}","--height","${resolution_height}"]},
 "libraries":[{"name":"com.mojang:logging:1.0.0"},{"name":"org.lwjgl:lwjgl:3.2.2"}]}
""";
string childJson = """
{"id":"fabric-loader-0.19.3-1.18.2","inheritsFrom":"1.18.2",
 "mainClass":"net.fabricmc.loader.impl.launch.knot.KnotClient",
 "arguments":{"jvm":["-DFabricMcEmu= net.minecraft.client.main.Main "],"game":[]},
 "libraries":[{"name":"net.fabricmc:fabric-loader:0.19.3"}]}
""";

using JsonDocument parentDoc = JsonDocument.Parse(parentJson);
using JsonDocument childDoc = JsonDocument.Parse(childJson);
JsonElement parentArgs = parentDoc.RootElement.GetProperty("arguments");
JsonElement childArgs = childDoc.RootElement.GetProperty("arguments");

JsonElement merged = MergeArguments(parentArgs, childArgs);
string jvm = merged.GetProperty("jvm").GetRawText();
string game = merged.GetProperty("game").GetRawText();
Console.WriteLine("merged.jvm  = " + jvm);
Console.WriteLine("merged.game = " + game);

bool okLib = jvm.Contains("-Djava.library.path=${natives_directory}");
bool okChild = jvm.Contains("-DFabricMcEmu");
bool okOrder = jvm.IndexOf("natives_directory") < jvm.IndexOf("FabricMcEmu");
bool okGame = game.Contains("--width") && game.Contains("--height");
Console.WriteLine($"断言 父版本库路径保留={okLib} 子版本条目在={okChild} 顺序(父在前)={okOrder} game 参数保留={okGame}");
Console.WriteLine((okLib && okChild && okOrder && okGame) ? "MERGE-CHECK RESULT=PASS" : "MERGE-CHECK RESULT=FAIL");
return (okLib && okChild && okOrder && okGame) ? 0 : 1;

// —— 与提案补丁 A 中 MergeArguments 相同的实现（父在前、子在后追加；其余字段以子版本为准）
static JsonElement MergeArguments(JsonElement parent, JsonElement child)
{
    if (parent.ValueKind != JsonValueKind.Object) return child.Clone();
    if (child.ValueKind != JsonValueKind.Object) return parent.Clone();
    using MemoryStream stream = new MemoryStream();
    using (Utf8JsonWriter writer = new Utf8JsonWriter(stream))
    {
        writer.WriteStartObject();
        foreach (string name in parent.EnumerateObject().Select(p => p.Name)
            .Concat(child.EnumerateObject().Select(p => p.Name)).Distinct(StringComparer.Ordinal))
        {
            JsonElement? p = parent.TryGetProperty(name, out var pv) ? pv : null;
            JsonElement? c = child.TryGetProperty(name, out var cv) ? cv : null;
            if (p.HasValue && c.HasValue && p.Value.ValueKind == JsonValueKind.Array && c.Value.ValueKind == JsonValueKind.Array)
            {
                writer.WritePropertyName(name);
                writer.WriteStartArray();
                foreach (JsonElement item in p.Value.EnumerateArray()) item.WriteTo(writer);
                foreach (JsonElement item in c.Value.EnumerateArray()) item.WriteTo(writer);
                writer.WriteEndArray();
            }
            else
            {
                JsonElement chosen = c ?? p!.Value;
                writer.WritePropertyName(name);
                chosen.WriteTo(writer);
            }
        }
        writer.WriteEndObject();
    }
    using JsonDocument mergedDoc = JsonDocument.Parse(stream.ToArray());
    return mergedDoc.RootElement.Clone();
}
