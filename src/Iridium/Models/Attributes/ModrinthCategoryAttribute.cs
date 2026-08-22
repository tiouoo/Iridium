namespace Iridium.Models.Attributes;

[AttributeUsage(AttributeTargets.Field)]
public sealed class ModrinthCategoryAttribute(string slug) : Attribute {
    public string Slug { get; } = slug;
}