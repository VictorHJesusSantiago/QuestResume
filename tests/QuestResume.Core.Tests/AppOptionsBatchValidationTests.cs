using QuestResume.Core.Configuration;

namespace QuestResume.Core.Tests;

public class AppOptionsBatchValidationTests
{
    [Fact]
    public void ValidateBatchQuestionCount_WithinLimit_DoesNotThrow()
    {
        var options = new AppOptions { MaxBatchQuestions = 20 };

        var exception = Record.Exception(() => options.ValidateBatchQuestionCount(20));

        Assert.Null(exception);
    }

    [Fact]
    public void ValidateBatchQuestionCount_ExceedsLimit_ThrowsWithPortugueseMessage()
    {
        var options = new AppOptions { MaxBatchQuestions = 5 };

        var exception = Assert.Throws<AppOptionsValidationException>(() => options.ValidateBatchQuestionCount(6));

        Assert.Contains("MaxBatchQuestions", exception.Message);
    }

    [Fact]
    public void ValidateBatchQuestionCount_Zero_Throws()
    {
        var options = new AppOptions();

        Assert.Throws<AppOptionsValidationException>(() => options.ValidateBatchQuestionCount(0));
    }

    [Fact]
    public void ValidateBatchQuestionCount_Negative_Throws()
    {
        var options = new AppOptions();

        Assert.Throws<AppOptionsValidationException>(() => options.ValidateBatchQuestionCount(-1));
    }

    [Fact]
    public void Validate_DefaultMaxBatchQuestions_IsPositive()
    {
        var options = new AppOptions
        {
            IndexPath = "idx",
            DocumentsFolder = "docs"
        };

        var exception = Record.Exception(() => options.Validate());

        Assert.Null(exception);
        Assert.True(options.MaxBatchQuestions > 0);
    }

    [Fact]
    public void Validate_MaxBatchQuestionsZero_Throws()
    {
        var options = new AppOptions { MaxBatchQuestions = 0 };

        var exception = Assert.Throws<AppOptionsValidationException>(() => options.Validate());

        Assert.Contains("MaxBatchQuestions", exception.Message);
    }
}
