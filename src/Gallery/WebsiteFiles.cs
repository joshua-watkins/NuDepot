using System;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using Microsoft.Net.Http.Headers;

namespace NuDepot.Gallery;

/// <summary>
/// The website's files, like its scripts, styles and images. They're embedded in the assembly from
/// the wwwroot folder, so hosts don't need to copy or serve them.
/// </summary>
internal static class WebsiteFiles
{
    /// <summary>
    /// The path the files are served under, named after the assembly like a Razor class library's
    /// static web assets. See <see cref="Pages.LayoutModel.Content"/>.
    /// </summary>
    public static readonly string RequestPath = "/_content/" + typeof(WebsiteFiles).Assembly.GetName().Name;

    private static readonly ManifestEmbeddedFileProvider Files = new(typeof(WebsiteFiles).Assembly, "wwwroot");
    private static readonly FileExtensionContentTypeProvider ContentTypes = new();

    // The files only change when the assembly does.
    private static readonly EntityTagHeaderValue EntityTag =
        new($"\"{typeof(WebsiteFiles).Assembly.ManifestModule.ModuleVersionId:N}\"");

    // GET /_content/NuDepot.Gallery/{path}
    public static IResult GetFile(string path)
    {
        var file = Files.GetFileInfo(path);
        if (!file.Exists || file.IsDirectory) return Results.NotFound();

        if (!ContentTypes.TryGetContentType(file.Name, out var contentType))
        {
            contentType = "application/octet-stream";
        }

        return Results.File(
            file.CreateReadStream(),
            contentType,
            lastModified: file.LastModified,
            entityTag: EntityTag,
            enableRangeProcessing: true);
    }
}
