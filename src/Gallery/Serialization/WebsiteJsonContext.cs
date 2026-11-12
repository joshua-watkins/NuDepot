using Microsoft.AspNetCore.Html;
using NuDepot.Gallery.Slices;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace NuDepot.Gallery.Serialization;

/// <summary>
/// Source generated JSON serialization for the data the slices pass to their scripts.
/// </summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(AccountScriptSettings))]
[JsonSerializable(typeof(ManageScriptSettings))]
[JsonSerializable(typeof(FeedAccount))]
internal partial class WebsiteJsonContext : JsonSerializerContext
{
    /// <inheritdoc cref="ToSettingsScript{T}(string, T, JsonTypeInfo{T})"/>
    public static HtmlString ToSettingsScript(string id, AccountScriptSettings value)
        => ToSettingsScript(id, value, Default.AccountScriptSettings);

    /// <inheritdoc cref="ToSettingsScript{T}(string, T, JsonTypeInfo{T})"/>
    public static HtmlString ToSettingsScript(string id, ManageScriptSettings value)
        => ToSettingsScript(id, value, Default.ManageScriptSettings);

    /// <summary>
    /// A script's settings, as JSON in a <c>&lt;script type="application/json"&gt;</c> element, which browsers don't
    /// run, so pages need no inline scripts. The scripts read it with <c>nudepot.settings(id)</c>. The default
    /// encoder escapes characters like &lt; and &amp;, so the JSON can't end the element early.
    /// </summary>
    private static HtmlString ToSettingsScript<T>(string id, T value, JsonTypeInfo<T> typeInfo)
        => new($"<script type=\"application/json\" id=\"{HtmlEncoder.Default.Encode(id)}\">{JsonSerializer.Serialize(value, typeInfo)}</script>");
}
