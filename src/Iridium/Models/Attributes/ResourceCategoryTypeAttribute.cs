using Iridium.Enums.Resources;

namespace Iridium.Models.Attributes;


[AttributeUsage(AttributeTargets.Enum)]
public sealed class ResourceCategoryTypeAttribute(ResourceType type) : Attribute {
    public ResourceType Type { get; } = type;
}
