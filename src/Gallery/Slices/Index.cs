using RazorSlices;

namespace NuDepot.Gallery.Slices;

/// <summary>
/// Creates the slice in <c>Slices/Index.cshtml</c>. Razor Slices would generate this, but only as a public class, which
/// can't take the gallery's internal models.
/// </summary>
internal sealed class Index : IRazorSliceProxy<IndexModel>
{
    /// <summary>
    /// Creates the slice with its model.
    /// </summary>
    public static RazorSlice<IndexModel> Create(IndexModel model)
        => RazorSliceFactory.Create(model, static model => new AspNetCoreGeneratedDocument.Slices_Index { Model = model });

    static RazorSlice<IndexModel> IRazorSliceProxy<IndexModel>.CreateSlice(IndexModel model) => Create(model);
}
