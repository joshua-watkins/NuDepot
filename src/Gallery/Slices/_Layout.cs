using RazorSlices;

namespace NuDepot.Gallery.Slices;

/// <summary>
/// Creates the slice in <c>Slices/_Layout.cshtml</c>. Razor Slices would generate this, but only as a public class, which
/// can't take the gallery's internal models.
/// </summary>
internal sealed class _Layout : IRazorSliceProxy<LayoutModel>
{
    /// <summary>
    /// Creates the slice with its model.
    /// </summary>
    public static RazorSlice<LayoutModel> Create(LayoutModel model)
        => RazorSliceFactory.Create(model, static model => new AspNetCoreGeneratedDocument.Slices__Layout { Model = model });

    static RazorSlice<LayoutModel> IRazorSliceProxy<LayoutModel>.CreateSlice(LayoutModel model) => Create(model);
}
