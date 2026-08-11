namespace HarrisCountyAI.Domain.Enums;

/// <summary>Kind of a normalized document field, used to pick the right validation rules.</summary>
public enum FieldKind
{
    Text,
    Date,
    Number,
    Checkbox,
    Signature,
}
