using RazorSlices;

namespace NuDepot.Gallery.Slices;

/// <summary>
/// Creates the slice in <c>Slices/Package.cshtml</c>. Razor Slices would generate this, but only as a public class, which
/// can't take the gallery's internal models.
/// </summary>
internal sealed class Package : IRazorSliceProxy<PackageModel>
{
    /// <summary>
    /// Creates the slice with its model.
    /// </summary>
    public static RazorSlice<PackageModel> Create(PackageModel model)
        => RazorSliceFactory.Create(model, static model => new AspNetCoreGeneratedDocument.Slices_Package { Model = model });

    static RazorSlice<PackageModel> IRazorSliceProxy<PackageModel>.CreateSlice(PackageModel model) => Create(model);
}
