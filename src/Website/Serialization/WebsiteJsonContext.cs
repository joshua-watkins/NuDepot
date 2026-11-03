using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Html;
using NuGetFeed.Pages;

namespace NuGetFeed.Serialization;

/// <summary>
/// Source generated JSON serialization for the data the pages pass to their scripts.
/// </summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(IReadOnlyList<InstallInstructions>))]
[JsonSerializable(typeof(IReadOnlyList<PushInstructions>))]
[JsonSerializable(typeof(AccountScriptSettings))]
internal partial class WebsiteJsonContext : JsonSerializerContext
{
    /// <inheritdoc cref="ToScriptValue(IReadOnlyList{InstallInstructions})"/>
    public static HtmlString ToScriptValue(AccountScriptSettings value)
        => new(JsonSerializer.Serialize(value, Default.AccountScriptSettings));

    /// <summary>
    /// Serialize data into a script. The default encoder escapes characters like &lt; and &amp;,
    /// so the JSON can't end the script element early.
    /// </summary>
    public static HtmlString ToScriptValue(IReadOnlyList<InstallInstructions> value)
        => new(JsonSerializer.Serialize(value, Default.IReadOnlyListInstallInstructions));

    /// <inheritdoc cref="ToScriptValue(IReadOnlyList{InstallInstructions})"/>
    public static HtmlString ToScriptValue(IReadOnlyList<PushInstructions> value)
        => new(JsonSerializer.Serialize(value, Default.IReadOnlyListPushInstructions));
}
