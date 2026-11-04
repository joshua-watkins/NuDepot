using NuDepot.Core.Models;
using System.Reflection;
using System.Text.Json.Serialization;
using Xunit;

namespace NuDepot.Core.Tests.Metadata;

public class ModelTests
{
    /// <summary>
    /// NuDepot extends the NuGet protocol to add more functionality.
    /// Since System.Text.Json does not support polymorphic serialization,
    /// this was implemented by duplicating the protocol's models from the
    /// "NuDepot.Core" project. These tests ensure that the duplicates
    /// stay in sync with the original protocol models.
    /// </summary>
    public static IEnumerable<object[]> ExtendedModelsData()
    {
        yield return new object[]
        {
            new ExtendedModelData
            {
                OriginalType = typeof(RegistrationIndexResponse),
                DerivedType = typeof(NuDepotRegistrationIndexResponse),

                AddedProperties = new Dictionary<string, Type>
                {
                    { "TotalDownloads", typeof(long) },
                },

                ModifiedProperties = new Dictionary<string, (Type From, Type To)>
                {
                    {
                        "Pages",
                        (
                            From: typeof(IReadOnlyList<RegistrationIndexPage>),
                            To: typeof(IReadOnlyList<NuDepotRegistrationIndexPage>)
                        )
                    },
                }
            }
        };

        yield return new object[]
        {
            new ExtendedModelData
            {
                OriginalType = typeof(RegistrationIndexPage),
                DerivedType = typeof(NuDepotRegistrationIndexPage),

                ModifiedProperties = new Dictionary<string, (Type From, Type To)>
                {
                    {
                        "ItemsOrNull",
                        (
                            From: typeof(IReadOnlyList<RegistrationIndexPageItem>),
                            To: typeof(IReadOnlyList<NuDepotRegistrationIndexPageItem>)
                        )
                    },
                }
            }
        };

        yield return new object[]
        {
            new ExtendedModelData
            {
                OriginalType = typeof(RegistrationIndexPageItem),
                DerivedType = typeof(NuDepotRegistrationIndexPageItem),

                ModifiedProperties = new Dictionary<string, (Type From, Type To)>
                {
                    {
                        "PackageMetadata", ( From: typeof(Models.PackageMetadata), To: typeof(NuDepotPackageMetadata) )
                    },
                }
            }
        };

        yield return new object[]
        {
            new ExtendedModelData
            {
                OriginalType = typeof(Models.PackageMetadata),
                DerivedType = typeof(NuDepotPackageMetadata),

                AddedProperties = new Dictionary<string, Type>
                {
                    { "Downloads", typeof(long) },
                    { "HasReadme", typeof(bool) },
                    { "PackageTypes", typeof(IReadOnlyList<string>) },
                    { "ReleaseNotes", typeof(string) },
                    { "RepositoryUrl", typeof(string) },
                    { "RepositoryType", typeof(string) },
                }
            }
        };
    }

