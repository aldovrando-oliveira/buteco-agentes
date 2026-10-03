using Buteco.Connectors.Connectors.GoogleDrive;

namespace Buteco.Connectors.Tests;

// google-drive-connector, "Markdown sem imagem embutida", nas formas que a regra cobre.
public class GoogleDriveMarkdownTests
{
    [Fact]
    public void ReferenciaSozinhaNaLinha_SaiComALinhaEADefinicao()
    {
        const string input = "![][image1]\n\n# Título\n\nTexto.\n\n[image1]: <data:image/png;base64,AAAA>";

        Assert.Equal("# Título\n\nTexto.\n", GoogleDriveMarkdown.StripEmbeddedImages(input));
    }

    [Fact]
    public void ReferenciaNoMeioDoTexto_SaiSoAReferencia()
    {
        const string input = "Antes ![alt][image2] depois.\n\n[image2]: data:image/jpeg;base64,BBBB\n";

        Assert.Equal("Antes  depois.\n\n", GoogleDriveMarkdown.StripEmbeddedImages(input));
    }

    [Fact]
    public void ImagemInlineComData_Sai()
    {
        const string input = "Um ![](data:image/png;base64,CCCC) e fim.";

        Assert.Equal("Um  e fim.", GoogleDriveMarkdown.StripEmbeddedImages(input));
    }

    [Fact]
    public void ImagemComUrlComum_Fica()
    {
        const string input = "![logo](https://exemplo.test/logo.png)\n\n![][ref]\n\n[ref]: https://exemplo.test/a.png";

        Assert.Equal(input, GoogleDriveMarkdown.StripEmbeddedImages(input));
    }

    [Fact]
    public void SemImagem_TextoIgual()
    {
        const string input = "# **Título**\n\ndef gerar\\_arquivo(caminho\\_arquivo):\n\n| a | b |\n| :---- | :---- |\n";

        Assert.Equal(input, GoogleDriveMarkdown.StripEmbeddedImages(input));
    }

    [Fact]
    public void RotuloDeReferenciaSemDiferencaDeCaixa()
    {
        const string input = "![][Image1]\n\nTexto\n\n[image1]: <data:image/png;base64,AAAA>";

        Assert.Equal("Texto\n", GoogleDriveMarkdown.StripEmbeddedImages(input));
    }
}
