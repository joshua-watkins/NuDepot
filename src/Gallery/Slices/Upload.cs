using RazorSlices;

namespace NuDepot.Gallery.Slices;

/// <summary>
/// Creates the slice in <c>Slices/Upload.cshtml</c>. Razor Slices would generate this, but only as a public class, which
/// can't take the gallery's internal models.
/// </summary>
internal sealed class Upload : IRazorSliceProxy<UploadModel>
{
    /// <summary>
    /// Creates the slice with its model.
    /// </summary>
    public static RazorSlice<UploadModel> Create(UploadModel model)
        => RazorSliceFactory.Create(model, static model => new AspNetCoreGeneratedDocument.Slices_Upload { Model = model });

    static RazorSlice<UploadModel> IRazorSliceProxy<UploadModel>.CreateSlice(UploadModel model) => Create(model);
}
