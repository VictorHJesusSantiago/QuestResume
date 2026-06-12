using QuestResume.Core.Extraction.Extractors;

namespace QuestResume.Core.Tests;

public class IpynbExtractorTests
{
    private const string SampleNotebook = """
        {
          "cells": [
            {
              "cell_type": "markdown",
              "source": ["# Título\n", "Texto explicativo."]
            },
            {
              "cell_type": "code",
              "source": ["print('ola mundo')"]
            },
            {
              "cell_type": "code",
              "source": []
            }
          ],
          "metadata": {},
          "nbformat": 4,
          "nbformat_minor": 5
        }
        """;

    [Fact]
    public async Task ExtractAsync_ConcatenatesMarkdownAndCodeCells()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.ipynb");

        try
        {
            await File.WriteAllTextAsync(path, SampleNotebook);

            var extractor = new IpynbExtractor();
            var document = await extractor.ExtractAsync(path);

            Assert.Contains("[markdown]", document.Text);
            Assert.Contains("# Título", document.Text);
            Assert.Contains("Texto explicativo.", document.Text);
            Assert.Contains("[code]", document.Text);
            Assert.Contains("print('ola mundo')", document.Text);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ExtractAsync_SkipsEmptyCells()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.ipynb");

        try
        {
            await File.WriteAllTextAsync(path, SampleNotebook);

            var extractor = new IpynbExtractor();
            var document = await extractor.ExtractAsync(path);

            // Only two non-empty cells should produce a "[code]"/"[markdown]" marker each.
            var codeMarkers = document.Text.Split("[code]").Length - 1;
            Assert.Equal(1, codeMarkers);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
