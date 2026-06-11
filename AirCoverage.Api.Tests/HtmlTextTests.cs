using AirCoverage.Api.Ado;

namespace AirCoverage.Api.Tests;

public class HtmlTextTests
{
    [Fact]
    public void ToPlainText_strips_tags_and_decodes_entities()
    {
        Assert.Equal("a & b", HtmlText.ToPlainText("<div>a &amp; b</div>"));
    }

    [Fact]
    public void ToPlainText_converts_breaks_to_newlines()
    {
        Assert.Equal("line1\nline2", HtmlText.ToPlainText("line1<br>line2"));
    }

    [Fact]
    public void ToPlainText_null_is_empty()
    {
        Assert.Equal("", HtmlText.ToPlainText(null));
    }

    [Fact]
    public void ToHtml_encodes_and_converts_newlines()
    {
        Assert.Equal("a &amp; b<br>c", HtmlText.ToHtml("a & b\nc"));
    }

    [Fact]
    public void ToHtml_null_is_empty()
    {
        Assert.Equal("", HtmlText.ToHtml(null));
    }

    [Fact]
    public void RoundTrip_plaintext_survives_html_then_back()
    {
        const string original = "line1\na & b\nline3";
        Assert.Equal(original, HtmlText.ToPlainText(HtmlText.ToHtml(original)));
    }
}
