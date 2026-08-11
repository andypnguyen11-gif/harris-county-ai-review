using Azure.Search.Documents.Indexes.Models;

namespace HarrisCountyAI.Infrastructure.Azure.Search;

/// <summary>
/// Canonical schema of the chunk index. One physical index holds both the
/// Harris County reference corpus and case-uploaded documents; the
/// <see cref="Fields.SourceType"/> and <see cref="Fields.CaseId"/> fields keep
/// the two strictly filterable apart at query time.
/// </summary>
public static class SearchIndexDefinition
{
    /// <summary>Embedding dimensionality (text-embedding-3-small).</summary>
    public const int EmbeddingDimensions = 1536;

    /// <summary>Name of the HNSW algorithm configuration.</summary>
    public const string HnswAlgorithmName = "chunk-hnsw";

    /// <summary>Name of the vector search profile applied to the embedding field.</summary>
    public const string VectorProfileName = "chunk-vector-profile";

    /// <summary>Analyzer used for the searchable text fields.</summary>
    public static readonly LexicalAnalyzerName TextAnalyzer = LexicalAnalyzerName.StandardLucene;

    /// <summary>Index field names.</summary>
    public static class Fields
    {
        public const string ChunkId = "chunkId";
        public const string DocumentId = "documentId";
        public const string SourceType = "sourceType";
        public const string Title = "title";
        public const string Department = "department";
        public const string PermitType = "permitType";
        public const string DocumentType = "documentType";
        public const string Section = "section";
        public const string Page = "page";
        public const string EffectiveDate = "effectiveDate";
        public const string SourceUrl = "sourceUrl";
        public const string Text = "text";
        public const string Embedding = "embedding";
        public const string CaseId = "caseId";
    }

    /// <summary>Builds the full index definition under <paramref name="indexName"/>.</summary>
    public static SearchIndex Build(string indexName) => new(indexName)
    {
        Fields =
        {
            new SearchField(Fields.ChunkId, SearchFieldDataType.String)
            {
                IsKey = true,
                IsFilterable = true,
            },
            new SearchField(Fields.DocumentId, SearchFieldDataType.String)
            {
                IsFilterable = true,
            },
            new SearchField(Fields.SourceType, SearchFieldDataType.String)
            {
                IsFilterable = true,
                IsFacetable = true,
            },
            new SearchField(Fields.Title, SearchFieldDataType.String)
            {
                IsSearchable = true,
                IsFilterable = true,
                AnalyzerName = TextAnalyzer,
            },
            new SearchField(Fields.Department, SearchFieldDataType.String)
            {
                IsFilterable = true,
                IsFacetable = true,
            },
            new SearchField(Fields.PermitType, SearchFieldDataType.String)
            {
                IsFilterable = true,
                IsFacetable = true,
            },
            new SearchField(Fields.DocumentType, SearchFieldDataType.String)
            {
                IsFilterable = true,
                IsFacetable = true,
            },
            new SearchField(Fields.Section, SearchFieldDataType.String)
            {
                IsFilterable = true,
            },
            new SearchField(Fields.Page, SearchFieldDataType.Int32)
            {
                IsFilterable = true,
            },
            new SearchField(Fields.EffectiveDate, SearchFieldDataType.DateTimeOffset)
            {
                IsFilterable = true,
                IsSortable = true,
            },
            new SearchField(Fields.SourceUrl, SearchFieldDataType.String),
            new SearchField(Fields.Text, SearchFieldDataType.String)
            {
                IsSearchable = true,
                AnalyzerName = TextAnalyzer,
            },
            new SearchField(Fields.Embedding, SearchFieldDataType.Collection(SearchFieldDataType.Single))
            {
                IsSearchable = true,
                VectorSearchDimensions = EmbeddingDimensions,
                VectorSearchProfileName = VectorProfileName,
            },
            new SearchField(Fields.CaseId, SearchFieldDataType.String)
            {
                IsFilterable = true,
            },
        },
        VectorSearch = new VectorSearch
        {
            Algorithms =
            {
                new HnswAlgorithmConfiguration(HnswAlgorithmName)
                {
                    Parameters = new HnswParameters
                    {
                        Metric = VectorSearchAlgorithmMetric.Cosine,
                    },
                },
            },
            Profiles =
            {
                new VectorSearchProfile(VectorProfileName, HnswAlgorithmName),
            },
        },
    };
}