    [Theory]
    [MemberData(nameof(ExtendedModelsData))]
    public void ValidateExtendedModels(ExtendedModelData data)
    {
        IReadOnlyDictionary<string, PropertyInfo> originalProperties = data
            .OriginalType!
            .GetProperties()
            .ToDictionary(p => p.Name, p => p);
        IReadOnlyDictionary<string, PropertyInfo> derivedProperties = data
            .DerivedType!
            .GetProperties()
            .ToDictionary(p => p.Name, p => p);

        // Check that all properties on the original model are present on the derived model.
        IEnumerable<string> missingProperties = originalProperties.Keys.Where(name => !derivedProperties.ContainsKey(name));

        Assert.True(
            !missingProperties.Any(),
            $"The following properties are missing from the derived type: {string.Join(',', missingProperties)}");

        // Check that all properties on the derived model are as expected compared to the original model.
        foreach (PropertyInfo derivedProperty in derivedProperties.Values)
        {
            // If the property was added, check that it is not on the original type.
            if (data.AddedProperties.TryGetValue(derivedProperty.Name, out Type? addedType))
            {
                Assert.True(
                    !originalProperties.ContainsKey(derivedProperty.Name),
                    $"Added property '{derivedProperty.Name}' exists on the original type {data.OriginalType}");
                Assert.True(
                    addedType == derivedProperty.PropertyType,
                    $"Added property '{derivedProperty.Name}' on type {data.DerivedType} has unexpected property type\n" +
                    $"Expected: {addedType}\n" +
                    $"Actual: {derivedProperty.PropertyType}");
                continue;
            }

            // This property should exist on both the original and derived models.
            // This property should have the same "JsonPropertyName" attribute values.
            PropertyInfo originalProperty = Assert.Contains(derivedProperty.Name, originalProperties);

            CustomAttributeTypedArgument? originalJsonName = GetAttributeArgs<JsonPropertyNameAttribute>(originalProperty)?.FirstOrDefault();
            CustomAttributeTypedArgument? derivedJsonName = GetAttributeArgs<JsonPropertyNameAttribute>(derivedProperty)?.FirstOrDefault();

            Assert.True(
                originalJsonName != null,
                $"Property '{originalProperty.Name}' on type '{data.OriginalType}' " +
                "does not have a JsonPropertyName attribute");
            Assert.True(
                derivedJsonName != null,
                $"Property '{derivedProperty.Name}' on type '{data.DerivedType}' " +
                "does not have a JsonPropertyName attribute");
            Assert.True(
                originalJsonName.ToString() == derivedJsonName.ToString(),
                $"Property '{derivedProperty.Name}' on type '{data.DerivedType}' " +
                "has a different JsonPropertyName attribute value than " +
                $"on type '{data.OriginalType}'.\nExpected: '{originalJsonName}'\n" +
                $"Actual: '{derivedJsonName}'");

            IList<CustomAttributeTypedArgument>? originalJsonConverterArgs = GetAttributeArgs<JsonConverterAttribute>(originalProperty);
            IList<CustomAttributeTypedArgument>? derivedJsonConverterArgs = GetAttributeArgs<JsonConverterAttribute>(derivedProperty);

            // If the property was modified, check that the property types are expected.
            if (data.ModifiedProperties.TryGetValue(derivedProperty.Name, out (Type From, Type To) modifiedTypes))
            {
                Assert.True(
                    originalProperty.PropertyType == modifiedTypes.From,
                    $"Modified property '{originalProperty.Name}' on type {data.OriginalType} has unexpected property type\n" +
                    $"Expected: {modifiedTypes.From}\n" +
                    $"Actual: {originalProperty.PropertyType}");
                Assert.True(
                    derivedProperty.PropertyType == modifiedTypes.To,
                    $"Modified property '{derivedProperty.Name}' on type {data.DerivedType} has unexpected property type\n" +
                    $"Expected: {modifiedTypes.To}\n" +
                    $"Actual: {derivedProperty.PropertyType}");

                if (originalJsonConverterArgs != null || derivedJsonConverterArgs != null)
                {
                    throw new NotSupportedException(
                        "JSON converters on modified properties is not supported");
                }

                continue;
            }

            // Otherwise, this property should be identical to the original property.
            Assert.True(
                originalProperty.PropertyType == derivedProperty.PropertyType,
                $"Property '{derivedProperty.Name}' on type {data.DerivedType} has unexpected property type\n" +
                $"Expected: {originalProperty.PropertyType}\n" +
                $"Actual: {derivedProperty.PropertyType}");
            Assert.True(
                originalJsonConverterArgs?.First() == derivedJsonConverterArgs?.First(),
                $"Property '{derivedProperty.Name}' on type '{data.DerivedType}' " +
                "has unexpected JsonConverter value.\n" +
                $"Expected: '{originalJsonConverterArgs?.First()}'\n" +
                $"Actual: '{derivedJsonConverterArgs?.First()}'");
        }
    }

    private IList<CustomAttributeTypedArgument>? GetAttributeArgs<TAttribute>(PropertyInfo property)
    {
        return property
            .CustomAttributes
            ?.SingleOrDefault(x => x.AttributeType == typeof(TAttribute))
            ?.ConstructorArguments;
    }

    public class ExtendedModelData
    {
        /// <summary>
        /// The model's type in the "NuDepot.Core" project that was extended.
        /// </summary>
        public Type? OriginalType { get; set; }

        /// <summary>
        /// The model's type in the "NuDepot.Core" project that extends a
        /// type from the "NuDepot.Core" project.
        /// </summary>
        public Type? DerivedType { get; set; }

        /// <summary>
        /// The properties added by the model type in the "NuDepot.Core" project.
        /// </summary>
        public Dictionary<string, Type> AddedProperties { get; set; } = [];

        /// <summary>
        /// The properties whose types were modified by the model type in the
        /// "NuDepot.Core" project.
        /// </summary>
        public Dictionary<string, (Type From, Type To)> ModifiedProperties { get; set; }
            = [];
    }
}
