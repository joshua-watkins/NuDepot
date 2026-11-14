using RazorSlices;

namespace NuDepot.Gallery.Slices;

/// <summary>
/// Creates the slice in <c>Slices/Admin.cshtml</c>. Razor Slices would generate this, but only as a public class, which
/// can't take the gallery's internal models.
/// </summary>
internal sealed class Admin : IRazorSliceProxy<AdminModel>
{
    /// <summary>
    /// Creates the slice with its model.
    /// </summary>
    public static RazorSlice<AdminModel> Create(AdminModel model)
        => RazorSliceFactory.Create(model, static model => new AspNetCoreGeneratedDocument.Slices_Admin { Model = model });

    static RazorSlice<AdminModel> IRazorSliceProxy<AdminModel>.CreateSlice(AdminModel model) => Create(model);
}
