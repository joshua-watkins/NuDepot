using RazorSlices;

namespace NuDepot.Gallery.Slices;

/// <summary>
/// Creates the slice in <c>Slices/SignIn.cshtml</c>. Razor Slices would generate this, but only as a public class, which
/// can't take the gallery's internal models.
/// </summary>
internal sealed class SignIn : IRazorSliceProxy<SignInModel>
{
    /// <summary>
    /// Creates the slice with its model.
    /// </summary>
    public static RazorSlice<SignInModel> Create(SignInModel model)
        => RazorSliceFactory.Create(model, static model => new AspNetCoreGeneratedDocument.Slices_SignIn { Model = model });

    static RazorSlice<SignInModel> IRazorSliceProxy<SignInModel>.CreateSlice(SignInModel model) => Create(model);
}
