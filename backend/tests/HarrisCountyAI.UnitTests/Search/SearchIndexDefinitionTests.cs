using Azure.Search.Documents.Indexes.Models;
using HarrisCountyAI.Infrastructure.Azure.Search;

namespace HarrisCountyAI.UnitTests.Search;

public class SearchIndexDefinitionTests
{
    private static SearchIndex BuildIndex() => SearchIndexDefinition.Build("test-index");

    private static SearchField Field(string name)
    {
        var field = BuildIndex().Fields.SingleOrDefault(f => f.Name == name);
        Assert.NotNull(field);
        return field;
    }

    [Fact]
    public void Build_Uses_Provided_Index_Name()
    {
        Assert.Equal("test-index", BuildIndex().Name);
    }

    [Fact]
    public void Build_Defines_Exactly_The_Expected_Fields()
    {
        var expected = new[]
        {
            "chunkId", "documentId", "sourceType", "title", "department",
            "permitType", "documentType", "section", "page", "effectiveDate",
            "sourceUrl", "text", "embedding", "caseId",
        };

        Assert.Equal(expected, BuildIndex().Fields.Select(f => f.Name).ToArray());
    }

    [Fact]
    public void ChunkId_Is_The_Key_Field()
    {
        var field = Field("chunkId");

        Assert.True(field.IsKey);
        Assert.Equal(SearchFieldDataType.String, field.Type);
        Assert.DoesNotContain(BuildIndex().Fields, f => f.Name != "chunkId" && f.IsKey == true);
    }

    [Theory]
    [InlineData("documentId")]
    [InlineData("sourceType")]
    [InlineData("title")]
    [InlineData("department")]
    [InlineData("permitType")]
    [InlineData("documentType")]
    [InlineData("section")]
    [InlineData("caseId")]
    public void Metadata_String_Fields_Are_Filterable(string name)
    {
        var field = Field(name);

        Assert.Equal(SearchFieldDataType.String, field.Type);
        Assert.True(field.IsFilterable);
    }

    [Fact]
    public void Page_Is_A_Filterable_Int32()
    {
        var field = Field("page");

        Assert.Equal(SearchFieldDataType.Int32, field.Type);
        Assert.True(field.IsFilterable);
    }

    [Fact]
    public void EffectiveDate_Is_A_Filterable_Sortable_DateTimeOffset()
    {
        var field = Field("effectiveDate");

        Assert.Equal(SearchFieldDataType.DateTimeOffset, field.Type);
        Assert.True(field.IsFilterable);
        Assert.True(field.IsSortable);
    }

    [Fact]
    public void Text_Is_Searchable_With_The_Standard_Analyzer()
    {
        var field = Field("text");

        Assert.Equal(SearchFieldDataType.String, field.Type);
        Assert.True(field.IsSearchable);
        Assert.Equal(LexicalAnalyzerName.StandardLucene, field.AnalyzerName);
    }

    [Fact]
    public void Title_Is_Searchable()
    {
        Assert.True(Field("title").IsSearchable);
    }

    [Fact]
    public void Embedding_Is_A_1536_Dimension_Vector_Field()
    {
        var field = Field("embedding");

        Assert.Equal(SearchFieldDataType.Collection(SearchFieldDataType.Single), field.Type);
        Assert.Equal(1536, field.VectorSearchDimensions);
        Assert.Equal(SearchIndexDefinition.VectorProfileName, field.VectorSearchProfileName);
        Assert.True(field.IsSearchable);
    }

    [Fact]
    public void Vector_Profile_Uses_An_Hnsw_Algorithm()
    {
        var vectorSearch = BuildIndex().VectorSearch;

        Assert.NotNull(vectorSearch);
        var profile = Assert.Single(vectorSearch.Profiles);
        Assert.Equal(SearchIndexDefinition.VectorProfileName, profile.Name);
        Assert.Equal(SearchIndexDefinition.HnswAlgorithmName, profile.AlgorithmConfigurationName);

        var algorithm = Assert.Single(vectorSearch.Algorithms);
        var hnsw = Assert.IsType<HnswAlgorithmConfiguration>(algorithm);
        Assert.Equal(SearchIndexDefinition.HnswAlgorithmName, hnsw.Name);
        Assert.Equal(VectorSearchAlgorithmMetric.Cosine, hnsw.Parameters?.Metric);
    }

    [Fact]
    public void Build_Omits_The_Semantic_Configuration_By_Default()
    {
        Assert.Null(BuildIndex().SemanticSearch);
    }

    [Fact]
    public void Build_Can_Include_A_Semantic_Configuration_Over_Title_And_Text()
    {
        var index = SearchIndexDefinition.Build("test-index", includeSemanticRanking: true);

        Assert.NotNull(index.SemanticSearch);
        var configuration = Assert.Single(index.SemanticSearch.Configurations);
        Assert.Equal(SearchIndexDefinition.SemanticConfigurationName, configuration.Name);
        Assert.Equal("title", configuration.PrioritizedFields.TitleField?.FieldName);
        var contentField = Assert.Single(configuration.PrioritizedFields.ContentFields);
        Assert.Equal("text", contentField.FieldName);
    }

    [Fact]
    public void Semantic_Configuration_Does_Not_Change_The_Field_Schema()
    {
        var plain = BuildIndex();
        var semantic = SearchIndexDefinition.Build("test-index", includeSemanticRanking: true);

        Assert.Equal(
            plain.Fields.Select(f => f.Name),
            semantic.Fields.Select(f => f.Name));
    }

    [Fact]
    public void CaseId_Is_Filterable_To_Separate_Case_Evidence_From_The_Corpus()
    {
        Assert.True(Field("caseId").IsFilterable);
        Assert.True(Field("sourceType").IsFilterable);
    }
}
