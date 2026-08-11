using HarrisCountyAI.Domain.Entities;

namespace HarrisCountyAI.Domain.Validation;

/// <summary>A field located by <see cref="ValidationContext.FindField"/>, paired with the document that contains it.</summary>
public sealed record FieldMatch(NormalizedDocument Document, DocumentField Field);
